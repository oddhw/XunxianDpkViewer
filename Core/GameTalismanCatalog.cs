namespace XunxianDpkViewer.Core;

public sealed record GameTalismanInfo(
    int Level,
    int DisplayRank,
    string RankText,
    int ProfessionId,
    string Profession,
    IReadOnlyList<string> RefinementRows)
{
    public int MaxGrowth { get; init; }
    public int InternalRank { get; init; }
    public int MaxProficiency { get; init; }
    public int SpiritConversionRate { get; init; }
    public int TalentTreeId { get; init; }
    public string? CoinIconPath { get; init; }
    public IReadOnlyList<GameTalismanTalent> Talents { get; init; } = Array.Empty<GameTalismanTalent>();
}

public sealed record GameTalismanTalent(
    int Id,
    string Name,
    string? IconPath,
    int X,
    int Y,
    int MaxLevel,
    int RequiredGrowth,
    int RequiredPoints,
    IReadOnlyList<int> Predecessors,
    string Description,
    string EffectFormat,
    IReadOnlyList<string> EffectParameters,
    string FullLevelBonus)
{
    public string EffectAt(int level)
    {
        int count = EffectParameters.Count > 0 && int.TryParse(EffectParameters[0], out int value) ? value : 0;
        int start = (Math.Clamp(level, 1, MaxLevel) - 1) * count + 1;
        int parameter = 0;
        return System.Text.RegularExpressions.Regex.Replace(EffectFormat, "%[s%]", match =>
            match.Value == "%%" ? "%" : EffectParameters.ElementAtOrDefault(start + parameter++) ?? string.Empty)
            .Replace("\\n", "\n", StringComparison.Ordinal);
    }
}

public sealed class GameTalismanSelection
{
    public int Growth { get; set; }
    public int ActivatedAttributes { get; set; }
    public int AppearanceIndex { get; set; }
    public Dictionary<int, int> TalentLevels { get; } = new();
    public int UsedPoints => TalentLevels.Values.Sum();

    public bool CanLearn(GameTalismanInfo info, GameTalismanTalent talent) =>
        TalentLevels.GetValueOrDefault(talent.Id) < talent.MaxLevel &&
        UsedPoints < Growth && IsUnlocked(info, talent);

    public bool Learn(GameTalismanInfo info, GameTalismanTalent talent)
    {
        if (!CanLearn(info, talent))
        {
            return false;
        }
        TalentLevels[talent.Id] = TalentLevels.GetValueOrDefault(talent.Id) + 1;
        return true;
    }

    public void Normalize(GameTalismanInfo info)
    {
        Growth = Math.Clamp(Growth, 0, info.MaxGrowth);
        ActivatedAttributes = Math.Clamp(ActivatedAttributes, 0, info.RefinementRows.Count);
        foreach (int id in TalentLevels.Keys.ToArray())
        {
            GameTalismanTalent? talent = info.Talents.FirstOrDefault(node => node.Id == id);
            if (talent is null || TalentLevels[id] <= 0)
            {
                TalentLevels.Remove(id);
            }
            else
            {
                TalentLevels[id] = Math.Min(TalentLevels[id], talent.MaxLevel);
            }
        }
        while (true)
        {
            GameTalismanTalent? invalid = info.Talents.Reverse().FirstOrDefault(talent =>
                TalentLevels.GetValueOrDefault(talent.Id) > 0 &&
                (UsedPoints > Growth || !IsUnlocked(info, talent)));
            if (invalid is null)
            {
                break;
            }
            TalentLevels.Remove(invalid.Id);
        }
    }

    public bool IsUnlocked(GameTalismanInfo info, GameTalismanTalent talent) =>
        Growth >= talent.RequiredGrowth &&
        info.Talents.Where(node => node.RequiredPoints < talent.RequiredPoints)
            .Sum(node => TalentLevels.GetValueOrDefault(node.Id)) >= talent.RequiredPoints &&
        talent.Predecessors.All(id => info.Talents.FirstOrDefault(node => node.Id == id) is { } predecessor &&
            TalentLevels.GetValueOrDefault(id) >= predecessor.MaxLevel);
}

public sealed record GameTalismanCatalog(
    IReadOnlyList<GameInfoRecord> Items,
    string Name,
    string AllText,
    string LevelLabel,
    string ProfessionLabel,
    string RefinementTitle)
{
    public IReadOnlyDictionary<string, string> ClientText { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Images { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> AppearanceNames { get; init; } = Array.Empty<string>();
    public string Text(string key) => ClientText.GetValueOrDefault(key, string.Empty);

    public static GameTalismanCatalog Empty { get; } = new(
        Array.Empty<GameInfoRecord>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}
