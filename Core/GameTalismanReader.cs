using System.Globalization;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static partial class GameClientInfoReader
{
    private static GameTalismanCatalog ParseTalismans(
        DpkReader reader,
        IReadOnlyDictionary<string, DpkEntry> entries,
        IReadOnlyDictionary<string, ClientItemRow> items,
        IReadOnlyDictionary<string, string> traits,
        IReadOnlyDictionary<string, string> texts,
        ClientIconResolver? iconResolver)
    {
        Dictionary<string, string> professions = LoadSimpleMap(
            reader, entries, "object/init_profession.txt", keyColumn: 1, valueColumn: 0);
        Dictionary<string, string[]> metadata = LoadRowsById(
            reader, entries, "skill/talisman_info.txt", idColumn: 4);
        Dictionary<string, string[]> additions = LoadRowsById(
            reader, entries, "skill/talisman_addition.txt", idColumn: 1);
        Dictionary<string, string[]> growthClasses = LoadRowsById(reader, entries, "skill/talisman_class.txt", 0);
        Dictionary<string, string[]> trees = LoadRowsById(reader, entries, "skill/talent_tree.txt", 1);
        Dictionary<string, string[]> talentGroups = LoadRowsById(reader, entries, "skill/talent_group.txt", 1);
        Dictionary<string, string> qualityColors = LoadSimpleMap(reader, entries, "etc/define.txt", 0, 1);
        Dictionary<string, string> appearanceNames = LoadSimpleMap(reader, entries, "etc/menu/menu_item.txt", 1, 0);
        string name = LoadSimpleMap(reader, entries, "item/item_type.txt", 0, 1)
            .GetValueOrDefault("101", string.Empty);
        string rankSuffix = iconResolver?.ResolveClientText("string/gb/hwhtool.xml", "163", isolatedEntry: true) ?? string.Empty;
        var parsedTrees = new Dictionary<string, IReadOnlyList<GameTalismanTalent>>(StringComparer.Ordinal);
        var records = new List<GameInfoRecord>();

        foreach (ClientItemRow item in items.Values.Where(item => item.TypeCode == "101")
                     .OrderByDescending(item => ParseInt(item.Level) ?? 0)
                     .ThenBy(item => ParseInt(item.Id) ?? 0))
        {
            metadata.TryGetValue(item.Id, out string[]? meta);
            int level = ParseInt(item.Level) ?? (meta is null ? 0 : ParseInt(Cell(meta, 1)) ?? 0);
            int professionId = ParseInt(Cell(item.Fields, 10)) ??
                               (meta is null ? 0 : ParseInt(Cell(meta, 3)) ?? 0);
            string profession = professions.GetValueOrDefault(professionId.ToString(CultureInfo.InvariantCulture), string.Empty);
            string shortProfession = iconResolver?.ResolveClientText("string/gb/career.xml", $"name_{professionId}", isolatedEntry: true)
                                     ?? profession;
            // Item column 60 is the displayed rank; column 8 and talisman_info use internal ranks.
            int displayRank = ParseInt(Cell(item.Fields, 60)) ?? 0;
            string rankText = displayRank > 0 && !string.IsNullOrWhiteSpace(rankSuffix)
                ? string.Concat(TalismanChineseNumber(displayRank), rankSuffix)
                : name;

            var baseRows = new List<GameInfoDetailRow>();
            AddBaseStat(baseRows, item.Fields, 19, "伤害力");
            AddBaseStat(baseRows, item.Fields, 20, "防御力");
            AddBaseStat(baseRows, item.Fields, 22, "力量");
            AddBaseStat(baseRows, item.Fields, 21, "体魄");
            AddBaseStat(baseRows, item.Fields, 23, "筋骨");
            AddBaseStat(baseRows, item.Fields, 24, "元神");
            AddBaseStat(baseRows, item.Fields, 53, "法术抗性");
            string[] equipRows = Enumerable.Range(25, 5)
                .SelectMany(index => ResolveTraits(Cell(item.Fields, index), traits))
                .Select(value => value == "生命恢复+5" ? "生命恢复提高5点" : value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string descriptionRaw = texts.GetValueOrDefault(item.DescriptionTextId, string.Empty);
            string description = string.Concat(GameRichTextParser.Parse(descriptionRaw).Select(span => span.Text))
                .Replace("\\n", "\n", StringComparison.Ordinal);

            var refinementRows = new List<string>();
            if (additions.TryGetValue(Cell(item.Fields, 48), out string[]? addition))
            {
                for (int column = 6; column < addition.Length; column += 5)
                {
                    string value = Cell(addition, column);
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    {
                        value = value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                    }
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        refinementRows.Add(value);
                    }
                }
            }

            var flags = new List<string>();
            if (ParseInt(Cell(item.Fields, 37)) is > 0)
            {
                flags.Add("不可卖店");
            }
            flags.Add("可加锁");
            var tooltip = new GameItemTooltip(
                iconResolver?.Resolve(item.IconName),
                item.Name,
                name,
                string.Empty,
                string.Empty,
                string.IsNullOrWhiteSpace(shortProfession) ? Array.Empty<string>() : new[] { shortProfession },
                level > 0 ? level.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ParseInt(Cell(item.Fields, 42)) is > 0,
                qualityColors.TryGetValue(((ParseInt(item.QualityCode) ?? 0) + 8).ToString(CultureInfo.InvariantCulture), out string? color)
                    ? $"#FF{color}" : "normal",
                baseRows,
                equipRows,
                Array.Empty<string>(),
                Array.Empty<string>(),
                flags,
                description,
                descriptionRaw,
                string.Empty,
                Cell(item.Fields, 18),
                ParseLong(Cell(item.Fields, 17))?.ToString(CultureInfo.InvariantCulture) ?? "0",
                iconResolver?.ResolveClientText("string/gb/tip.xml", "cr_use_skill", isolatedEntry: true) ?? string.Empty)
            {
                ClientTypeText = rankText
            };
            growthClasses.TryGetValue(Cell(item.Fields, 8), out string[]? growthClass);
            string treeId = Cell(item.Fields, 46);
            if (!parsedTrees.TryGetValue(treeId, out IReadOnlyList<GameTalismanTalent>? talents))
            {
                talents = trees.TryGetValue(treeId, out string[]? tree)
                    ? Cell(tree, 2).Split('*', StringSplitOptions.RemoveEmptyEntries)
                        .Where(talentGroups.ContainsKey)
                        .Select(id => ParseTalismanTalent(talentGroups[id], iconResolver)).ToArray()
                    : Array.Empty<GameTalismanTalent>();
                parsedTrees[treeId] = talents;
            }
            int maxGrowth = growthClass is null ? 0 : Math.Max(ParseInt(Cell(growthClass, 1)) ?? 0,
                (ParseInt(item.QualityCode) ?? 0) >= 7 ? ParseInt(Cell(growthClass, 5)) ?? 0 : 0);
            records.Add(new GameInfoRecord(
                item.Id, item.Name, tooltip.IconPath, profession, string.Empty, tooltip)
            {
                Talisman = new GameTalismanInfo(
                    level, displayRank, rankText, professionId, profession, refinementRows)
                {
                    MaxGrowth = maxGrowth,
                    InternalRank = ParseInt(Cell(item.Fields, 8)) ?? 0,
                    MaxProficiency = growthClass is null ? 0 : ParseInt(Cell(growthClass, 2)) ?? 0,
                    SpiritConversionRate = growthClass is null ? 0 : ParseInt(Cell(growthClass, 3)) ?? 0,
                    TalentTreeId = ParseInt(treeId) ?? 0,
                    CoinIconPath = iconResolver?.ResolvePath("image/item/money0.png"),
                    Talents = talents
                }
            });
        }

        return new GameTalismanCatalog(
            records,
            name,
            iconResolver?.ResolveClientText("string/gb/hwhtool.xml", "evo:all2evo", stopAtMatch: true) ?? string.Empty,
            iconResolver?.ResolveClientText("string/gb/prop.xml", "level_title") ?? string.Empty,
            iconResolver?.ResolveClientText("string/gb/prop.xml", "career_title") ?? string.Empty,
            iconResolver?.ResolveClientText("string/gb/hwh.xml", "talisman_title") ?? string.Empty)
        {
            ClientText = new Dictionary<string, string>
            {
                ["title"] = Text("text", "h2"),
                ["rank"] = Text("text", "h3"),
                ["growth"] = Text("text", "h4"),
                ["points"] = Text("text", "h5"),
                ["reserve"] = Text("text", "h6"),
                ["conversion"] = Text("text", "h7"),
                ["proficiency"] = Text("text", "h8"),
                ["level"] = Text("text", "h19"),
                ["learn"] = Text("text", "h20"),
                ["bonus"] = Text("text", "h21"),
                ["effect"] = Text("text", "h22"),
                ["requirePoints"] = Text("text", "h61"),
                ["requireGrowth"] = Text("text", "h62"),
                ["talents"] = Text("hwhtool", "161"),
                ["activated"] = Text("hwh", "talisman1"),
                ["reset"] = Text("hwh", "talent_reset"),
                ["novice"] = Text("hwh", "talent_new")
            },
            AppearanceNames = Enumerable.Range(138, 15)
                .Select(id => appearanceNames.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture), string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            Images = new[]
                {
                    "image/talent_frm/t1.png", "image/talent_frm/t2.png",
                    "image/talent_frm/t3.png", "image/talent_frm/t4.png",
                    "image/cmn/frm_lace_0.png", "image/cmn/frm_lace_2.png", "image/cmn/frm_lace_3.png",
                    "image/cmn/frm_card.png", "image/cmn/frm_border_head.png",
                    "image/cmn/frm_border_foot.png", "image/cmn/frm_border_inner.png",
                    "image/item/money0.png", "image/common/btn_common.png", "image/cmn/btn_close.png",
                    "image/cmn/pic_background.png", "image/cmn/pic_border_bottom.png", "image/cmn/pic_title_hi.png",
                    "image/cmn/pic_card.png", "image/cmn/frm_edit.png", "image/map/image_server.png",
                    "image/cmn/frm_progress.png", "image/cmn/pic_progres.png", "image/cmn/btn_dropdown.png",
                    "image/cmn/btn_plus.png", "image/cmn/btn_up.png", "image/cmn/btn_down.png",
                    "image/cmn/btn_scroll.png", "image/cmn/frm_slider.png"
                }
                .Select(path => (Path: path, Resolved: iconResolver?.ResolvePath(path)))
                .Where(asset => asset.Resolved is not null)
                .ToDictionary(asset => asset.Path, asset => asset.Resolved!)
        };

        string Text(string file, string key) =>
            iconResolver?.ResolveClientText($"string/gb/{file}.xml", key, isolatedEntry: true) ?? string.Empty;
    }

    private static GameTalismanTalent ParseTalismanTalent(string[] row, ClientIconResolver? icons)
    {
        int[] position = Cell(row, 5).Split('*').Select(value => ParseInt(value) ?? 0).ToArray();
        return new GameTalismanTalent(
            ParseInt(Cell(row, 1)) ?? 0, Cell(row, 0), icons?.ResolvePath($"icon/talent/{Cell(row, 4)}.png"),
            position.ElementAtOrDefault(0), position.ElementAtOrDefault(1), Math.Max(1, ParseInt(Cell(row, 6)) ?? 1),
            ParseInt(Cell(row, 9)) ?? 0, ParseInt(Cell(row, 10)) ?? 0,
            Cell(row, 7).Split('*', StringSplitOptions.RemoveEmptyEntries).Select(value => ParseInt(value) ?? 0).ToArray(),
            Cell(row, 14), Cell(row, 16), Cell(row, 17).Split('*'), Cell(row, 18));
    }

    private static string TalismanChineseNumber(int number)
    {
        const string digits = "零一二三四五六七八九";
        if (number < 10)
        {
            return digits[Math.Max(number, 0)].ToString();
        }
        if (number < 100)
        {
            return (number < 20 ? string.Empty : digits[number / 10].ToString()) + "十" +
                   (number % 10 == 0 ? string.Empty : digits[number % 10].ToString());
        }
        return number.ToString(CultureInfo.InvariantCulture);
    }
}
