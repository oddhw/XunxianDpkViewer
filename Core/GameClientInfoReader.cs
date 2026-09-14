using System.Security.Cryptography;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed record GameClientInfoSnapshot(
    IReadOnlyDictionary<string, GameItemTooltip> Tooltips,
    IReadOnlyDictionary<string, GameMaterialIcon> MaterialIcons,
    IReadOnlyDictionary<string, GameClientEquipmentTooltip> EquipmentTooltips,
    IReadOnlyList<GameInfoRecord> Weapons,
    IReadOnlyList<GameInfoRecord> Equipment,
    GameWanxiangCatalog Wanxiang,
    GameWeaponEnhancementCatalog WeaponEnhancement,
    GameEquipmentWashCatalog EquipmentWash,
    GameEquipmentEnhancementCatalog EquipmentEnhancement,
    GameEquipmentUpgradeCatalog EquipmentUpgrades,
    GameYinYangCatalog YinYang);

public sealed record GameClientEquipmentTooltip(
    string ItemId,
    string Name,
    string Part,
    IReadOnlyList<GameInfoDetailRow> BaseRows,
    IReadOnlyList<string> EquipRows,
    IReadOnlyList<string> FeatureRows,
    GameItemTooltip Tooltip);

public static class GameClientInfoReader
{
    private const int LevelsPerRank = 10;

    private static readonly IReadOnlyDictionary<string, (string Group, string[] Professions)> ProfessionByCode =
        new Dictionary<string, (string Group, string[] Professions)>(StringComparer.OrdinalIgnoreCase)
        {
            ["1*5"] = ("游侠系", new[] { "游侠", "天狐" }),
            ["2*5"] = ("力士系", new[] { "力士", "天狐" }),
            ["3*5"] = ("法师系", new[] { "法师", "天狐" }),
            ["4*5"] = ("符咒系", new[] { "符咒师", "天狐" })
        };

