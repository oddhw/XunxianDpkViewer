namespace XunxianDpkViewer.Core;

public enum GameTooltipAlignment
{
    Left,
    Center
}

public sealed record GameTooltipSpan(string Text, string Color);

public sealed record GameTooltipLine(
    GameTooltipAlignment Alignment,
    IReadOnlyList<GameTooltipSpan> Spans)
{
    public IReadOnlyList<GameTooltipSpan> TrailingSpans { get; init; } = Array.Empty<GameTooltipSpan>();
    public IReadOnlyList<string> IconPaths { get; init; } = Array.Empty<string>();
    public string? IconFramePath { get; init; }
}

public sealed record GameWeaponTooltipOverlay(
    string Status,
    IReadOnlyList<string> EnhancementRows);

public sealed record GameTooltipSupplementLine(
    string Label,
    string Text,
    string Color = GameTooltipFormatter.EquipColor);

public sealed record GameTooltipHintLine(
    string Text,
    string Color = GameTooltipFormatter.HintColor);

public sealed record GameWanxiangTooltipOverlay(
    string GuaText,
    string LevelText,
    string ExpText,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<string> GemIconPaths,
    string? GemFramePath);

public static class GameTooltipFormatter
{
    public const string DefaultColor = "#FFFFFFFF";
    public const string BoundColor = "#FF00FF00";
    public const string StatusColor = "#FFFF6600";
    public const string EquipColor = "#FF00FF00";
    public const string FeatureColor = "#FFFF6400";
    public const string FlagColor = "#FFFFFF00";
    public const string CommandColor = "#FFFF6600";
    public const string HintColor = "#FF808080";
    public const string WanxiangLevelColor = "#FFB6FF00";

    public static IReadOnlyList<GameTooltipLine> Format(
        GameInfoRecord record,
        GameWeaponTooltipOverlay? weaponOverlay = null,
        IReadOnlyList<GameTooltipSupplementLine>? supplementLines = null,
        IReadOnlyList<GameTooltipHintLine>? hintLines = null)
    {
        var lines = new List<GameTooltipLine>();
        GameItemTooltip? tooltip = record.Tooltip;

        AddLine(
            lines,
            tooltip?.Name ?? record.Name,
            ResolveQualityColor(tooltip?.Quality),
            GameTooltipAlignment.Center);

        if (tooltip is null)
        {
            AddLine(lines, record.Group);
            AddLine(lines, record.Summary);
            if (!string.IsNullOrWhiteSpace(record.Id))
            {
                AddLine(lines, $"编号：{record.Id}");
            }

            return lines;
        }

        AddBlankLine(lines);

        if (tooltip.Bound.HasValue)
        {
            string boundText = string.IsNullOrWhiteSpace(tooltip.BoundText)
                ? tooltip.Bound.Value ? "已绑定" : "未绑定"
                : tooltip.BoundText;
            AddLine(lines, boundText, BoundColor);
        }

        string typeText = string.IsNullOrWhiteSpace(tooltip.ClientTypeText)
            ? tooltip.Type
            : tooltip.ClientTypeText;
        if (string.IsNullOrWhiteSpace(tooltip.ClientQualityText))
        {
            AddLine(lines, typeText);
        }
        else
        {
            AddSplitLine(
                lines,
                typeText,
                DefaultColor,
                tooltip.ClientQualityText,
                ResolveQualityColor(tooltip.Quality));
        }
        AddLine(lines, weaponOverlay?.Status ?? tooltip.Status, StatusColor);
        if (string.IsNullOrWhiteSpace(tooltip.ClientTypeText))
        {
            AddLine(lines, tooltip.Part);
        }

        if (tooltip.RequiredProfessions.Count > 0)
        {
            IReadOnlyList<string> professions = record.Weapon is not null ||
                                                tooltip.Type.Equals("装备", StringComparison.Ordinal)
                ? GameProfessionFormatter.NormalizeRequirementTokens(tooltip.RequiredProfessions)
                : tooltip.RequiredProfessions;
            AddLine(lines, $"需要：{string.Join(' ', professions)}");
        }

        if (!string.IsNullOrWhiteSpace(tooltip.Level))
        {
            AddLine(lines, $"等级：{tooltip.Level}");
        }

        foreach (GameInfoDetailRow row in tooltip.BaseRows)
        {
            string separator = string.IsNullOrWhiteSpace(row.Label) ||
                               row.Label.EndsWith('：') ||
                               row.Label.EndsWith(':')
                ? string.Empty
                : "：";
            AddLine(lines, $"{row.Label}{separator}{row.Value}");
        }

        foreach (string value in tooltip.EquipRows)
        {
            AddLine(lines, $"装备：{value}", EquipColor);
        }

        foreach (string value in tooltip.FeatureRows)
        {
            AddLine(lines, $"特性：{value}", FeatureColor);
        }

        if (weaponOverlay is not null)
        {
            foreach (string value in weaponOverlay.EnhancementRows)
            {
                AddLine(lines, $"强化：{value}", StatusColor);
            }
        }

        if (supplementLines is not null)
        {
            foreach (GameTooltipSupplementLine line in supplementLines)
            {
                AddLine(lines, $"{line.Label}：{line.Text}", line.Color);
            }
        }

        if (tooltip.Flags.Count > 0)
        {
            AddBlankLine(lines);
            foreach (string value in tooltip.Flags)
            {
                AddLine(lines, value, FlagColor);
            }
        }

        string description = !string.IsNullOrWhiteSpace(tooltip.DescriptionRaw)
            ? tooltip.DescriptionRaw
            : tooltip.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            AddBlankLine(lines);
            AddRichLines(lines, description);
        }

