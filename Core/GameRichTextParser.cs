using System.Text.RegularExpressions;

namespace XunxianDpkViewer.Core;

public static partial class GameRichTextParser
{
    public const string DefaultColor = "#FFFFFFFF";

    public static IReadOnlyList<GameRichTextSpan> Parse(string? raw, string defaultColor = DefaultColor)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<GameRichTextSpan>();
        }

        string text = raw
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var spans = new List<GameRichTextSpan>();
        string currentColor = NormalizeColor(defaultColor, DefaultColor);
        int offset = 0;

        foreach (Match match in ColorTagRegex().Matches(text))
        {
            AddSpan(spans, text[offset..match.Index], currentColor);
            currentColor = match.Groups[1].Success
                ? NormalizeColor(match.Groups[1].Value, DefaultColor)
                : NormalizeColor(defaultColor, DefaultColor);
            offset = match.Index + match.Length;
        }

        AddSpan(spans, text[offset..], currentColor);
        return spans;
    }

    private static void AddSpan(List<GameRichTextSpan> spans, string text, string color)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && string.Equals(spans[^1].Color, color, StringComparison.OrdinalIgnoreCase))
        {
            GameRichTextSpan previous = spans[^1];
            spans[^1] = previous with { Text = previous.Text + text };
            return;
        }

        spans.Add(new GameRichTextSpan(text, color));
    }

    private static string NormalizeColor(string value, string fallback)
    {
        string normalized = value.Trim().TrimStart('#');
        if (normalized.Length == 8 && normalized.All(Uri.IsHexDigit))
        {
            return $"#{normalized.ToUpperInvariant()}";
        }

        if (normalized.Length == 6 && normalized.All(Uri.IsHexDigit))
        {
            return $"#FF{normalized.ToUpperInvariant()}";
        }

        return fallback;
    }

    [GeneratedRegex(@"<c:([0-9A-Fa-f]{8})>|<c:>|<c>", RegexOptions.CultureInvariant)]
    private static partial Regex ColorTagRegex();
}

public sealed record GameRichTextSpan(string Text, string Color);