    static GameClientInfoReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static GameClientInfoSnapshot? Load(string resourceFolder)
    {
        if (!TryResolveClientPaths(resourceFolder, out string resFolder, out string mbPath))
        {
            return null;
        }

        using var mbReader = new DpkReader(mbPath);
        var mbEntries = mbReader.ReadEntries()
            .ToDictionary(entry => NormalizePath(entry.Path), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ClientItemRow> items = LoadItems(mbReader, mbEntries);
        Dictionary<string, string> traits = LoadSimpleMap(
            mbReader,
            mbEntries,
            "item/item_trait.txt",
            keyColumn: 0,
            valueColumn: 5);
        Dictionary<string, string> texts = LoadSimpleMap(
            mbReader,
            mbEntries,
            "etc/text.txt",
            keyColumn: 0,
            valueColumn: 1);
        Dictionary<string, ClientWeaponMeta> metadata = LoadWeaponMetadata(mbReader, mbEntries);
        Dictionary<string, string[]> mainEnhancementRows = LoadRowsById(
            mbReader,
            mbEntries,
            "life/new_wuqi/new_weapon_data.txt",
            idColumn: 1);
        Dictionary<string, string[]> newestEnhancementRows = LoadRowsById(
            mbReader,
            mbEntries,
            "life/new_wuqi/new_weapon_data_new.txt",
            idColumn: 1);

        if (items.Count == 0 || traits.Count == 0 || mainEnhancementRows.Count == 0)
        {
            return null;
        }

        using var iconResolver = ClientIconResolver.TryCreate(resFolder);
        var materialIcons = new Dictionary<string, GameMaterialIcon>(StringComparer.OrdinalIgnoreCase);
        var rankNames = new Dictionary<int, string>
        {
            [0] = "凡品",
            [1] = "中品",
            [2] = "上品"
        };
        IReadOnlyList<ClientSoulCapClause> soulCapClauses = ParseSoulCapClauses(texts.Values);
        GameYinYangCatalog yinYang = ParseYinYang(
            mbReader,
            mbEntries,
            items,
            traits,
            texts,
            iconResolver);
        GameWanxiangCatalog wanxiang = ParseWanxiang(
            mbReader,
            mbEntries,
            items,
            texts,
            iconResolver,
            materialIcons);

        foreach ((string id, ClientWeaponMeta meta) in metadata)
        {
            if (!items.TryGetValue(id, out ClientItemRow? item) || !meta.CapRankIndex.HasValue)
            {
                continue;
            }

            string capName = FindSoulCapRankName(item.Name, soulCapClauses);
            if (!string.IsNullOrWhiteSpace(capName))
            {
                rankNames[meta.CapRankIndex.Value] = capName;
            }
        }

        List<string[]> rawCosts = ReadRows(mbReader, mbEntries, "life/new_wuqi/new_weapon_lvl.txt")
            .Where(row => row.Length > 6 && int.TryParse(Cell(row, 0), out _))
            .OrderBy(row => ParseInt(Cell(row, 0)) ?? int.MaxValue)
            .ToList();
        DiscoverCostRankNames(rankNames, rawCosts, items, texts);

        List<GameWeaponEnhancementCost> costs = ParseEnhancementCosts(
            rawCosts,
            rankNames,
            items,
            texts,
            iconResolver,
            materialIcons);
        Dictionary<string, IReadOnlyList<GameWeaponEnhancementRow>> enhancementByItem =
            ParseEnhancementRows(
                mainEnhancementRows,
                newestEnhancementRows,
                metadata,
                items,
                soulCapClauses,
                rankNames,
                costs,
                traits);
        GameEquipmentWashCatalog equipmentWash = ParseEquipmentWash(
            mbReader,
            mbEntries,
            items,
            iconResolver,
            materialIcons,
            texts.GetValueOrDefault("18733", string.Empty));
        GameEquipmentEnhancementCatalog equipmentEnhancement = ParseEquipmentEnhancement(
            mbReader,
            mbEntries,
            items,
            iconResolver,
            materialIcons);
        GameEquipmentUpgradeCatalog equipmentUpgrades = ParseEquipmentUpgrades(
            mbReader,
            mbEntries,
            items,
            iconResolver,
            materialIcons);
        IReadOnlyDictionary<string, GameClientEquipmentTooltip> equipmentTooltips =
            ParseEquipmentTooltips(mbReader, mbEntries, items, traits, texts, iconResolver);

        var tooltips = new Dictionary<string, GameItemTooltip>(StringComparer.OrdinalIgnoreCase);
        foreach (GameYinYangJade jade in yinYang.Jades)
        {
            if (jade.Record.Tooltip is not null)
            {
                tooltips[jade.Record.Id] = jade.Record.Tooltip;
            }
        }

        foreach ((string id, GameClientEquipmentTooltip equipment) in equipmentTooltips)
        {
            tooltips[id] = equipment.Tooltip;
        }

        var weapons = new List<GameInfoRecord>();
        foreach ((string id, IReadOnlyList<GameWeaponEnhancementRow> enhancementRows) in enhancementByItem)
        {
            if (!items.TryGetValue(id, out ClientItemRow? item) || enhancementRows.Count == 0)
            {
                continue;
            }

            GameItemTooltip tooltip = CreateWeaponTooltip(item, traits, texts, iconResolver);
            tooltips[id] = tooltip;

            ClientWeaponMeta? meta = FindWeaponMetadata(item, metadata);
            int capRankIndex = meta?.CapRankIndex ?? enhancementRows.Max(row => row.RankIndex);
            string capRankName = FindSoulCapRankName(item.Name, soulCapClauses);
            if (string.IsNullOrWhiteSpace(capRankName))
            {
                capRankName = rankNames.GetValueOrDefault(capRankIndex, string.Empty);
            }

            (string group, _) = ResolveProfession(item.ProfessionCode);
            int level = ParseInt(item.Level) ?? 0;
            string levelLabel = level > 0 ? $"{level} 级武器" : string.Empty;
            var weaponInfo = new GameWeaponInfo(
                levelLabel,
                level > 0 ? level : int.MaxValue,
                "武魂武器",
                string.IsNullOrWhiteSpace(capRankName) ? string.Empty : $"{capRankName}十阶",
                capRankIndex,
                capRankName,
                new[] { id });

            weapons.Add(new GameInfoRecord(
                id,
                item.Name,
                tooltip.IconPath,
                group,
                string.Empty,
                tooltip,
                weaponInfo));
        }

        if (weapons.Count == 0)
        {
            return null;
        }

        GameInfoRecord[] equipmentRecords = equipmentTooltips.Values
            .Select(item => new GameInfoRecord(
                item.ItemId,
                item.Name,
                item.Tooltip.IconPath,
                GameProfessionFormatter.NormalizeRequirement(item.Tooltip.RequiredProfessions),
                item.Part,
                item.Tooltip))
            .ToArray();
        foreach (GameInfoRecord record in wanxiang.Plates)
        {
            if (record.Tooltip is not null)
            {
                tooltips[record.Id] = record.Tooltip;
            }
        }

        foreach (GameWanxiangGem gem in wanxiang.GemSeries.SelectMany(series => series.Gems.Values))
        {
            if (gem.Record.Tooltip is not null)
            {
                tooltips[gem.Record.Id] = gem.Record.Tooltip;
            }
        }

        return new GameClientInfoSnapshot(
            tooltips,
            materialIcons,
            equipmentTooltips,
            weapons,
            equipmentRecords,
            wanxiang,
            new GameWeaponEnhancementCatalog(
                "mb.dpk · life/new_wuqi/new_weapon_data.txt + new_weapon_data_new.txt",
                "mb.dpk · life/new_wuqi/new_weapon_lvl.txt",
                enhancementByItem,
                costs),
            equipmentWash,
            equipmentEnhancement,
            equipmentUpgrades,
            yinYang);
    }

    private static GameWanxiangCatalog ParseWanxiang(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons)
    {
        string[] guaNames = { "乾", "坤", "震", "兑", "离", "坎", "艮", "巽" };
        (string Profession, int Column)[] professionColumns =
        {
            ("力士", 4),
            ("游侠", 5),
            ("法师", 6),
            ("符咒师", 7)
        };

        Dictionary<string, string[]> stateRows = LoadRowsById(
            reader,
            entries,
            "skill/state_group.txt",
            idColumn: 1);
        var stateCache = new Dictionary<string, GameWanxiangState>(StringComparer.OrdinalIgnoreCase);

        GameWanxiangState ResolveState(string stateId)
        {
            if (stateCache.TryGetValue(stateId, out GameWanxiangState? cached))
            {
                return cached;
            }

            stateRows.TryGetValue(stateId, out string[]? stateRow);
            string iconName = Cell(stateRow ?? Array.Empty<string>(), 9);
            var state = new GameWanxiangState(
                stateId,
                Cell(stateRow ?? Array.Empty<string>(), 0),
                Cell(stateRow ?? Array.Empty<string>(), 2),
                iconResolver?.ResolveStateIcon(iconName) ?? iconResolver?.Resolve(iconName));
            stateCache[stateId] = state;
            return state;
        }

        var levels = new List<GameWanxiangLevelState>();
        foreach (string[] row in ReadRows(
                     reader,
                     entries,
                     "life/new_platestone/new_platestone_state.txt"))
        {
            int? id = ParseInt(Cell(row, 1));
            int? qualityIndex = ParseInt(Cell(row, 2));
            string[] expData = Cell(row, 3).Split(
                '*',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int? maxExp = expData.Length > 0 ? ParseInt(expData[0]) : null;
            int? segment = expData.Length > 1 ? ParseInt(expData[1]) : null;
            Match qualityMatch = Regex.Match(
                Cell(row, 0),
                @"：(?<quality>.+?)->(?<segment>\d+)段",
                RegexOptions.CultureInvariant);
            if (!id.HasValue || !qualityIndex.HasValue || !maxExp.HasValue || !segment.HasValue ||
                !qualityMatch.Success)
            {
                continue;
            }

            var professionStates = new Dictionary<string, GameWanxiangProfessionStates>(StringComparer.Ordinal);
            foreach ((string profession, int column) in professionColumns)
            {
                string[] stateIds = Cell(row, column).Split(
                    '*',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (stateIds.Length < 15)
                {
                    continue;
                }

                GameWanxiangState[] scatterStates = stateIds
                    .Take(7)
                    .Select(ResolveState)
                    .ToArray();
                var guaStates = new Dictionary<string, GameWanxiangState>(StringComparer.Ordinal);
                for (int guaIndex = 0; guaIndex < guaNames.Length; guaIndex++)
                {
                    guaStates[guaNames[guaIndex]] = ResolveState(stateIds[7 + guaIndex]);
                }

                professionStates[profession] = new GameWanxiangProfessionStates(
                    profession,
                    scatterStates,
                    guaStates);
            }

            levels.Add(new GameWanxiangLevelState(
                id.Value,
                qualityIndex.Value,
                qualityMatch.Groups["quality"].Value,
                segment.Value,
                maxExp.Value,
                professionStates)
            {
                ClientDisplayText = Cell(row, 0)
            });
        }

        levels.Sort((left, right) => left.Id.CompareTo(right.Id));

        var gemSeries = new List<GameWanxiangGemSeries>();
        foreach (string[] row in ReadRows(
                     reader,
                     entries,
                     "life/new_platestone/new_extractstone.txt"))
        {
            int? seriesId = ParseInt(Cell(row, 1));
            string seriesName = Cell(row, 0);
            if (!seriesId.HasValue || string.IsNullOrWhiteSpace(seriesName))
            {
                continue;
            }

            var gems = new Dictionary<string, GameWanxiangGem>(StringComparer.Ordinal);
            for (int guaIndex = 0; guaIndex < guaNames.Length; guaIndex++)
            {
                string itemId = Cell(row, 2 + guaIndex);
                if (!items.TryGetValue(itemId, out ClientItemRow? item))
                {
                    continue;
                }

                GameInfoRecord record = CreateSimpleClientRecord(item, texts, iconResolver, "宝石");
                string descriptionRaw = texts.GetValueOrDefault(item.DescriptionTextId, string.Empty);
                string attributeRaw = descriptionRaw
                    .Replace("\\n", "\n", StringComparison.Ordinal)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .FirstOrDefault(line => line.StartsWith("镶嵌成功后", StringComparison.Ordinal))
                    ?.Trim() ?? string.Empty;
                string attributeText = string.Concat(
                    GameRichTextParser.Parse(attributeRaw).Select(span => span.Text));
                gems[guaNames[guaIndex]] = new GameWanxiangGem(
                    guaNames[guaIndex],
                    record,
                    attributeRaw,
                    attributeText);
            }

            if (gems.Count == guaNames.Length)
            {
                gemSeries.Add(new GameWanxiangGemSeries(
                    seriesId.Value,
                    seriesName,
                    seriesName.Contains("绑定", StringComparison.Ordinal),
                    gems));
            }
        }

        gemSeries.Sort((left, right) => left.Id.CompareTo(right.Id));

        int[] qualityIndices = levels
            .Select(level => level.QualityIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        var upgradeMethods = new List<GameWanxiangUpgradeMethod>();
        foreach (string[] row in ReadRows(
                     reader,
                     entries,
                     "life/new_platestone/new_platelvl.txt"))
        {
            int? id = ParseInt(Cell(row, 1));
            string rowName = Cell(row, 0);
            int methodKind = rowName.StartsWith("真火炼卦", StringComparison.Ordinal)
                ? 1
                : rowName.StartsWith("仙缘炼卦", StringComparison.Ordinal) ? 2 : 0;
            if (!id.HasValue || methodKind == 0)
            {
                continue;
            }

            string methodName = methodKind == 1 ? "真火炼卦" : "仙缘炼卦";
            int[] methodQualityIndices = id.Value <= 2
                ? qualityIndices.Where(index => index <= 4).ToArray()
                : new[] { 5 + ((id.Value - 3) / 2) };

            string[] materialData = Cell(row, 2).Split(
                '*',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (materialData.Length < 3 || !int.TryParse(materialData[2], out int materialCount))
            {
                continue;
            }

            string[] materialIds = materialData.Take(2).ToArray();
            ClientItemRow[] materialItems = materialIds
                .Select(itemId => items.GetValueOrDefault(itemId))
                .Where(item => item is not null)
                .Cast<ClientItemRow>()
                .ToArray();
            ClientItemRow? displayMaterial = materialIds
                .Skip(1)
                .Select(itemId => items.GetValueOrDefault(itemId))
                .FirstOrDefault(item => item is not null) ?? materialItems.FirstOrDefault();
            string? materialImagePath = displayMaterial is null
                ? null
                : iconResolver?.Resolve(displayMaterial.IconName);
            var material = new GameWanxiangUpgradeMaterial(
                materialIds,
                materialItems.Select(item => item.Name).ToArray(),
                displayMaterial?.Name ?? string.Empty,
                materialCount,
                materialImagePath);
            foreach (ClientItemRow materialItem in materialItems)
            {
                materialIcons[materialItem.Id] = new GameMaterialIcon(
                    materialItem.Id,
                    materialItem.Name,
                    iconResolver?.Resolve(materialItem.IconName),
                    "mb.dpk · life/new_platestone/new_platelvl.txt");
            }

            string[] outcomeData = Cell(row, 3).Split(
                '*',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var outcomes = new List<GameWanxiangUpgradeOutcome>();
            for (int index = 0; index + 2 < outcomeData.Length; index += 3)
            {
                if (int.TryParse(outcomeData[index], out int exp) &&
                    int.TryParse(outcomeData[index + 1], out int weight))
                {
                    outcomes.Add(new GameWanxiangUpgradeOutcome(exp, weight, outcomeData[index + 2]));
                }
            }

            upgradeMethods.Add(new GameWanxiangUpgradeMethod(
                id.Value,
                methodKind,
                methodName,
                methodQualityIndices,
                material,
                outcomes));
        }

        GameInfoRecord[] plates = items.Values
            .Where(item => item.TypeCode.Equals("102", StringComparison.Ordinal) &&
                           item.Name.Equals("万象宝盘", StringComparison.Ordinal))
            .OrderBy(item => ParseInt(item.Id) ?? int.MaxValue)
            .Select(item => CreateWanxiangPlateRecord(item, texts, iconResolver))
            .ToArray();
        var petalPaths = new Dictionary<int, string>();
        for (int index = 1; index <= 168; index++)
        {
            string? path = iconResolver?.ResolvePath($"image/customequip/new_plate/{index}.png");
            if (!string.IsNullOrWhiteSpace(path))
            {
                petalPaths[index] = path;
            }
        }

        var expPaths = new Dictionary<int, string>();
        foreach (int qualityIndex in levels.Select(level => level.QualityIndex).Distinct())
        {
            string? path = iconResolver?.ResolvePath(
                $"image/customequip/new_plate/exp{qualityIndex}.png");
            if (!string.IsNullOrWhiteSpace(path))
            {
                expPaths[qualityIndex] = path;
            }
        }

        return new GameWanxiangCatalog(
            "mb.dpk · life/new_platestone + skill/state_group.txt",
            plates,
            levels,
            gemSeries,
            upgradeMethods.OrderBy(method => method.Id).ToArray(),
            guaNames,
            iconResolver?.ResolveClientText("string/gb/equip.xml", "plate_guatext19") ?? "无",
            iconResolver?.ResolveClientText("string/gb/equip.xml", "newplate_lvltip1") ??
                "消耗材料:<c:FFB6FF00>[%s]<c>*%d",
            iconResolver?.ResolveClientText("string/gb/equip.xml", "newplate_lvltip2") ??
                "消耗材料:<c:FFFF6A00>[%s]<c>*%d",
            iconResolver?.ResolvePath("image/customequip/bg2.png"),
            iconResolver?.ResolvePath("image/customequip/unlock_card.png"),
            iconResolver?.ResolvePath("image/cmn/frm_select.png"),
            iconResolver?.ResolvePath("image/cmn/pic_card.png"),
            iconResolver?.ResolvePath("image/customequip/new_plate/dituo2.png"),
            iconResolver?.ResolvePath("image/customequip/new_plate/zhongxindituo.png"),
            iconResolver?.ResolvePath("image/customequip/new_plate/zhongxindingceng.png"),
            petalPaths,
            expPaths,
            iconResolver?.ResolvePath("image/cmn/frm_border_head.png"),
            iconResolver?.ResolvePath("image/cmn/frm_border_foot.png"),
            iconResolver?.ResolvePath("image/cmn/frm_border_inner.png"),
            iconResolver?.ResolvePath("image/cmn/pic_background.png"),
            iconResolver?.ResolvePath("image/cmn/pic_border_bottom.png"),
            iconResolver?.ResolvePath("image/common/btn_common.png"),
            iconResolver?.ResolvePath("image/cmn/frm_tip.png"));
    }

    private static GameInfoRecord CreateWanxiangPlateRecord(
        ClientItemRow item,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        GameInfoRecord record = CreateSimpleClientRecord(item, texts, iconResolver, "万象宝盘");
        return record.Tooltip is null
            ? record
            : record with
            {
                Tooltip = record.Tooltip with
                {
                    Type = "八卦盘",
                    Action = "右键单击使用其能力"
                }
            };
    }

    private static GameYinYangCatalog ParseYinYang(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> traits,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        var qualityNames = new Dictionary<int, string>();
        var clothingAttributes = new List<GameYinYangAttribute>();
        IReadOnlyList<string[]> clothingRows = ReadRows(reader, entries, "life/new_clothing_data.txt");
        for (int order = 0; order < clothingRows.Count; order++)
        {
            string[] row = clothingRows[order];
            int? slot = ParseInt(Cell(row, 3));
            int? qualityId = ParseInt(Cell(row, 4));
            int? generationId = ParseInt(Cell(row, 5));
            if (slot is not (1 or 2 or 3) || qualityId is not >= 1 or > 5)
            {
                continue;
            }

            string traitId = Cell(row, 2);
            if (!traits.TryGetValue(traitId, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            string qualityName = ExtractQualityName(Cell(row, 0), text, qualityId.Value);
            if (!string.IsNullOrWhiteSpace(qualityName))
            {
                qualityNames[qualityId.Value] = qualityName;
            }

            clothingAttributes.Add(new GameYinYangAttribute(
                ParseInt(traitId) ?? 0,
                slot.Value,
                Math.Max(1, generationId ?? 1),
                qualityId.Value,
                qualityName,
                ExtractYinYangAttributeName(text),
                text,
                order));
        }

        var useRows = LoadRowsById(reader, entries, "item/item_use.txt", idColumn: 0);
        var builders = new Dictionary<string, YinYangGroupBuilder>(StringComparer.Ordinal);
        foreach (ClientItemRow item in items.Values.OrderBy(item => ParseInt(item.Id) ?? int.MaxValue))
        {
            if (!TryParseYinYangScrollName(
                    item.Name,
                    out string role,
                    out string roleLabel,
                    out string prefix,
                    out string attributeName,
                    out bool isBound,
                    out bool isBest))
            {
                continue;
            }

            string useId = Cell(item.Fields, 30);
            useRows.TryGetValue(useId, out string[]? useRow);
            int useKind = ParseInt(Cell(useRow ?? Array.Empty<string>(), 1)) ?? 0;
            if ((role.Equals("soul", StringComparison.Ordinal) && useKind != 56) ||
                (!role.Equals("soul", StringComparison.Ordinal) && useKind != 163))
            {
                continue;
            }

            int generationId = role.Equals("soul", StringComparison.Ordinal)
                ? 0
                : Math.Max(1, ParseInt(Cell(useRow ?? Array.Empty<string>(), 3)) ?? 1);
            string groupKey = string.Concat(role, ":", prefix);
            if (!builders.TryGetValue(groupKey, out YinYangGroupBuilder? builder))
            {
                builder = new YinYangGroupBuilder(
                    role,
                    roleLabel,
                    generationId,
                    prefix,
                    role.Equals("soul", StringComparison.Ordinal) ? "附魂诀" : roleLabel + "诀");
                builders[groupKey] = builder;
            }

            bool useRowMarksBest = Cell(useRow ?? Array.Empty<string>(), 4).Equals("5", StringComparison.Ordinal);
            builder.Scrolls.Add(new GameYinYangScroll(
                item.Id,
                item.Name,
                attributeName,
                isBound,
                isBest || useRowMarksBest,
                iconResolver?.Resolve(item.IconName),
                texts.GetValueOrDefault(item.DescriptionTextId, string.Empty)));
        }

        var enchantBuilders = builders.Values
            .Where(builder => builder.Role.Equals("enchant", StringComparison.Ordinal))
            .ToArray();
        var spiritBuilders = builders.Values
            .Where(builder => builder.Role.Equals("spirit", StringComparison.Ordinal))
            .ToArray();
        Dictionary<string, int> generationBySeries = enchantBuilders
            .Concat(spiritBuilders)
            .Where(builder => !string.IsNullOrWhiteSpace(builder.Prefix))
            .GroupBy(builder => builder.Prefix[0].ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(builder => builder.GenerationId), StringComparer.Ordinal);

        foreach (YinYangGroupBuilder builder in builders.Values.Where(builder => builder.Role.Equals("soul", StringComparison.Ordinal)))
        {
            builder.GenerationId = builder.Prefix.Equals("阴阳", StringComparison.Ordinal)
                ? 0
                : generationBySeries.GetValueOrDefault(
                    builder.Prefix.Length == 0 ? string.Empty : builder.Prefix[0].ToString(),
                    1);
        }

        var recipeRows = ReadRows(reader, entries, "life/recipe_serve.txt")
            .Where(row => IsSoulRecipeRow(row))
            .ToArray();
        for (int groupIndex = 0; groupIndex + 4 < recipeRows.Length; groupIndex += 5)
        {
            string[][] batch = recipeRows.Skip(groupIndex).Take(5).ToArray();
            string firstTraitText = traits.GetValueOrDefault(Cell(batch[0], 14), string.Empty);
            string prefix = ExtractTraitPrefix(firstTraitText);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = groupIndex / 5 < 10 ? "阴阳" : groupIndex / 5 < 20 ? "太极" : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(prefix) ||
                !builders.TryGetValue(string.Concat("soul:", prefix), out YinYangGroupBuilder? builder))
            {
                continue;
            }

            for (int qualityIndex = 0; qualityIndex < batch.Length; qualityIndex++)
            {
                string traitId = Cell(batch[qualityIndex], 14);
                if (!traits.TryGetValue(traitId, out string? text) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                int qualityId = ParseQualityId(Cell(batch[qualityIndex], 0)) ?? qualityIndex + 1;
                string qualityName = ExtractQualityName(Cell(batch[qualityIndex], 0), text, qualityId);
                if (!string.IsNullOrWhiteSpace(qualityName))
                {
                    qualityNames[qualityId] = qualityName;
                }

                string attributeName = ExtractYinYangAttributeName(text);
                if (builder.Scrolls.Any(scroll => NormalizeYinYangAttributeName(scroll.AttributeName)
                                                  .Equals(NormalizeYinYangAttributeName(attributeName), StringComparison.Ordinal)))
                {
                    builder.Attributes.Add(new GameYinYangAttribute(
                        ParseInt(traitId) ?? 0,
                        0,
                        builder.GenerationId,
                        qualityId,
                        qualityName,
                        attributeName,
                        text,
                        groupIndex + qualityIndex));
                }
            }
        }

        foreach (YinYangGroupBuilder builder in builders.Values.Where(builder =>
                     builder.Role.Equals("enchant", StringComparison.Ordinal) ||
                     builder.Role.Equals("spirit", StringComparison.Ordinal)))
        {
            int[] slots = builder.Role.Equals("enchant", StringComparison.Ordinal) ? new[] { 1, 2 } : new[] { 3 };
            builder.Attributes.AddRange(clothingAttributes.Where(attribute =>
                attribute.QualityId is >= 1 and <= 5 &&
                attribute.Slot is 1 or 2 or 3 &&
                attribute.GenerationId == builder.GenerationId &&
                slots.Contains(attribute.Slot)));
        }

        var qualities = qualityNames
            .Where(pair => pair.Key is >= 1 and <= 5 && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key)
            .Select(pair => new GameYinYangQuality(pair.Key, pair.Value))
            .ToArray();
        if (qualities.Length == 0)
        {
            qualities = new[]
            {
                new GameYinYangQuality(1, "普通"),
                new GameYinYangQuality(2, "中级"),
                new GameYinYangQuality(3, "高级"),
                new GameYinYangQuality(4, "稀有"),
                new GameYinYangQuality(5, "极品")
            };
        }

        var groups = builders.Values
            .Where(builder => builder.Scrolls.Count > 0)
            .Select(builder => new GameYinYangAffixGroup(
                builder.Role,
                builder.RoleLabel,
                builder.GenerationId,
                builder.Prefix.Length > 0 ? builder.Prefix[0].ToString() : builder.Prefix,
                builder.Prefix,
                builder.Prefix + builder.Suffix,
                builder.Scrolls
                    .OrderBy(scroll => scroll.IsBest ? 1 : 0)
                    .ThenBy(scroll => scroll.IsBound ? 1 : 0)
                    .ThenBy(scroll => ParseInt(scroll.ItemId) ?? int.MaxValue)
                    .ToArray(),
                builder.Attributes
                    .OrderBy(attribute => attribute.SourceOrder)
                    .ThenBy(attribute => attribute.QualityId)
                    .ToArray()))
            .OrderBy(group => group.Role.Equals("enchant", StringComparison.Ordinal) ? 1 :
                              group.Role.Equals("spirit", StringComparison.Ordinal) ? 2 : 3)
            .ThenBy(group => group.GenerationId)
            .ThenBy(group => group.Prefix, StringComparer.Ordinal)
            .ToArray();

        var jades = new List<GameYinYangJade>();
        foreach ((string side, string id, string type, string fallbackTextId) in new[]
                 {
                     ("阴", "5734", "阴玉", "27607"),
                     ("阳", "5735", "阳玉", "27608")
                 })
        {
            if (!items.TryGetValue(id, out ClientItemRow? item))
            {
                continue;
            }

            string description = texts.GetValueOrDefault(item.DescriptionTextId, texts.GetValueOrDefault(fallbackTextId, string.Empty));
            GameItemTooltip tooltip = new(
                iconResolver?.Resolve(item.IconName),
                item.Name,
                type,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                string.IsNullOrWhiteSpace(item.Level) ? "1" : item.Level,
                true,
                GameTooltipFormatter.StatusColor,
                Array.Empty<GameInfoDetailRow>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "不可卖店", "可加锁" },
                description,
                description,
                string.Empty,
                string.Empty,
                "0",
                "右键单击使用其他能力")
            {
                BoundText = texts.GetValueOrDefault("163", string.Empty)
            };
            jades.Add(new GameYinYangJade(
                side,
                new GameInfoRecord(item.Id, item.Name, tooltip.IconPath, "曲灵玉", string.Empty, tooltip)));
        }

        return new GameYinYangCatalog(
            "mb.dpk · item/item_list*.txt + item/item_use.txt + item/item_trait.txt + etc/text.txt + life/new_clothing_data.txt + life/recipe_serve.txt",
            jades,
            groups.Where(group => group.Role.Equals("enchant", StringComparison.Ordinal)).ToArray(),
            groups.Where(group => group.Role.Equals("spirit", StringComparison.Ordinal)).ToArray(),
            groups.Where(group => group.Role.Equals("soul", StringComparison.Ordinal)).ToArray(),
            qualities,
            ResolveYinYangEmptyAttributeHints(texts));
    }

    private static IReadOnlyList<GameYinYangEmptyAttributeHint> ResolveYinYangEmptyAttributeHints(
        IReadOnlyDictionary<string, string> texts)
    {
        (string Role, string Label)[] roles =
        {
            ("enchant", "附魔"),
            ("spirit", "附灵"),
            ("soul", "附魂")
        };
        return roles
            .Select(role => new GameYinYangEmptyAttributeHint(
                role.Role,
                texts.Values.FirstOrDefault(text =>
                text.StartsWith("您可以点化[", StringComparison.Ordinal) &&
                text.Contains($"以获得{role.Label}属性", StringComparison.Ordinal)) ?? string.Empty))
            .Where(hint => !string.IsNullOrWhiteSpace(hint.Text))
            .ToArray();
    }

    private static bool TryParseYinYangScrollName(
        string rawName,
        out string role,
        out string roleLabel,
        out string prefix,
        out string attributeName,
        out bool isBound,
        out bool isBest)
    {
        role = string.Empty;
        roleLabel = string.Empty;
        prefix = string.Empty;
        attributeName = string.Empty;
        isBound = rawName.Contains("绑定", StringComparison.Ordinal);
        isBest = rawName.Contains("极品", StringComparison.Ordinal);

        string clean = rawName
            .Replace("【极品】", string.Empty, StringComparison.Ordinal)
            .Replace("（绑定）", string.Empty, StringComparison.Ordinal)
            .Replace("(绑定)", string.Empty, StringComparison.Ordinal)
            .Trim();
        Match match = Regex.Match(
            clean,
            "^(?<prefix>.+?)(?<suffix>附魔诀|附灵诀|附魂诀)(?:\\[(?<attribute>[^\\]]+)\\])?$");
        if (!match.Success)
        {
            return false;
        }

        prefix = match.Groups["prefix"].Value.Trim();
        attributeName = match.Groups["attribute"].Value.Trim();
        string suffix = match.Groups["suffix"].Value;
        (role, roleLabel) = suffix switch
        {
            "附魔诀" => ("enchant", "附魔"),
            "附灵诀" => ("spirit", "附灵"),
            "附魂诀" => ("soul", "附魂"),
            _ => (string.Empty, string.Empty)
        };
        return !string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(prefix);
    }

    private static bool IsSoulRecipeRow(IReadOnlyList<string> row)
    {
        string quality = Cell(row, 0);
        bool qualityRow = quality.StartsWith("普通附魂", StringComparison.Ordinal) ||
                          quality.StartsWith("中级附魂", StringComparison.Ordinal) ||
                          quality.StartsWith("高级附魂", StringComparison.Ordinal) ||
                          quality.StartsWith("稀有附魂", StringComparison.Ordinal) ||
                          quality.StartsWith("极品附魂", StringComparison.Ordinal);
        string[] targetIds = Cell(row, 9).Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return qualityRow && targetIds.Contains("103", StringComparer.Ordinal) && targetIds.Contains("104", StringComparer.Ordinal);
    }

    private static int? ParseQualityId(string value)
    {
        if (value.StartsWith("普通", StringComparison.Ordinal)) return 1;
        if (value.StartsWith("中级", StringComparison.Ordinal)) return 2;
        if (value.StartsWith("高级", StringComparison.Ordinal)) return 3;
        if (value.StartsWith("稀有", StringComparison.Ordinal)) return 4;
        if (value.StartsWith("极品", StringComparison.Ordinal)) return 5;
        return null;
    }

    private static string ExtractQualityName(string rowName, string text, int qualityId)
    {
        string value = rowName + " " + text;
        return ParseQualityId(value) switch
        {
            1 => "普通",
            2 => "中级",
            3 => "高级",
            4 => "稀有",
            5 => "极品",
            _ => qualityId switch
            {
                1 => "普通",
                2 => "中级",
                3 => "高级",
                4 => "稀有",
                5 => "极品",
                _ => string.Empty
            }
        };
    }

    private static string ExtractTraitPrefix(string text)
    {
        int separator = text.IndexOf('·');
        return separator > 0 ? text[..separator].Trim() : string.Empty;
    }

    private static string ExtractYinYangAttributeName(string text)
    {
        string value = text.Trim();
        int separator = value.IndexOf('·');
        if (separator >= 0)
        {
            value = value[(separator + 1)..].Trim();
        }

        foreach (string quality in new[] { "普通", "中级", "高级", "稀有", "极品" })
        {
            if (value.StartsWith(quality, StringComparison.Ordinal))
            {
                value = value[quality.Length..].Trim();
                break;
            }
        }

        int improve = value.IndexOf("提高", StringComparison.Ordinal);
        return (improve > 0 ? value[..improve] : value).Trim();
    }

    private static string NormalizeYinYangAttributeName(string value) => value
        .Replace("生命值上限", "生命上限", StringComparison.Ordinal)
        .Replace("物理攻击", "攻击", StringComparison.Ordinal)
        .Replace("物理防御", "防御", StringComparison.Ordinal)
        .Replace("攻击力", "攻击", StringComparison.Ordinal)
        .Replace("防御力", "防御", StringComparison.Ordinal)
        .Replace("忽略物理防御", "忽略防御", StringComparison.Ordinal)
        .Replace("忽略法术抗性", "忽略法抗", StringComparison.Ordinal)
        .Replace("物理会心几率", "物理会心", StringComparison.Ordinal)
        .Replace("法术会心几率", "法术会心", StringComparison.Ordinal)
        .Replace("抵抗会心几率", "抵抗会心", StringComparison.Ordinal)
        .Trim();

    private sealed class YinYangGroupBuilder
    {
        public YinYangGroupBuilder(string role, string roleLabel, int generationId, string prefix, string suffix)
        {
            Role = role;
            RoleLabel = roleLabel;
            GenerationId = generationId;
            Prefix = prefix;
            Suffix = suffix;
        }

        public string Role { get; }
        public string RoleLabel { get; }
        public int GenerationId { get; set; }
        public string Prefix { get; }
        public string Suffix { get; }
        public List<GameYinYangScroll> Scrolls { get; } = new();
        public List<GameYinYangAttribute> Attributes { get; } = new();
    }

    private static IReadOnlyDictionary<string, GameClientEquipmentTooltip> ParseEquipmentTooltips(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> traits,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        Dictionary<string, string[]> attributeGroups = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atbs.txt",
            idColumn: 1);
        Dictionary<string, string[]> attributeMenus = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atb_menu.txt",
            idColumn: 1);
        Dictionary<string, string[]> attributeValues = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atb_value.txt",
            idColumn: 1);
        Dictionary<string, string[]> featureValues = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_features_group.txt",
            idColumn: 1);
        Dictionary<string, string> attributeNames = LoadSimpleMap(
            reader,
            entries,
            "etc/atb_fight.txt",
            keyColumn: 1,
            valueColumn: 0);
        Dictionary<string, string> itemTypeNames = LoadSimpleMap(
            reader,
            entries,
            "item/item_type.txt",
            keyColumn: 0,
            valueColumn: 1);

        var result = new Dictionary<string, GameClientEquipmentTooltip>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in ReadRows(reader, entries, "life/legend_equip/legend_equip_list.txt"))
        {
            string itemId = Cell(row, 0);
            items.TryGetValue(itemId, out ClientItemRow? item);
            string itemName = item?.Name ?? Cell(row, 1);
            if (!int.TryParse(itemId, out _) || string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            int quality = Math.Clamp(ParseInt(Cell(row, 19)) ?? 4, 1, 4);
            var baseRows = new List<GameInfoDetailRow>();
            AddLegendBaseStat(baseRows, row, 3, "防御力");
            AddLegendBaseStat(baseRows, row, 4, "法术抗性");
            AddLegendBaseStat(baseRows, row, 6, "力量");
            AddLegendBaseStat(baseRows, row, 5, "体魄");
            AddLegendBaseStat(baseRows, row, 8, "元神");
            AddLegendBaseStat(baseRows, row, 7, "筋骨");

            var fixedEquipRows = new List<string>();
            var randomEquipRows = new List<string>();
            var featureRows = new List<string>();
            for (int column = 9; column <= 12; column++)
            {
                (int _, string[] candidates) = ResolveLegendCandidates(
                    Cell(row, column),
                    attributeGroups,
                    attributeMenus);
                if (candidates.Length == 0)
                {
                    continue;
                }

                string value = candidates.Length == 1
                    ? FormatLegendAttribute(candidates[0], quality, attributeValues, attributeNames)
                    : "随机属性";
                if (!string.IsNullOrWhiteSpace(value))
                {
                    (candidates.Length == 1 ? fixedEquipRows : randomEquipRows).Add(value);
                }
            }

            bool featureUsesEquipmentStyle = Cell(row, 22).Equals("1", StringComparison.Ordinal);
            for (int column = 13; column <= 14; column++)
            {
                (int kind, string[] candidates) = ResolveLegendCandidates(
                    Cell(row, column),
                    attributeGroups,
                    attributeMenus);
                if (candidates.Length == 0)
                {
                    continue;
                }

                string value = candidates.Length == 1
                    ? FormatLegendFeature(candidates[0], featureValues)
                    : "随机属性";
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (featureUsesEquipmentStyle || kind == 1)
                {
                    (candidates.Length == 1 ? fixedEquipRows : randomEquipRows).Add(value);
                }
                else
                {
                    featureRows.Add(value);
                }
            }

            string part = item is null
                ? string.Empty
                : itemTypeNames.GetValueOrDefault(item.TypeCode, string.Empty);
            GameItemTooltip tooltip = CreateEquipmentTooltip(
                item,
                itemName,
                part,
                baseRows,
                fixedEquipRows.Concat(randomEquipRows).ToArray(),
                featureRows,
                traits,
                texts,
                iconResolver);
            result[itemId] = new GameClientEquipmentTooltip(
                itemId,
                itemName,
                part,
                baseRows,
                fixedEquipRows.Concat(randomEquipRows).ToArray(),
                featureRows,
                tooltip);
        }

        return result;
    }

    private static GameItemTooltip CreateEquipmentTooltip(
        ClientItemRow? item,
        string name,
        string part,
        IReadOnlyList<GameInfoDetailRow> baseRows,
        IReadOnlyList<string> equipRows,
        IReadOnlyList<string> featureRows,
        IReadOnlyDictionary<string, string> traits,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        string descriptionRaw = item is null
            ? string.Empty
            : texts.GetValueOrDefault(item.DescriptionTextId, string.Empty);
        string description = string.Concat(GameRichTextParser.Parse(descriptionRaw).Select(span => span.Text))
            .Replace("\\n", "\n", StringComparison.Ordinal);
        (_, string[] professions) = item is null
            ? (string.Empty, Array.Empty<string>())
            : ResolveProfession(item.ProfessionCode);
        var flags = new List<string>();
        if (item is not null && !string.IsNullOrWhiteSpace(Cell(item.Fields, 37)))
        {
            flags.Add("不可卖店");
        }
        flags.Add("可加锁");

        bool usesAccessoryTypeLine = item?.TypeCode is "201" or "208";
        string clientTypeText = usesAccessoryTypeLine ? part : string.Empty;
        if (usesAccessoryTypeLine &&
            Cell(item!.Fields, 54).Equals("1", StringComparison.Ordinal) &&
            texts.TryGetValue("19192", out string? uniqueText) &&
            !string.IsNullOrWhiteSpace(uniqueText))
        {
            clientTypeText = $"{clientTypeText}({uniqueText})";
        }

        string clientQualityText = item?.QualityCode == "13"
            ? texts.GetValueOrDefault("18638", string.Empty)
            : string.Empty;

        return new GameItemTooltip(
            item is null ? null : iconResolver?.Resolve(item.IconName),
            name,
            "装备",
            string.Empty,
            part,
            professions,
            item?.Level ?? string.Empty,
            item is null ? null : !string.IsNullOrWhiteSpace(Cell(item.Fields, 42)),
            ResolveClientQuality(item?.QualityCode),
            baseRows,
            equipRows,
            featureRows,
            Array.Empty<string>(),
            flags,
            description,
            descriptionRaw,
            item is null ? string.Empty : traits.GetValueOrDefault(Cell(item.Fields, 30), string.Empty),
            item is null ? string.Empty : Cell(item.Fields, 18),
            item is null ? string.Empty : ParseLong(Cell(item.Fields, 17))?.ToString() ?? "0",
            usesAccessoryTypeLine ? "右键单击使用其他能力" : "鼠标单击使用")
        {
            ClientTypeText = clientTypeText,
            ClientQualityText = clientQualityText
        };
    }

    private static GameInfoRecord CreateSimpleClientRecord(
        ClientItemRow item,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver,
        string group)
    {
        string descriptionRaw = texts.GetValueOrDefault(item.DescriptionTextId, string.Empty);
        string description = string.Concat(GameRichTextParser.Parse(descriptionRaw).Select(span => span.Text))
            .Replace("\\n", "\n", StringComparison.Ordinal);
        var flags = new List<string>();
        if (!string.IsNullOrWhiteSpace(Cell(item.Fields, 37)))
        {
            flags.Add("不可卖店");
        }
        flags.Add("可加锁");
        var tooltip = new GameItemTooltip(
            iconResolver?.Resolve(item.IconName),
            item.Name,
            group,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            item.Level,
            !string.IsNullOrWhiteSpace(Cell(item.Fields, 42)),
            ResolveClientQuality(item.QualityCode),
            Array.Empty<GameInfoDetailRow>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            flags,
            description,
            descriptionRaw,
            string.Empty,
            Cell(item.Fields, 18),
            ParseLong(Cell(item.Fields, 17))?.ToString() ?? "0",
            string.Empty);
        return new GameInfoRecord(item.Id, item.Name, tooltip.IconPath, group, string.Empty, tooltip);
    }

    private static (int Kind, string[] Candidates) ResolveLegendCandidates(
        string groupId,
        IReadOnlyDictionary<string, string[]> attributeGroups,
        IReadOnlyDictionary<string, string[]> attributeMenus)
    {
        if (string.IsNullOrWhiteSpace(groupId) ||
            !attributeGroups.TryGetValue(groupId, out string[]? group))
        {
            return (0, Array.Empty<string>());
        }

        var candidates = new List<string>();
        int kind = 0;
        for (int column = 3; column < group.Length; column += 2)
        {
            string menuId = Cell(group, column);
            if (string.IsNullOrWhiteSpace(menuId) ||
                !attributeMenus.TryGetValue(menuId, out string[]? menu))
            {
                continue;
            }

            kind = ParseInt(Cell(menu, 2)) ?? kind;
            for (int menuColumn = 3; menuColumn < menu.Length; menuColumn += 2)
            {
                string candidateId = Cell(menu, menuColumn);
                if (!string.IsNullOrWhiteSpace(candidateId))
                {
                    candidates.Add(candidateId);
                }
            }
        }

        return (
            kind,
            candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string FormatLegendAttribute(
        string valueId,
        int quality,
        IReadOnlyDictionary<string, string[]> attributeValues,
        IReadOnlyDictionary<string, string> attributeNames)
    {
        if (!attributeValues.TryGetValue(valueId, out string[]? row))
        {
            return string.Empty;
        }

        string attributeName = attributeNames.GetValueOrDefault(Cell(row, 2), string.Empty);
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            Match bracket = Regex.Match(Cell(row, 0), "【(?:随机[-—]?)?([^】]+)】");
            attributeName = bracket.Success ? bracket.Groups[1].Value : Cell(row, 0);
        }

        int valueColumn = 4 + (quality - 1) * 3;
        if (ParseLong(Cell(row, valueColumn)) is not long minimum ||
            ParseLong(Cell(row, valueColumn + 1)) is not long maximum)
        {
            return attributeName;
        }

        bool percentage = Cell(row, 3).Equals("1", StringComparison.Ordinal);
        bool decrease = minimum < 0 && maximum < 0;
        string minimumText = FormatLegendValue(decrease ? Math.Abs(minimum) : minimum, percentage);
        string maximumText = FormatLegendValue(decrease ? Math.Abs(maximum) : maximum, percentage);
        string range = minimumText.Equals(maximumText, StringComparison.Ordinal)
            ? minimumText
            : $"{minimumText}~{maximumText}";
        return $"{attributeName}{(decrease ? "降低" : "提高")}{range}{(percentage ? "%" : "点")}";
    }

    private static string FormatLegendValue(long value, bool percentage) =>
        percentage
            ? (value / 100m).ToString("0.##", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private static string FormatLegendFeature(
        string featureId,
        IReadOnlyDictionary<string, string[]> featureValues)
    {
        if (!featureValues.TryGetValue(featureId, out string[]? row))
        {
            return string.Empty;
        }

        string template = Cell(row, 6);
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        int placeholderCount = Regex.Matches(template, "%s").Count;
        string[] values = Cell(row, 7)
            .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < placeholderCount; index++)
        {
            int valuesPerPlaceholder = placeholderCount > 0 && values.Length >= placeholderCount
                ? Math.Max(1, values.Length / placeholderCount)
                : 0;
            string replacement = string.Empty;
            if (valuesPerPlaceholder > 0)
            {
                string[] group = values
                    .Skip(index * valuesPerPlaceholder)
                    .Take(valuesPerPlaceholder)
                    .ToArray();
                replacement = group.Length > 1 && !group[0].Equals(group[^1], StringComparison.Ordinal)
                    ? $"{group[0]}~{group[^1]}"
                    : group.FirstOrDefault() ?? string.Empty;
            }

            int marker = template.IndexOf("%s", StringComparison.Ordinal);
            if (marker < 0)
            {
                break;
            }

            template = string.Concat(template.AsSpan(0, marker), replacement, template.AsSpan(marker + 2));
        }

        template = template.Replace("%%", "%", StringComparison.Ordinal);
        return template;
    }

    private static void AddLegendBaseStat(
        ICollection<GameInfoDetailRow> rows,
        IReadOnlyList<string> fields,
        int index,
        string label)
    {
        if (ParseLong(Cell(fields, index)) is not long value || value == 0)
        {
            return;
        }

        rows.Add(new GameInfoDetailRow(
            label,
            value > 0
                ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
                : value.ToString(CultureInfo.InvariantCulture)));
    }

    private static GameEquipmentWashCatalog ParseEquipmentWash(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons,
        string maxTierMarker)
    {
        Dictionary<string, string[]> attributeGroups = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atbs.txt",
            idColumn: 1);
        Dictionary<string, string[]> attributeMenus = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atb_menu.txt",
            idColumn: 1);
        Dictionary<string, string[]> attributeValues = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_atb_value.txt",
            idColumn: 1);
        Dictionary<string, string[]> featureValues = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_features_group.txt",
            idColumn: 1);
        Dictionary<string, string> attributeNames = LoadSimpleMap(
            reader,
            entries,
            "etc/atb_fight.txt",
            keyColumn: 1,
            valueColumn: 0);
        Dictionary<string, string[]> refreshRows = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_refresh.txt",
            idColumn: 1);
        Dictionary<string, string> itemTypeNames = LoadSimpleMap(
            reader,
            entries,
            "item/item_type.txt",
            keyColumn: 0,
            valueColumn: 1);

