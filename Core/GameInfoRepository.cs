namespace XunxianDpkViewer.Core;

public sealed class GameInfoRepository
{
    private readonly string? _resourceFolder;
    private readonly Dictionary<string, GameItemTooltip> _tooltips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameMaterialIcon> _materialIcons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<GameInfoCategory> _categories = Array.Empty<GameInfoCategory>();

    public IReadOnlyList<GameInfoCategory> Categories => _categories;
    public GameWeaponEnhancementCatalog WeaponEnhancement { get; private set; } = GameWeaponEnhancementCatalog.Empty;
    public GameEquipmentWashCatalog EquipmentWash { get; private set; } = GameEquipmentWashCatalog.Empty;
    public GameEquipmentEnhancementCatalog EquipmentEnhancement { get; private set; } = GameEquipmentEnhancementCatalog.Empty;
    public GameEquipmentUpgradeCatalog EquipmentUpgrades { get; private set; } = GameEquipmentUpgradeCatalog.Empty;
    public GameYinYangCatalog YinYang { get; private set; } = GameYinYangCatalog.Empty;
    public GameWanxiangCatalog Wanxiang { get; private set; } = GameWanxiangCatalog.Empty;

    private GameInfoRepository(string? resourceFolder)
    {
        _resourceFolder = resourceFolder;
    }

    public Task<IReadOnlyList<GameInfoCategory>> LoadAsync()
    {
        _tooltips.Clear();
        _materialIcons.Clear();
        var categories = new List<GameInfoCategory>();
        GameClientInfoSnapshot? clientSnapshot = string.IsNullOrWhiteSpace(_resourceFolder)
            ? null
            : GameClientInfoReader.Load(_resourceFolder);
        if (clientSnapshot is null)
        {
            WeaponEnhancement = GameWeaponEnhancementCatalog.Empty;
            EquipmentWash = GameEquipmentWashCatalog.Empty;
            EquipmentEnhancement = GameEquipmentEnhancementCatalog.Empty;
            EquipmentUpgrades = GameEquipmentUpgradeCatalog.Empty;
            YinYang = GameYinYangCatalog.Empty;
            Wanxiang = GameWanxiangCatalog.Empty;
            _categories = Array.Empty<GameInfoCategory>();
            return Task.FromResult(_categories);
        }

        foreach ((string id, GameItemTooltip tooltip) in clientSnapshot.Tooltips)
        {
            _tooltips[id] = tooltip;
        }

        foreach ((string id, GameMaterialIcon icon) in clientSnapshot.MaterialIcons)
        {
            _materialIcons[id] = icon;
        }

        WeaponEnhancement = clientSnapshot.WeaponEnhancement;
        EquipmentWash = clientSnapshot.EquipmentWash;
        EquipmentEnhancement = clientSnapshot.EquipmentEnhancement;
        EquipmentUpgrades = clientSnapshot.EquipmentUpgrades;
        YinYang = clientSnapshot.YinYang;
        Wanxiang = clientSnapshot.Wanxiang;

        IReadOnlyList<GameInfoStage> weaponStages = BuildClientWeaponStages(clientSnapshot.Weapons);
        if (weaponStages.Count > 0)
        {
            categories.Add(new GameInfoCategory(
                "weapon",
                "武器",
                string.Empty,
                "\uE7FC",
                weaponStages));
        }

        IReadOnlyList<GameInfoStage> equipmentStages = BuildClientEquipmentStages(clientSnapshot.Equipment);
        if (equipmentStages.Count > 0)
        {
            categories.Add(new GameInfoCategory(
                "equipment",
                "装备",
                string.Empty,
                "\uE7BC",
                equipmentStages));
        }

        IReadOnlyList<GameInfoStage> yinyangStages = clientSnapshot.YinYang.Jades.Count == 0
            ? Array.Empty<GameInfoStage>()
            : new[]
            {
                new GameInfoStage(
                    "yinyang",
                    "阴阳玉",
                    string.Empty,
                    string.Empty,
                    clientSnapshot.YinYang.Jades.Select(jade => jade.Record).ToArray())
            };
        if (yinyangStages.Count > 0)
        {
            categories.Add(new GameInfoCategory(
                "yinyang",
                "阴阳玉",
                string.Empty,
                "\uE9F5",
                yinyangStages));
        }

        IReadOnlyList<GameInfoStage> wanxiangStages = clientSnapshot.Wanxiang.Plates.Count == 0
            ? Array.Empty<GameInfoStage>()
            : new[]
            {
                new GameInfoStage(
                    "wanxiang",
                    "万象宝盘",
                    string.Empty,
                    string.Empty,
                    clientSnapshot.Wanxiang.Plates)
            };
        if (wanxiangStages.Count > 0)
        {
            categories.Add(new GameInfoCategory(
                "wanxiang",
                "万象宝盘",
                string.Empty,
                "\uE734",
                wanxiangStages));
        }

        _categories = categories;
        return Task.FromResult(_categories);
    }

