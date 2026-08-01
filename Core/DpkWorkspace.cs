using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed class DpkWorkspace : IDisposable
{
    private const string MbArchiveName = "mb.dpk";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".dds", ".tga", ".ico"
    };

    private static readonly HashSet<string> SoundExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".ogg"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".ttc"
    };

    private static readonly string[] PreferredArchiveOrder =
    {
        "gui.dpk", "font.dpk", "sound.dpk", "music.dpk", "obj.dpk", "cha.dpk",
        "gfx.dpk", "scn.dpk", "terr.dpk", "water.dpk", "sky.dpk", "movie.dpk", "system.dpk"
    };

    private readonly Dictionary<string, DpkReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssetEntry> _assets = new();
    private ModelTextureResolver? _modelTextureResolver;

    public IReadOnlyList<AssetEntry> Assets => _assets;
    public IReadOnlyCollection<string> ArchivePaths => _readers.Keys;

    public void Clear()
    {
        foreach (DpkReader reader in _readers.Values) reader.Dispose();
        _readers.Clear();
        _assets.Clear();
        _modelTextureResolver = null;
    }

    public void OpenClientResourceFolder(string folder)
    {
        var archiveOrder = PreferredArchiveOrder
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        string[] archives = Directory.GetFiles(folder, "*.dpk", SearchOption.TopDirectoryOnly)
            .OrderBy(path => archiveOrder.TryGetValue(System.IO.Path.GetFileName(path), out int index) ? index : int.MaxValue)
            .ThenBy(path => System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (archives.Length == 0)
            throw new DirectoryNotFoundException("所选目录中没有 DPK 文件。");

        Clear();
        foreach (string archive in archives) AddArchive(archive);
        string? clientRoot = Directory.GetParent(System.IO.Path.GetFullPath(folder).TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar))?.FullName;
        string? mbArchive = clientRoot is null ? null : System.IO.Path.Combine(clientRoot, MbArchiveName);
        if (mbArchive is not null && File.Exists(mbArchive))
            AddArchive(mbArchive, AssetKind.MbTable);
    }

    public void OpenSingleArchive(string path)
    {
        Clear();
        AssetKind? forcedKind = System.IO.Path.GetFileName(path).Equals(MbArchiveName, StringComparison.OrdinalIgnoreCase)
            ? AssetKind.MbTable
            : null;
        AddArchive(path, forcedKind);
    }

    public byte[] Extract(AssetEntry asset) => _readers[asset.ArchivePath].Extract(asset.Entry);

    public IReadOnlyList<ModelTextureBinding> ResolveModelTextures(AssetEntry model) =>
        (_modelTextureResolver ??= new ModelTextureResolver(this)).Resolve(model);

    public IReadOnlyList<CompositeModelEntry> FindCompositeModels(string archivePath, string folderPath) =>
        (_modelTextureResolver ??= new ModelTextureResolver(this)).FindComposites(archivePath, folderPath);

    public void ExtractTo(AssetEntry asset, string rootFolder)
    {
        string archiveFolder = System.IO.Path.GetFileNameWithoutExtension(asset.ArchivePath);
        string relative = asset.Entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        string fullRoot = System.IO.Path.GetFullPath(rootFolder);
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, archiveFolder, relative));
        if (!target.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源路径试图越出导出目录。");
        _readers[asset.ArchivePath].ExtractTo(asset.Entry, target);
    }

    private void AddArchive(string path, AssetKind? forcedKind = null)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (_readers.ContainsKey(fullPath)) return;
        var reader = new DpkReader(fullPath);
        try
        {
            IReadOnlyList<DpkEntry> entries = reader.ReadEntries();
            _readers.Add(fullPath, reader);
            foreach (DpkEntry entry in entries)
                _assets.Add(new AssetEntry(fullPath, entry, forcedKind ?? Classify(entry.Path)));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static AssetKind Classify(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) return AssetKind.Image;
        if (SoundExtensions.Contains(extension)) return AssetKind.Sound;
        if (extension.Equals(".pmf", StringComparison.OrdinalIgnoreCase)) return AssetKind.Model;
        if (FontExtensions.Contains(extension)) return AssetKind.Font;
        return AssetKind.Other;
    }

    public void Dispose() => Clear();
}
