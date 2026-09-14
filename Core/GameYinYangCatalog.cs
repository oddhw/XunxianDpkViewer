namespace XunxianDpkViewer.Core;

public sealed record GameYinYangCatalog(
    string Source,
    IReadOnlyList<GameYinYangJade> Jades,
    IReadOnlyList<GameYinYangAffixGroup> EnchantGroups,
    IReadOnlyList<GameYinYangAffixGroup> SpiritGroups,
    IReadOnlyList<GameYinYangAffixGroup> SoulGroups,
    IReadOnlyList<GameYinYangQuality> Qualities,
    IReadOnlyList<GameYinYangEmptyAttributeHint> EmptyAttributeHints)
{
    public static GameYinYangCatalog Empty { get; } = new(
        string.Empty,
        Array.Empty<GameYinYangJade>(),
        Array.Empty<GameYinYangAffixGroup>(),
        Array.Empty<GameYinYangAffixGroup>(),
        Array.Empty<GameYinYangAffixGroup>(),
        Array.Empty<GameYinYangQuality>(),
        Array.Empty<GameYinYangEmptyAttributeHint>());

    public int GenerationCount => EnchantGroups
        .Concat(SpiritGroups)
        .Concat(SoulGroups)
        .Select(group => group.GenerationId)
        .Distinct()
        .Count();

    public int AttributeCount => EnchantGroups
        .Concat(SpiritGroups)
        .Concat(SoulGroups)
        .Sum(group => group.Attributes.Count);
}

public sealed record GameYinYangJade(
    string Side,
    GameInfoRecord Record);

public sealed record GameYinYangQuality(
    int Id,
    string Name);

public sealed record GameYinYangEmptyAttributeHint(
    string Role,
    string Text);

public sealed record GameYinYangAffixGroup(
    string Role,
    string RoleLabel,
    int GenerationId,
    string SeriesKey,
    string Prefix,
    string Name,
    IReadOnlyList<GameYinYangScroll> Scrolls,
    IReadOnlyList<GameYinYangAttribute> Attributes)
{
    public GameYinYangScroll? ResolveScroll(string? attributeName, int qualityId)
    {
        IEnumerable<GameYinYangScroll> candidates = Scrolls;
        if (!string.IsNullOrWhiteSpace(attributeName))
        {
            candidates = candidates.Where(scroll =>
                scroll.AttributeName.Equals(attributeName, StringComparison.Ordinal));
        }

        GameYinYangScroll[] unbound = candidates
            .Where(scroll => !scroll.IsBound)
            .ToArray();
        if (unbound.Length > 0)
        {
            candidates = unbound;
        }

        bool preferBest = qualityId == 5;
        return candidates.FirstOrDefault(scroll => scroll.IsBest == preferBest) ??
               candidates.FirstOrDefault(scroll => !scroll.IsBest) ??
               candidates.FirstOrDefault();
    }
}

public sealed record GameYinYangScroll(
    string ItemId,
    string Name,
    string AttributeName,
    bool IsBound,
    bool IsBest,
    string? IconPath,
    string DescriptionRaw);

public sealed record GameYinYangAttribute(
    int TraitId,
    int Slot,
    int GenerationId,
    int QualityId,
    string QualityName,
    string Name,
    string Text,
    int SourceOrder);