    private static IReadOnlyList<GameInfoStage> BuildClientEquipmentStages(
        IReadOnlyList<GameInfoRecord> equipment)
    {
        (string Id, string Name, string Series, bool? LingOnly)[] stages =
        {
            ("fenglin", "凤麟装备", "凤麟", false),
            ("fenglin-ling", "凤麟灵装备", "凤麟", true),
            ("yuling", "羽灵装备", "羽灵", null),
            ("lianhua", "莲华装备", "莲华", null),
            ("fenglan", "风岚装备", "风岚", null),
            ("mingzhou", "明昼装备", "明昼", null),
            ("yufeng", "玉峰装备", "玉峰", null),
            ("taiyu", "泰宇装备", "泰宇", null),
            ("guanshi", "冠世装备", "冠世", null),
            ("dice", "帝策装备", "帝策", null),
            ("yingxuan", "影轩装备", "影轩", null),
            ("chanyou", "阐幽装备", "阐幽", null),
            ("biyue", "碧月装备", "碧月", null),
            ("pojun", "破军装备", "破军", null),
            ("xuanying", "玄影装备", "玄影", null),
            ("xingyun", "星陨装备", "星陨", null)
        };

        return stages
            .Select(stage => new GameInfoStage(
                stage.Id,
                stage.Name,
                string.Empty,
                string.Empty,
                equipment
                    .Where(record =>
                        record.Name.StartsWith(string.Concat(stage.Series, "【"), StringComparison.Ordinal) &&
                        (!stage.LingOnly.HasValue ||
                         record.Name.EndsWith("·灵", StringComparison.Ordinal) == stage.LingOnly.Value))
                    .OrderBy(record => int.TryParse(record.Id, out int itemId) ? itemId : int.MaxValue)
                    .ToArray()))
            .Where(stage => stage.Records.Count > 0)
            .ToArray();
    }

    public static GameInfoRepository Load(string? resourceFolder = null)
    {
        var repository = new GameInfoRepository(resourceFolder);
        Task.Run(repository.LoadAsync).GetAwaiter().GetResult();
        return repository;
    }


    private static IReadOnlyList<GameInfoStage> BuildClientWeaponStages(
        IReadOnlyList<GameInfoRecord> clientWeapons)
    {
        return clientWeapons
            .GroupBy(record => record.Weapon?.LevelLabel ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Min(record => record.Weapon?.StageOrder ?? int.MaxValue))
            .Select(group => new GameInfoStage(
                $"client-weapon-{group.Key}",
                group.Key,
                string.Empty,
                string.Empty,
                group
                    .OrderBy(record => record.Weapon?.StageOrder ?? int.MaxValue)
                    .ThenBy(record => record.Group, StringComparer.CurrentCulture)
                    .ThenBy(record => record.Name, StringComparer.CurrentCulture)
                    .ToArray()))
            .ToArray();
    }

}

public sealed record GameInfoCategory(
    string Id,
    string Name,
    string Description,
    string Glyph,
    IReadOnlyList<GameInfoStage> Stages)
{
    public string CountTextOverride { get; set; } = string.Empty;
    public string CountText => Id is "yinyang" or "wanxiang"
        ? string.Empty
        : string.IsNullOrWhiteSpace(CountTextOverride)
            ? $"{Stages.Sum(stage => stage.Records.Count):N0} 件"
            : CountTextOverride;
    public int RecordCount => Stages.Sum(stage => stage.Records.Count);
}

