using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class CompositeModelDiagnostics
{
    private static readonly IReadOnlyDictionary<string, (string Group, string Name, bool Required)> PartSlots =
        new Dictionary<string, (string Group, string Name, bool Required)>(StringComparer.OrdinalIgnoreCase)
        {
            ["hd"] = ("head", "头部", true),
            ["mz"] = ("face", "面部", false),
            ["st"] = ("torso", "上身", true),
            ["sz"] = ("waist", "腰部", true),
            ["tui"] = ("lower", "腿部", true),
            ["kz"] = ("lower", "下装", true),
            ["xz"] = ("feet", "鞋子/脚部", true),
            ["gl"] = ("left-hand", "左手", true),
            ["gla"] = ("left-hand-detail", "左手附属", true),
            ["gr"] = ("right-hand", "右手", true),
            ["gra"] = ("right-hand-detail", "右手附属", true),
            ["gj"] = ("neck", "颈部饰件", false),
            ["mj"] = ("mask", "面饰", false),
            ["dp"] = ("cape", "披风", false),
            ["tf"] = ("hair-accessory", "头饰", false),
            ["qz"] = ("front-accessory", "前饰", false),
            ["xw"] = ("tail-accessory", "尾饰", false),
            ["yd"] = ("waist-accessory", "腰饰", false),
            ["sy"] = ("hand-accessory", "手持饰件", false),
            ["gb"] = ("back-accessory", "背部饰件", false)
        };

    private static readonly (string Group, string Name)[] RequiredGroups =
    {
        ("head", "头部"),
        ("torso", "上身"),
        ("waist", "腰部"),
        ("lower", "下装/腿部"),
        ("feet", "鞋子/脚部")
    };

    public static CompositeModelDiagnostic Analyze(
        IReadOnlyList<CompositeModelPart> parts,
        bool forceCharacter = false)
    {
        CompositeModelPartDescriptor[] descriptors = parts
            .Select(Describe)
            .ToArray();
        bool isCharacter = forceCharacter;

        string[] missing = isCharacter
            ? RequiredGroups
                .Where(required => descriptors.All(part =>
                    !part.SlotGroup.Equals(required.Group, StringComparison.OrdinalIgnoreCase)))
                .Select(required => required.Name)
                .ToArray()
            : Array.Empty<string>();

        string[] duplicates = descriptors
            .Where(part => part.IsBodyPart)
            .GroupBy(part => part.SlotGroup, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().SlotName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int optionalCount = descriptors.Count(part => part.IsOptional);
        int unclassifiedCount = descriptors.Count(part => !part.IsClassified);
        return new CompositeModelDiagnostic(
            isCharacter,
            descriptors,
            missing,
            duplicates,
            optionalCount,
            unclassifiedCount);
    }

    public static CompositeModelPartDescriptor Describe(CompositeModelPart part)
    {
        string fileName = Path.GetFileNameWithoutExtension(part.MeshAsset.Name);
        string prefix = fileName.Split('_', 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        if (PartSlots.TryGetValue(prefix, out (string Group, string Name, bool Required) slot))
        {
            bool bodyPart = slot.Required || prefix.Equals("mz", StringComparison.OrdinalIgnoreCase);
            return new CompositeModelPartDescriptor(
                GetPartKey(part),
                slot.Group,
                slot.Name,
                fileName,
                bodyPart,
                !bodyPart,
                true);
        }

        string readableName = fileName.Replace('_', ' ');
        return new CompositeModelPartDescriptor(
            GetPartKey(part),
            $"part:{prefix}",
            string.IsNullOrWhiteSpace(prefix) ? "模型部件" : prefix.ToUpperInvariant(),
            readableName,
            false,
            false,
            false);
    }

    public static string GetPartKey(CompositeModelPart part) =>
        $"{part.MeshAsset.ArchivePath}|{part.MeshAsset.Entry.Path}|{part.MaterialName}";

    public static string GetPresetKey(CompositeModelEntry composite) =>
        $"{composite.ConfigAsset.ArchivePath}|{composite.ConfigAsset.Entry.Path}|{composite.VariantLabel}";
}

public sealed record CompositeModelPartDescriptor(
    string Key,
    string SlotGroup,
    string SlotName,
    string FileName,
    bool IsBodyPart,
    bool IsOptional,
    bool IsClassified);

public sealed record CompositeModelDiagnostic(
    bool IsCharacter,
    IReadOnlyList<CompositeModelPartDescriptor> Parts,
    IReadOnlyList<string> MissingRequiredParts,
    IReadOnlyList<string> DuplicateSlots,
    int OptionalPartCount,
    int UnclassifiedPartCount)
{
    public bool HasWarning => MissingRequiredParts.Count > 0 || DuplicateSlots.Count > 0;

    public string StatusText
    {
        get
        {
            if (!IsCharacter)
                return $"检测到 {Parts.Count:N0} 个独立部件";
            if (MissingRequiredParts.Count == 0 && DuplicateSlots.Count == 0)
                return $"人物结构完整 · {Parts.Count:N0} 个部件";

            var messages = new List<string>();
            if (MissingRequiredParts.Count > 0)
                messages.Add($"可能缺少 {string.Join("、", MissingRequiredParts)}");
            if (DuplicateSlots.Count > 0)
                messages.Add($"重复部位 {string.Join("、", DuplicateSlots)}");
            return string.Join(" · ", messages);
        }
    }
}
