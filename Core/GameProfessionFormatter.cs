namespace XunxianDpkViewer.Core;

public static class GameProfessionFormatter
{
    public static IReadOnlyList<string> NormalizeRequirementTokens(IEnumerable<string>? values)
    {
        string text = string.Join(" ", values ?? Array.Empty<string>());
        string family = ResolveFamily(text);
        return string.IsNullOrWhiteSpace(family)
            ? (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : new[] { family, "天狐" };
    }

    public static string NormalizeRequirement(IEnumerable<string>? values) =>
        string.Join(" / ", NormalizeRequirementTokens(values));

    public static string NormalizeRequirement(string? value) =>
        NormalizeRequirement(string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ' ', '/', '、', '+', '＋' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ResolveFamily(string value)
    {
        string text = value.Replace("符咒师", "符咒", StringComparison.Ordinal);
        if (text.Contains("力士", StringComparison.Ordinal))
        {
            return "力士";
        }

        if (text.Contains("法师", StringComparison.Ordinal))
        {
            return "法师";
        }

        if (text.Contains("符咒", StringComparison.Ordinal))
        {
            return "符咒师";
        }

        return text.Contains("游侠", StringComparison.Ordinal) ||
               text.Contains("天狐", StringComparison.Ordinal)
            ? "游侠"
            : string.Empty;
    }
}