public sealed record GameInfoStage(
    string Id,
    string Name,
    string Period,
    string Summary,
    IReadOnlyList<GameInfoRecord> Records)
{
    public IReadOnlyList<string> OfficialForms { get; init; } = Array.Empty<string>();
    public string OfficialTier { get; init; } = string.Empty;
    public string OfficialLine { get; init; } = string.Empty;
}

public sealed record GameInfoRecord(
    string Id,
    string Name,
    string? ImagePath,
    string Group,
    string Summary,
    GameItemTooltip? Tooltip,
    GameWeaponInfo? Weapon = null);

public sealed record GameWeaponInfo(
    string LevelLabel,
    int StageOrder,
    string System,
    string SoulCap,
    int? EnhancementCapRankIndex,
    string EnhancementCapRankName,
    IReadOnlyList<string> EnhancementLookupIds)
{
    public bool SupportsWuhun =>
        !System.Contains("仙魔", StringComparison.Ordinal) &&
        !SoulCap.Contains("非武魂", StringComparison.Ordinal);
}

public sealed record GameInfoDetailRow(string Label, string Value);

public sealed record GameItemTooltip(
    string? IconPath,
    string Name,
    string Type,
    string Status,
    string Part,
    IReadOnlyList<string> RequiredProfessions,
    string Level,
    bool? Bound,
    string Quality,
    IReadOnlyList<GameInfoDetailRow> BaseRows,
    IReadOnlyList<string> EquipRows,
    IReadOnlyList<string> FeatureRows,
    IReadOnlyList<string> RandomRows,
    IReadOnlyList<string> Flags,
    string Description,
    string DescriptionRaw,
    string UseText,
    string Durability,
    string SellPrice,
    string Action)
{
    public string BoundText { get; init; } = string.Empty;
    public string ClientTypeText { get; init; } = string.Empty;
    public string ClientQualityText { get; init; } = string.Empty;
}

public sealed record GameEquipmentWashOption(
    string Id,
    string Slot,
    string Label,
    string ShortName,
    string ValueType,
    IReadOnlyList<int> RawValues,
    string RawName);

public sealed record GameEquipmentWashMaterial(
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<string> Names,
    string DisplayName,
    int Count,
    string? ImagePath,
    string Source);

public sealed record GameEquipmentWashCandidate(
    string Id,
    string Name,
    long Weight,
    IReadOnlyList<string> TierTexts,
    IReadOnlyList<int> InitialTierWeights,
    bool IsFeature);

public sealed record GameEquipmentWashSlot(
    int Index,
    string Name,
    bool IsWashable,
    bool UsesFeatureColor,
    IReadOnlyList<GameEquipmentWashCandidate> Candidates);

public sealed record GameEquipmentWashCost(
    int SlotIndex,
    GameEquipmentWashMaterial? Material,
    long Money,
    bool BoundMoney);

public sealed record GameEquipmentWashRecord(
    string ItemId,
    string Name,
    string Part,
    string StageId,
    IReadOnlyList<string> RandomIds,
    IReadOnlyList<GameEquipmentWashOption> Options,
    IReadOnlyList<GameEquipmentWashMaterial> RefreshMaterials,
    IReadOnlyList<GameEquipmentWashSlot> Slots,
    IReadOnlyList<GameEquipmentWashCost> Costs)
{
    public string MaxTierMarker { get; init; } = string.Empty;
}

