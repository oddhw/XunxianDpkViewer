using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace XunxianDpkViewer.Models;

public enum AssetKind
{
    Image,
    Sound,
    Model,
    Font,
    MbTable,
    GlobalSearch,
    DungeonSummary,
    Other
}

public sealed record DpkEntry(string Path, uint RootBlock);

public sealed record AssetEntry(
    string ArchivePath,
    DpkEntry Entry,
    AssetKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Entry.Path);
    public string Extension => System.IO.Path.GetExtension(Entry.Path).ToLowerInvariant();
    public string ArchiveName => System.IO.Path.GetFileName(ArchivePath);
    public string DisplayPath => $"{ArchiveName}  /  {Entry.Path}";
}

public sealed record FolderNodeInfo(
    string Name,
    string ArchivePath,
    string InternalPath)
{
    public string ArchiveName => System.IO.Path.GetFileName(ArchivePath);
    public string DisplayPath => string.IsNullOrEmpty(InternalPath)
        ? ArchiveName
        : $"{ArchiveName}  /  {InternalPath}";

    public override string ToString() => Name;
}

public sealed class AssetItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private string _subtitle;
    private readonly string _name;

    public AssetItemViewModel(AssetEntry asset, string? displayName = null, string? subtitle = null)
    {
        Asset = asset;
        _name = displayName ?? asset.Name;
        _subtitle = subtitle ?? asset.Entry.Path;
    }

    public AssetItemViewModel(CompositeModelEntry composite)
    {
        Composite = composite;
        _name = composite.Name;
        _subtitle = $"完整组合 · {composite.Parts.Count:N0} 个 PMF 部件";
    }

    public AssetEntry? Asset { get; }
    public CompositeModelEntry? Composite { get; }
    public bool IsThumbnailLoading { get; set; }
    public string Name => _name;
    public string Path => Asset?.Entry.Path ?? Composite?.ConfigAsset.Entry.Path ?? string.Empty;
    public string ArchiveName => Asset?.ArchiveName ?? Composite?.ConfigAsset.ArchiveName ?? string.Empty;
    public string Glyph => Composite is not null ? "\uE902" : Asset?.Kind switch
    {
        AssetKind.Sound => "\uE8D6",
        AssetKind.Model => "\uE809",
        AssetKind.Font => "\uE8D2",
        AssetKind.MbTable => "\uE8A5",
        AssetKind.Other => "\uE8A5",
        _ => "\uEB9F"
    };

    public string Subtitle
    {
        get => _subtitle;
        set => SetField(ref _subtitle, value);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class MbTableViewModel
{
    public MbTableViewModel(
        AssetEntry asset,
        string tableName,
        string path,
        string summary,
        IReadOnlyList<string> headers,
        IReadOnlyList<int> activeColumns,
        IReadOnlyList<MbRecordViewModel> records)
    {
        Asset = asset;
        TableName = tableName;
        Path = path;
        Summary = summary;
        Headers = headers;
        ActiveColumns = activeColumns;
        Records = records;
    }

    public AssetEntry Asset { get; }
    public string TableName { get; }
    public string Path { get; }
    public string Summary { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<int> ActiveColumns { get; }
    public IReadOnlyList<MbRecordViewModel> Records { get; }
}

public sealed class MbRecordViewModel
{
    public MbRecordViewModel(int sourceRow, string title, string previewText, string[] row)
    {
        SourceRow = sourceRow;
        Title = title;
        PreviewText = previewText;
        Row = row;
    }

    public int SourceRow { get; }
    public string SourceRowText => $"#{SourceRow:N0}";
    public string Title { get; }
    public string PreviewText { get; }
    public string[] Row { get; }
}

public sealed class MbFieldViewModel
{
    public MbFieldViewModel(string name, string value, string note, int columnIndex)
    {
        Name = name;
        Value = value;
        Note = note;
        ColumnIndex = columnIndex;
    }

    public string Name { get; }
    public string Value { get; }
    public string Note { get; }
    public int ColumnIndex { get; }
    public string IndexText => $"字段 {ColumnIndex + 1}";
}

public sealed class DungeonSummaryViewModel
{
    public DungeonSummaryViewModel(string name, string subtitle, IReadOnlyList<DungeonMonsterViewModel> monsters)
    {
        Name = name;
        Subtitle = subtitle;
        Monsters = monsters;
    }

    public string Name { get; }
    public string Subtitle { get; }
    public IReadOnlyList<DungeonMonsterViewModel> Monsters { get; }
}

public sealed class DungeonMonsterViewModel : INotifyPropertyChanged
{
    private ImageSource? _portrait;

    public string Name { get; init; } = string.Empty;
    public string RoleId { get; init; } = string.Empty;
    public string FightId { get; init; } = string.Empty;
    public string PicId { get; init; } = string.Empty;
    public string IconName { get; init; } = string.Empty;
    public string KindText { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string CompactStats { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string HiddenSummary { get; init; } = string.Empty;
    public string SourceIds { get; set; } = string.Empty;
    public int SortRank { get; init; }
    public IReadOnlyList<StatLineViewModel> Stats { get; init; } = Array.Empty<StatLineViewModel>();
    public IReadOnlyList<DropLineViewModel> Drops { get; init; } = Array.Empty<DropLineViewModel>();

    public ImageSource? Portrait
    {
        get => _portrait;
        set
        {
            if (Equals(_portrait, value)) return;
            _portrait = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Portrait)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record StatLineViewModel(string Label, string Value, string Note);

public sealed record DropLineViewModel(string Name, string Detail);

public sealed class GlobalSearchResultViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string MatchReason { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public int SortRank { get; init; }
    public AssetEntry? Asset { get; init; }
    public IReadOnlyList<GlobalSearchLinkViewModel> Links { get; init; } = Array.Empty<GlobalSearchLinkViewModel>();
    public IReadOnlyList<GlobalSearchFactViewModel> Facts { get; init; } = Array.Empty<GlobalSearchFactViewModel>();
    public IReadOnlyList<GlobalSearchSkillViewModel> Skills { get; init; } = Array.Empty<GlobalSearchSkillViewModel>();
    public IReadOnlyList<GlobalSearchResourceSectionViewModel> ResourceSections =>
        GlobalSearchResourceSectionViewModel.Build(Links);

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (Equals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record GlobalSearchFactViewModel(string Label, string Value);

public sealed class GlobalSearchSkillViewModel : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public GlobalSearchSkillViewModel(
        string name,
        string id,
        string unlockText,
        string description,
        AssetEntry? iconAsset)
    {
        Name = name;
        Id = id;
        UnlockText = unlockText;
        Description = description;
        IconAsset = iconAsset;
    }

    public string Name { get; }
    public string Id { get; }
    public string UnlockText { get; }
    public string Description { get; }
    public AssetEntry? IconAsset { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Equals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record GlobalSearchResourceSectionViewModel(
    string Title,
    string EmptyText,
    IReadOnlyList<GlobalSearchLinkViewModel> Links)
{
    public static IReadOnlyList<GlobalSearchResourceSectionViewModel> Build(IReadOnlyList<GlobalSearchLinkViewModel> links)
    {
        if (links.Count == 0) return Array.Empty<GlobalSearchResourceSectionViewModel>();

        var used = new HashSet<GlobalSearchLinkViewModel>();
        var sections = new List<GlobalSearchResourceSectionViewModel>();

        AddSection(sections, used, "展示图标", "没有找到图标。", links.Where(IsIconLink));
        AddSection(sections, used, "模型 / 外观", "没有找到模型配置。", links.Where(IsModelLink));
        AddSection(sections, used, "贴图 / 图片", "没有找到贴图资源。", links.Where(IsTextureLink));
        AddSection(sections, used, "物品 / 部件", "没有找到关联物品。", links.Where(IsItemLink));
        AddSection(sections, used, "资料来源", "没有来源记录。", links.Where(IsSourceLink));
        AddSection(sections, used, "其他线索", "没有其他线索。", links.Where(link => !used.Contains(link)));

        return sections;
    }

    private static void AddSection(
        ICollection<GlobalSearchResourceSectionViewModel> sections,
        ISet<GlobalSearchLinkViewModel> used,
        string title,
        string emptyText,
        IEnumerable<GlobalSearchLinkViewModel> source)
    {
        GlobalSearchLinkViewModel[] sectionLinks = source
            .Where(link => used.Add(link))
            .Take(24)
            .ToArray();
        if (sectionLinks.Length == 0) return;
        sections.Add(new GlobalSearchResourceSectionViewModel(title, emptyText, sectionLinks));
    }

    private static bool IsIconLink(GlobalSearchLinkViewModel link) =>
        ContainsAny(link.Kind, "图标", "头像") ||
        ContainsAny(link.Path, "/icon/", "\\icon\\", "portrait") ||
        IsImageLink(link) && !ContainsAny(link.Path, "/texture/", "\\texture\\");

    private static bool IsTextureLink(GlobalSearchLinkViewModel link) =>
        IsImageLink(link) && !IsIconLink(link) ||
        ContainsAny(link.Kind, "贴图", "图像");

    private static bool IsImageLink(GlobalSearchLinkViewModel link) =>
        link.Asset?.Kind == AssetKind.Image ||
        HasExtension(link.Path, ".png", ".jpg", ".jpeg", ".dds", ".tga", ".ico");

    private static bool IsModelLink(GlobalSearchLinkViewModel link) =>
        link.Asset?.Kind == AssetKind.Model ||
        ContainsAny(link.Kind, "模型", "外观") ||
        HasExtension(link.Path, ".pmf", ".cct", ".cmf", ".psf", ".paf");

    private static bool IsItemLink(GlobalSearchLinkViewModel link) =>
        ContainsAny(link.Kind, "物品", "部件", "套装", "技能", "状态");

    private static bool IsSourceLink(GlobalSearchLinkViewModel link) =>
        ContainsAny(link.Kind, "源", "来源", "MB 表") ||
        HasExtension(link.Path, ".txt");

    private static bool HasExtension(string value, params string[] extensions) =>
        extensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

public sealed class GlobalSearchLinkViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public GlobalSearchLinkViewModel(
        string kind,
        string name,
        string path,
        string detail,
        AssetEntry? asset)
    {
        Kind = kind;
        Name = name;
        Path = path;
        Detail = detail;
        Asset = asset;
    }

    public string Kind { get; }
    public string Name { get; }
    public string Path { get; }
    public string Detail { get; }
    public AssetEntry? Asset { get; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (Equals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public readonly record struct PmfBoneIndices(ushort X, ushort Y, ushort Z, ushort W)
{
    public ushort this[int index] => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        3 => W,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public sealed record PmfMesh(
    IReadOnlyList<System.Numerics.Vector3> Vertices,
    IReadOnlyList<System.Numerics.Vector2> TextureCoordinates,
    IReadOnlyList<ushort> Indices,
    uint Version,
    uint VertexFlags,
    uint UvChannelCount,
    uint DeclaredTriangleCount)
{
    public IReadOnlyList<System.Numerics.Vector4> BoneWeights { get; init; } =
        Array.Empty<System.Numerics.Vector4>();

    public IReadOnlyList<PmfBoneIndices> BoneIndices { get; init; } =
        Array.Empty<PmfBoneIndices>();

    public bool HasSkinning =>
        BoneWeights.Count == Vertices.Count &&
        BoneIndices.Count == Vertices.Count;
}

public sealed record ModelAnimationReference(
    string Name,
    AssetEntry Asset,
    int Priority);

public sealed record SkeletonBone(
    int Index,
    string Name,
    int ParentIndex,
    System.Numerics.Vector3 BindTranslation,
    System.Numerics.Quaternion BindRotation,
    System.Numerics.Vector3 InverseBindTranslation,
    System.Numerics.Quaternion InverseBindRotation,
    IReadOnlyList<int> Children);

public sealed record SkeletonData(
    int CoreBoneCount,
    IReadOnlyList<SkeletonBone> Bones);

public sealed record AnimationTrack(
    int BoneIndex,
    IReadOnlyList<System.Numerics.Quaternion> RotationKeys,
    IReadOnlyList<System.Numerics.Vector3> TranslationKeys);

public sealed record SkeletalAnimation(
    string Name,
    int SampleRate,
    float Duration,
    IReadOnlyDictionary<int, AnimationTrack> Tracks,
    AssetEntry SourceAsset)
{
    public int FrameCount => Math.Max(
        1,
        Tracks.Count == 0
            ? (int)MathF.Round(Duration * SampleRate) + 1
            : Tracks.Values.Max(track => Math.Max(track.RotationKeys.Count, track.TranslationKeys.Count)));

    public override string ToString() => Name;
}

public sealed record ModelAnimationSet(
    SkeletonData Skeleton,
    IReadOnlyList<SkeletalAnimation> Animations);

public sealed record DecodedTexture(
    int Width,
    int Height,
    byte[] BgraPixels,
    string Format);

public sealed record ModelTextureBinding(
    AssetEntry TextureAsset,
    string MapType,
    string MaterialName,
    string ConfigPath)
{
    public string DisplayName => string.IsNullOrWhiteSpace(MaterialName)
        ? $"{MapType} · {TextureAsset.Name}"
        : $"{MaterialName} · {MapType} · {TextureAsset.Name}";

    public override string ToString() => DisplayName;
}

public sealed record CompositeModelPart(
    AssetEntry MeshAsset,
    string MaterialName,
    ModelTextureBinding? TextureBinding);

public sealed record CompositeModelEntry(
    string Name,
    AssetEntry ConfigAsset,
    IReadOnlyList<CompositeModelPart> Parts)
{
    public string DisplayPath => $"{ConfigAsset.ArchiveName}  /  {ConfigAsset.Entry.Path}";

    public AssetEntry? SkeletonAsset { get; init; }

    public IReadOnlyList<ModelAnimationReference> Animations { get; init; } =
        Array.Empty<ModelAnimationReference>();
}

public sealed record ModelRenderPart(
    string Name,
    PmfMesh Mesh,
    DecodedTexture? Texture,
    string TextureName);
