using System.Text.RegularExpressions;

namespace XunxianDpkViewer.Core;

public sealed record GameWanxiangCatalog(
    string Source,
    IReadOnlyList<GameInfoRecord> Plates,
    IReadOnlyList<GameWanxiangLevelState> Levels,
    IReadOnlyList<GameWanxiangGemSeries> GemSeries,
    IReadOnlyList<GameWanxiangUpgradeMethod> UpgradeMethods,
    IReadOnlyList<string> GuaNames,
    string NoGuaText,
    string FireUpgradeTipFormat,
    string FairyUpgradeTipFormat,
    string? InlayBackgroundPath,
    string? EmptySlotPath,
    string? SelectionFramePath,
    string? PlateSlotFramePath,
    string? LevelBasePath,
    string? LevelCenterBottomPath,
    string? LevelCenterTopPath,
    IReadOnlyDictionary<int, string> LevelPetalPaths,
    IReadOnlyDictionary<int, string> LevelExpPaths,
    string? WindowHeaderPath,
    string? WindowFootPath,
    string? WindowInnerPath,
    string? WindowBackgroundPath,
    string? WindowBottomPath,
    string? ButtonSpritePath,
    string? TooltipFramePath)
{
    public static GameWanxiangCatalog Empty { get; } = new(
        string.Empty,
        Array.Empty<GameInfoRecord>(),
        Array.Empty<GameWanxiangLevelState>(),
        Array.Empty<GameWanxiangGemSeries>(),
        Array.Empty<GameWanxiangUpgradeMethod>(),
        Array.Empty<string>(),
        "无",
        "消耗材料:<c:FFB6FF00>[%s]<c>*%d",
        "消耗材料:<c:FFFF6A00>[%s]<c>*%d",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        new Dictionary<int, string>(),
        new Dictionary<int, string>(),
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public GameWanxiangUpgradeMethod? ResolveUpgradeMethod(int qualityIndex, int methodKind) =>
        UpgradeMethods.FirstOrDefault(method =>
            method.MethodKind == methodKind && method.QualityIndices.Contains(qualityIndex));
}

public sealed record GameWanxiangLevelState(
    int Id,
    int QualityIndex,
    string QualityName,
    int Segment,
    int MaxExp,
    IReadOnlyDictionary<string, GameWanxiangProfessionStates> ProfessionStates)
{
    public string DisplayName => $"{QualityName}{Segment}段";
    public string ClientDisplayText { get; init; } = $"品阶（段位）：{QualityName}->{Segment}段";

    public GameWanxiangState? ResolveState(string profession, int stoneCount, string guaName)
    {
        if (!ProfessionStates.TryGetValue(profession, out GameWanxiangProfessionStates? states))
        {
            return null;
        }

        if (stoneCount is > 0 and < 8)
        {
            return states.ScatterStates.ElementAtOrDefault(stoneCount - 1);
        }

        return stoneCount >= 8
            ? states.GuaStates.GetValueOrDefault(guaName)
            : null;
    }

    public IReadOnlyList<GameWanxiangState> ResolveStates(int stoneCount, string guaName) =>
        ProfessionStates.Values
            .Select(states => stoneCount is > 0 and < 8
                ? states.ScatterStates.ElementAtOrDefault(stoneCount - 1)
                : stoneCount >= 8 ? states.GuaStates.GetValueOrDefault(guaName) : null)
            .Where(state => state is not null)
            .Cast<GameWanxiangState>()
            .GroupBy(state => state.Description, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
}

public sealed record GameWanxiangProfessionStates(
    string Profession,
    IReadOnlyList<GameWanxiangState> ScatterStates,
    IReadOnlyDictionary<string, GameWanxiangState> GuaStates);

public sealed record GameWanxiangState(
    string Id,
    string Name,
    string Description,
    string? IconPath);

public sealed record GameWanxiangGemSeries(
    int Id,
    string Name,
    bool IsBound,
    IReadOnlyDictionary<string, GameWanxiangGem> Gems)
{
    public GameWanxiangGem? Resolve(string guaName) => Gems.GetValueOrDefault(guaName);
}

public sealed record GameWanxiangGem(
    string GuaName,
    GameInfoRecord Record,
    string AttributeRaw,
    string AttributeText);

public sealed record GameWanxiangAttributeTotal(
    string Name,
    long Value,
    string Unit,
    int Order)
{
    public string Text => $"{Name}提高{Value}{Unit}";
}

public static class GameWanxiangAttributeAggregator
{
    private static readonly Regex AttributePattern = new(
        @"(?<value>\d+)(?<unit>%|点)(?:的)?(?<name>[\p{IsCJKUnifiedIdeographs}]+?)(?=、|和|，|；|。|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<GameWanxiangAttributeTotal> Aggregate(IEnumerable<GameWanxiangGem> gems)
    {
        var totals = new Dictionary<(string Name, string Unit), GameWanxiangAttributeTotal>();
        int order = 0;
        foreach (GameWanxiangGem gem in gems)
        {
            foreach (Match match in AttributePattern.Matches(gem.AttributeText))
            {
                if (!long.TryParse(match.Groups["value"].Value, out long value))
                {
                    continue;
                }

                string name = NormalizeName(match.Groups["name"].Value);
                string unit = match.Groups["unit"].Value;
                var key = (name, unit);
                if (totals.TryGetValue(key, out GameWanxiangAttributeTotal? total))
                {
                    totals[key] = total with { Value = total.Value + value };
                }
                else
                {
                    totals[key] = new GameWanxiangAttributeTotal(name, value, unit, order++);
                }
            }
        }

        return totals.Values
            .OrderBy(total => ResolveOrder(total.Name))
            .ThenBy(total => total.Order)
            .ToArray();
    }

    private static string NormalizeName(string name) => name switch
    {
        "物理攻击力" => "攻击",
        "攻击力" => "攻击",
        _ => name
    };

    private static int ResolveOrder(string name)
    {
        if (name.Contains("会心几率", StringComparison.Ordinal))
        {
            return 10;
        }

        if (name is "攻击" or "法术效果")
        {
            return 20;
        }

        return name.StartsWith("忽略", StringComparison.Ordinal) ? 30 : 40;
    }
}

public sealed record GameWanxiangUpgradeMaterial(
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<string> Names,
    string DisplayName,
    int Count,
    string? ImagePath);

public sealed record GameWanxiangUpgradeOutcome(
    int Exp,
    int Weight,
    string TraitId);

public sealed record GameWanxiangUpgradeMethod(
    int Id,
    int MethodKind,
    string Name,
    IReadOnlyList<int> QualityIndices,
    GameWanxiangUpgradeMaterial Material,
    IReadOnlyList<GameWanxiangUpgradeOutcome> Outcomes)
{
    public int RollExp(Random random)
    {
        int totalWeight = Outcomes.Sum(outcome => Math.Max(0, outcome.Weight));
        if (totalWeight <= 0)
        {
            return 0;
        }

        int roll = random.Next(totalWeight);
        foreach (GameWanxiangUpgradeOutcome outcome in Outcomes)
        {
            roll -= Math.Max(0, outcome.Weight);
            if (roll < 0)
            {
                return outcome.Exp;
            }
        }

        return Outcomes[^1].Exp;
    }
}