public sealed class GameEquipmentWashCatalog
{
    public static GameEquipmentWashCatalog Empty { get; } = new(
        string.Empty,
        new Dictionary<string, GameEquipmentWashRecord>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, GameEquipmentWashRecord> _records;

    public GameEquipmentWashCatalog(
        string source,
        IReadOnlyDictionary<string, GameEquipmentWashRecord> records)
    {
        Source = source;
        _records = records;
    }

    public string Source { get; }
    public int Count => _records.Count;
    public IEnumerable<GameEquipmentWashRecord> Records => _records.Values;

    public GameEquipmentWashRecord? Get(string itemId) =>
        _records.GetValueOrDefault(itemId);

    public GameEquipmentWashRecord? Get(GameInfoRecord record) =>
        Get(record.Id) ?? _records.Values.FirstOrDefault(item =>
            GameEquipmentNameKey.Normalize(item.Name).Equals(
                GameEquipmentNameKey.Normalize(record.Name),
                StringComparison.OrdinalIgnoreCase));
}

public sealed record GameEquipmentMaterial(
    string Id,
    string Name,
    string? ImagePath);

public sealed record GameEquipmentUpgrade(
    string SourceItemId,
    string SourceName,
    string TargetItemId,
    string TargetName,
    IReadOnlyList<GameEquipmentMaterial> Materials,
    long? GoldCost,
    int PartCode,
    string EventId,
    bool IsBoundRecipe);

public sealed class GameEquipmentUpgradeCatalog
{
    public static GameEquipmentUpgradeCatalog Empty { get; } = new(
        string.Empty,
        Array.Empty<GameEquipmentUpgrade>());

    private readonly IReadOnlyList<GameEquipmentUpgrade> _steps;

    public GameEquipmentUpgradeCatalog(
        string source,
        IReadOnlyList<GameEquipmentUpgrade> steps)
    {
        Source = source;
        _steps = steps
            .Where(step => !string.IsNullOrWhiteSpace(step.SourceItemId) &&
                          !string.IsNullOrWhiteSpace(step.TargetItemId))
            .GroupBy(step => new
            {
                step.SourceItemId,
                step.TargetItemId,
                MaterialIds = string.Join("*", step.Materials.Select(material => material.Id))
            })
            .Select(group => group.First())
            .ToArray();
    }

    public string Source { get; }
    public int Count => _steps.Count;
    public IReadOnlyList<GameEquipmentUpgrade> Steps => _steps;

    public IReadOnlyList<GameEquipmentUpgrade> Get(string itemId) =>
        _steps
            .Where(step => step.SourceItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
                           step.TargetItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public IReadOnlyList<GameEquipmentUpgrade> Get(GameInfoRecord record) => Get(record.Id);
}

public sealed record GameEquipmentEnhancementEvent(
    string Id,
    string Name,
    int Probability,
    int AttributeCount,
    int TierIncrease,
    bool ConsumesMaterial,
    bool OnlyUnfilled);

public sealed record GameEquipmentFireOption(
    int Id,
    string Name,
    string Description,
    string BoostedEventId,
    int BonusProbability);

public sealed record GameEquipmentEnhancementInfo(
    string ItemId,
    string Name,
    IReadOnlyList<GameEquipmentMaterial> Materials,
    string BoundMaterialId,
    int MaterialCount,
    long Money,
    string BaseEventId,
    int FireAccuracy,
    IReadOnlyList<GameEquipmentEnhancementEvent> Events,
    IReadOnlyList<GameEquipmentFireOption> FireOptions,
    string Rule);

public sealed class GameEquipmentEnhancementCatalog
{
    public static GameEquipmentEnhancementCatalog Empty { get; } = new(
        string.Empty,
        new Dictionary<string, GameEquipmentEnhancementInfo>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, GameEquipmentEnhancementInfo> _records;

    public GameEquipmentEnhancementCatalog(
        string source,
        IReadOnlyDictionary<string, GameEquipmentEnhancementInfo> records)
    {
        Source = source;
        _records = records;
    }

    public string Source { get; }
    public int Count => _records.Count;

    public GameEquipmentEnhancementInfo? Get(string itemId) =>
        _records.GetValueOrDefault(itemId);

    public GameEquipmentEnhancementInfo? Get(GameInfoRecord record) =>
        Get(record.Id) ?? _records.Values.FirstOrDefault(item =>
            GameEquipmentNameKey.Normalize(item.Name).Equals(
                GameEquipmentNameKey.Normalize(record.Name),
                StringComparison.OrdinalIgnoreCase));
}

internal static class GameEquipmentNameKey
{
    public static string Normalize(string value)
    {
        string result = value
            .Replace("（绑定）", string.Empty, StringComparison.Ordinal)
            .Replace("(绑定)", string.Empty, StringComparison.Ordinal)
            .Trim();
        int separator = result.LastIndexOf('·');
        if (separator > 0 && separator + 1 < result.Length && result.Length - separator <= 5)
        {
            result = result[..separator];
        }

        return result.Trim();
    }
}

public sealed record GameMaterialIcon(
    string Id,
    string Name,
    string? ImagePath,
    string Source);

public sealed record GameWeaponEnhancementRow(
    int StepIndex,
    int RankIndex,
    string RankName,
    int Level,
    IReadOnlyList<string> Traits)
{
    public string ClientLevelText => Level switch
    {
        1 => "一",
        2 => "二",
        3 => "三",
        4 => "四",
        5 => "五",
        6 => "六",
        7 => "七",
        8 => "八",
        9 => "九",
        10 => "十",
        _ => string.Empty
    };

    public string ClientDisplayName => string.IsNullOrWhiteSpace(ClientLevelText)
        ? RankName
        : $"{RankName}{ClientLevelText}阶";
}

public sealed record GameWeaponEnhancementCost(
    int StepIndex,
    int RankIndex,
    string RankName,
    int Level,
    IReadOnlyList<string> MaterialIds,
    string MaterialName,
    string MaterialText,
    GameMaterialIcon? MaterialIcon,
    string BoundGold,
    int? SuccessValue);

public sealed class GameWeaponEnhancementCatalog
{
    public static GameWeaponEnhancementCatalog Empty { get; } = new(
        string.Empty,
        string.Empty,
        new Dictionary<string, IReadOnlyList<GameWeaponEnhancementRow>>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<GameWeaponEnhancementCost>());