        if (hintLines is not null)
        {
            foreach (GameTooltipHintLine line in hintLines)
            {
                AddLine(lines, line.Text, line.Color);
            }
        }

        if (!string.IsNullOrWhiteSpace(tooltip.Durability))
        {
            AddLine(lines, $"耐久：{tooltip.Durability}");
        }

        if (!string.IsNullOrWhiteSpace(tooltip.SellPrice))
        {
            AddLine(lines, $"售店价格：{tooltip.SellPrice}", FlagColor);
        }

        AddLine(lines, tooltip.Action, CommandColor);
        return lines;
    }

    public static IReadOnlyList<GameTooltipLine> FormatYinYang(
        GameInfoRecord record,
        IReadOnlyList<GameTooltipSupplementLine>? supplementLines = null,
        IReadOnlyList<GameTooltipHintLine>? hintLines = null)
    {
        var lines = new List<GameTooltipLine>();
        GameItemTooltip? tooltip = record.Tooltip;

        AddLine(
            lines,
            tooltip?.Name ?? record.Name,
            ResolveQualityColor(tooltip?.Quality),
            GameTooltipAlignment.Center);
        if (tooltip is null)
        {
            return lines;
        }

        AddBlankLine(lines);
        if (tooltip.Bound.HasValue)
        {
            string boundText = string.IsNullOrWhiteSpace(tooltip.BoundText)
                ? tooltip.Bound.Value ? "已绑定" : "未绑定"
                : tooltip.BoundText;
            AddLine(lines, boundText, BoundColor);
        }

        AddLine(lines, tooltip.Type);
        if (!string.IsNullOrWhiteSpace(tooltip.Level))
        {
            AddLine(lines, $"等级：{tooltip.Level}");
        }

        if (tooltip.Flags.Count > 0)
        {
            AddBlankLine(lines);
            foreach (string value in tooltip.Flags)
            {
                AddLine(lines, value, FlagColor);
            }
        }

        string description = !string.IsNullOrWhiteSpace(tooltip.DescriptionRaw)
            ? tooltip.DescriptionRaw
            : tooltip.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            AddBlankLine(lines);
            AddRichLines(lines, description);
        }

        if (supplementLines is not null)
        {
            foreach (GameTooltipSupplementLine line in supplementLines)
            {
                AddLine(lines, $"{line.Label}：{line.Text}", line.Color);
            }
        }

        if (hintLines is not null)
        {
            foreach (GameTooltipHintLine line in hintLines)
            {
                AddLine(lines, line.Text, line.Color);
            }
        }

        if (!string.IsNullOrWhiteSpace(tooltip.SellPrice))
        {
            AddLine(lines, $"售店价格：{tooltip.SellPrice}", FlagColor);
        }

        AddLine(lines, tooltip.Action, CommandColor);
        return lines;
    }

    public static IReadOnlyList<GameTooltipLine> FormatWanxiang(
        GameInfoRecord record,
        GameWanxiangTooltipOverlay overlay)
    {
        var lines = new List<GameTooltipLine>();
        GameItemTooltip? tooltip = record.Tooltip;

        AddLine(
            lines,
            tooltip?.Name ?? record.Name,
            FlagColor,
            GameTooltipAlignment.Center);
        if (tooltip is null)
        {
            return lines;
        }

        AddBlankLine(lines);
        if (tooltip.Bound.HasValue)
        {
            string boundText = string.IsNullOrWhiteSpace(tooltip.BoundText)
                ? tooltip.Bound.Value ? "已绑定" : "未绑定"
                : tooltip.BoundText;
            AddLine(lines, boundText, BoundColor);
        }

        AddLine(lines, tooltip.Type);
        AddLine(lines, $"卦象：{overlay.GuaText}", FlagColor);
        AddLine(lines, overlay.LevelText, WanxiangLevelColor);
        AddLine(lines, overlay.ExpText, FlagColor);

        if (overlay.Attributes.Count > 0)
        {
            AddLine(lines, "八卦属性", EquipColor);
            for (int index = 0; index < overlay.Attributes.Count; index++)
            {
                AddLine(lines, $"属性{index + 1}：{overlay.Attributes[index]}", EquipColor);
            }
        }

        lines.Add(new GameTooltipLine(
            GameTooltipAlignment.Left,
            new[] { new GameTooltipSpan("宝石：", FlagColor) })
        {
            IconPaths = overlay.GemIconPaths,
            IconFramePath = overlay.GemFramePath
        });

        if (tooltip.Flags.Count > 0)
        {
            AddBlankLine(lines);
            foreach (string value in tooltip.Flags)
            {
                AddLine(lines, value, FlagColor);
            }
        }

        string description = !string.IsNullOrWhiteSpace(tooltip.DescriptionRaw)
            ? tooltip.DescriptionRaw
            : tooltip.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            AddBlankLine(lines);
            AddRichLines(lines, description);
        }

        if (!string.IsNullOrWhiteSpace(tooltip.SellPrice))
        {
            AddLine(lines, $"售店价格：{tooltip.SellPrice}", FlagColor);
        }

        AddLine(lines, tooltip.Action, CommandColor);
        return lines;
    }

    public static string ResolveQualityColor(string? quality)
    {
        if (string.Equals(quality, "red", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFFF0000";
        }

        if (string.Equals(quality, "normal", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(quality))
        {
            return DefaultColor;
        }

        string directColor = quality.Trim().TrimStart('#');
        if (directColor.Length == 6 && directColor.All(Uri.IsHexDigit))
        {
            return $"#FF{directColor.ToUpperInvariant()}";
        }

        if (directColor.Length == 8 && directColor.All(Uri.IsHexDigit))
        {
            return $"#{directColor.ToUpperInvariant()}";
        }

        return DefaultColor;
    }

    private static void AddRichLines(List<GameTooltipLine> lines, string raw)
    {
        var lineSpans = new List<GameTooltipSpan>();

        foreach (GameRichTextSpan span in GameRichTextParser.Parse(raw))
        {
            string normalized = span.Text
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            string[] parts = normalized.Split('\n');

            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length > 0)
                {
                    lineSpans.Add(new GameTooltipSpan(parts[index], span.Color));
                }

                if (index < parts.Length - 1)
                {
                    lines.Add(new GameTooltipLine(GameTooltipAlignment.Left, lineSpans.ToArray()));
                    lineSpans.Clear();
                }
            }
        }

        if (lineSpans.Count > 0)
        {
            lines.Add(new GameTooltipLine(GameTooltipAlignment.Left, lineSpans.ToArray()));
        }
    }

    private static void AddLine(
        List<GameTooltipLine> lines,
        string? text,
        string color = DefaultColor,
        GameTooltipAlignment alignment = GameTooltipAlignment.Left)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lines.Add(new GameTooltipLine(
            alignment,
            new[] { new GameTooltipSpan(text, color) }));
    }

    private static void AddSplitLine(
        List<GameTooltipLine> lines,
        string? text,
        string color,
        string? trailingText,
        string trailingColor)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            AddLine(lines, trailingText, trailingColor);
            return;
        }

        if (string.IsNullOrWhiteSpace(trailingText))
        {
            AddLine(lines, text, color);
            return;
        }

        lines.Add(new GameTooltipLine(
            GameTooltipAlignment.Left,
            new[] { new GameTooltipSpan(text, color) })
        {
            TrailingSpans = new[] { new GameTooltipSpan(trailingText, trailingColor) }
        });
    }

    private static void AddBlankLine(List<GameTooltipLine> lines)
    {
        if (lines.Count == 0 || lines[^1].Spans.Count == 0)
        {
            return;
        }

        lines.Add(new GameTooltipLine(GameTooltipAlignment.Left, Array.Empty<GameTooltipSpan>()));
    }
}