        var records = new Dictionary<string, GameEquipmentWashRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in ReadRows(reader, entries, "life/legend_equip/legend_equip_list.txt"))
        {
            string itemId = Cell(row, 0);
            if (!refreshRows.TryGetValue(itemId, out string[]? refreshRow))
            {
                continue;
            }

            int quality = Math.Clamp(ParseInt(Cell(row, 19)) ?? 4, 1, 4);
            bool featureUsesEquipmentStyle = Cell(row, 22).Equals("1", StringComparison.Ordinal);
            var slots = new List<GameEquipmentWashSlot>();
            var options = new List<GameEquipmentWashOption>();
            for (int column = 9; column <= 14; column++)
            {
                (int kind, bool isWashable, ClientWashCandidateRef[] candidateRefs) = ResolveWashCandidates(
                    Cell(row, column),
                    attributeGroups,
                    attributeMenus,
                    featureValues);
                if (candidateRefs.Length == 0)
                {
                    continue;
                }

                int slotIndex = column - 8;
                var candidates = new List<GameEquipmentWashCandidate>();
                foreach (ClientWashCandidateRef candidateRef in candidateRefs)
                {
                    if (candidateRef.Kind == 2 &&
                        featureValues.TryGetValue(candidateRef.Id, out string[]? featureRow))
                    {
                        IReadOnlyList<string> tierTexts = BuildLegendFeatureTierTexts(featureRow);
                        string featureName = ResolveLegendFeatureName(featureRow, tierTexts);
                        candidates.Add(new GameEquipmentWashCandidate(
                            candidateRef.Id,
                            featureName,
                            candidateRef.Weight,
                            tierTexts,
                            ParseTierWeights(Cell(featureRow, 18), tierTexts.Count),
                            true));
                        options.Add(new GameEquipmentWashOption(
                            candidateRef.Id,
                            $"第{slotIndex}条",
                            featureName,
                            featureName,
                            "feature-field",
                            Array.Empty<int>(),
                            Cell(featureRow, 0)));
                        continue;
                    }

                    if (!attributeValues.TryGetValue(candidateRef.Id, out string[]? attributeRow))
                    {
                        continue;
                    }

                    IReadOnlyList<string> attributeTierTexts = BuildLegendAttributeTierTexts(
                        attributeRow,
                        quality,
                        attributeNames);
                    string attributeName = ResolveLegendAttributeName(attributeRow, attributeNames);
                    candidates.Add(new GameEquipmentWashCandidate(
                        candidateRef.Id,
                        attributeName,
                        candidateRef.Weight,
                        attributeTierTexts,
                        ParseTierWeights(Cell(attributeRow, 16), attributeTierTexts.Count),
                        false));
                    options.Add(new GameEquipmentWashOption(
                        candidateRef.Id,
                        $"第{slotIndex}条",
                        attributeName,
                        ExtractLegendShortName(Cell(attributeRow, 0)),
                        Cell(attributeRow, 3).Equals("1", StringComparison.Ordinal)
                            ? "percent-field"
                            : "point-field",
                        ReadLegendQualityValues(attributeRow, quality),
                        Cell(attributeRow, 0)));
                }

                if (candidates.Count > 0)
                {
                    slots.Add(new GameEquipmentWashSlot(
                        slotIndex,
                        $"第{slotIndex}条属性",
                        isWashable,
                        kind == 2 && !featureUsesEquipmentStyle,
                        candidates));
                }
            }

            if (slots.Count == 0)
            {
                continue;
            }

            var costs = new List<GameEquipmentWashCost>();
            var materials = new Dictionary<string, GameEquipmentWashMaterial>(StringComparer.OrdinalIgnoreCase);
            foreach (GameEquipmentWashSlot slot in slots)
            {
                string rawCost = ResolveWashCostExpression(refreshRow, slot.Index);
                GameEquipmentWashMaterial? material = ParseWashMaterial(
                    rawCost,
                    items,
                    iconResolver,
                    materialIcons);
                long money = 0;
                bool boundMoney = false;
                if (material is not null)
                {
                    materials[string.Join('/', material.ItemIds)] = material;
                }
                else
                {
                    string[] parts = rawCost.Split(
                        '*',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    money = parts.Length > 0 ? ParseLong(parts[0]) ?? 0 : 0;
                    boundMoney = parts.Length > 1 && parts[1].Equals("1", StringComparison.Ordinal);
                }

                costs.Add(new GameEquipmentWashCost(slot.Index, material, money, boundMoney));
            }

            string name = items.TryGetValue(itemId, out ClientItemRow? item)
                ? item.Name
                : Cell(row, 1);
            records[itemId] = new GameEquipmentWashRecord(
                itemId,
                name,
                item is null ? string.Empty : itemTypeNames.GetValueOrDefault(item.TypeCode, string.Empty),
                item?.Level ?? string.Empty,
                options.Select(option => option.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                options,
                materials.Values.ToArray(),
                slots,
                costs)
            {
                MaxTierMarker = maxTierMarker
            };
        }

        return new GameEquipmentWashCatalog(
            "mb.dpk · life/legend_equip/legend_equip_list.txt + legend_equip_refresh.txt",
            records);
    }

    private static (int Kind, bool IsWashable, ClientWashCandidateRef[] Candidates) ResolveWashCandidates(
        string groupId,
        IReadOnlyDictionary<string, string[]> attributeGroups,
        IReadOnlyDictionary<string, string[]> attributeMenus,
        IReadOnlyDictionary<string, string[]> featureValues)
    {
        if (string.IsNullOrWhiteSpace(groupId) ||
            !attributeGroups.TryGetValue(groupId, out string[]? group))
        {
            return (0, false, Array.Empty<ClientWashCandidateRef>());
        }

        var candidates = new Dictionary<string, ClientWashCandidateRef>(StringComparer.OrdinalIgnoreCase);
        int kind = 0;
        int firstGroupWeight = 0;
        int firstCandidateWeight = 0;
        string firstCandidateId = string.Empty;
        for (int groupColumn = 2; groupColumn + 1 < group.Length; groupColumn += 2)
        {
            int groupWeight = ParseInt(Cell(group, groupColumn)) ?? 0;
            string menuId = Cell(group, groupColumn + 1);
            if (groupWeight <= 0 || string.IsNullOrWhiteSpace(menuId) ||
                !attributeMenus.TryGetValue(menuId, out string[]? menu))
            {
                continue;
            }

            if (firstGroupWeight == 0)
            {
                firstGroupWeight = groupWeight;
            }

            int menuKind = ParseInt(Cell(menu, 2)) ?? 0;
            kind = kind == 0 ? menuKind : kind;
            for (int menuColumn = 3; menuColumn + 1 < menu.Length; menuColumn += 2)
            {
                string candidateId = Cell(menu, menuColumn);
                int candidateWeight = ParseInt(Cell(menu, menuColumn + 1)) ?? 0;
                if (string.IsNullOrWhiteSpace(candidateId) || candidateWeight <= 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(firstCandidateId))
                {
                    firstCandidateId = candidateId;
                    firstCandidateWeight = candidateWeight;
                }

                long combinedWeight = (long)groupWeight * candidateWeight;
                if (candidates.TryGetValue(candidateId, out ClientWashCandidateRef? existing))
                {
                    candidates[candidateId] = existing with { Weight = existing.Weight + combinedWeight };
                }
                else
                {
                    candidates[candidateId] = new ClientWashCandidateRef(candidateId, menuKind, combinedWeight);
                }
            }
        }

        bool isWashable;
        if (kind == 2)
        {
            int facultyLimit = featureValues.TryGetValue(firstCandidateId, out string[]? featureRow)
                ? ParseInt(Cell(featureRow, 2)) ?? 0
                : 0;
            isWashable = firstCandidateWeight < 10000 && facultyLimit == 0;
        }
        else
        {
            isWashable = firstGroupWeight < 10000 || firstCandidateWeight < 10000;
        }

        return (kind, isWashable, candidates.Values.ToArray());
    }

    private static string ResolveWashCostExpression(IReadOnlyList<string> row, int slotIndex)
    {
        int start1 = ParseInt(Cell(row, 3)) ?? 0;
        int start2 = ParseInt(Cell(row, 5)) ?? 0;
        int start3 = ParseInt(Cell(row, 7)) ?? 0;
        if (start1 > slotIndex)
        {
            return Cell(row, 2);
        }

        if (start2 > slotIndex)
        {
            return Cell(row, 4);
        }

        if (start3 > slotIndex)
        {
            return Cell(row, 6);
        }

        return Cell(row, 2);
    }

    private static GameEquipmentWashMaterial? ParseWashMaterial(
        string expression,
        IReadOnlyDictionary<string, ClientItemRow> items,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons)
    {
        string[] parts = expression.Split(
            '*',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || ParseInt(parts[^1]) is not int count || count <= 0)
        {
            return null;
        }

        string[] itemIds = parts[..^1]
            .Where(items.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (itemIds.Length == 0)
        {
            return null;
        }

        ClientItemRow first = items[itemIds[0]];
        string? imagePath = iconResolver?.Resolve(first.IconName);
        string[] names = itemIds.Select(id => items[id].Name).ToArray();
        foreach (string id in itemIds)
        {
            ClientItemRow item = items[id];
            materialIcons[id] = new GameMaterialIcon(
                id,
                item.Name,
                iconResolver?.Resolve(item.IconName) ?? imagePath,
                "mb.dpk · item/item_list*.txt");
        }

        return new GameEquipmentWashMaterial(
            itemIds,
            names,
            string.Join(" / ", names),
            count,
            imagePath,
            "mb.dpk · life/legend_equip/legend_equip_refresh.txt");
    }

    private static IReadOnlyList<int> ReadLegendQualityValues(IReadOnlyList<string> row, int quality)
    {
        int column = 4 + (quality - 1) * 3;
        return new[] { Cell(row, column), Cell(row, column + 1), Cell(row, column + 2) }
            .Select(ParseInt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildLegendAttributeTierTexts(
        IReadOnlyList<string> row,
        int quality,
        IReadOnlyDictionary<string, string> attributeNames)
    {
        string name = ResolveLegendAttributeName(row, attributeNames);
        int column = 4 + (quality - 1) * 3;
        if (ParseLong(Cell(row, column)) is not long minimum ||
            ParseLong(Cell(row, column + 1)) is not long maximum)
        {
            return new[] { name };
        }

        long step = ParseLong(Cell(row, column + 2)) ?? 0;
        IReadOnlyList<long> values = BuildLegendTierValues(minimum, maximum, step);
        bool percentage = Cell(row, 3).Equals("1", StringComparison.Ordinal);
        return values.Select(value =>
        {
            bool decrease = value < 0;
            return $"{name}{(decrease ? "降低" : "提高")}" +
                   $"{FormatLegendValue(Math.Abs(value), percentage)}{(percentage ? "%" : "点")}";
        }).ToArray();
    }

    private static IReadOnlyList<long> BuildLegendTierValues(long minimum, long maximum, long step)
    {
        if (minimum == maximum || step == 0)
        {
            return new[] { minimum };
        }

        long direction = Math.Sign(maximum - minimum);
        long increment = Math.Abs(step) * direction;
        var result = new List<long> { minimum };
        long current = minimum;
        while (result.Count < 512)
        {
            long next = current + increment;
            if ((direction > 0 && next >= maximum) || (direction < 0 && next <= maximum))
            {
                if (result[^1] != maximum)
                {
                    result.Add(maximum);
                }
                break;
            }

            result.Add(next);
            current = next;
        }

        return result;
    }

    private static IReadOnlyList<string> BuildLegendFeatureTierTexts(IReadOnlyList<string> row)
    {
        string template = Cell(row, 6);
        string[] values = Cell(row, 7)
            .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int placeholderCount = Regex.Matches(template, "%s").Count;
        if (placeholderCount == 0 || values.Length == 0)
        {
            return string.IsNullOrWhiteSpace(template) ? Array.Empty<string>() : new[] { template };
        }

        int tierCount = Math.Max(1, values.Length / placeholderCount);
        var result = new List<string>(tierCount);
        for (int tier = 0; tier < tierCount; tier++)
        {
            string text = template;
            for (int placeholder = 0; placeholder < placeholderCount; placeholder++)
            {
                int valueIndex = placeholder * tierCount + Math.Min(tier, tierCount - 1);
                string replacement = valueIndex < values.Length ? values[valueIndex] : string.Empty;
                int marker = text.IndexOf("%s", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    text = string.Concat(text.AsSpan(0, marker), replacement, text.AsSpan(marker + 2));
                }
            }

            text = text.Replace("%%", "%", StringComparison.Ordinal);
            result.Add(text);
        }

        return result;
    }

    private static string ResolveLegendFeatureName(
        IReadOnlyList<string> row,
        IReadOnlyList<string> tierTexts)
    {
        string name = Cell(row, 0);
        if (!name.Equals("装备", StringComparison.Ordinal) &&
            !name.Equals("特性", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        string text = tierTexts.FirstOrDefault() ?? Cell(row, 6);
        Match valueSuffix = Regex.Match(text, "^(?<name>.+?)(?:提高|降低)[+-]?[0-9.]+");
        return valueSuffix.Success ? valueSuffix.Groups["name"].Value.Trim() : text.Trim();
    }

    private static IReadOnlyList<int> ParseTierWeights(string expression, int tierCount)
    {
        int[] source = expression
            .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseInt)
            .Where(value => value.HasValue)
            .Select(value => Math.Max(0, value!.Value))
            .ToArray();
        var result = new int[Math.Max(1, tierCount)];
        Array.Copy(source, result, Math.Min(source.Length, result.Length));
        if (result.Sum() == 0)
        {
            result[0] = 10000;
        }

        return result;
    }

    private static string ResolveLegendAttributeName(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, string> attributeNames)
    {
        string name = attributeNames.GetValueOrDefault(Cell(row, 2), string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        Match bracket = Regex.Match(Cell(row, 0), "【(?:随机[-—]?)?([^】]+)】");
        return bracket.Success ? bracket.Groups[1].Value : Cell(row, 0);
    }

    private static string ExtractLegendShortName(string rawName)
    {
        Match bracket = Regex.Match(rawName, "【([^】]+)】");
        return bracket.Success ? bracket.Groups[1].Value : rawName;
    }

    private static GameEquipmentEnhancementCatalog ParseEquipmentEnhancement(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons)
    {
        var result = new Dictionary<string, GameEquipmentEnhancementInfo>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]> events = LoadRowsById(
            reader,
            entries,
            "life/legend_equip/legend_equip_improve_event.txt",
            idColumn: 1);
        GameEquipmentEnhancementEvent[] enhancementEvents = events
            .Values
            .Select(eventRow => new GameEquipmentEnhancementEvent(
                Cell(eventRow, 1),
                Cell(eventRow, 0),
                ParseInt(Cell(eventRow, 3)) ?? 0,
                Math.Max(1, ParseInt(Cell(eventRow, 5)) ?? 1),
                1 + Math.Max(0, ParseInt(Cell(eventRow, 6)) ?? 0),
                !Cell(eventRow, 7).Equals("1", StringComparison.Ordinal),
                Cell(eventRow, 8).Equals("1", StringComparison.Ordinal)))
            .Where(gameEvent => !string.IsNullOrWhiteSpace(gameEvent.Id))
            .OrderBy(gameEvent => ParseInt(gameEvent.Id) ?? int.MaxValue)
            .ToArray();

        Dictionary<int, (string EventId, int Bonus)> fireBonuses = ReadRows(
                reader,
                entries,
                "life/legend_equip/legend_equip_improve_rand.txt")
            .Where(randRow => ParseInt(Cell(randRow, 0)) is >= 1001 and <= 1003)
            .Select(randRow =>
            {
                int fireId = (ParseInt(Cell(randRow, 0)) ?? 1000) - 1000;
                string[] bonus = Cell(randRow, 6).Split(
                    '*',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new
                {
                    FireId = fireId,
                    EventId = bonus.ElementAtOrDefault(0) ?? string.Empty,
                    Bonus = ParseInt(bonus.ElementAtOrDefault(1) ?? string.Empty) ?? 0
                };
            })
            .Where(value => value.FireId is >= 1 and <= 3 &&
                            !string.IsNullOrWhiteSpace(value.EventId) &&
                            value.Bonus > 0)
            .ToDictionary(value => value.FireId, value => (value.EventId, value.Bonus));

        string[] fireDescriptions =
        {
            "一条属性提升两档【5倍概率】",
            "两条属性各提升一档【5倍概率】",
            "一条未满属性提升一档【5倍概率】"
        };
        GameEquipmentFireOption[] fireOptions = Enumerable.Range(1, 3)
            .Select(fireId =>
            {
                (string eventId, int bonus) = fireBonuses.GetValueOrDefault(fireId);
                return new GameEquipmentFireOption(
                    fireId,
                    fireId.ToString(CultureInfo.InvariantCulture),
                    fireDescriptions[fireId - 1],
                    eventId ?? string.Empty,
                    bonus);
            })
            .ToArray();

        int fireAccuracy = ReadRows(reader, entries, "etc/define.txt")
            .Where(defineRow => Cell(defineRow, 0).Equals("1019", StringComparison.Ordinal))
            .Select(defineRow => ParseInt(Cell(defineRow, 1)) ?? 0)
            .FirstOrDefault();

        foreach (string[] row in ReadRows(reader, entries, "life/legend_equip/legend_equip_improve.txt"))
        {
            string itemId = Cell(row, 1);
            if (!int.TryParse(itemId, out _) || !items.TryGetValue(itemId, out ClientItemRow? item))
            {
                continue;
            }

            string[] materialIds = Cell(row, 2)
                .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var materials = new List<GameEquipmentMaterial>();
            foreach (string materialId in materialIds)
            {
                if (!items.TryGetValue(materialId, out ClientItemRow? material))
                {
                    continue;
                }

                string? imagePath = iconResolver?.Resolve(material.IconName);
                var icon = new GameMaterialIcon(
                    materialId,
                    material.Name,
                    imagePath,
                    "mb.dpk · item/item_list*.txt");
                materialIcons[materialId] = icon;
                materials.Add(new GameEquipmentMaterial(materialId, material.Name, imagePath));
            }

            int materialCount = ParseInt(Cell(row, 3)) ?? 0;
            if (materialCount <= 0 && materials.Count == 0)
            {
                continue;
            }

            result[itemId] = new GameEquipmentEnhancementInfo(
                itemId,
                item.Name,
                materials,
                Cell(row, 7),
                materialCount,
                ParseLong(Cell(row, 5)) ?? 0,
                Cell(row, 6),
                fireAccuracy,
                enhancementEvents,
                fireOptions,
                "请点击右键放入需要精炼的传奇装备。\n提示：每次提升会随机选择一条属性进行。\n           若属性达到最高档次，效果不变。");
        }

        return new GameEquipmentEnhancementCatalog(
            "mb.dpk · life/legend_equip/legend_equip_improve.txt",
            result);
    }

    private static GameEquipmentUpgradeCatalog ParseEquipmentUpgrades(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons)
    {
        var steps = new List<GameEquipmentUpgrade>();
        foreach (string[] row in ReadRows(
                     reader,
                     entries,
                     "life/legend_equip/legend_equip_convert_to_new.txt"))
        {
            string sourceItemId = Cell(row, 1);
            string targetItemId = Cell(row, 2);
            if (!int.TryParse(sourceItemId, out _) || !int.TryParse(targetItemId, out _))
            {
                continue;
            }

            string displayText = Cell(row, 0);
            string fallbackSourceName = displayText;
            string fallbackTargetName = string.Empty;
            int arrowIndex = displayText.IndexOf('→');
            if (arrowIndex >= 0)
            {
                fallbackSourceName = displayText[..arrowIndex].Trim();
                fallbackTargetName = displayText[(arrowIndex + 1)..].Trim();
            }

            string sourceName = items.TryGetValue(sourceItemId, out ClientItemRow? sourceItem)
                ? sourceItem.Name
                : StripRecipeAnnotation(fallbackSourceName);
            string targetName = items.TryGetValue(targetItemId, out ClientItemRow? targetItem)
                ? targetItem.Name
                : StripRecipeAnnotation(fallbackTargetName);
            if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
            {
                continue;
            }

            var materials = new List<GameEquipmentMaterial>();
            foreach (string materialId in Cell(row, 4)
                         .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                items.TryGetValue(materialId, out ClientItemRow? material);
                string materialName = material?.Name ?? materialId;
                string? imagePath = material is null ? null : iconResolver?.Resolve(material.IconName);
                if (material is not null)
                {
                    materialIcons[materialId] = new GameMaterialIcon(
                        materialId,
                        materialName,
                        imagePath,
                        "mb.dpk · item/item_list*.txt");
                }

                materials.Add(new GameEquipmentMaterial(materialId, materialName, imagePath));
            }

            steps.Add(new GameEquipmentUpgrade(
                sourceItemId,
                sourceName,
                targetItemId,
                targetName,
                materials,
                ParseLong(Cell(row, 3)),
                ParseInt(Cell(row, 5)) ?? 0,
                Cell(row, 6),
                Cell(row, 7).Equals("1", StringComparison.Ordinal) ||
                Cell(row, 8).Equals("1", StringComparison.Ordinal) ||
                displayText.Contains("绑定配方升级", StringComparison.Ordinal)));
        }

        return new GameEquipmentUpgradeCatalog(
            "mb.dpk · life/legend_equip/legend_equip_convert_to_new.txt",
            steps);
    }

    private static string StripRecipeAnnotation(string value) =>
        value
            .Replace("（绑定配方升级）", string.Empty, StringComparison.Ordinal)
            .Replace("(绑定配方升级)", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static Dictionary<string, ClientItemRow> LoadItems(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries)
    {
        var result = new Dictionary<string, ClientItemRow>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" })
        {
            foreach (string[] row in ReadRows(reader, entries, path))
            {
                string id = Cell(row, 0);
                if (!int.TryParse(id, out _) || string.IsNullOrWhiteSpace(Cell(row, 1)))
                {
                    continue;
                }

                result[id] = new ClientItemRow(
                    id,
                    Cell(row, 1),
                    Cell(row, 2),
                    Cell(row, 4),
                    Cell(row, 6),
                    Cell(row, 7),
                    Cell(row, 9),
                    Cell(row, 11),
                    row);
            }
        }

        return result;
    }

    private static Dictionary<string, ClientWeaponMeta> LoadWeaponMetadata(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries)
    {
        var result = new Dictionary<string, ClientWeaponMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in ReadRows(reader, entries, "life/new_wuqi/new_weapon_id.txt"))
        {
            string id = Cell(row, 1);
            if (!int.TryParse(id, out _))
            {
                continue;
            }

            result[id] = new ClientWeaponMeta(
                id,
                Cell(row, 0),
                ParseInt(Cell(row, 2)),
                ParseInt(Cell(row, 3)) ?? LevelsPerRank);
        }

        return result;
    }

    private static Dictionary<string, string> LoadSimpleMap(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        string path,
        int keyColumn,
        int valueColumn)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in ReadRows(reader, entries, path))
        {
            string key = Cell(row, keyColumn);
            string value = Cell(row, valueColumn);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, string[]> LoadRowsById(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        string path,
        int idColumn)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in ReadRows(reader, entries, path))
        {
            string id = Cell(row, idColumn);
            if (int.TryParse(id, out _))
            {
                result[id] = row;
            }
        }

        return result;
    }

    private static IReadOnlyList<string[]> ReadRows(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        string path)
    {
        if (!entries.TryGetValue(NormalizePath(path), out DpkEntry? entry))
        {
            return Array.Empty<string[]>();
        }

        string text = Encoding.GetEncoding("GB18030")
            .GetString(reader.Extract(entry))
            .Trim('\0', '\uFEFF');
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('\t'))
            .ToArray();
    }

    private static List<GameWeaponEnhancementCost> ParseEnhancementCosts(
        IReadOnlyList<string[]> rows,
        IReadOnlyDictionary<int, string> rankNames,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver,
        IDictionary<string, GameMaterialIcon> materialIcons)
    {
        var result = new List<GameWeaponEnhancementCost>();
        for (int index = 0; index < rows.Count; index++)
        {
            string[] row = rows[index];
            int rankIndex = index / LevelsPerRank;
            int level = index % LevelsPerRank + 1;
            ClientMaterialRequirement[] requirements = new[] { Cell(row, 3), Cell(row, 4) }
                .Select(value => ParseMaterialRequirement(value, items))
                .Where(requirement => requirement is not null)
                .Cast<ClientMaterialRequirement>()
                .ToArray();
            string[] materialIds = requirements
                .SelectMany(requirement => requirement.ItemIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ClientItemRow? materialItem = requirements.FirstOrDefault()?.Item;
            string materialName = string.Join(" + ", requirements.Select(requirement => requirement.Name));
            string materialText = string.Join(" + ", requirements.Select(requirement => requirement.DisplayText));

            GameMaterialIcon? materialIcon = null;
            if (materialItem is not null)
            {
                string? imagePath = iconResolver?.Resolve(materialItem.IconName);
                foreach (string materialId in materialIds)
                {
                    if (!items.TryGetValue(materialId, out ClientItemRow? variant))
                    {
                        continue;
                    }

                    var icon = new GameMaterialIcon(
                        materialId,
                        variant.Name,
                        iconResolver?.Resolve(variant.IconName) ?? imagePath,
                        "mb.dpk · item/item_list*.txt");
                    materialIcons[materialId] = icon;
                    materialIcon ??= icon;
                }
            }

            result.Add(new GameWeaponEnhancementCost(
                index,
                rankIndex,
                rankNames.GetValueOrDefault(rankIndex, string.Empty),
                level,
                materialIds,
                materialName,
                materialText,
                materialIcon,
                FormatBoundGold(Cell(row, 5)),
                ParseInt(Cell(row, 6))));
        }

        return result;
    }

    private static ClientMaterialRequirement? ParseMaterialRequirement(
        string value,
        IReadOnlyDictionary<string, ClientItemRow> items)
    {
        string[] parts = value.Split(
            '*',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || ParseInt(parts[0]) is not int count || count <= 0)
        {
            return null;
        }

        string[] ids = parts.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ClientItemRow? item = ids
            .Select(id => items.GetValueOrDefault(id))
            .FirstOrDefault(candidate => candidate is not null);
        if (item is null)
        {
            return null;
        }

        string name = NormalizeMaterialName(item.Name);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new ClientMaterialRequirement(ids, item, name, $"{name} ×{count}");
    }

    private static Dictionary<string, IReadOnlyList<GameWeaponEnhancementRow>> ParseEnhancementRows(
        IReadOnlyDictionary<string, string[]> mainRows,
        IReadOnlyDictionary<string, string[]> newestRows,
        IReadOnlyDictionary<string, ClientWeaponMeta> metadata,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyList<ClientSoulCapClause> soulCapClauses,
        IDictionary<int, string> rankNames,
        IReadOnlyList<GameWeaponEnhancementCost> costs,
        IReadOnlyDictionary<string, string> traits)
    {
        var result = new Dictionary<string, IReadOnlyList<GameWeaponEnhancementRow>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, string[] sourceRow) in mainRows)
        {
            var rows = new List<GameWeaponEnhancementRow>();
            for (int column = 2; column < sourceRow.Length; column++)
            {
                string traitList = Cell(sourceRow, column);
                if (string.IsNullOrWhiteSpace(traitList))
                {
                    continue;
                }

                int stepIndex = column - 2;
                GameWeaponEnhancementCost? cost = stepIndex < costs.Count ? costs[stepIndex] : null;
                int rankIndex = cost?.RankIndex ?? stepIndex / LevelsPerRank;
                int level = cost?.Level ?? stepIndex % LevelsPerRank + 1;
                IReadOnlyList<string> resolvedTraits = ResolveTraits(traitList, traits);
                if (resolvedTraits.Count == 0)
                {
                    continue;
                }

                rankNames.TryGetValue(rankIndex, out string? rankName);
                rows.Add(new GameWeaponEnhancementRow(
                    stepIndex,
                    rankIndex,
                    rankName ?? string.Empty,
                    level,
                    resolvedTraits));
            }

            if (newestRows.TryGetValue(id, out string[]? newestRow))
            {
                ClientWeaponMeta? meta = FindWeaponMetadataByIdOrName(id, items, metadata);
                int firstNewStepIndex = rows.Count == 0 ? 0 : rows.Max(row => row.StepIndex) + 1;
                int newestRankIndex = firstNewStepIndex < costs.Count
                    ? costs[firstNewStepIndex].RankIndex
                    : firstNewStepIndex / LevelsPerRank;
                string newestRankName = items.TryGetValue(id, out ClientItemRow? item)
                    ? FindSoulCapRankName(item.Name, soulCapClauses)
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(newestRankName) && meta?.CapRankIndex is int capRankIndex)
                {
                    rankNames[capRankIndex] = newestRankName;
                }

                for (int column = 2; column < newestRow.Length; column++)
                {
                    string traitList = Cell(newestRow, column);
                    int stepIndex = firstNewStepIndex + column - 2;
                    GameWeaponEnhancementCost? cost = stepIndex < costs.Count ? costs[stepIndex] : null;
                    int rankIndex = cost?.RankIndex ?? stepIndex / LevelsPerRank;
                    int level = cost?.Level ?? stepIndex % LevelsPerRank + 1;
                    if (string.IsNullOrWhiteSpace(traitList) ||
                        rows.Any(row => row.StepIndex == stepIndex))
                    {
                        continue;
                    }

                    IReadOnlyList<string> resolvedTraits = ResolveTraits(traitList, traits);
                    if (resolvedTraits.Count == 0)
                    {
                        continue;
                    }

                    string resolvedRankName = rankNames.TryGetValue(rankIndex, out string? knownRankName)
                        ? knownRankName
                        : newestRankName;
                    rows.Add(new GameWeaponEnhancementRow(
                        stepIndex,
                        rankIndex,
                        resolvedRankName,
                        level,
                        resolvedTraits));
                }
            }

            if (rows.Count > 0)
            {
                result[id] = rows.OrderBy(row => row.StepIndex).ToArray();
            }
        }

        foreach ((string id, string[] newestRow) in newestRows.Where(pair => !result.ContainsKey(pair.Key)))
        {
            ClientWeaponMeta? meta = FindWeaponMetadataByIdOrName(id, items, metadata);
            int rankIndex = meta?.CapRankIndex ?? 0;
            string rankName = items.TryGetValue(id, out ClientItemRow? item)
                ? FindSoulCapRankName(item.Name, soulCapClauses)
                : string.Empty;
            var rows = new List<GameWeaponEnhancementRow>();
            for (int column = 2; column < newestRow.Length; column++)
            {
                IReadOnlyList<string> resolvedTraits = ResolveTraits(Cell(newestRow, column), traits);
                if (resolvedTraits.Count > 0)
                {
                    int stepIndex = rankIndex * LevelsPerRank + column - 2;
                    int level = stepIndex % LevelsPerRank + 1;
                    rows.Add(new GameWeaponEnhancementRow(
                        stepIndex,
                        stepIndex / LevelsPerRank,
                        rankNames.TryGetValue(stepIndex / LevelsPerRank, out string? knownRankName)
                            ? knownRankName
                            : rankName,
                        level,
                        resolvedTraits));
                }
            }

            if (rows.Count > 0)
            {
                result[id] = rows;
            }
        }

        return result;
    }

    private static GameItemTooltip CreateWeaponTooltip(
        ClientItemRow item,
        IReadOnlyDictionary<string, string> traits,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        var baseRows = new List<GameInfoDetailRow>();
        AddBaseStat(baseRows, item.Fields, 19, "伤害力");
        AddBaseStat(baseRows, item.Fields, 20, "防御力");
        AddBaseStat(baseRows, item.Fields, 21, "体魄");
        AddBaseStat(baseRows, item.Fields, 22, "力量");
        AddBaseStat(baseRows, item.Fields, 23, "筋骨");
        AddBaseStat(baseRows, item.Fields, 24, "元神");
        AddBaseStat(baseRows, item.Fields, 53, "法术抗性");

        string[] equipRows = Enumerable.Range(25, 5)
            .Select(index => traits.GetValueOrDefault(Cell(item.Fields, index), string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string descriptionRaw = texts.GetValueOrDefault(item.DescriptionTextId, string.Empty);
        string description = string.Concat(GameRichTextParser.Parse(descriptionRaw).Select(span => span.Text))
            .Replace("\\n", "\n", StringComparison.Ordinal);
        (_, string[] professions) = ResolveProfession(item.ProfessionCode);
        var flags = new List<string>();
        if (!string.IsNullOrWhiteSpace(Cell(item.Fields, 37)))
        {
            flags.Add("不可卖店");
        }
        flags.Add("可加锁");

        return new GameItemTooltip(
            iconResolver?.Resolve(item.IconName),
            item.Name,
            "武器",
            "武魂未激活",
            string.Empty,
            professions,
            item.Level,
            !string.IsNullOrWhiteSpace(Cell(item.Fields, 42)),
            ResolveClientQuality(item.QualityCode),
            baseRows,
            equipRows,
            Array.Empty<string>(),
            Array.Empty<string>(),
            flags,
            description,
            descriptionRaw,
            traits.GetValueOrDefault(Cell(item.Fields, 30), string.Empty),
            Cell(item.Fields, 18),
            ParseLong(Cell(item.Fields, 17))?.ToString() ?? "0",
            "鼠标单击使用");
    }

    private static void AddBaseStat(List<GameInfoDetailRow> rows, string[] fields, int index, string label)
    {
        if (ParseLong(Cell(fields, index)) is not long value || value == 0)
        {
            return;
        }

        rows.Add(new GameInfoDetailRow(label, value > 0 ? $"+{value}" : value.ToString()));
    }

    private static string ResolveClientQuality(string? qualityCode) => qualityCode switch
    {
        "14" => "red",
        "13" => "#FFDA44FF",
        _ => "normal"
    };

    private static IReadOnlyList<string> ResolveTraits(
        string value,
        IReadOnlyDictionary<string, string> traits)
    {
        return value
            .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => traits.GetValueOrDefault(id, string.Empty))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void DiscoverCostRankNames(
        IDictionary<int, string> rankNames,
        IReadOnlyList<string[]> costs,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> texts)
    {
        foreach (IGrouping<int, (string[] Row, int Index)> group in costs
                     .Select((row, index) => (Row: row, Index: index))
                     .GroupBy(item => item.Index / LevelsPerRank))
        {
            string[] materialIds = group
                .SelectMany(item => new[] { Cell(item.Row, 3), Cell(item.Row, 4) })
                .SelectMany(value => value.Split(
                    '*',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string rankName = materialIds
                .Select(id => items.GetValueOrDefault(id))
                .Where(item => item is not null)
                .Select(item => texts.GetValueOrDefault(item!.DescriptionTextId, string.Empty))
                .Select(ExtractRankName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
            if (group.Key >= 3 && !string.IsNullOrWhiteSpace(rankName))
            {
                rankNames[group.Key] = rankName;
            }
        }
    }

    private static string ExtractRankName(string text)
    {
        Match match = Regex.Match(
            text,
            @"武魂(?<rank>[\p{IsCJKUnifiedIdeographs}]{1,2}品)等级|的(?<rank>[\p{IsCJKUnifiedIdeographs}]{1,2}品)等级|最高可强化至(?<rank>[\p{IsCJKUnifiedIdeographs}]{1,2}品)十阶");
        return match.Success ? match.Groups["rank"].Value : string.Empty;
    }

    private static IReadOnlyList<ClientSoulCapClause> ParseSoulCapClauses(IEnumerable<string> texts)
    {
        var result = new List<ClientSoulCapClause>();
        var expression = new Regex(
            @"(?<weapons>[^【\\n]{1,700})【最高可附魔(?<rank>[^】\\n]{1,8}品)十阶武魂】",
            RegexOptions.CultureInvariant);
        foreach (string text in texts.Where(value => value.Contains("最高可附魔", StringComparison.Ordinal)))
        {
            foreach (Match match in expression.Matches(text))
            {
                result.Add(new ClientSoulCapClause(
                    match.Groups["weapons"].Value,
                    match.Groups["rank"].Value));
            }
        }

        return result;
    }

    private static string FindSoulCapRankName(
        string weaponName,
        IReadOnlyList<ClientSoulCapClause> clauses)
    {
        string lookupName = NormalizeWeaponFamilyName(weaponName);
        if (string.IsNullOrWhiteSpace(lookupName))
        {
            return string.Empty;
        }

        return clauses
            .LastOrDefault(clause => clause.WeaponNames.Contains(lookupName, StringComparison.Ordinal))?
            .RankName ?? string.Empty;
    }

    private static ClientWeaponMeta? FindWeaponMetadata(
        ClientItemRow item,
        IReadOnlyDictionary<string, ClientWeaponMeta> metadata)
    {
        return metadata.GetValueOrDefault(item.Id) ?? metadata.Values.FirstOrDefault(meta =>
            NormalizeWeaponName(meta.Name).Equals(NormalizeWeaponName(item.Name), StringComparison.Ordinal));
    }

    private static ClientWeaponMeta? FindWeaponMetadataByIdOrName(
        string id,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, ClientWeaponMeta> metadata)
    {
        if (metadata.TryGetValue(id, out ClientWeaponMeta? direct))
        {
            return direct;
        }

        return items.TryGetValue(id, out ClientItemRow? item) ? FindWeaponMetadata(item, metadata) : null;
    }

    private static (string Group, string[] Professions) ResolveProfession(string code)
    {
        return ProfessionByCode.TryGetValue(code, out var result)
            ? result
            : ("其他职业", Array.Empty<string>());
    }

    private static string NormalizeWeaponName(string value)
    {
        return value
            .Replace("（绑定）", string.Empty, StringComparison.Ordinal)
            .Replace("(绑定)", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeWeaponFamilyName(string value)
    {
        string result = NormalizeWeaponName(value)
            .Replace("·极", string.Empty, StringComparison.Ordinal);
        int separator = result.LastIndexOf('之');
        return separator >= 0 && separator + 1 < result.Length ? result[(separator + 1)..] : result;
    }

    private static string NormalizeMaterialName(string value)
    {
        return Regex.Replace(value, @"[（(](?:绑定|限时)[^）)]*[）)]", string.Empty).Trim();
    }

    private static string FormatBoundGold(string value)
    {
        if (ParseLong(value) is not long raw || raw <= 0)
        {
            return string.Empty;
        }

        return raw % 1_000_000 == 0 ? $"{raw / 1_000_000}砖" : raw.ToString();
    }

    private static bool TryResolveClientPaths(
        string selectedPath,
        out string resourceFolder,
        out string mbPath)
    {
        resourceFolder = string.Empty;
        mbPath = string.Empty;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(selectedPath);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        string directGui = Path.Combine(fullPath, "gui.dpk");
        string nestedRes = Path.Combine(fullPath, "res");
        if (File.Exists(directGui))
        {
            resourceFolder = fullPath;
        }
        else if (File.Exists(Path.Combine(nestedRes, "gui.dpk")))
        {
            resourceFolder = nestedRes;
        }
        else
        {
            return false;
        }

        string? clientRoot = Directory.GetParent(resourceFolder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))?.FullName;
        mbPath = clientRoot is null ? string.Empty : Path.Combine(clientRoot, "mb.dpk");
        return File.Exists(mbPath);
    }

    private static string Cell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

    private static int? ParseInt(string value) => int.TryParse(value, out int result) ? result : null;

    private static long? ParseLong(string value) => long.TryParse(value, out long result) ? result : null;

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private sealed record ClientItemRow(
        string Id,
        string Name,
        string TypeCode,
        string IconName,
        string DescriptionTextId,
        string QualityCode,
        string ProfessionCode,
        string Level,
        string[] Fields);

    private sealed record ClientWeaponMeta(
        string Id,
        string Name,
        int? CapRankIndex,
        int LevelsPerRank);

    private sealed record ClientSoulCapClause(string WeaponNames, string RankName);

    private sealed record ClientMaterialRequirement(
        IReadOnlyList<string> ItemIds,
        ClientItemRow Item,
        string Name,
        string DisplayText);

    private sealed record ClientWashCandidateRef(
        string Id,
        int Kind,
        long Weight);

    private sealed class ClientIconResolver : IDisposable
    {
        private readonly DpkReader _reader;
        private readonly IReadOnlyDictionary<string, DpkEntry> _entryByFileName;
        private readonly IReadOnlyDictionary<string, DpkEntry> _entryByPath;
        private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _cacheFolder;

        private ClientIconResolver(string guiPath)
        {
            _reader = new DpkReader(guiPath);
            DpkEntry[] allEntries = _reader.ReadEntries().ToArray();
            DpkEntry[] pngEntries = allEntries
                .Where(entry => entry.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _entryByFileName = pngEntries
                .GroupBy(entry => Path.GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(entry => entry.Path.Contains("icon/item/", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
            _entryByPath = allEntries.ToDictionary(
                entry => NormalizePath(entry.Path),
                entry => entry,
                StringComparer.OrdinalIgnoreCase);

            FileInfo archive = new(guiPath);
            string archiveVersion = $"{archive.Length:X}-{archive.LastWriteTimeUtc.Ticks:X}";
            _cacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XunxianDpkViewer",
                "GameInfoCache",
                archiveVersion);
            Directory.CreateDirectory(_cacheFolder);
        }

        public static ClientIconResolver? TryCreate(string resourceFolder)
        {
            string guiPath = Path.Combine(resourceFolder, "gui.dpk");
            return File.Exists(guiPath) ? new ClientIconResolver(guiPath) : null;
        }

        public string? Resolve(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return null;
            }

            if (_resolved.TryGetValue(iconName, out string? cached))
            {
                return cached;
            }

            string fileName = Path.GetFileNameWithoutExtension(iconName) + ".png";
            if (!_entryByFileName.TryGetValue(fileName, out DpkEntry? entry))
            {
                _resolved[iconName] = null;
                return null;
            }

            return ResolveEntry(iconName, entry);
        }

        public string? ResolvePath(string path)
        {
            string normalizedPath = NormalizePath(path);
            string cacheKey = $"path:{normalizedPath}";
            if (_resolved.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }

            if (!_entryByPath.TryGetValue(normalizedPath, out DpkEntry? entry))
            {
                _resolved[cacheKey] = null;
                return null;
            }

            return ResolveEntry(cacheKey, entry);
        }

        public string? ResolveStateIcon(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return null;
            }

            string normalizedName = NormalizePath(iconName);
            string fileName = Path.GetFileNameWithoutExtension(normalizedName);
            if (normalizedName.StartsWith("icon/state/", StringComparison.OrdinalIgnoreCase))
            {
                return ResolvePath(normalizedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? normalizedName
                    : normalizedName + ".png");
            }

            return ResolvePath($"icon/state/{fileName}.png");
        }

        public string? ResolveClientText(string path, string key)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string cacheKey = $"text:{NormalizePath(path)}:{key}";
            if (_resolved.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }

            if (!_entryByPath.TryGetValue(NormalizePath(path), out DpkEntry? entry))
            {
                _resolved[cacheKey] = null;
                return null;
            }

            try
            {
                string xml = Encoding.UTF8.GetString(_reader.Extract(entry)).Trim('\0', '\uFEFF');
                string? value = XDocument.Parse(xml)
                    .Descendants("string")
                    .FirstOrDefault(element =>
                        string.Equals(element.Attribute("name")?.Value, key, StringComparison.Ordinal))
                    ?.Attribute("value")?.Value;
                value = value is null ? null : WebUtility.HtmlDecode(value);
                _resolved[cacheKey] = value;
                return value;
            }
            catch
            {
                _resolved[cacheKey] = null;
                return null;
            }
        }

        private string ResolveEntry(string cacheKey, DpkEntry entry)
        {
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Path)))[..12];
            string targetPath = Path.Combine(
                _cacheFolder,
                $"{Path.GetFileNameWithoutExtension(entry.Path)}-{digest}.png");
            if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
            {
                string temporaryPath = targetPath + ".tmp";
                File.WriteAllBytes(temporaryPath, _reader.Extract(entry));
                File.Move(temporaryPath, targetPath, overwrite: true);
            }

            _resolved[cacheKey] = targetPath;
            return targetPath;
        }

        public void Dispose() => _reader.Dispose();
    }
}