    private readonly IReadOnlyDictionary<string, IReadOnlyList<GameWeaponEnhancementRow>> _rowsByItemId;
    private readonly IReadOnlyDictionary<int, GameWeaponEnhancementCost> _costs;

    public GameWeaponEnhancementCatalog(
        string attributeSource,
        string costSource,
        IReadOnlyDictionary<string, IReadOnlyList<GameWeaponEnhancementRow>> rowsByItemId,
        IReadOnlyList<GameWeaponEnhancementCost> costs)
    {
        AttributeSource = attributeSource;
        CostSource = costSource;
        _rowsByItemId = rowsByItemId;
        _costs = costs
            .GroupBy(cost => cost.StepIndex)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public string AttributeSource { get; }
    public string CostSource { get; }
    public int ItemCount => _rowsByItemId.Count;
    public int CostCount => _costs.Count;

    public IReadOnlyDictionary<int, string> RankNames => _rowsByItemId.Values
        .SelectMany(rows => rows)
        .GroupBy(row => row.RankIndex)
        .ToDictionary(group => group.Key, group => group.First().RankName);

    public IReadOnlyList<GameWeaponEnhancementRow> GetRows(GameInfoRecord record)
    {
        if (record.Weapon is null || !record.Weapon.SupportsWuhun)
        {
            return Array.Empty<GameWeaponEnhancementRow>();
        }

        string? sourceItemId = FindSourceItemId(record);
        if (sourceItemId is null || !_rowsByItemId.TryGetValue(sourceItemId, out var rows))
        {
            return Array.Empty<GameWeaponEnhancementRow>();
        }

        int? cap = record.Weapon.EnhancementCapRankIndex;
        return rows
            .Where(row => !cap.HasValue || row.RankIndex <= cap.Value)
            .OrderBy(row => row.StepIndex)
            .ToArray();
    }

    public string? FindSourceItemId(GameInfoRecord record)
    {
        if (record.Weapon is null || !record.Weapon.SupportsWuhun)
        {
            return null;
        }

        return new[] { record.Id }
            .Concat(record.Weapon.EnhancementLookupIds)
            .FirstOrDefault(id => _rowsByItemId.TryGetValue(id, out var rows) && rows.Count > 0);
    }

    public GameWeaponEnhancementCost? FindCost(GameWeaponEnhancementRow row)
    {
        return _costs.GetValueOrDefault(row.StepIndex);
    }
}
