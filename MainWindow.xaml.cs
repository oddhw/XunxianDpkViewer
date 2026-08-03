using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Xml.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;
using XunxianDpkViewer.Controls;
using XunxianDpkViewer.Core;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer;

public sealed partial class MainWindow : Window
{
    private static readonly string AppVersion =
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0";
    private const string AppAuthor = "黑风岭-梵心似火";
    private readonly DpkWorkspace _workspace = new();
    private List<AssetItemViewModel> _items = new();
    private readonly DispatcherTimer _searchTimer;
    private readonly MediaPlayer _mediaPlayer = new();
    private readonly MediaPlayerElement _audioPlayer;
    private readonly ModelPreviewControl _modelPreview;
    private readonly ModelPreviewControl _globalModelPreview;
    private readonly UpdateService _updateService = new();
    private readonly SemaphoreSlim _globalSearchLock = new(1, 1);
    private CancellationTokenSource? _globalSearchCancellation;
    private List<AssetEntry> _filteredAssets = new();
    private AssetKind _currentKind = AssetKind.Image;
    private AssetEntry? _selectedAsset;
    private CompositeModelEntry? _selectedComposite;
    private MbTableViewModel? _currentMbTableView;
    private List<GlobalSearchResultViewModel> _globalSearchResults = new();
    private List<DungeonSummaryViewModel> _dungeonSummaries = new();
    private DungeonSummaryViewModel? _selectedDungeonSummary;
    private bool _dungeonSummariesBuilt;
    private int _sortMode;
    private int _thumbnailGeneration;
    private int _globalSearchGeneration;
    private int _globalDetailPreviewGeneration;
    private bool _isBusy;
    private bool _modelExpanded;
    private bool _buildingFolderTree;
    private bool _multiSelectMode;
    private bool _settingModelTextureSelection;
    private bool _checkingForUpdates;
    private FolderNodeInfo? _selectedFolder;
    private int _previewGeneration;
    private readonly Dictionary<string, string> _mbSearchTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _globalSearchTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string[]>> _mbRowsByPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string[]>? _legendEquipAtbs;
    private Dictionary<string, string[]>? _legendEquipAtbValues;
    private Dictionary<string, string[]>? _chaFightRows;
    private Dictionary<string, string[]>? _chaPicRows;
    private Dictionary<string, string[]>? _stateDataRows;
    private Dictionary<string, string[]>? _stateGroupRows;
    private Dictionary<string, string[]>? _chaListRows;
    private Dictionary<string, string[]>? _itemRandRows;
    private Dictionary<string, string>? _itemNameById;
    private Dictionary<string, string>? _itemIconReferenceById;
    private Dictionary<string, AssetEntry>? _globalImageAssetByKey;
    private readonly Dictionary<string, BitmapImage> _globalThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string[]>>? _chaSkillRowsByPicId;
    private Dictionary<string, List<string[]>>? _globalCarSkillRowsByEntity;
    private Dictionary<string, (string Path, string[] Row)>? _globalSkillDataRows;

    private static readonly HashSet<string> GlobalSearchTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".xml", ".cct", ".cmf", ".cfg", ".ini", ".lua", ".json"
    };

    private static IReadOnlySet<string> Ids(params string[] ids) =>
        new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> Labels(params (string Id, string Label)[] labels) =>
        labels.ToDictionary(item => item.Id, item => item.Label, StringComparer.OrdinalIgnoreCase);

    private static readonly DungeonDefinition[] DungeonDefinitions =
    {
        new("葬龙渊", "170 级起 · 龙宫线副本",
            new[] { "葬龙渊", "zanglongyuan", "fb_zanglongyuan" },
            new[] { "41412", "41413", "41414", "41415", "41416", "41417", "41418", "41419", "41420", "41421" },
            new[] { "41412" },
            Array.Empty<IntRange>(),
            Labels(("41412", "困难首领"))),
        new("寒潭禁地", "180 级起 · 六百里号山",
            new[] { "寒潭禁地", "fb_liubailihaoshan" },
            new[] { "41512", "41513", "41514", "41531", "41532", "41534" },
            new[] { "41512", "41513", "41514", "41532" },
            Array.Empty<IntRange>(),
            Labels(
                ("41512", "首领"),
                ("41513", "首领"),
                ("41514", "首领"),
                ("41531", "宝箱/秘宝"),
                ("41532", "首领"),
                ("41534", "小怪")),
            MechanismRoleIds: Ids("41531")),
        new("玄水塔", "180 级起 · 封魔塔",
            new[] { "玄水塔", "封魔塔·玄水塔", "fb_shuita", "玄水峰" },
            new[] { "30501", "30502", "30503", "30504" },
            new[] { "30501", "30502", "30503", "30504" },
            Array.Empty<IntRange>(),
            Labels(
                ("30501", "首领"),
                ("30502", "首领"),
                ("30503", "首领"),
                ("30504", "首领"))),
        new("响马寨", "210 级起 · 高昌县",
            new[] { "响马寨", "fb_gaochangxian", "gcx_xiangmazhai" },
            new[] { "42055", "42056", "42057", "42058", "42059", "42060", "42061", "42062", "42063", "42064", "42065", "42077", "42078", "42080" },
            new[] { "42055", "42056", "42057", "42058", "42059" },
            Array.Empty<IntRange>(),
            Labels(
                ("42055", "首领"),
                ("42056", "首领"),
                ("42057", "首领"),
                ("42058", "首领"),
                ("42059", "首领"))),
        new("千秋莲华阵·天权阵眼", "210 级起 · 元寿山",
            new[] { "千秋莲华阵·天权阵眼", "fb_ssjshangu" },
            new[] { "42185" },
            new[] { "42185" },
            Array.Empty<IntRange>(),
            Labels(("42185", "首领"))),
        new("千秋莲华阵·天枢阵眼", "210 级起 · 元寿山",
            new[] { "千秋莲华阵·天枢阵眼", "fb_ssjshanmen" },
            new[] { "42194" },
            new[] { "42194" },
            Array.Empty<IntRange>(),
            Labels(("42194", "首领"))),
        new("千秋莲华阵·天玑阵眼", "210 级起 · 元寿山",
            new[] { "千秋莲华阵·天玑阵眼", "fb_ssjshulin" },
            new[] { "42192" },
            new[] { "42192" },
            Array.Empty<IntRange>(),
            Labels(("42192", "首领"))),
        new("千秋莲华阵·天璇阵眼", "210 级起 · 元寿山",
            new[] { "千秋莲华阵·天璇阵眼", "fb_ssjxiulianchang" },
            new[] { "42195" },
            new[] { "42195" },
            Array.Empty<IntRange>(),
            Labels(("42195", "首领"))),
        new("桃香谷", "210 级起 · 万寿山",
            new[] { "桃香谷", "万寿山·桃香谷", "fb_wsstaoxianggu" },
            new[] { "31512", "31513", "31514", "31515", "31516", "31517", "31518", "31519", "31520", "31521", "31522" },
            new[] { "31515", "31522" },
            Array.Empty<IntRange>(),
            Labels(
                ("31515", "首领"),
                ("31522", "首领"))),
        new("盘丝洞", "240 级起 · 乌鸡国",
            new[] { "盘丝洞", "乌鸡国·盘丝洞", "fb_pansidong" },
            new[] { "31577", "31578", "31579", "31580", "31581", "31585", "31586" },
            new[] { "31577", "31578", "31585", "31586" },
            Array.Empty<IntRange>(),
            Labels(
                ("31577", "困难首领"),
                ("31578", "困难首领"),
                ("31585", "简单首领"),
                ("31586", "简单首领"))),
        new("七星定魂", "250 级起 · 乌鸡国",
            new[] { "七星定魂", "fb_wujiguo1" },
            new[]
            {
                "31648", "31649", "31650", "31651", "31652", "31653", "31654", "31655",
                "31656", "31657", "31658", "31659", "31660", "31661", "31662"
            },
            new[] { "31648", "31649", "31650", "31651", "31652" },
            Array.Empty<IntRange>(),
            Labels(
                ("31648", "首领/核心"),
                ("31649", "首领"),
                ("31650", "首领"),
                ("31651", "首领"),
                ("31652", "首领"))),
        new("车迟国斗法", "260 级起",
            new[] { "车迟国斗法", "fb_chechiguo" },
            new[] { "31697", "31698", "31699", "31731", "31732", "31733" },
            new[] { "31697", "31698", "31699", "31731", "31732", "31733" },
            Array.Empty<IntRange>(),
            Labels(
                ("31697", "困难首领"),
                ("31698", "困难首领"),
                ("31699", "困难首领"),
                ("31731", "简单首领"),
                ("31732", "简单首领"),
                ("31733", "简单首领")))
    };

    private bool UseBeginnerNames => false;

    static MainWindow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public MainWindow()
    {
        InitializeComponent();
        _audioPlayer = new MediaPlayerElement { AreTransportControlsEnabled = true };
        AudioPlayerHost.Content = _audioPlayer;
        _modelPreview = new ModelPreviewControl();
        ModelPreviewHost.Content = _modelPreview;
        _globalModelPreview = new ModelPreviewControl();
        GlobalSearchModelPreviewHost.Content = _globalModelPreview;
        _modelPreview.AnimationExportRequested += ModelPreview_AnimationExportRequested;
        _globalModelPreview.AnimationExportRequested += ModelPreview_AnimationExportRequested;
        ExtendsContentIntoTitleBar = false;
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Xunxian.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        AppWindow.Resize(new SizeInt32(
            Math.Min(1600, Math.Max(1100, displayArea.WorkArea.Width - 32)),
            Math.Min(960, Math.Max(720, displayArea.WorkArea.Height - 32))));

        ImageGrid.ItemsSource = _items;
        AssetList.ItemsSource = _items;
        MbAssetList.ItemsSource = _items;
        _audioPlayer.SetMediaPlayer(_mediaPlayer);
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        Closed += (_, _) =>
        {
            _mediaPlayer.Dispose();
            _workspace.Dispose();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = CheckForUpdatesAsync(silent: true);

        string? explicitArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--resource-folder=", StringComparison.OrdinalIgnoreCase));
        string? explicitFolder = explicitArgument is null
            ? null
            : explicitArgument[(explicitArgument.IndexOf('=') + 1)..].Trim('"');
        if (!string.IsNullOrWhiteSpace(explicitFolder))
        {
            await LoadResourceFolderAsync(explicitFolder);
            return;
        }

        string? rememberedFolder = UserPreferences.LoadResourceFolder();
        if (!string.IsNullOrWhiteSpace(rememberedFolder) &&
            File.Exists(System.IO.Path.Combine(rememberedFolder, "gui.dpk")))
        {
            await LoadResourceFolderAsync(rememberedFolder);
            return;
        }

        PathSetupPanel.Visibility = Visibility.Visible;
        SetStatus("首次使用，请选择《新寻仙》客户端或 res 目录");
    }

    private async Task LoadResourceFolderAsync(string folder)
    {
        if (_isBusy) return;
        SetBusy(true, "正在读取 DPK 索引…");
        try
        {
            string resourceFolder = ResolveResourceFolder(folder);
            await Task.Run(() => _workspace.OpenClientResourceFolder(resourceFolder));
            UserPreferences.SaveResourceFolder(resourceFolder);
            CurrentPathText.Text = resourceFolder;
            PathSetupPanel.Visibility = Visibility.Collapsed;
            AfterWorkspaceLoaded();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("资源目录读取失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadArchiveAsync(string path)
    {
        if (_isBusy) return;
        SetBusy(true, "正在读取 DPK 索引…");
        try
        {
            await Task.Run(() => _workspace.OpenSingleArchive(path));
            CurrentPathText.Text = path;
            PathSetupPanel.Visibility = Visibility.Collapsed;
            AfterWorkspaceLoaded();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("DPK 读取失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AfterWorkspaceLoaded()
    {
        _mbSearchTextCache.Clear();
        _mbRowsByPath.Clear();
        _legendEquipAtbs = null;
        _legendEquipAtbValues = null;
        _globalSearchTextCache.Clear();
        _chaFightRows = null;
        _chaPicRows = null;
        _stateDataRows = null;
        _stateGroupRows = null;
        _chaListRows = null;
        _itemRandRows = null;
        _itemNameById = null;
        _itemIconReferenceById = null;
        _globalImageAssetByKey = null;
        _globalThumbnailCache.Clear();
        _chaSkillRowsByPicId = null;
        _globalCarSkillRowsByEntity = null;
        _globalSkillDataRows = null;
        _globalSearchResults.Clear();
        ClearGlobalSearchView();
        _dungeonSummaries.Clear();
        _selectedDungeonSummary = null;
        _dungeonSummariesBuilt = false;
        int images = _workspace.Assets.Count(asset => asset.Kind == AssetKind.Image);
        int sounds = _workspace.Assets.Count(asset => asset.Kind == AssetKind.Sound);
        int models = _workspace.Assets.Count(asset => asset.Kind == AssetKind.Model);
        int fonts = _workspace.Assets.Count(asset => asset.Kind == AssetKind.Font);
        int mbTables = _workspace.Assets.Count(asset => asset.Kind == AssetKind.MbTable);
        int others = _workspace.Assets.Count(asset => asset.Kind == AssetKind.Other);
        string mbSummary = mbTables > 0 ? $" · {mbTables:N0} MB表" : string.Empty;
        ArchiveSummaryText.Text = $"{_workspace.ArchivePaths.Count} 个包 · {images:N0} 图像 · {sounds:N0} 声音 · {models:N0} 模型 · {fonts:N0} 字体{mbSummary} · {others:N0} 其他";
        BatchExportButton.IsEnabled = true;
        BuildFolderTree();
        ApplyFilter();
    }

    private static string ResolveResourceFolder(string selectedFolder)
    {
        string fullPath = System.IO.Path.GetFullPath(selectedFolder);
        string nestedRes = System.IO.Path.Combine(fullPath, "res");
        if (File.Exists(System.IO.Path.Combine(fullPath, "gui.dpk"))) return fullPath;
        if (File.Exists(System.IO.Path.Combine(nestedRes, "gui.dpk"))) return nestedRes;
        throw new DirectoryNotFoundException("所选位置不是有效的新寻仙客户端目录：没有找到 res\\gui.dpk。");
    }

    private void BuildFolderTree()
    {
        _buildingFolderTree = true;
        FolderTree.RootNodes.Clear();
        _selectedFolder = null;

        TreeViewNode? firstRoot = null;
        TreeViewNode? preferredNode = null;
        string preferredArchive = _currentKind switch
        {
            AssetKind.Image => "gui.dpk",
            AssetKind.Sound => "sound.dpk",
            AssetKind.Model => "obj.dpk",
            AssetKind.Font => "font.dpk",
            AssetKind.MbTable => "mb.dpk",
            AssetKind.Other => "gfx.dpk",
            _ => string.Empty
        };
        foreach (IGrouping<string, AssetEntry> archive in _workspace.Assets
                     .Where(asset => asset.Kind == _currentKind)
                     .GroupBy(asset => asset.ArchivePath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => string.Equals(System.IO.Path.GetFileName(group.Key), preferredArchive, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(group => System.IO.Path.GetFileName(group.Key), StringComparer.OrdinalIgnoreCase))
        {
            string archiveName = System.IO.Path.GetFileName(archive.Key);
            string archiveDisplayName = UseBeginnerNames
                ? ResourceExplanationService.GetArchiveDisplayName(archiveName)
                : archiveName;
            var root = new TreeViewNode
            {
                Content = new FolderNodeInfo(archiveDisplayName, archive.Key, string.Empty),
                IsExpanded = false
            };
            FolderTree.RootNodes.Add(root);
            firstRoot ??= root;
            if (string.Equals(archiveName, preferredArchive, StringComparison.OrdinalIgnoreCase))
                preferredNode ??= root;

            var nodesByPath = new Dictionary<string, TreeViewNode>(StringComparer.OrdinalIgnoreCase)
            {
                [string.Empty] = root
            };
            var directorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string assetDirectory in archive.Select(asset => GetInternalDirectory(asset.Entry.Path)))
            {
                string directory = assetDirectory;
                while (directory.Length > 0 && directorySet.Add(directory))
                {
                    int parentSlash = directory.LastIndexOf('/');
                    directory = parentSlash < 0 ? string.Empty : directory[..parentSlash];
                }
            }
            IEnumerable<string> directories = directorySet
                .OrderBy(path => path.Count(character => character == '/'))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (string directory in directories)
            {
                int slash = directory.LastIndexOf('/');
                string parentPath = slash < 0 ? string.Empty : directory[..slash];
                string folderName = slash < 0 ? directory : directory[(slash + 1)..];
                string folderDisplayName = UseBeginnerNames
                    ? ResourceExplanationService.GetFolderDisplayName(folderName)
                    : folderName;
                if (!nodesByPath.TryGetValue(parentPath, out TreeViewNode? parent)) continue;

                var node = new TreeViewNode
                {
                    Content = new FolderNodeInfo(folderDisplayName, archive.Key, directory)
                };
                parent.Children.Add(node);
                nodesByPath[directory] = node;
            }

        }

        TreeViewNode? initialNode = preferredNode ?? firstRoot;
        if (initialNode?.Content is FolderNodeInfo firstFolder)
        {
            _selectedFolder = firstFolder;
            FolderTree.SelectedNode = initialNode;
            CurrentFolderText.Text = firstFolder.DisplayPath;
        }
        else
        {
            CurrentFolderText.Text = "当前分类没有资源目录";
        }
        _buildingFolderTree = false;
    }

    private void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_buildingFolderTree || sender.SelectedNode?.Content is not FolderNodeInfo folder) return;
        _selectedFolder = folder;
        CurrentFolderText.Text = folder.DisplayPath;
        ApplyFilter();
    }

    private bool IsAssetInSelectedFolder(AssetEntry asset)
    {
        if (_selectedFolder is null) return false;
        if (!string.Equals(asset.ArchivePath, _selectedFolder.ArchivePath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_currentKind == AssetKind.MbTable && !string.IsNullOrWhiteSpace(SearchBox.Text))
            return true;

        string assetDirectory = GetInternalDirectory(asset.Entry.Path);
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            return string.Equals(assetDirectory, _selectedFolder.InternalPath, StringComparison.OrdinalIgnoreCase);

        if (_selectedFolder.InternalPath.Length == 0) return true;
        return string.Equals(assetDirectory, _selectedFolder.InternalPath, StringComparison.OrdinalIgnoreCase) ||
               assetDirectory.StartsWith(_selectedFolder.InternalPath + '/', StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInternalDirectory(string entryPath)
    {
        string normalized = entryPath.Replace('\\', '/').Trim('/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private void ApplyFilter()
    {
        if (_currentKind == AssetKind.GlobalSearch)
        {
            RefreshGlobalSearch();
            return;
        }

        if (_currentKind == AssetKind.DungeonSummary)
        {
            RefreshDungeonSummary();
            return;
        }

        string[] terms = GetSearchTerms();
        IEnumerable<AssetEntry> query = _workspace.Assets
            .Where(asset => asset.Kind == _currentKind)
            .Where(IsAssetInSelectedFolder)
            .Where(asset => terms.Length == 0 || terms.All(term => AssetMatchesSearchTerm(asset, term)));
        query = _sortMode switch
        {
            1 => query.OrderByDescending(asset => asset.Name, NaturalStringComparer.Instance),
            2 => query.OrderBy(asset => asset.Extension, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(asset => asset.Name, NaturalStringComparer.Instance),
            3 => query.OrderBy(asset => asset.Entry.Path, NaturalStringComparer.Instance),
            4 => query,
            _ => query.OrderBy(asset => asset.Name, NaturalStringComparer.Instance)
        };
        _filteredAssets = query.ToList();
        PopulateAssets();
    }

    private string[] GetSearchTerms() => SearchBox.Text
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private bool AssetMatchesSearchTerm(AssetEntry asset, string term)
    {
        string[] variants = GetSearchTermVariants(term).ToArray();
        if (variants.Any(variant =>
                asset.Entry.Path.Contains(variant, StringComparison.OrdinalIgnoreCase) ||
                asset.ArchiveName.Contains(variant, StringComparison.OrdinalIgnoreCase) ||
                (UseBeginnerNames && ResourceExplanationService.GetSearchText(asset)
                    .Contains(variant, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return asset.Kind == AssetKind.MbTable &&
               term.Length >= 2 &&
               TryGetMbSearchText(asset, out string text) &&
               variants.Any(variant => text.Contains(variant, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetMbSearchText(AssetEntry asset, out string text)
    {
        string key = asset.DisplayPath;
        if (_mbSearchTextCache.TryGetValue(key, out string? cachedText))
        {
            text = cachedText;
            return text.Length > 0;
        }

        try
        {
            byte[] data = _workspace.Extract(asset);
            if (!TryDecodeTextPreview(asset, data, out text))
                text = string.Empty;
        }
        catch
        {
            text = string.Empty;
        }

        _mbSearchTextCache[key] = text;
        return text.Length > 0;
    }

    private async void RefreshGlobalSearch()
    {
        int generation = ++_globalSearchGeneration;
        _globalSearchCancellation?.Cancel();
        _globalSearchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _globalSearchCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        string[] terms = GetSearchTerms();
        if (_workspace.Assets.Count == 0)
        {
            _globalSearchResults.Clear();
            ClearGlobalSearchView();
            GlobalSearchStatusText.Text = "请先打开资源目录或 DPK。";
            GlobalSearchCountText.Text = "0 条";
            return;
        }

        if (terms.Length == 0)
        {
            _globalSearchResults.Clear();
            ClearGlobalSearchView();
            GlobalSearchStatusText.Text = "输入名称、ID、图标号或路径后开始反查。";
            GlobalSearchCountText.Text = "0 条";
            return;
        }

        GlobalSearchStatusText.Text = "正在整理资料卡，匹配名称、图标、物品部件和关联资源...";
        GlobalSearchCountText.Text = "扫描中";
        GlobalSearchEmptyPanel.Visibility = Visibility.Collapsed;
        BusyRing.IsActive = true;

        AssetEntry[] assets = _workspace.Assets.ToArray();
        bool lockTaken = false;
        try
        {
            await _globalSearchLock.WaitAsync(token);
            lockTaken = true;
            token.ThrowIfCancellationRequested();
            if (generation != _globalSearchGeneration) return;
            List<GlobalSearchResultViewModel> results = await Task.Run(() => BuildGlobalSearchResults(assets, terms, token), token);
            token.ThrowIfCancellationRequested();
            if (generation != _globalSearchGeneration) return;

            _globalSearchResults = results;
            GlobalSearchResultList.ItemsSource = _globalSearchResults;
            GlobalSearchResultCountText.Text = $"{_globalSearchResults.Count:N0} 条";
            GlobalSearchCountText.Text = $"{_globalSearchResults.Count:N0} 条";
            GlobalSearchEmptyPanel.Visibility = _globalSearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GlobalSearchStatusText.Text = _globalSearchResults.Count == 0
                ? "没有找到匹配项。可以换名称、ID、图标号或更短的关键词再查。"
                : $"已整理出 {_globalSearchResults.Count:N0} 张资料卡，相关图标和可跳转资源会显示在右侧。";

            if (_globalSearchResults.Count > 0)
            {
                GlobalSearchResultList.SelectedIndex = 0;
                ShowGlobalSearchResult(_globalSearchResults[0]);
                _ = LoadGlobalSearchThumbnailsAsync(_globalSearchResults, generation);
            }
            else
            {
                ShowGlobalSearchResult(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (lockTaken)
                _globalSearchLock.Release();
            if (generation == _globalSearchGeneration && ReferenceEquals(_globalSearchCancellation, cancellation))
                BusyRing.IsActive = false;
        }
    }

    private List<GlobalSearchResultViewModel> BuildGlobalSearchResults(
        IReadOnlyList<AssetEntry> assets,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        string[][] variants = terms
            .Select(term => GetSearchTermVariants(term).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .ToArray();
        var results = new List<GlobalSearchResultViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mbRowResults = new List<GlobalSearchResultViewModel>();
        var seenMbRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool broadSearch = ShouldRunBroadGlobalSearch(terms);
        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (AssetEntry asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string searchable = $"{asset.ArchiveName}\n{asset.Entry.Path}\n{asset.Name}";
            if (!GlobalTextMatches(searchable, variants)) continue;
            AddGlobalSearchResult(results, seen, new GlobalSearchResultViewModel
            {
                Title = asset.Kind == AssetKind.MbTable ? GetMbTableDisplayName(asset) : asset.Name,
                Subtitle = asset.DisplayPath,
                Category = GetGlobalAssetKindText(asset.Kind),
                SourcePath = asset.DisplayPath,
                PreviewText = CreateGlobalAssetSummary(asset),
                MatchReason = "资源路径或文件名命中",
                RawText = asset.DisplayPath,
                SortRank = asset.Kind == AssetKind.MbTable ? 80 : 70,
                Asset = asset,
                Links = BuildGlobalSearchLinks(assets, asset, asset.DisplayPath)
            });
        }

        foreach (AssetEntry asset in GetGlobalSearchMbTables(assets, broadSearch))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.ElapsedMilliseconds > 4500 && mbRowResults.Count > 0)
                break;
            if (mbRowResults.Count >= 520) break;
            if (!TryGetGlobalSearchText(asset, out string text)) continue;

            string[] lines = SplitTextLines(text);
            char delimiter = ChooseMbTableDelimiter(lines);
            string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
            int tableMatches = 0;
            for (int index = 0; index < lines.Length && tableMatches < 80 && mbRowResults.Count < 520; index++)
            {
                if ((index & 127) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                string line = lines[index];
                string[] row = SplitMbTableLine(line, delimiter);
                if (!GlobalMbRowMatchesSearch(normalizedPath, row, line, variants, broadSearch)) continue;
                tableMatches++;
                GlobalMbDisplay display = CreateGlobalMbDisplay(assets, asset, row, line, index + 1, includeLinks: false);
                AddGlobalSearchResult(mbRowResults, seenMbRows, new GlobalSearchResultViewModel
                {
                    Title = display.Title,
                    Subtitle = $"{asset.Entry.Path} · 第 {index + 1:N0} 行",
                    Category = display.Category,
                    SourcePath = asset.DisplayPath,
                    PreviewText = display.PreviewText,
                    MatchReason = display.MatchReason,
                    RawText = line,
                    SortRank = display.SortRank,
                    Asset = asset,
                    Links = display.Links
                });
            }
        }

        foreach (GlobalSearchResultViewModel entityResult in BuildGlobalKnowledgeCards(mbRowResults.Take(160).ToArray()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddGlobalSearchResult(results, seen, entityResult);
            if (stopwatch.ElapsedMilliseconds > 7000 && results.Count > 0)
                break;
        }

        if (broadSearch)
        {
        foreach (AssetEntry asset in assets.Where(IsGlobalSearchTextAsset).Take(120))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= 380) break;
            if (!TryGetGlobalSearchText(asset, out string text)) continue;
            if (!GlobalTextMatches(text, variants)) continue;

            string snippet = CreateFocusedGlobalSnippet(text, variants, 420);
            AddGlobalSearchResult(results, seen, new GlobalSearchResultViewModel
            {
                Title = asset.Name,
                Subtitle = asset.DisplayPath,
                Category = "配置文本",
                SourcePath = asset.DisplayPath,
                PreviewText = CreateGlobalTextAssetSummary(asset, snippet),
                MatchReason = "配置/脚本文本命中",
                RawText = snippet,
                SortRank = 90,
                Asset = asset,
                Links = BuildGlobalSearchLinks(assets, asset, snippet)
            });
        }
        }

        return results
            .OrderBy(result => result.SortRank)
            .ThenBy(result => result.Title, NaturalStringComparer.Instance)
            .Take(380)
            .ToList();
    }

    private GlobalMbDisplay CreateGlobalMbDisplay(
        IReadOnlyList<AssetEntry> assets,
        AssetEntry asset,
        IReadOnlyList<string> row,
        string rawLine,
        int sourceRow,
        bool includeLinks = true)
    {
        string tableName = GetMbTableDisplayName(asset);
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        var links = includeLinks
            ? BuildGlobalSearchLinks(assets, asset, rawLine).ToList()
            : new List<GlobalSearchLinkViewModel>();
        var linkKeys = new HashSet<string>(links.Select(link => $"{link.Kind}|{link.Path}|{link.Name}"), StringComparer.OrdinalIgnoreCase);

        string title = CreateGlobalMbRowTitle(tableName, row, sourceRow);
        string category = GetGlobalMbCategory(normalizedPath, tableName);
        string preview = normalizedPath switch
        {
            string path when path.StartsWith("help_bank/bank_tz", StringComparison.Ordinal) =>
                BuildGlobalSetSummary(row, title, links, linkKeys),
            string path when path.StartsWith("item/item_set", StringComparison.Ordinal) =>
                BuildGlobalSetConfigSummary(row, title, links, linkKeys),
            string path when path.StartsWith("item/", StringComparison.Ordinal) =>
                BuildGlobalItemSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("object/ride", StringComparison.Ordinal) =>
                BuildGlobalRideSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("object/cha_list", StringComparison.Ordinal) =>
                BuildGlobalCharacterSummary(row, title, links, linkKeys),
            string path when path.StartsWith("object/cha_fight", StringComparison.Ordinal) =>
                BuildGlobalFightSummary(row, title),
            string path when path.StartsWith("object/cha_pic", StringComparison.Ordinal) =>
                BuildGlobalAppearanceSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("object/", StringComparison.Ordinal) =>
                BuildGlobalObjectSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("quest/", StringComparison.Ordinal) =>
                BuildGlobalQuestSummary(asset, row, title),
            string path when path.StartsWith("skill/", StringComparison.Ordinal) =>
                BuildGlobalSkillSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("pet/car_skill", StringComparison.Ordinal) =>
                BuildGlobalSkillSummary(asset, row, title, links, linkKeys),
            string path when path.StartsWith("pet/", StringComparison.Ordinal) =>
                BuildGlobalPetSummary(asset, row, title, links, linkKeys),
            _ => BuildGlobalGenericMbSummary(asset, row, title)
        };

        return new GlobalMbDisplay(
            title,
            category,
            preview,
            $"{category}命中：{tableName}",
            GetGlobalMbSortRank(category),
            links.Take(48).ToArray());
    }

    private IEnumerable<GlobalSearchResultViewModel> BuildGlobalKnowledgeCards(
        IReadOnlyList<GlobalSearchResultViewModel> rowResults)
    {
        if (rowResults.Count == 0) yield break;

        foreach (IGrouping<string, GlobalSearchResultViewModel> group in rowResults
                     .GroupBy(GetGlobalKnowledgeKey, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Min(result => result.SortRank))
                     .ThenBy(group => GetGlobalKnowledgeTitle(group), NaturalStringComparer.Instance))
        {
            GlobalSearchResultViewModel[] rows = group
                .OrderBy(result => result.SortRank)
                .ThenBy(result => result.Title, NaturalStringComparer.Instance)
                .ToArray();
            yield return CreateGlobalKnowledgeCard(rows);
        }
    }

    private static string GetGlobalKnowledgeKey(GlobalSearchResultViewModel result)
    {
        if (TryGetGlobalOwnerTitle(result, out string ownerTitle))
            return NormalizeGlobalEntityTitle(ownerTitle);

        string title = result.Category == "套装"
            ? ExtractGlobalSetFamilyName(result.Title)
            : NormalizeGlobalEntityTitle(result.Title);
        if (string.IsNullOrWhiteSpace(title))
            title = NormalizeGlobalEntityTitle(result.SourcePath);
        return title;
    }

    private static string GetGlobalKnowledgeTitle(IEnumerable<GlobalSearchResultViewModel> rows)
    {
        GlobalSearchResultViewModel first = rows.First();
        foreach (GlobalSearchResultViewModel row in rows)
        {
            if (TryGetGlobalOwnerTitle(row, out string ownerTitle))
                return NormalizeGlobalEntityTitle(ownerTitle);
        }

        return first.Category == "套装"
            ? ExtractGlobalSetFamilyName(first.Title)
            : NormalizeGlobalEntityTitle(first.Title);
    }

    private static bool TryGetGlobalOwnerTitle(GlobalSearchResultViewModel result, out string ownerTitle)
    {
        ownerTitle = string.Empty;
        string source = $"{result.SourcePath}\n{result.Subtitle}".Replace('\\', '/');
        string[] cells = SplitGlobalRawRow(result.RawText);

        if (source.Contains("pet/car_skill", StringComparison.OrdinalIgnoreCase))
        {
            string skillName = cells.Length > 0 ? CleanGlobalTitle(cells[0]) : NormalizeGlobalEntityTitle(result.Title);
            string? owner = cells
                .Select(ExtractGlobalNameCandidate)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .LastOrDefault(value => !value.Equals(skillName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(owner))
            {
                ownerTitle = owner;
                return true;
            }
        }

        if (source.Contains("pet/pet_type_prompt", StringComparison.OrdinalIgnoreCase))
        {
            string? owner = cells
                .Select(ExtractGlobalNameCandidate)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(owner))
            {
                ownerTitle = owner;
                return true;
            }
        }

        return false;
    }

    private static string[] SplitGlobalRawRow(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return Array.Empty<string>();
        string line = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')[0];
        if (line.Contains('\t', StringComparison.Ordinal))
            return line.Split('\t');
        if (line.Contains(',', StringComparison.Ordinal))
            return line.Split(',');
        return Regex.Split(line.Trim(), @"\s+");
    }

    private GlobalSearchResultViewModel CreateGlobalKnowledgeCard(IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        GlobalSearchResultViewModel first = rows[0];
        string entityName = GetGlobalKnowledgeTitle(rows);
        if (string.IsNullOrWhiteSpace(entityName))
            entityName = first.Title;

        string category = GetGlobalKnowledgeCategory(rows);
        string[] sourceTables = rows
            .Select(result => result.Subtitle.Split('·')[0].Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, NaturalStringComparer.Instance)
            .ToArray();
        IReadOnlyList<GlobalSearchLinkViewModel> links = EnrichGlobalKnowledgeLinks(
            MergeGlobalKnowledgeLinks(rows, sourceTables),
            rows,
            entityName);
        IReadOnlyList<GlobalSearchSkillViewModel> skills = BuildGlobalEntitySkills(entityName);
        IReadOnlyList<GlobalSearchFactViewModel> facts = BuildGlobalEntityFacts(entityName, category, rows, links, skills);
        string preview = rows.Any(row => row.Category == "套装")
            ? BuildGlobalSetKnowledgePreview(entityName, rows, sourceTables)
            : BuildGlobalProfileKnowledgePreview(entityName, rows, sourceTables);

        string rawText = BuildGlobalKnowledgeRawText(rows);
        return new GlobalSearchResultViewModel
        {
            Title = entityName,
            Subtitle = rows.Count == 1 ? first.Subtitle : $"已整理 {rows.Count:N0} 条关联来源",
            Category = category,
            SourcePath = sourceTables.Length == 0 ? first.SourcePath : string.Join("、", sourceTables.Take(4)),
            PreviewText = preview,
            MatchReason = category == "套装图鉴"
                ? "套装图鉴，可查看部件并跳转"
                : "已整理图标、说明、模型和相关资源",
            RawText = rawText,
            SortRank = Math.Max(0, first.SortRank - 1),
            Asset = first.Asset,
            Links = links,
            Facts = facts,
            Skills = skills
        };
    }

    private string BuildGlobalSetKnowledgePreview(
        string entityName,
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        IReadOnlyList<string> sourceTables)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "套装名称", entityName);
        AppendGlobalDistinctList(builder, "外观分支", rows.Select(ExtractGlobalSetVariantWithId), 18);
        AppendGlobalDistinctList(builder, "套装部件", ExtractGlobalFieldValues(rows, "包含物品").Concat(ExtractGlobalFieldValues(rows, "可能关联的物品")), 28);
        AppendGlobalDistinctList(builder, "可继续追踪", ExtractGlobalFieldValues(rows, "关联物品"), 18);
        return builder.ToString().Trim();
    }

    private static string GetGlobalKnowledgeCategory(IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        if (rows.Any(row => row.Category == "套装")) return "套装图鉴";
        if (rows.Any(row => row.Category == "宠物/骑宠")) return "坐骑资料";
        if (rows.Any(row => row.Category == "物品")) return "物品资料";
        if (rows.Any(row => row.Category == "角色/怪物")) return "角色/怪物资料";
        if (rows.Any(row => row.Category == "外观/模型")) return "外观资料";
        return $"{rows[0].Category}资料";
    }

    private IReadOnlyList<GlobalSearchLinkViewModel> EnrichGlobalKnowledgeLinks(
        IReadOnlyList<GlobalSearchLinkViewModel> sourceLinks,
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        string entityName)
    {
        string normalizedEntityName = NormalizeGlobalEntityTitle(entityName);
        bool hasExactProfile =
            FindGlobalRowByExactName("object/ride_list.txt", normalizedEntityName) is not null ||
            FindGlobalRowByExactName("pet/pet_list.txt", normalizedEntityName) is not null;
        var links = hasExactProfile
            ? sourceLinks.Where(link => link.Asset?.Kind == AssetKind.MbTable).ToList()
            : sourceLinks.ToList();
        var seen = new HashSet<string>(
            links.Select(GetGlobalSearchLinkKey),
            StringComparer.OrdinalIgnoreCase);

        if (!hasExactProfile)
        {
            foreach (GlobalSearchResultViewModel row in rows.Take(32))
            {
                if (row.Asset?.Kind != AssetKind.MbTable) continue;
                AddGlobalResourcesFromMbRow(
                    row.Asset.Entry.Path,
                    SplitGlobalRawRow(row.RawText),
                    entityName,
                    links,
                    seen);
            }
        }

        EnrichGlobalRelatedProfileResources(links, seen, rows, entityName);

        return links
            .OrderBy(GetGlobalKnowledgeLinkRank)
            .ThenBy(link => link.Name, NaturalStringComparer.Instance)
            .Take(72)
            .ToArray();
    }

    private static string GetGlobalSearchLinkKey(GlobalSearchLinkViewModel link) =>
        link.Asset is not null
            ? link.Asset.DisplayPath
            : $"{link.Kind}|{link.Path}|{link.Name}";

    private void AddGlobalProfileIconLink(
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        string idOrName,
        string detail)
    {
        AssetEntry? icon = FindItemIconAsset(idOrName) ?? FindGlobalImageAsset(idOrName);
        if (icon is null) return;
        AddGlobalSearchLink(links, seen, icon, "图标", detail);
    }

    private void EnrichGlobalRelatedProfileResources(
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        string entityName)
    {
        string name = NormalizeGlobalEntityTitle(entityName);
        if (string.IsNullOrWhiteSpace(name)) return;

        string[]? ride = FindGlobalRowByExactName("object/ride_list.txt", name);
        if (ride is not null)
            AddGlobalResourcesFromMbRow("object/ride_list.txt", ride, name, links, seen);

        string[]? pet = FindGlobalRowByExactName("pet/pet_list.txt", name);
        if (pet is not null)
            AddGlobalResourcesFromMbRow("pet/pet_list.txt", pet, name, links, seen);

        var petConfigIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pet is not null && !string.IsNullOrWhiteSpace(GetCell(pet, 1)))
            petConfigIds.Add(GetCell(pet, 1).Trim());

        foreach (string[] groupRow in LoadMbRows("pet/pet_list_group.txt"))
        {
            bool exactName = CleanGlobalTitle(GetCell(groupRow, 0))
                .Equals(name, StringComparison.OrdinalIgnoreCase);
            bool exactConfig = petConfigIds.Contains(GetCell(groupRow, 2).Trim());
            if (!exactName && !exactConfig) continue;
            AddGlobalResourcesFromMbRow("pet/pet_list_group.txt", groupRow, name, links, seen);
        }

        if (GetGlobalCarSkillRowsByEntity().TryGetValue(name, out List<string[]>? skillRows))
        {
            foreach (string[] skillRow in skillRows)
                AddGlobalResourcesFromMbRow("pet/car_skill.txt", skillRow, name, links, seen);
        }

        AddGlobalRelatedItemResources(name, links, seen);
    }

    private string[]? FindGlobalRowByExactName(string tablePath, string entityName) =>
        LoadMbRows(tablePath).FirstOrDefault(row =>
            CleanGlobalTitle(GetCell(row, 0))
                .Equals(entityName, StringComparison.OrdinalIgnoreCase));

    private void AddGlobalRelatedItemResources(
        string entityName,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        foreach (string tablePath in new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" })
        {
            foreach (string[] row in LoadMbRows(tablePath))
            {
                string itemId = GetCell(row, 0).Trim();
                string itemName = CleanGlobalTitle(GetCell(row, 1));
                if (string.IsNullOrWhiteSpace(itemId) ||
                    string.IsNullOrWhiteSpace(itemName) ||
                    !itemName.Contains(entityName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddGlobalSyntheticLink(
                    links,
                    seen,
                    $"related-item:{itemId}",
                    "相关物品",
                    itemName,
                    itemId,
                    tablePath);

                AssetEntry? icon = FindItemIconAsset(itemId);
                if (icon is not null && IsLikelyItemIconAsset(icon))
                    AddGlobalSearchLink(links, seen, icon, "物品图标", itemName);
            }
        }
    }

    private static bool IsLikelyItemIconAsset(AssetEntry asset)
    {
        string path = asset.Entry.Path.Replace('\\', '/');
        return path.Contains("/icon/item/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("icon/item/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/image/item/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("image/item/", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CollectGlobalProfileNames(
        string entityName,
        IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        if (!string.IsNullOrWhiteSpace(entityName))
            yield return entityName;

        foreach (GlobalSearchResultViewModel row in rows.Take(12))
        {
            foreach (string value in new[] { row.Title })
            {
                foreach (string cell in SplitGlobalRawRow(value))
                {
                    string candidate = ExtractGlobalNameCandidate(cell);
                    if (!string.IsNullOrWhiteSpace(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private void AddGlobalRowsByName(
        string tablePath,
        string entityName,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(entityName)) return;
        int added = 0;
        foreach (string[] row in LoadMbRows(tablePath))
        {
            if (!RowContainsGlobalEntity(row, entityName)) continue;
            AddGlobalResourcesFromMbRow(tablePath, row, entityName, links, seen);
            added++;
            if (added >= maximum || links.Count >= 140) break;
        }
    }

    private void AddGlobalResourcesFromMbRow(
        string tablePath,
        IReadOnlyList<string> row,
        string entityName,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        string normalizedPath = tablePath.Replace('\\', '/').ToLowerInvariant();
        if (links.Count >= 72) return;

        if (normalizedPath.StartsWith("pet/car_skill", StringComparison.Ordinal))
        {
            AddGlobalSkillRowResources(row, links, seen);
            return;
        }

        if (normalizedPath.StartsWith("object/ride", StringComparison.Ordinal))
        {
            AddGlobalCharacterResourcesByRoleId(GetCell(row, 2), links, seen);
            foreach (string id in row.Skip(16).Take(5).SelectMany(ExtractGlobalNumericTokens))
            {
                AddGlobalSkillResourcesById(id, links, seen);
                if (links.Count >= 72) break;
            }

            return;
        }

        if (normalizedPath.StartsWith("pet/pet_list_group", StringComparison.Ordinal))
        {
            AddGlobalExplicitResourceReferences(row, links, seen, "主图标", imagesOnly: true);
            return;
        }

        if (normalizedPath.StartsWith("pet/pet_list", StringComparison.Ordinal))
        {
            AddGlobalCharacterResourcesByRoleId(GetCell(row, 2), links, seen);
            return;
        }

        if (normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal))
        {
            AddGlobalAppearanceResourcesByPicId(GetCell(row, 5), links, seen);
            return;
        }

        if (normalizedPath.StartsWith("object/cha_pic", StringComparison.Ordinal))
        {
            AddGlobalAppearanceResourcesFromPicRow(row, links, seen);
            return;
        }

        AddGlobalExplicitResourceReferences(row, links, seen, tablePath, imagesOnly: false);
    }

    private void AddGlobalCharacterResourcesByRoleId(
        string roleId,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(roleId)) return;
        if (!GetChaListRows().TryGetValue(roleId.Trim(), out string[]? row)) return;
        AddGlobalAppearanceResourcesByPicId(GetCell(row, 5), links, seen);
    }

    private void AddGlobalAppearanceResourcesByPicId(
        string picId,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(picId)) return;
        if (GetChaPicRows().TryGetValue(picId.Trim(), out string[]? picRow))
            AddGlobalAppearanceResourcesFromPicRow(picRow, links, seen);
    }

    private void AddGlobalAppearanceResourcesFromPicRow(
        IReadOnlyList<string> row,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        string config = GetCell(row, 2);
        if (!string.IsNullOrWhiteSpace(config))
        {
            foreach (AssetEntry linkedAsset in FindAssetsByReferenceFlexible(_workspace.Assets, config)
                         .OrderBy(asset => asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                         .Take(16))
            {
                AddGlobalSearchLink(links, seen, linkedAsset, GetGlobalAssetKindText(linkedAsset), "模型配置");
                if (linkedAsset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase))
                    AddGlobalCompositeResources(linkedAsset, links, seen);
            }
        }

        AddGlobalExplicitResourceReferences(row, links, seen, "主图标", imagesOnly: true);
    }

    private void AddGlobalSkillResourcesById(
        string skillId,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return;
        if (GetGlobalSkillDataRows().TryGetValue(skillId.Trim(), out (string Path, string[] Row) skill))
            AddGlobalSkillDataResources(skill.Path, skill.Row, links, seen);
    }

    private void AddGlobalSkillRowResources(
        IReadOnlyList<string> row,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        string skillName = CleanGlobalTitle(GetCell(row, 0));
        string skillId = GetCell(row, 1);
        if (!string.IsNullOrWhiteSpace(skillId))
            AddGlobalSkillResourcesById(skillId, links, seen);
        if (!string.IsNullOrWhiteSpace(skillId))
        {
            AddGlobalSyntheticLink(
                links,
                seen,
                $"skill:{skillId}",
                "技能描述",
                string.IsNullOrWhiteSpace(skillName) ? skillId : skillName,
                skillId,
                FormatGlobalSkillLine(row, string.Empty));
        }
    }

    private void AddGlobalSkillDataResources(
        string tablePath,
        IReadOnlyList<string> row,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        string skillName = CleanGlobalTitle(GetCell(row, 0));
        string skillId = GetCell(row, 1);
        string iconReference = GetCell(row, 3);
        AssetEntry? icon = FindGlobalImageAsset(iconReference);
        if (icon is not null)
            AddGlobalSearchLink(links, seen, icon, "技能图标", skillName);

        AddGlobalSyntheticLink(
            links,
            seen,
            $"skill-data:{skillId}",
            "技能说明",
            string.IsNullOrWhiteSpace(skillName) ? skillId : skillName,
            tablePath,
            CleanGlobalMarkup(GetCell(row, 4)));
    }

    private void AddGlobalExplicitResourceReferences(
        IReadOnlyList<string> row,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        string detail,
        bool imagesOnly)
    {
        string extensionPattern = imagesOnly
            ? @"(?i)(?:[\w.-]+[\\/])*[\w.-]+\.(?:png|jpg|jpeg|dds|tga|ico)"
            : @"(?i)(?:[\w.-]+[\\/])*[\w.-]+\.(?:png|jpg|jpeg|dds|tga|ico|pmf|cct|cmf|psf|paf|xml|gfx|wav|ogg)";
        foreach (string cell in row)
        {
            string cleaned = CleanGlobalMarkup(cell);
            foreach (Match match in Regex.Matches(cleaned, extensionPattern))
            {
                foreach (AssetEntry asset in FindAssetsByReferenceFlexible(_workspace.Assets, match.Value).Take(8))
                {
                    string kind = detail == "主图标" && asset.Kind == AssetKind.Image
                        ? "主图标"
                        : GetGlobalAssetKindText(asset);
                    AddGlobalSearchLink(links, seen, asset, kind, detail);
                }
            }
        }
    }

    private void AddGlobalCompositeResources(
        AssetEntry config,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen)
    {
        CompositeModelEntry? composite = FindGlobalCompositeModel(config);
        if (composite is null) return;

        foreach (CompositeModelPart part in composite.Parts)
        {
            AddGlobalSearchLink(links, seen, part.MeshAsset, "模型部件", composite.Name);
            if (part.TextureBinding is ModelTextureBinding binding)
                AddGlobalSearchLink(links, seen, binding.TextureAsset, "贴图", binding.DisplayName);
        }
    }

    private CompositeModelEntry? FindGlobalCompositeModel(AssetEntry config)
    {
        string folder = GetInternalDirectory(config.Entry.Path);
        if (folder.EndsWith("/config", StringComparison.OrdinalIgnoreCase))
            folder = GetInternalDirectory(folder);

        return _workspace.FindCompositeModels(config.ArchivePath, folder)
            .Where(composite => composite.ConfigAsset.DisplayPath.Equals(
                config.DisplayPath,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(composite => composite.Parts.Count)
            .FirstOrDefault();
    }

    private IReadOnlyList<GlobalSearchSkillViewModel> BuildGlobalEntitySkills(string entityName)
    {
        string key = NormalizeGlobalEntityTitle(entityName);
        if (string.IsNullOrWhiteSpace(key) ||
            !GetGlobalCarSkillRowsByEntity().TryGetValue(key, out List<string[]>? rows))
        {
            return Array.Empty<GlobalSearchSkillViewModel>();
        }

        var result = new List<GlobalSearchSkillViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in rows)
        {
            string skillName = CleanGlobalTitle(GetCell(row, 0));
            string skillId = GetCell(row, 1);
            if (string.IsNullOrWhiteSpace(skillId) || !seen.Add(skillId)) continue;

            string description = string.Empty;
            AssetEntry? icon = null;
            if (GetGlobalSkillDataRows().TryGetValue(skillId, out (string Path, string[] Row) skillData))
            {
                skillName = CleanGlobalTitle(GetCell(skillData.Row, 0)) is { Length: > 0 } resolvedName
                    ? resolvedName
                    : skillName;
                description = CleanGlobalMarkup(GetCell(skillData.Row, 4));
                icon = FindGlobalImageAsset(GetCell(skillData.Row, 3));
            }

            string unlockValue = GetCell(row, 2);
            string unlockText = unlockValue switch
            {
                "" or "0" or "1" => "初始技能",
                _ => $"解锁值 {FormatNumberCell(unlockValue)}"
            };
            result.Add(new GlobalSearchSkillViewModel(
                skillName,
                $"ID {skillId}",
                unlockText,
                description,
                icon));
        }

        return result;
    }

    private Dictionary<string, List<string[]>> GetGlobalCarSkillRowsByEntity()
    {
        if (_globalCarSkillRowsByEntity is not null) return _globalCarSkillRowsByEntity;

        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in LoadMbRows("pet/car_skill.txt"))
        {
            string owner = row
                .Select(CleanGlobalTitle)
                .LastOrDefault(value =>
                    value.Length >= 2 &&
                    !IsCompactNumericCell(value) &&
                    !value.Equals(CleanGlobalTitle(GetCell(row, 0)), StringComparison.OrdinalIgnoreCase)) ??
                string.Empty;
            owner = NormalizeGlobalEntityTitle(owner);
            if (string.IsNullOrWhiteSpace(owner)) continue;
            if (!result.TryGetValue(owner, out List<string[]>? ownerRows))
            {
                ownerRows = new List<string[]>();
                result[owner] = ownerRows;
            }
            ownerRows.Add(row);
        }

        _globalCarSkillRowsByEntity = result;
        return _globalCarSkillRowsByEntity;
    }

    private Dictionary<string, (string Path, string[] Row)> GetGlobalSkillDataRows()
    {
        if (_globalSkillDataRows is not null) return _globalSkillDataRows;

        var result = new Dictionary<string, (string Path, string[] Row)>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetEntry table in _workspace.Assets.Where(asset =>
                     asset.Kind == AssetKind.MbTable &&
                     asset.Entry.Path.Replace('\\', '/').StartsWith("skill/skill_data_npc_", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string[] row in LoadMbRows(table.Entry.Path))
            {
                string id = GetCell(row, 1);
                if (!IsCompactNumericCell(id) || result.ContainsKey(id)) continue;
                result[id] = (table.Entry.Path, row);
            }
        }

        _globalSkillDataRows = result;
        return _globalSkillDataRows;
    }

    private IReadOnlyList<GlobalSearchFactViewModel> BuildGlobalEntityFacts(
        string entityName,
        string category,
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        IReadOnlyList<GlobalSearchLinkViewModel> links,
        IReadOnlyList<GlobalSearchSkillViewModel> skills)
    {
        var result = new List<GlobalSearchFactViewModel>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string label, string value)
        {
            value = CleanGlobalMarkup(value);
            if (string.IsNullOrWhiteSpace(value) || !labels.Add(label)) return;
            result.Add(new GlobalSearchFactViewModel(label, value));
        }

        string normalizedName = NormalizeGlobalEntityTitle(entityName);
        Add("名称", normalizedName);
        Add("类型", category.Replace("资料", string.Empty, StringComparison.Ordinal)
            .Replace("图鉴", string.Empty, StringComparison.Ordinal));

        string[]? ride = LoadMbRows("object/ride_list.txt")
            .FirstOrDefault(row => CleanGlobalTitle(GetCell(row, 0)).Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        if (ride is not null)
        {
            Add("坐骑编号", GetCell(ride, 1));
            Add("角色编号", GetCell(ride, 2));
            Add("移动速度", $"{FormatNumberCell(GetCell(ride, 9))} / {FormatNumberCell(GetCell(ride, 10))}");
        }

        string[]? pet = LoadMbRows("pet/pet_list.txt")
            .FirstOrDefault(row => CleanGlobalTitle(GetCell(row, 0)).Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        if (pet is not null)
        {
            Add("宠物配置", GetCell(pet, 1));
            Add("角色编号", GetCell(pet, 2));
            Add("坐骑编号", GetCell(pet, 6));
        }

        string roleId = ride is not null ? GetCell(ride, 2) : pet is not null ? GetCell(pet, 2) : string.Empty;
        if (!string.IsNullOrWhiteSpace(roleId) &&
            GetChaListRows().TryGetValue(roleId, out string[]? character))
        {
            Add("战斗属性", GetCell(character, 4));
            Add("外观编号", GetCell(character, 5));
        }

        string[] relatedItems = new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" }
            .SelectMany(LoadMbRows)
            .Where(row => CleanGlobalTitle(GetCell(row, 1)).Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
            .Select(row => $"{CleanGlobalTitle(GetCell(row, 1))}（ID {GetCell(row, 0)}）")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (relatedItems.Length > 0)
            Add("相关物品", string.Join("、", relatedItems));

        AssetEntry? modelConfig = links.Select(link => link.Asset)
            .FirstOrDefault(asset => asset?.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase) == true);
        if (modelConfig is not null)
            Add("模型配置", System.IO.Path.GetFileNameWithoutExtension(modelConfig.Name));
        int modelPartCount = links.Count(link =>
            link.Asset?.Extension.Equals(".pmf", StringComparison.OrdinalIgnoreCase) == true);
        int textureCount = links.Count(link =>
            link.Asset?.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) == true);
        if (modelPartCount > 0)
            Add("模型部件", modelPartCount.ToString("N0"));
        if (textureCount > 0)
            Add("关联贴图", textureCount.ToString("N0"));
        if (skills.Count > 0)
            Add("坐骑技能", $"{skills.Count:N0} 个");

        string petConfigId = pet is null ? string.Empty : GetCell(pet, 1).Trim();
        string promptDescription = string.IsNullOrWhiteSpace(petConfigId)
            ? string.Empty
            : LoadMbRows("pet/pet_type_prompt.txt")
                .Where(row => GetCell(row, 0).Trim().Equals(petConfigId, StringComparison.OrdinalIgnoreCase))
                .Select(row => CleanGlobalMarkup(GetCell(row, 2)))
                .FirstOrDefault(value => value.Length is >= 6 and <= 360 && ContainsChinese(value)) ??
              string.Empty;

        string description = !string.IsNullOrWhiteSpace(promptDescription)
            ? promptDescription
            : rows
            .Select(row => ExtractGlobalBestDescription(entityName, new[] { row }))
            .Select(CleanGlobalMarkup)
            .FirstOrDefault(value =>
                value.Length is >= 6 and <= 240 &&
                !value.Contains('\t') &&
                !Regex.IsMatch(value, @"(?:\d+\s+){8,}")) ??
            string.Empty;
        Add("说明", description);

        return result;
    }

    private void AddGlobalAssetReferencesFromText(
        string text,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        string detail)
    {
        if (links.Count >= 72) return;
        string cleanedText = CleanGlobalMarkup(text);
        foreach (Match match in Regex.Matches(cleanedText, @"(?i)(?:[\w.-]+[\\/])+[\w.-]+\.(?:png|jpg|jpeg|dds|tga|pmf|cct|cmf|psf|paf|xml|txt|gfx|wav|ogg)"))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(_workspace.Assets, match.Value).Take(6))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), detail);
            if (links.Count >= 72) break;
        }

        foreach (Match match in Regex.Matches(cleanedText, @"(?i)\b[\w.-]+\.(?:png|jpg|jpeg|dds|tga|pmf|cct|cmf|psf|paf|xml|txt|gfx|wav|ogg)\b"))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(_workspace.Assets, match.Value).Take(6))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), detail);
            if (links.Count >= 72) break;
        }

        foreach (string token in ExtractGlobalResourceTokens(cleanedText).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(_workspace.Assets, token).Take(6))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), detail);
            if (links.Count >= 72) break;
        }
    }

    private static bool RowContainsGlobalEntity(IReadOnlyList<string> row, string entityName)
    {
        string normalizedEntity = NormalizeGlobalEntityTitle(entityName);
        if (string.IsNullOrWhiteSpace(normalizedEntity)) return false;
        foreach (string cell in row)
        {
            string cleaned = CleanGlobalMarkup(cell);
            if (cleaned.Equals(normalizedEntity, StringComparison.OrdinalIgnoreCase) ||
                cleaned.Contains(normalizedEntity, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetGlobalKnowledgeLinkRank(GlobalSearchLinkViewModel link)
    {
        if (link.Kind.Equals("主图标", StringComparison.Ordinal)) return -2;
        if (link.Kind.Contains("图标", StringComparison.Ordinal) || link.Kind.Contains("头像", StringComparison.Ordinal)) return 0;
        if (link.Kind.Contains("模型", StringComparison.Ordinal) || link.Kind.Contains("外观", StringComparison.Ordinal)) return 1;
        if (link.Kind.Contains("贴图", StringComparison.Ordinal) || link.Kind.Contains("图像", StringComparison.Ordinal)) return 2;
        if (link.Kind.Contains("物品", StringComparison.Ordinal) || link.Kind.Contains("部件", StringComparison.Ordinal)) return 3;
        if (link.Kind.Contains("技能", StringComparison.Ordinal) || link.Kind.Contains("状态", StringComparison.Ordinal)) return 4;
        if (link.Kind.Contains("源", StringComparison.Ordinal) || link.Kind.Contains("来源", StringComparison.Ordinal)) return 9;
        return 6;
    }

    private string BuildGlobalProfileKnowledgePreview(
        string entityName,
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        IReadOnlyList<string> sourceTables)
    {
        var builder = new StringBuilder();
        string category = GetGlobalKnowledgeCategory(rows);
        AppendGlobalValue(builder, "名称", entityName);
        AppendGlobalValue(builder, "类型", category.Replace("资料", string.Empty));
        AppendGlobalValue(builder, "说明", ExtractGlobalBestDescription(entityName, rows));
        AppendGlobalDistinctList(builder, "编号", rows.Select(ExtractGlobalIdFromTitle), 10);
        AppendGlobalDistinctList(builder, "技能说明", GetGlobalSkillDescriptionLines(entityName, rows), 12);
        AppendGlobalDistinctList(builder, "可打开资源", rows.SelectMany(result => result.Links)
            .Where(link => !link.Kind.Contains("源", StringComparison.Ordinal) &&
                           !link.Kind.Contains("来源", StringComparison.Ordinal))
            .Select(link => $"{link.Kind}：{link.Name}"), 16);
        AppendGlobalDistinctList(builder, "核心信息", rows.SelectMany(result => GetLeadingGlobalPreviewLines(result.PreviewText)), 16);
        return builder.ToString().Trim();
    }

    private IEnumerable<string> GetGlobalSkillDescriptionLines(
        string entityName,
        IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        foreach (GlobalSearchResultViewModel rowResult in rows)
        {
            string source = $"{rowResult.SourcePath}\n{rowResult.Subtitle}".Replace('\\', '/');
            if (!source.Contains("pet/car_skill", StringComparison.OrdinalIgnoreCase) &&
                !source.Contains("skill/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string line = FormatGlobalSkillLine(SplitGlobalRawRow(rowResult.RawText), entityName);
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }

        foreach (string[] row in LoadMbRows("pet/car_skill.txt"))
        {
            string owner = NormalizeGlobalEntityTitle(GetCell(row, 4));
            if (!owner.Equals(NormalizeGlobalEntityTitle(entityName), StringComparison.OrdinalIgnoreCase)) continue;
            string line = FormatGlobalSkillLine(row, entityName);
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    private static string FormatGlobalSkillLine(IReadOnlyList<string> row, string entityName)
    {
        if (row.Count == 0) return string.Empty;
        string name = CleanGlobalTitle(ExtractGlobalNameCandidate(GetCell(row, 0)));
        if (string.IsNullOrWhiteSpace(name))
            name = CleanGlobalTitle(GetCell(row, 1));
        string id = row.Skip(1).FirstOrDefault(IsCompactNumericCell) ?? GuessGlobalId(row);
        string ui = row.Skip(1).FirstOrDefault(cell => IsCompactNumericCell(cell.Trim()) && !cell.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        string detail = string.Join(" ",
            row.Skip(2)
                .Select(CleanGlobalMarkup)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !value.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                .Where(value => !IsCompactNumericCell(value))
                .Take(4));
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"：{detail}";
        string meta = string.IsNullOrWhiteSpace(ui) ? $"ID {id}" : $"ID {id}，UI {ui}";
        return string.IsNullOrWhiteSpace(name) ? string.Empty : $"{name}（{meta}）{suffix}";
    }

    private static string ExtractGlobalBestDescription(
        string entityName,
        IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        foreach (string text in rows.Select(row => row.RawText).Concat(rows.Select(row => row.PreviewText)))
        {
            foreach (string line in SplitGlobalDescriptionCandidates(text))
            {
                if (!line.Contains(entityName, StringComparison.OrdinalIgnoreCase)) continue;
                if (line.Length <= entityName.Length + 6) continue;
                return line.Length <= 260 ? line : line[..257] + "...";
            }
        }

        foreach (string text in rows.Select(row => row.RawText).Concat(rows.Select(row => row.PreviewText)))
        {
            string candidate = SplitGlobalDescriptionCandidates(text)
                .FirstOrDefault(line => line.Length >= 14 && ContainsChinese(line)) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Length <= 260 ? candidate : candidate[..257] + "...";
        }

        return string.Empty;
    }

    private static IEnumerable<string> SplitGlobalDescriptionCandidates(string text)
    {
        string cleaned = CleanGlobalMarkup(text)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
        foreach (string line in cleaned.Split('\n', '。', '；', ';')
                     .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                     .Where(line => line.Length > 0))
        {
            if (line.Count(char.IsDigit) > Math.Max(8, line.Length / 2)) continue;
            yield return line;
        }
    }

    private static IReadOnlyList<GlobalSearchLinkViewModel> MergeGlobalKnowledgeLinks(
        IReadOnlyList<GlobalSearchResultViewModel> rows,
        IReadOnlyList<string> sourceTables)
    {
        var links = new List<GlobalSearchLinkViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (GlobalSearchLinkViewModel link in rows.SelectMany(result => result.Links)
                     .Where(link => !link.Kind.Contains("源", StringComparison.Ordinal) &&
                                    !link.Kind.Contains("来源", StringComparison.Ordinal)))
        {
            string key = $"{link.Kind}|{link.Path}|{link.Name}";
            if (!seen.Add(key)) continue;
            links.Add(link);
            if (links.Count >= 80) break;
        }

        foreach (string table in sourceTables)
            AddGlobalSyntheticLink(links, seen, $"source:{table}", "资料来源", System.IO.Path.GetFileName(table), table, "原始资料位置");

        return links;
    }

    private static string BuildGlobalKnowledgeRawText(IReadOnlyList<GlobalSearchResultViewModel> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("资料卡来源：");
        foreach (GlobalSearchResultViewModel row in rows.Take(120))
            builder.AppendLine($"- {row.Title} | {row.Subtitle}");
        builder.AppendLine();
        builder.AppendLine("原始命中文本：");
        foreach (GlobalSearchResultViewModel row in rows.Take(120))
        {
            builder.AppendLine($"[{row.Subtitle}]");
            builder.AppendLine(row.RawText);
        }

        return builder.ToString().Trim();
    }

    private static string ExtractGlobalSetFamilyName(string title)
    {
        string cleaned = NormalizeGlobalEntityTitle(title);
        cleaned = Regex.Replace(cleaned, @"^\[[^\]]+\]\s*", string.Empty);
        cleaned = Regex.Replace(cleaned, @"^\[[^\]]+\]\s*", string.Empty);
        int splitIndex = cleaned.IndexOf("--", StringComparison.Ordinal);
        if (splitIndex > 0)
            cleaned = cleaned[..splitIndex].Trim();
        int setIndex = cleaned.IndexOf("套装", StringComparison.Ordinal);
        if (setIndex >= 0)
            cleaned = cleaned[..(setIndex + "套装".Length)].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? NormalizeGlobalEntityTitle(title) : cleaned;
    }

    private static string NormalizeGlobalEntityTitle(string title)
    {
        string cleaned = Regex.Replace(title ?? string.Empty, @"（ID\s*\d+）", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Trim(' ', '-', '·');
    }

    private static string ExtractGlobalSetVariantWithId(GlobalSearchResultViewModel result)
    {
        string variant = ExtractGlobalSetVariant(result.Title);
        string id = ExtractGlobalIdFromTitle(result.Title);
        if (string.IsNullOrWhiteSpace(variant)) return string.Empty;
        return string.IsNullOrWhiteSpace(id) ? variant : $"{variant}（ID {id}）";
    }

    private static string ExtractGlobalSetVariant(string title)
    {
        Match match = Regex.Match(title, @"--\s*\[([^\]]+)\]");
        if (match.Success) return match.Groups[1].Value.Trim();
        string cleaned = NormalizeGlobalEntityTitle(title);
        int splitIndex = cleaned.IndexOf("--", StringComparison.Ordinal);
        return splitIndex >= 0 && splitIndex + 2 < cleaned.Length
            ? cleaned[(splitIndex + 2)..].Trim(' ', '[', ']')
            : string.Empty;
    }

    private static string ExtractGlobalIdFromTitle(GlobalSearchResultViewModel result) =>
        ExtractGlobalIdFromTitle(result.Title);

    private static string ExtractGlobalIdFromTitle(string title)
    {
        Match match = Regex.Match(title, @"ID\s*(\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static IEnumerable<string> ExtractGlobalFieldValues(
        IEnumerable<GlobalSearchResultViewModel> rows,
        string label)
    {
        string prefix = label + "：";
        foreach (string line in rows.SelectMany(result => result.PreviewText.Split('\n')))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            foreach (string value in SplitGlobalDisplayList(trimmed[prefix.Length..]))
                yield return value;
        }
    }

    private static IEnumerable<string> GetLeadingGlobalPreviewLines(string preview)
    {
        foreach (string line in preview.Split('\n').Select(line => line.Trim()))
        {
            if (line.Length == 0 ||
                line.StartsWith("类型：", StringComparison.Ordinal) ||
                line.StartsWith("资料类型：", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
        }
    }

    private static IEnumerable<string> SplitGlobalDisplayList(string value)
    {
        foreach (string part in value.Split(new[] { '、', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string cleaned = Regex.Replace(part, @"，?另有\s*\d+\s*个$", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
                yield return cleaned;
        }
    }

    private static void AppendGlobalDistinctList(
        StringBuilder builder,
        string label,
        IEnumerable<string> values,
        int maximum)
    {
        string[] distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximum + 1)
            .ToArray();
        if (distinct.Length == 0) return;
        string suffix = distinct.Length > maximum ? $"，另有 {distinct.Length - maximum} 个" : string.Empty;
        AppendGlobalValue(builder, label, string.Join("、", distinct.Take(maximum)) + suffix);
    }

    private void AddGlobalSearchResult(
        ICollection<GlobalSearchResultViewModel> results,
        ISet<string> seen,
        GlobalSearchResultViewModel result)
    {
        string key = $"{result.Category}|{result.SourcePath}|{result.RawText}";
        if (!seen.Add(key)) return;
        results.Add(result);
    }

    private bool TryGetGlobalSearchText(AssetEntry asset, out string text)
    {
        text = string.Empty;
        string key = "global:" + asset.DisplayPath;
        lock (_globalSearchTextCache)
        {
            if (_globalSearchTextCache.TryGetValue(key, out string? cachedText))
            {
                text = cachedText;
                return text.Length > 0;
            }
        }

        try
        {
            byte[] data = _workspace.Extract(asset);
            if (!TryDecodeTextPreview(asset, data, out text))
                text = string.Empty;
        }
        catch
        {
            text = string.Empty;
        }

        lock (_globalSearchTextCache)
        {
            _globalSearchTextCache[key] = text;
        }

        return text.Length > 0;
    }

    private static IEnumerable<AssetEntry> GetGlobalSearchMbTables(IReadOnlyList<AssetEntry> assets, bool broadSearch)
    {
        foreach (AssetEntry asset in assets.Where(asset => asset.Kind == AssetKind.MbTable && IsPriorityGlobalSearchMbTable(asset.Entry.Path)))
            yield return asset;

        if (!broadSearch)
            yield break;

        foreach (AssetEntry asset in assets.Where(asset => asset.Kind == AssetKind.MbTable && !IsPriorityGlobalSearchMbTable(asset.Entry.Path)).Take(80))
            yield return asset;
    }

    private static bool IsPriorityGlobalSearchMbTable(string path)
    {
        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        return normalized.StartsWith("help_bank/bank_tz", StringComparison.Ordinal) ||
               normalized.StartsWith("help_bank/bank_text", StringComparison.Ordinal) ||
               normalized.StartsWith("item/item_", StringComparison.Ordinal) ||
               normalized.StartsWith("item/raw_list", StringComparison.Ordinal) ||
               normalized.StartsWith("object/ride", StringComparison.Ordinal) ||
               normalized.StartsWith("object/cha_list", StringComparison.Ordinal) ||
               normalized.StartsWith("object/cha_pic", StringComparison.Ordinal) ||
               normalized.StartsWith("object/npc_business", StringComparison.Ordinal) ||
               normalized.StartsWith("pet/", StringComparison.Ordinal) ||
               normalized.StartsWith("skill/", StringComparison.Ordinal);
    }

    private static bool ShouldRunBroadGlobalSearch(IReadOnlyList<string> terms) =>
        terms.Any(term =>
            term.Contains('/', StringComparison.Ordinal) ||
            term.Contains('\\', StringComparison.Ordinal) ||
            term.Contains('.', StringComparison.Ordinal) ||
            term.Contains('_', StringComparison.Ordinal) ||
            IsCompactNumericCell(term) ||
            term.All(character => character < 128));

    private static bool IsGlobalSearchTextAsset(AssetEntry asset) =>
        asset.Kind != AssetKind.MbTable &&
        (asset.Kind == AssetKind.Other || asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase)) &&
        GlobalSearchTextExtensions.Contains(asset.Extension);

    private static bool GlobalMbRowMatchesSearch(
        string normalizedPath,
        IReadOnlyList<string> row,
        string line,
        IReadOnlyList<string[]> variants,
        bool broadSearch)
    {
        if (!GlobalTextMatches(line, variants))
            return false;

        if (broadSearch)
            return true;

        string primaryText = string.Join("\n", GetGlobalPrimarySearchFields(normalizedPath, row));
        return primaryText.Length > 0 && GlobalTextMatches(primaryText, variants);
    }

    private static IEnumerable<string> GetGlobalPrimarySearchFields(string normalizedPath, IReadOnlyList<string> row)
    {
        if (normalizedPath.StartsWith("pet/pet_type_prompt", StringComparison.Ordinal))
        {
            yield return CleanGlobalTitle(GetCell(row, 0));
            yield break;
        }

        if (normalizedPath.StartsWith("pet/car_skill", StringComparison.Ordinal))
        {
            yield return CleanGlobalTitle(GetCell(row, 0));
            yield return CleanGlobalTitle(GetCell(row, 4));
            yield break;
        }

        if (normalizedPath.StartsWith("object/ride", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("pet/pet_list", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("pet/pet_list_group", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("pet/pet_dye", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("object/cha_pic", StringComparison.Ordinal))
        {
            foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 7, 25, 26 })
                yield return CleanGlobalTitle(ExtractGlobalNameCandidate(GetCell(row, index)));
            yield break;
        }

        foreach (string cell in row.Take(12))
        {
            string candidate = CleanGlobalTitle(ExtractGlobalNameCandidate(cell));
            if (!string.IsNullOrWhiteSpace(candidate))
                yield return candidate;
        }
    }

    private static string[] SplitTextLines(string text) =>
        text.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    private static bool GlobalTextMatches(string text, IReadOnlyList<string[]> variants) =>
        variants.All(group => group.Any(variant => text.Contains(variant, StringComparison.OrdinalIgnoreCase)));

    private static string CreateGlobalMbRowTitle(string tableName, IReadOnlyList<string> row, int sourceRow)
    {
        string name = row.Take(12)
            .Select(ExtractGlobalNameCandidate)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        string id = row.Take(8).Select(cell => cell.Trim()).FirstOrDefault(IsCompactNumericCell) ?? string.Empty;
        if (name.Length > 0 && id.Length > 0) return $"{name}（ID {id}）";
        if (name.Length > 0) return name;
        if (id.Length > 0) return $"{tableName} · ID {id}";
        return $"{tableName} · 第 {sourceRow:N0} 行";
    }

    private static string ExtractGlobalNameCandidate(string value)
    {
        string cleaned = CleanGlobalMarkup(value);
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

        string firstLine = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? cleaned;
        int splitIndex = firstLine.IndexOf("——", StringComparison.Ordinal);
        if (splitIndex > 0)
            firstLine = firstLine[..splitIndex].Trim();
        splitIndex = firstLine.IndexOf("--", StringComparison.Ordinal);
        if (splitIndex > 0)
            firstLine = firstLine[..splitIndex].Trim();

        firstLine = firstLine.Trim(' ', '：', ':', '，', ',', '。');
        return IsMeaningfulGlobalNameCell(firstLine) ? firstLine : string.Empty;
    }

    private static bool IsMeaningfulGlobalNameCell(string value) =>
        value.Length is > 0 and <= 48 &&
        value.Any(character => char.IsLetter(character) || character > 127) &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('*');

    private static bool IsCompactNumericCell(string value) =>
        value.Length is > 0 and <= 12 && value.All(char.IsDigit);

    private static string CreateGlobalAssetSummary(AssetEntry asset)
    {
        ResourceExplanation explanation = ResourceExplanationService.Explain(asset);
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", explanation.FriendlyName);
        AppendGlobalValue(builder, "用途", explanation.Purpose);
        AppendGlobalValue(builder, "读取场景", explanation.UsedWhen);
        AppendGlobalValue(builder, "路径", asset.DisplayPath);
        return builder.ToString().Trim();
    }

    private static string CreateGlobalTextAssetSummary(AssetEntry asset, string snippet)
    {
        ResourceExplanation explanation = ResourceExplanationService.Explain(asset);
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", explanation.FriendlyName);
        AppendGlobalValue(builder, "用途", explanation.Purpose);
        AppendGlobalValue(builder, "命中片段", snippet);
        return builder.ToString().Trim();
    }

    private static string GetGlobalMbCategory(string normalizedPath, string tableName)
    {
        if (normalizedPath.StartsWith("help_bank/bank_tz", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("item/item_set", StringComparison.Ordinal))
        {
            return "套装";
        }

        if (normalizedPath.StartsWith("object/ride", StringComparison.Ordinal)) return "宠物/骑宠";
        if (normalizedPath.StartsWith("object/cha_pic", StringComparison.Ordinal)) return "外观/模型";
        if (normalizedPath.StartsWith("item/", StringComparison.Ordinal)) return "物品";
        if (normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal)) return "角色/怪物";
        if (normalizedPath.StartsWith("object/cha_fight", StringComparison.Ordinal)) return "战斗属性";
        if (normalizedPath.StartsWith("object/", StringComparison.Ordinal)) return "对象/NPC";
        if (normalizedPath.StartsWith("quest/", StringComparison.Ordinal)) return "任务";
        if (normalizedPath.StartsWith("skill/", StringComparison.Ordinal)) return "技能/状态";
        if (normalizedPath.StartsWith("pet/", StringComparison.Ordinal)) return "宠物/骑宠";
        return tableName.Contains("骑宠", StringComparison.Ordinal) ? "宠物/骑宠" : "MB 摘要";
    }

    private static int GetGlobalMbSortRank(string category) => category switch
    {
        "套装" => 1,
        "宠物/骑宠" => 2,
        "物品" => 3,
        "外观/模型" => 4,
        "角色/怪物" => 5,
        "对象/NPC" => 6,
        "战斗属性" => 7,
        "任务" => 8,
        "技能/状态" => 9,
        _ => 30
    };

    private string BuildGlobalSetSummary(
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "套装图鉴/套装说明");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(title));
        AppendGlobalValue(builder, "套装ID", GetCell(row, 1));
        AppendGlobalValue(builder, "等级/显示档位", GetCell(row, 2));
        AppendGlobalValue(builder, "分类/件数配置", FormatGlobalListCell(GetCell(row, 3)));
        AppendGlobalValue(builder, "组别/品质", GetCell(row, 4));
        AppendGlobalItemReferences(builder, "包含物品", row.Skip(5), links, linkKeys, 16);
        return builder.ToString().Trim();
    }

    private string BuildGlobalSetConfigSummary(
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "套装效果/套装配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(title));
        AppendGlobalValue(builder, "配置ID", GuessGlobalId(row));
        AppendGlobalItemReferences(builder, "可能关联的物品", row.Skip(2), links, linkKeys, 14);
        AppendReadableGlobalFields(builder, null, row, new HashSet<int> { 0, 1 }, 6);
        return builder.ToString().Trim();
    }

    private string BuildGlobalItemSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        bool rawList = normalizedPath.StartsWith("item/raw_list", StringComparison.Ordinal);
        string id = rawList ? GetCell(row, 1) : GuessGlobalId(row);
        string name = rawList ? GetCell(row, 0) : GuessGlobalName(row, title);

        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", GetItemGlobalType(normalizedPath));
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(name));
        AppendGlobalValue(builder, "物品ID", id);
        AppendGlobalItemReferences(builder, "关联物品", row.Skip(2), links, linkKeys, 12);
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int> { rawList ? 0 : -1, rawList ? 1 : -1 }, 8);
        return builder.ToString().Trim();
    }

    private string BuildGlobalRideSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "骑宠/坐骑");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "坐骑ID", GetCell(row, 1));
        AppendGlobalValue(builder, "角色ID", GetCell(row, 2));
        AppendGlobalValue(builder, "外观/模型组", FormatGlobalListCell(string.Join("*", row.Skip(3).Take(4))));
        AppendGlobalValue(builder, "移动速度", FormatNumberCell(GetCell(row, 9)));
        AppendGlobalValue(builder, "可学技能", FormatGlobalListCell(string.Join("*", row.Skip(16).Take(5))));
        AppendGlobalItemReferences(builder, "关联物品", row.Skip(1), links, linkKeys, 12);
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 9, 16, 17, 18, 19, 20 }, 6);
        return builder.ToString().Trim();
    }

    private string BuildGlobalAppearanceSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        string config = GetCell(row, 2);
        string icon = NormalizePortraitIconName(GetCell(row, 25));

        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "外观/模型配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "外观ID", GetCell(row, 1));
        AppendGlobalValue(builder, "模型配置", config.Replace('\\', '/'));
        AppendGlobalValue(builder, "模型部件", FormatGlobalListCell(GetCell(row, 3)));
        AppendGlobalValue(builder, "缩放", GetCell(row, 4));
        AppendGlobalValue(builder, "图标", icon);

        foreach (string reference in new[] { config, icon }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (AssetEntry linkedAsset in FindAssetsByReferenceFlexible(_workspace.Assets, reference).Take(12))
                AddGlobalSearchLink(links, linkKeys, linkedAsset, GetGlobalAssetKindText(linkedAsset), "外观配置引用");
        }

        return builder.ToString().Trim();
    }

    private string BuildGlobalCharacterSummary(
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        string name = CleanMonsterName(GetCell(row, 0));
        string roleId = GetCell(row, 1);
        string fightId = GetCell(row, 4);
        string picId = GetCell(row, 5);
        Dictionary<string, string[]> fightRows = GetChaFightRows();
        Dictionary<string, string[]> picRows = GetChaPicRows();

        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "怪物/NPC/角色条目");
        AppendGlobalValue(builder, "名称", string.IsNullOrWhiteSpace(name) ? CleanGlobalTitle(title) : name);
        AppendGlobalValue(builder, "角色ID", roleId);
        AppendGlobalValue(builder, "战斗属性ID", fightId);
        AppendGlobalValue(builder, "外观模型ID", picId);

        if (fightRows.TryGetValue(fightId, out string[]? fightRow))
        {
            AppendGlobalValue(builder, "等级", FormatNumberCell(GetCell(fightRow, 1)));
            AppendGlobalValue(builder, "生命", FormatNumberCell(GetCell(fightRow, 25)));
            AppendGlobalValue(builder, "伤害", FormatNumberCell(GetCell(fightRow, 28)));
            AppendGlobalValue(builder, "防御", FormatNumberCell(GetCell(fightRow, 29)));
            AppendGlobalValue(builder, "法抗", FormatNumberCell(GetCell(fightRow, 32)));
        }

        if (picRows.TryGetValue(picId, out string[]? picRow))
        {
            AppendGlobalValue(builder, "外观名称", GetCell(picRow, 0));
            AppendGlobalValue(builder, "模型配置", GetCell(picRow, 2));
        }

        AppendGlobalValue(builder, "常驻状态ID", FormatGlobalListCell(GetCell(row, 13)));
        AppendGlobalValue(builder, "技能/掉落组ID", FormatGlobalListCell(GetCell(row, 18)));
        AppendGlobalItemReferences(builder, "可能掉落/奖励物品", row.Skip(18), links, linkKeys, 10);
        return builder.ToString().Trim();
    }

    private static string BuildGlobalFightSummary(IReadOnlyList<string> row, string title)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "战斗属性行");
        AppendGlobalValue(builder, "名称/编号", CleanGlobalTitle(title));
        AppendGlobalValue(builder, "战斗属性ID", GetCell(row, 0));
        AppendGlobalValue(builder, "等级", FormatNumberCell(GetCell(row, 1)));
        AppendGlobalValue(builder, "生命", FormatNumberCell(GetCell(row, 25)));
        AppendGlobalValue(builder, "伤害", FormatNumberCell(GetCell(row, 28)));
        AppendGlobalValue(builder, "防御", FormatNumberCell(GetCell(row, 29)));
        AppendGlobalValue(builder, "法抗", FormatNumberCell(GetCell(row, 32)));
        AppendGlobalValue(builder, "综合评分/战力", FormatNumberCell(GetCell(row, 49)));
        return builder.ToString().Trim();
    }

    private string BuildGlobalObjectSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", asset.Name.Contains("business", StringComparison.OrdinalIgnoreCase) ? "NPC 商店/兑换配置" : "对象/NPC 配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "ID", GuessGlobalId(row));
        AppendGlobalItemReferences(builder, "关联物品", row.Skip(1), links, linkKeys, 16);
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int>(), 8);
        return builder.ToString().Trim();
    }

    private static string BuildGlobalQuestSummary(AssetEntry asset, IReadOnlyList<string> row, string title)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "任务/任务文本配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "任务ID", GuessGlobalId(row));
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int>(), 10);
        return builder.ToString().Trim();
    }

    private string BuildGlobalSkillSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", asset.Entry.Path.Contains("state", StringComparison.OrdinalIgnoreCase) ? "状态/Buff 配置" : "技能配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "ID", GuessGlobalId(row));
        string uiIcon = row.Skip(1).FirstOrDefault(cell =>
            IsCompactNumericCell(cell.Trim()) &&
            !cell.Trim().Equals(GuessGlobalId(row), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(uiIcon))
        {
            AppendGlobalValue(builder, "技能UI", uiIcon);
            AddGlobalProfileIconLink(links, linkKeys, uiIcon, "技能UI");
        }
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int>(), 10);
        return builder.ToString().Trim();
    }

    private string BuildGlobalPetSummary(
        AssetEntry asset,
        IReadOnlyList<string> row,
        string title,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys)
    {
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", "宠物/骑宠配置");
        AppendGlobalValue(builder, "名称", CleanGlobalTitle(GuessGlobalName(row, title)));
        AppendGlobalValue(builder, "ID", GuessGlobalId(row));
        AppendGlobalItemReferences(builder, "关联物品", row.Skip(1), links, linkKeys, 10);
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int>(), 10);
        return builder.ToString().Trim();
    }

    private static string BuildGlobalGenericMbSummary(AssetEntry asset, IReadOnlyList<string> row, string title)
    {
        ResourceExplanation explanation = ResourceExplanationService.Explain(asset);
        var builder = new StringBuilder();
        AppendGlobalValue(builder, "类型", explanation.FriendlyName);
        AppendGlobalValue(builder, "名称/编号", CleanGlobalTitle(title));
        AppendReadableGlobalFields(builder, asset, row, new HashSet<int>(), 10);
        return builder.ToString().Trim();
    }

    private static string GetItemGlobalType(string normalizedPath)
    {
        if (normalizedPath.Contains("brandbox", StringComparison.Ordinal)) return "礼包/宝箱/碎片物品";
        if (normalizedPath.Contains("rand", StringComparison.Ordinal)) return "掉落组/随机物品组";
        if (normalizedPath.Contains("set", StringComparison.Ordinal)) return "套装/装备集合";
        if (normalizedPath.Contains("raw", StringComparison.Ordinal)) return "物品基础名单";
        return "物品/道具记录";
    }

    private void AppendGlobalItemReferences(
        StringBuilder builder,
        string label,
        IEnumerable<string> cells,
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> linkKeys,
        int maximum)
    {
        Dictionary<string, string> itemNames = GetItemNameById();
        string[] ids = cells
            .SelectMany(ExtractGlobalNumericTokens)
            .Where(id => id.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximum + 1)
            .ToArray();
        if (ids.Length == 0) return;

        string[] visible = ids.Take(maximum)
            .Select(id => itemNames.TryGetValue(id, out string? name) && !string.IsNullOrWhiteSpace(name)
                ? $"{name}（{id}）"
                : id)
            .ToArray();
        string suffix = ids.Length > maximum ? $"，另有 {ids.Length - maximum} 个" : string.Empty;
        AppendGlobalValue(builder, label, string.Join("、", visible) + suffix);

        foreach (string id in ids.Take(maximum))
        {
            itemNames.TryGetValue(id, out string? name);
            AddGlobalSyntheticLink(
                links,
                linkKeys,
                $"item:{id}",
                "物品",
                string.IsNullOrWhiteSpace(name) ? id : name,
                id,
                label);
        }
    }

    private static void AppendReadableGlobalFields(
        StringBuilder builder,
        AssetEntry? asset,
        IReadOnlyList<string> row,
        ISet<int> skippedColumns,
        int maximum)
    {
        if (row.Count == 0 || maximum <= 0) return;
        string[] headers = asset is not null
            ? GetKnownMbHeaders(asset, row.Count)
            : Array.Empty<string>();
        int added = 0;
        for (int index = 0; index < row.Count && added < maximum; index++)
        {
            if (skippedColumns.Contains(index)) continue;
            string value = GetCell(row, index);
            if (!IsUsefulGlobalFieldValue(value)) continue;

            string name = asset is not null ? GetColumnName(headers, index) : $"关键内容{index + 1}";
            if (name.StartsWith("字段", StringComparison.Ordinal) && !IsMeaningfulGlobalNameCell(value)) continue;
            if (name.Contains("保留", StringComparison.Ordinal)) continue;

            AppendGlobalValue(builder, name, FormatGlobalListCell(value));
            added++;
        }
    }

    private static bool IsUsefulGlobalFieldValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim();
        if (normalized is "0" or "-1" or "null" or "NULL") return false;
        return normalized.Length <= 160;
    }

    private static string GuessGlobalName(IReadOnlyList<string> row, string fallback)
    {
        string? name = row.Take(10)
            .Select(ExtractGlobalNameCandidate)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(name) ? CleanGlobalTitle(fallback) : name;
    }

    private static string GuessGlobalId(IReadOnlyList<string> row)
    {
        string? id = row.Take(10)
            .Select(cell => cell.Trim())
            .FirstOrDefault(IsCompactNumericCell);
        return id ?? string.Empty;
    }

    private static string CleanGlobalTitle(string value)
    {
        string cleaned = Regex.Replace(CleanGlobalMarkup(value), @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"（ID\s*\d+）", string.Empty).Trim();
        return cleaned;
    }

    private static string CleanGlobalMarkup(string value)
    {
        string cleaned = value ?? string.Empty;
        cleaned = cleaned.Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"<c(?::[0-9A-Fa-f]+)?>", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"</?[^>]+>", string.Empty);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        return cleaned.Trim();
    }

    private static IEnumerable<string> ExtractGlobalNumericTokens(string value) =>
        SplitIdList(value)
            .Select(token => token.Trim())
            .Where(token => token.Length is > 0 and <= 12 && token.All(char.IsDigit));

    private static string FormatGlobalListCell(string value)
    {
        string[] ids = ExtractGlobalNumericTokens(value).ToArray();
        if (ids.Length >= 2)
        {
            string suffix = ids.Length > 16 ? $"，另有 {ids.Length - 16} 个" : string.Empty;
            return string.Join("、", ids.Take(16)) + suffix;
        }

        return TrimPreviewCell(value);
    }

    private static void AppendGlobalValue(StringBuilder builder, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        builder.Append(label);
        builder.Append("：");
        builder.AppendLine(value.Trim());
    }

    private static void AddGlobalSyntheticLink(
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        string key,
        string kind,
        string name,
        string path,
        string detail)
    {
        if (!seen.Add(key)) return;
        links.Add(new GlobalSearchLinkViewModel(kind, name, path, detail, null));
    }

    private static string CreateFocusedGlobalSnippet(string text, IReadOnlyList<string[]> variants, int maximumLength)
    {
        string normalized = Regex.Replace(text, @"\s+", " ").Trim();
        int matchIndex = variants
            .SelectMany(group => group)
            .Select(variant => normalized.IndexOf(variant, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        int start = Math.Max(0, matchIndex - maximumLength / 3);
        if (start + maximumLength > normalized.Length)
            start = Math.Max(0, normalized.Length - maximumLength);
        string snippet = normalized.Substring(start, Math.Min(maximumLength, normalized.Length - start));
        if (start > 0) snippet = "..." + snippet;
        if (start + maximumLength < normalized.Length) snippet += "...";
        return snippet;
    }

    private static string CreateGlobalSnippet(string text, int maximumLength)
    {
        string normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength] + "...";
    }

    private IReadOnlyList<GlobalSearchLinkViewModel> BuildGlobalSearchLinks(
        IReadOnlyList<AssetEntry> assets,
        AssetEntry source,
        string text)
    {
        var links = new List<GlobalSearchLinkViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGlobalSearchLink(links, seen, source, "源文件", "命中所在文件");
        string cleanedText = CleanGlobalMarkup(text);

        foreach (Match match in Regex.Matches(text, @"<ic:(\d+)>", RegexOptions.IgnoreCase))
        {
            string iconName = match.Groups[1].Value + ".png";
            foreach (AssetEntry asset in FindAssetsByReference(assets, iconName).Take(4))
                AddGlobalSearchLink(links, seen, asset, "图标", $"来自 {match.Value}");
        }

        foreach (Match match in Regex.Matches(cleanedText, @"(?i)(?:[\w.-]+[\\/])+[\w.-]+\.(?:png|jpg|jpeg|dds|tga|pmf|cct|cmf|psf|paf|xml|txt|gfx|wav|ogg)"))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(assets, match.Value).Take(8))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), "文本中的资源路径");
            if (links.Count >= 60) break;
        }

        foreach (Match match in Regex.Matches(cleanedText, @"(?i)\b[\w.-]+\.(?:png|jpg|jpeg|dds|tga|pmf|cct|cmf|psf|paf|xml|txt|gfx|wav|ogg)\b"))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(assets, match.Value).Take(8))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), "文本中的文件名");
            if (links.Count >= 60) break;
        }

        foreach (string token in ExtractGlobalResourceTokens(cleanedText))
        {
            foreach (AssetEntry asset in FindAssetsByReferenceFlexible(assets, token).Take(10))
                AddGlobalSearchLink(links, seen, asset, GetGlobalAssetKindText(asset), "文本中的资源标识");
            if (links.Count >= 60) break;
        }

        return links.Take(60).ToArray();
    }

    private static IEnumerable<AssetEntry> FindAssetsByReference(IReadOnlyList<AssetEntry> assets, string reference)
    {
        string normalized = reference.Replace('\\', '/').Trim().Trim('"', '\'', '<', '>', ',', ';');
        if (normalized.Length == 0) yield break;
        string fileName = System.IO.Path.GetFileName(normalized);
        foreach (AssetEntry asset in assets)
        {
            string path = asset.Entry.Path.Replace('\\', '/');
            if (path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
                asset.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                asset.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                yield return asset;
            }
        }
    }

    private static IEnumerable<AssetEntry> FindAssetsByReferenceFlexible(IReadOnlyList<AssetEntry> assets, string reference)
    {
        string normalized = reference.Replace('\\', '/').Trim().Trim('"', '\'', '<', '>', ',', ';', '，', '。');
        if (normalized.Length == 0) yield break;

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetEntry asset in FindAssetsByReference(assets, normalized))
        {
            if (yielded.Add(asset.DisplayPath))
                yield return asset;
        }

        string fileName = System.IO.Path.GetFileName(normalized);
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string token = string.IsNullOrWhiteSpace(stem) ? normalized : stem;
        if (token.Length < 3) yield break;

        foreach (AssetEntry asset in assets
                     .Where(asset => IsGlobalResourceLinkCandidate(asset))
                     .Where(asset => GlobalAssetMatchesReferenceToken(asset, token))
                     .OrderBy(asset => ScoreGlobalLinkedAsset(asset, token))
                     .ThenBy(asset => asset.Entry.Path, NaturalStringComparer.Instance))
        {
            if (yielded.Add(asset.DisplayPath))
                yield return asset;
        }
    }

    private static bool GlobalAssetMatchesReferenceToken(AssetEntry asset, string token)
    {
        string path = asset.Entry.Path.Replace('\\', '/');
        string stem = System.IO.Path.GetFileNameWithoutExtension(asset.Name);
        return stem.Equals(token, StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/" + token + "/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGlobalResourceLinkCandidate(AssetEntry asset) =>
        asset.Kind is AssetKind.Image or AssetKind.Model ||
        asset.Extension is ".cct" or ".cmf" or ".psf" or ".paf" or ".xml" or ".txt";

    private static int ScoreGlobalLinkedAsset(AssetEntry asset, string token)
    {
        string path = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        string extension = asset.Extension;
        int score = 50;
        if (path.Contains("/" + token.ToLowerInvariant() + "/", StringComparison.Ordinal)) score -= 20;
        if (System.IO.Path.GetFileNameWithoutExtension(asset.Name).Equals(token, StringComparison.OrdinalIgnoreCase)) score -= 10;
        if (extension.Equals(".cct", StringComparison.OrdinalIgnoreCase)) score -= 30;
        else if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && path.Contains("/icon/", StringComparison.Ordinal)) score -= 28;
        else if (extension.Equals(".pmf", StringComparison.OrdinalIgnoreCase)) score -= 24;
        else if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)) score -= 18;
        else if (extension.Equals(".cmf", StringComparison.OrdinalIgnoreCase)) score -= 12;
        else if (extension.Equals(".psf", StringComparison.OrdinalIgnoreCase)) score -= 8;
        else if (extension.Equals(".paf", StringComparison.OrdinalIgnoreCase)) score -= 4;
        return score;
    }

    private static IEnumerable<string> ExtractGlobalResourceTokens(string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?i)\b(?:gw|ys|zj|fb|npc|obj|m|st|hd|mz|pf|pj|qz|yd|xz|sz|my|p)_[A-Za-z0-9_.-]{2,}\b"))
        {
            string token = match.Value.Trim('_', '.', '-');
            if (token.Length < 4) continue;
            yield return token;
            if (token.StartsWith("m_", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                yield return token[2..];
        }
    }

    private static void AddGlobalSearchLink(
        ICollection<GlobalSearchLinkViewModel> links,
        ISet<string> seen,
        AssetEntry asset,
        string kind,
        string detail)
    {
        string key = asset.DisplayPath;
        if (!seen.Add(key)) return;
        links.Add(new GlobalSearchLinkViewModel(kind, asset.Name, asset.DisplayPath, detail, asset));
    }

    private AssetEntry? FindLinkedGlobalAsset(GlobalSearchLinkViewModel link)
    {
        if (link.Asset is not null) return link.Asset;

        if (link.Kind.Contains("图标", StringComparison.OrdinalIgnoreCase))
            return FindGlobalImageAsset(link.Path) ?? FindGlobalImageAsset(link.Name);

        string normalized = link.Path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        string fileName = System.IO.Path.GetFileName(normalized);
        return _workspace.Assets.FirstOrDefault(asset =>
            asset.DisplayPath.Equals(link.Path, StringComparison.OrdinalIgnoreCase) ||
            asset.Entry.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            asset.Entry.Path.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
            asset.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private AssetEntry? FindGlobalImageAsset(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        Dictionary<string, AssetEntry> images = GetGlobalImageAssetsByKey();
        foreach (string key in GetGlobalImageReferenceKeys(reference))
        {
            if (images.TryGetValue(key, out AssetEntry? asset))
                return asset;
        }

        return null;
    }

    private Dictionary<string, AssetEntry> GetGlobalImageAssetsByKey()
    {
        if (_globalImageAssetByKey is not null) return _globalImageAssetByKey;

        var result = new Dictionary<string, AssetEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetEntry asset in _workspace.Assets
                     .Where(asset => asset.Kind == AssetKind.Image)
                     .OrderByDescending(ScoreGlobalImageAsset)
                     .ThenBy(asset => asset.Entry.Path, NaturalStringComparer.Instance))
        {
            string normalized = asset.Entry.Path.Replace('\\', '/').Trim('/');
            string fileName = System.IO.Path.GetFileName(normalized);
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            AddGlobalImageKey(result, normalized, asset);
            AddGlobalImageKey(result, fileName, asset);
            AddGlobalImageKey(result, stem, asset);
            if (!fileName.Equals(asset.Name, StringComparison.OrdinalIgnoreCase))
                AddGlobalImageKey(result, asset.Name, asset);
        }

        _globalImageAssetByKey = result;
        return _globalImageAssetByKey;
    }

    private static int ScoreGlobalImageAsset(AssetEntry asset)
    {
        string path = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        int score = 0;
        if (path.Contains("/icon/", StringComparison.Ordinal)) score += 80;
        if (path.Contains("/item/", StringComparison.Ordinal)) score += 70;
        if (path.Contains("icon", StringComparison.Ordinal)) score += 50;
        if (path.Contains("portrait", StringComparison.Ordinal)) score += 35;
        if (path.Contains("head", StringComparison.Ordinal)) score += 25;
        if (asset.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) score += 8;
        return score;
    }

    private static void AddGlobalImageKey(IDictionary<string, AssetEntry> keys, string key, AssetEntry asset)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        keys.TryAdd(key.Trim(), asset);
    }

    private static IEnumerable<string> GetGlobalImageReferenceKeys(string reference)
    {
        string normalized = reference.Replace('\\', '/').Trim().Trim('"', '\'', '<', '>', ',', ';');
        foreach (Match match in Regex.Matches(normalized, @"<ic:(\d+)>", RegexOptions.IgnoreCase))
        {
            yield return match.Groups[1].Value;
            yield return match.Groups[1].Value + ".png";
        }

        if (normalized.Length == 0) yield break;
        string fileName = System.IO.Path.GetFileName(normalized);
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        yield return normalized;
        yield return fileName;
        if (!string.IsNullOrWhiteSpace(stem)) yield return stem;
        if (!normalized.Contains('.', StringComparison.Ordinal))
        {
            yield return normalized + ".png";
            yield return normalized + ".dds";
        }
    }

    private static string GetGlobalAssetKindText(AssetKind kind) => kind switch
    {
        AssetKind.Image => "图像",
        AssetKind.Sound => "声音",
        AssetKind.Model => "模型",
        AssetKind.Font => "字体",
        AssetKind.MbTable => "MB 表",
        AssetKind.DungeonSummary => "副本",
        _ => "资源"
    };

    private static string GetGlobalAssetKindText(AssetEntry asset)
    {
        string path = asset.Entry.Path.Replace('\\', '/');
        return asset.Extension switch
        {
            ".cct" => "模型配置",
            ".cmf" => "材质配置",
            ".psf" => "骨骼",
            ".paf" => "动作",
            ".pmf" => "模型部件",
            ".dds" => "贴图",
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".ico" => path.Contains("/icon/", StringComparison.OrdinalIgnoreCase) ||
                                                                  path.Contains("portrait", StringComparison.OrdinalIgnoreCase)
                ? "图标"
                : "图像",
            _ => GetGlobalAssetKindText(asset.Kind)
        };
    }

    private async Task LoadGlobalSearchThumbnailsAsync(IReadOnlyList<GlobalSearchResultViewModel> results, int generation)
    {
        foreach (GlobalSearchResultViewModel result in results.Take(160))
        {
            if (generation != _globalSearchGeneration) return;

            AssetEntry? resultImage = result.Links
                .Where(link => link.Kind.Equals("主图标", StringComparison.Ordinal))
                .Select(link => link.Asset)
                .FirstOrDefault(asset => asset?.Kind == AssetKind.Image) ??
                (result.Asset?.Kind == AssetKind.Image ? result.Asset : null);
            foreach (GlobalSearchLinkViewModel link in result.Links.Take(36))
            {
                AssetEntry? linkImage = ResolveGlobalSearchLinkThumbnailAsset(link);
                if (linkImage is null) continue;

                BitmapImage? thumbnail = await LoadGlobalThumbnailAsync(linkImage, generation);
                if (thumbnail is null) continue;

                link.Thumbnail = thumbnail;
                resultImage ??= linkImage;
                if (result.Thumbnail is null)
                {
                    result.Thumbnail = thumbnail;
                    if (Equals(GlobalSearchResultList.SelectedItem, result))
                        GlobalSearchPreviewImage.Source = result.Thumbnail;
                }
            }

            foreach (GlobalSearchSkillViewModel skill in result.Skills)
            {
                if (generation != _globalSearchGeneration) return;
                if (skill.IconAsset is null) continue;
                skill.Icon = await LoadGlobalThumbnailAsync(skill.IconAsset, generation);
            }

            if (result.Thumbnail is null && resultImage is not null)
            {
                BitmapImage? thumbnail = await LoadGlobalThumbnailAsync(resultImage, generation);
                if (thumbnail is null) continue;
                result.Thumbnail = thumbnail;
                if (Equals(GlobalSearchResultList.SelectedItem, result))
                    GlobalSearchPreviewImage.Source = result.Thumbnail;
            }
        }
    }

    private AssetEntry? ResolveGlobalSearchLinkThumbnailAsset(GlobalSearchLinkViewModel link)
    {
        if (link.Asset?.Kind == AssetKind.Image) return link.Asset;
        if (link.Kind.Contains("图标", StringComparison.Ordinal) ||
            link.Kind.Contains("头像", StringComparison.Ordinal) ||
            link.Kind.Contains("物品", StringComparison.Ordinal) ||
            link.Kind.Contains("套装", StringComparison.Ordinal))
        {
            return FindItemIconAsset(link.Path) ??
                   FindItemIconAsset(link.Name) ??
                   FindGlobalImageAsset(link.Path) ??
                   FindGlobalImageAsset(link.Name);
        }

        return FindGlobalImageAsset(link.Path) ?? FindGlobalImageAsset(link.Name);
    }

    private async Task<BitmapImage?> LoadGlobalThumbnailAsync(AssetEntry image, int generation)
    {
        if (_globalThumbnailCache.TryGetValue(image.DisplayPath, out BitmapImage? cached))
            return cached;

        try
        {
            byte[] data = await Task.Run(() => _workspace.Extract(image));
            if (generation != _globalSearchGeneration) return null;
            BitmapImage bitmap = await CreateBitmapAsync(data, 96);
            _globalThumbnailCache[image.DisplayPath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void GlobalSearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GlobalSearchResultViewModel? result = e.AddedItems.OfType<GlobalSearchResultViewModel>().LastOrDefault() ??
                                              GlobalSearchResultList.SelectedItem as GlobalSearchResultViewModel;
        ShowGlobalSearchResult(result);
    }

    private void ShowGlobalSearchResult(GlobalSearchResultViewModel? result)
    {
        GlobalSearchDetailScrollViewer.ChangeView(null, 0, null, disableAnimation: true);

        if (result is null)
        {
            _globalDetailPreviewGeneration++;
            _globalModelPreview.SetMesh(null);
            GlobalSearchModelPanel.Visibility = Visibility.Collapsed;
            GlobalSearchModelStatusText.Text = string.Empty;
            GlobalSearchFactsPanel.Visibility = Visibility.Collapsed;
            GlobalSearchFactsList.ItemsSource = null;
            GlobalSearchSkillsPanel.Visibility = Visibility.Collapsed;
            GlobalSearchSkillsList.ItemsSource = null;
            GlobalSearchPreviewImage.Source = null;
            GlobalSearchDetailTitleText.Text = "选择一条结果";
            GlobalSearchDetailMetaText.Text = string.Empty;
            GlobalSearchDetailSourceText.Text = string.Empty;
            GlobalSearchResourceSectionList.ItemsSource = null;
            GlobalSearchNoLinksText.Visibility = Visibility.Visible;
            GlobalSearchRawTextBox.Text = string.Empty;
            return;
        }

        GlobalSearchPreviewImage.Source = result.Thumbnail;
        GlobalSearchDetailTitleText.Text = result.Title;
        GlobalSearchDetailMetaText.Text = $"{result.Category} · {result.Subtitle}";
        GlobalSearchDetailSourceText.Text =
            $"已关联 {result.Links.Count:N0} 项资源" +
            (result.Skills.Count > 0 ? $" · {result.Skills.Count:N0} 个技能" : string.Empty);
        GlobalSearchFactsList.ItemsSource = result.Facts;
        GlobalSearchFactsPanel.Visibility = result.Facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GlobalSearchSkillsList.ItemsSource = result.Skills;
        GlobalSearchSkillsPanel.Visibility = result.Skills.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GlobalSearchResourceSectionList.ItemsSource = result.ResourceSections;
        GlobalSearchNoLinksText.Visibility = result.Links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GlobalSearchRawTextBox.Text = result.RawText;
        _ = PreviewGlobalSearchModelAsync(result, ++_globalDetailPreviewGeneration);
    }

    private void GlobalSearchLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GlobalSearchLinkViewModel link }) return;
        NavigateGlobalSearchLink(link);
    }

    private void NavigateGlobalSearchLink(GlobalSearchLinkViewModel link)
    {
        AssetEntry? asset = FindLinkedGlobalAsset(link);
        if (asset is not null)
        {
            NavigateToAsset(asset);
            return;
        }

        string query = ExtractGlobalNumericTokens(link.Path).FirstOrDefault() ??
                       ExtractGlobalNumericTokens(link.Name).FirstOrDefault() ??
                       link.Name;
        if (!string.IsNullOrWhiteSpace(query))
            RunGlobalSearchForLink(query);
    }

    private void NavigateToAsset(AssetEntry asset)
    {
        NavigationViewItem? navigationItem = FindNavigationItemForAsset(asset);
        if (navigationItem is null)
        {
            RunGlobalSearchForLink(asset.Name);
            return;
        }

        if (!Equals(CategoryNavigation.SelectedItem, navigationItem))
            CategoryNavigation.SelectedItem = navigationItem;

        SelectFolderForAsset(asset);
        if (IsCompositeConfigAsset(asset))
        {
            SearchBox.Text = string.Empty;
            ApplyFilter();
            AssetItemViewModel? compositeItem = _items.FirstOrDefault(candidate =>
                candidate.Composite?.ConfigAsset.DisplayPath.Equals(asset.DisplayPath, StringComparison.OrdinalIgnoreCase) == true);
            if (compositeItem is not null)
            {
                ListViewBase compositeList = GetActiveAssetList();
                compositeList.SelectedItem = compositeItem;
                compositeList.ScrollIntoView(compositeItem);
                return;
            }
        }

        SearchBox.Text = System.IO.Path.GetFileNameWithoutExtension(asset.Name);
        ApplyFilter();

        AssetItemViewModel? item = _items.FirstOrDefault(candidate =>
            candidate.Asset?.DisplayPath.Equals(asset.DisplayPath, StringComparison.OrdinalIgnoreCase) == true);
        if (item is null)
        {
            SearchBox.Text = asset.Name;
            ApplyFilter();
            item = _items.FirstOrDefault(candidate =>
                candidate.Asset?.DisplayPath.Equals(asset.DisplayPath, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (item is null) return;
        ListViewBase list = GetActiveAssetList();
        list.SelectedItem = item;
        list.ScrollIntoView(item);
    }

    private NavigationViewItem? FindNavigationItemForAsset(AssetEntry asset) =>
        FindNavigationItemForKind(IsModelResourceAsset(asset) ? AssetKind.Model : asset.Kind);

    private static bool IsModelResourceAsset(AssetEntry asset) =>
        asset.Kind == AssetKind.Model ||
        asset.Extension is ".cct" or ".cmf" or ".psf" or ".paf";

    private static bool IsCompositeConfigAsset(AssetEntry asset) =>
        asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase);

    private void RunGlobalSearchForLink(string query)
    {
        if (!Equals(CategoryNavigation.SelectedItem, GlobalSearchNavigationItem))
            CategoryNavigation.SelectedItem = GlobalSearchNavigationItem;
        SearchBox.Text = query;
        RefreshGlobalSearch();
    }

    private NavigationViewItem? FindNavigationItemForKind(AssetKind kind)
    {
        string tag = kind switch
        {
            AssetKind.Image => "image",
            AssetKind.Sound => "sound",
            AssetKind.Model => "model",
            AssetKind.Font => "font",
            AssetKind.MbTable => "mb",
            AssetKind.GlobalSearch => "global",
            AssetKind.DungeonSummary => "dungeon",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(tag)) return null;

        return CategoryNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is string itemTag &&
                                    itemTag.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectFolderForAsset(AssetEntry asset)
    {
        string directory = GetNavigationFolderPathForAsset(asset);
        FolderNodeInfo folder = new(
            string.IsNullOrWhiteSpace(directory) ? asset.ArchiveName : System.IO.Path.GetFileName(directory),
            asset.ArchivePath,
            directory);
        _selectedFolder = folder;
        CurrentFolderText.Text = folder.DisplayPath;
        TreeViewNode? node = FindFolderTreeNode(asset.ArchivePath, directory) ??
                             FindFolderTreeNode(asset.ArchivePath, string.Empty);
        if (node is not null)
            FolderTree.SelectedNode = node;
    }

    private static string GetNavigationFolderPathForAsset(AssetEntry asset)
    {
        string directory = GetInternalDirectory(asset.Entry.Path);
        if (IsCompositeConfigAsset(asset) &&
            directory.EndsWith("/config", StringComparison.OrdinalIgnoreCase))
        {
            int slash = directory.LastIndexOf('/');
            return slash < 0 ? string.Empty : directory[..slash];
        }

        return directory;
    }

    private TreeViewNode? FindFolderTreeNode(string archivePath, string internalPath)
    {
        foreach (TreeViewNode root in FolderTree.RootNodes)
        {
            TreeViewNode? match = FindFolderTreeNode(root, archivePath, internalPath);
            if (match is not null) return match;
        }

        return null;
    }

    private static TreeViewNode? FindFolderTreeNode(TreeViewNode node, string archivePath, string internalPath)
    {
        if (node.Content is FolderNodeInfo folder &&
            folder.ArchivePath.Equals(archivePath, StringComparison.OrdinalIgnoreCase) &&
            folder.InternalPath.Equals(internalPath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (TreeViewNode child in node.Children)
        {
            TreeViewNode? match = FindFolderTreeNode(child, archivePath, internalPath);
            if (match is not null) return match;
        }

        return null;
    }

    private void ClearGlobalSearchView()
    {
        GlobalSearchResultList.ItemsSource = null;
        GlobalSearchResultCountText.Text = string.Empty;
        GlobalSearchCountText.Text = string.Empty;
        GlobalSearchEmptyPanel.Visibility = Visibility.Visible;
        ShowGlobalSearchResult(null);
    }

    private void PopulateAssets()
    {
        _thumbnailGeneration++;
        var items = new List<AssetItemViewModel>();
        int compositeCount = 0;
        if (_currentKind == AssetKind.Model && _selectedFolder is not null && string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            IReadOnlyList<CompositeModelEntry> composites = _workspace.FindCompositeModels(
                _selectedFolder.ArchivePath,
                _selectedFolder.InternalPath);
            items.AddRange(composites.Select(composite => new AssetItemViewModel(composite)));
            compositeCount = composites.Count;
        }
        items.AddRange(_filteredAssets.Select(CreateAssetItem));
        _items = items;
        ImageGrid.ItemsSource = _items;
        AssetList.ItemsSource = _items;
        MbAssetList.ItemsSource = _items;
        EmptyPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string scope = string.IsNullOrWhiteSpace(SearchBox.Text) ? "当前文件夹" : "当前目录及子目录";
        string compositeStatus = compositeCount > 0 ? $"，其中 {compositeCount:N0} 个完整组合模型" : string.Empty;
        SetStatus($"{scope}找到 {_items.Count:N0} 个资源{compositeStatus}，已全部显示");
        UpdateSelectionUi(0);
    }

    private AssetItemViewModel CreateAssetItem(AssetEntry asset)
    {
        if (!UseBeginnerNames) return new AssetItemViewModel(asset);
        ResourceExplanation explanation = ResourceExplanationService.Explain(asset);
        if (asset.Kind == AssetKind.MbTable)
            return new AssetItemViewModel(asset, CreateMbTableListName(asset),
                $"{asset.Name} · {explanation.Purpose}");
        return new AssetItemViewModel(asset, explanation.FriendlyName,
            $"原始文件：{asset.Name} · {explanation.Purpose}");
    }

    private static string CreateMbTableListName(AssetEntry asset)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/');
        string[] parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string tableName = GetMbTableDisplayName(asset);
        if (parts.Length == 0) return tableName;
        string folderName = ResourceExplanationService.GetFolderDisplayName(parts[0]);
        return $"{tableName}（{folderName}）";
    }

    private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not AssetItemViewModel item) return;
        _ = LoadThumbnailAsync(item, _thumbnailGeneration);
    }

    private async Task LoadThumbnailAsync(AssetItemViewModel item, int generation)
    {
        if (item.Asset is null || item.Thumbnail is not null || item.IsThumbnailLoading) return;
        item.IsThumbnailLoading = true;
        try
        {
            if (generation != _thumbnailGeneration) return;
            byte[] data = await Task.Run(() => _workspace.Extract(item.Asset));
            if (generation != _thumbnailGeneration) return;
            item.Thumbnail = await CreateBitmapAsync(data, 128);
        }
        catch
        {
            item.Subtitle = "无法生成缩略图";
        }
        finally
        {
            item.IsThumbnailLoading = false;
        }
    }

    private async void Asset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListViewBase list) return;
        int selectedCount = list.SelectedItems.Count;
        UpdateSelectionUi(selectedCount);
        AssetItemViewModel? item = e.AddedItems.OfType<AssetItemViewModel>().LastOrDefault() ??
                                   list.SelectedItems.OfType<AssetItemViewModel>().LastOrDefault();
        if (item is null)
        {
            _previewGeneration++;
            PreviewLoadingOverlay.Visibility = Visibility.Collapsed;
            _selectedAsset = null;
            ResetPreviewSelection();
            return;
        }

        int previewGeneration = ++_previewGeneration;
        PreviewLoadingOverlay.Visibility = Visibility.Collapsed;
        if (item.Composite is CompositeModelEntry composite)
        {
            _selectedAsset = null;
            _selectedComposite = composite;
            SelectedNameText.Text = composite.Name;
            SelectedPathText.Text = composite.DisplayPath;
            SelectedMetadataText.Text = $"正在组合 {composite.Parts.Count:N0} 个模型部件及贴图…";
            ModelTextureSelector.Visibility = Visibility.Collapsed;
            ShowPreviewLoading(previewGeneration, $"正在加载组合模型：{composite.Parts.Count:N0} 个部件");
            try
            {
                await PreviewCompositeModelAsync(composite, previewGeneration);
            }
            finally
            {
                HidePreviewLoading(previewGeneration);
            }
            return;
        }
        if (item.Asset is not AssetEntry asset) return;

        _selectedComposite = null;
        _selectedAsset = asset;
        SelectedNameText.Text = selectedCount > 1 ? $"{item.Name}（已选择 {selectedCount:N0} 项）" : item.Name;
        SelectedPathText.Text = asset.DisplayPath;
        SelectedMetadataText.Text = "正在读取…";
        if (asset.Kind == AssetKind.Model)
            ShowPreviewLoading(previewGeneration, $"正在加载模型：{asset.Name}");

        try
        {
            byte[] data = await Task.Run(() => _workspace.Extract(asset));
            if (_selectedAsset != asset || !IsPreviewCurrent(previewGeneration)) return;

            if (asset.Kind == AssetKind.Image)
            {
                PreviewImage.Source = await CreateBitmapAsync(data, 0);
                SelectedMetadataText.Text = $"{asset.Extension.TrimStart('.').ToUpperInvariant()} · {FormatBytes(data.Length)}";
            }
            else if (asset.Kind == AssetKind.Sound)
            {
                await PlaySoundAsync(asset, data);
                SelectedMetadataText.Text = $"{asset.Extension.TrimStart('.').ToUpperInvariant()} · {FormatBytes(data.Length)}";
            }
            else if (asset.Kind == AssetKind.Model)
            {
                (PmfMesh mesh, IReadOnlyList<ModelTextureBinding> textures) = await Task.Run(() =>
                    (PmfParser.Parse(data), _workspace.ResolveModelTextures(asset)));
                if (_selectedAsset != asset || !IsPreviewCurrent(previewGeneration)) return;
                _modelPreview.SetMesh(mesh);
                ModelTextureSelector.Visibility = Visibility.Visible;
                _settingModelTextureSelection = true;
                ModelTextureComboBox.ItemsSource = textures;
                ModelTextureComboBox.SelectedIndex = textures.Count > 0 ? 0 : -1;
                _settingModelTextureSelection = false;
                SelectedMetadataText.Text = $"PMF v{mesh.Version} · {mesh.Vertices.Count:N0} 顶点 · {mesh.DeclaredTriangleCount:N0} 三角面 · {mesh.UvChannelCount} UV 通道 · {textures.Count:N0} 个关联贴图 · {FormatBytes(data.Length)}";
                if (textures.Count > 0) await LoadModelTextureAsync(asset, textures[0]);
            }
            else
            {
                ResourceExplanation explanation = ResourceExplanationService.Explain(asset);
                bool mbTable = asset.Kind == AssetKind.MbTable;
                if (mbTable)
                {
                    GenericPreviewPanel.Visibility = Visibility.Collapsed;
                    MbTableDataPanel.Visibility = Visibility.Visible;
                    MbTablePreviewPanel.Visibility = Visibility.Collapsed;
                    if (TryBuildMbTableView(asset, data, GetSearchTerms(), out MbTableViewModel? tableView, out string mbTableMessage) &&
                        tableView is not null)
                    {
                        ShowMbTableView(tableView);
                    }
                    else
                    {
                        ShowMbTableError(asset, mbTableMessage);
                    }
                }
                else
                {
                    MbTableDataPanel.Visibility = Visibility.Collapsed;
                    GenericPreviewPanel.Visibility = Visibility.Visible;
                    SetGenericPreviewChromeVisibility(true);
                    GenericPreviewNameText.Text = UseBeginnerNames ? explanation.FriendlyName : asset.Name;
                    GenericPreviewRawNameText.Text = $"原始文件：{asset.Name}\n包内路径：{asset.DisplayPath}";
                    GenericPreviewIcon.Glyph = asset.Kind == AssetKind.Font ? "\uE8D2" : "\uE8A5";
                    GenericPreviewPurposeText.Text = explanation.Purpose;
                    GenericPreviewUsageText.Text = explanation.UsedWhen;
                    GenericPreviewConfidenceText.Text = $"识别程度：{explanation.Confidence}";
                    GenericPreviewTechnicalText.Text = $"技术信息：{ResourceExplanationService.GetTechnicalSummary(asset, data.Length)}";
                    GenericPreviewHintText.Text = explanation.PreviewAdvice;
                    MbTablePreviewPanel.Visibility = Visibility.Collapsed;
                    MbTablePreviewBox.Text = string.Empty;
                }
                string? textPreview = TryCreateTextPreview(asset, data);
                GenericTextPreviewBox.Text = textPreview ?? string.Empty;
                GenericTextExpander.Visibility = mbTable || textPreview is null ? Visibility.Collapsed : Visibility.Visible;
                GenericTextExpander.IsExpanded = false;
                SelectedMetadataText.Text = $"{explanation.FriendlyName} · {ResourceExplanationService.GetTechnicalSummary(asset, data.Length)}";
            }
        }
        catch (Exception ex)
        {
            SelectedMetadataText.Text = $"预览失败：{ex.Message}";
        }
        finally
        {
            if (asset.Kind == AssetKind.Model)
                HidePreviewLoading(previewGeneration);
        }
    }

    private async Task PreviewGlobalSearchModelAsync(
        GlobalSearchResultViewModel result,
        int previewGeneration)
    {
        AssetEntry? config = result.Links
            .Select(link => link.Asset)
            .FirstOrDefault(asset => asset?.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase) == true);
        CompositeModelEntry? composite = config is null ? null : FindGlobalCompositeModel(config);
        if (composite is null)
        {
            if (previewGeneration != _globalDetailPreviewGeneration) return;
            _globalModelPreview.SetMesh(null);
            GlobalSearchModelPanel.Visibility = Visibility.Collapsed;
            GlobalSearchModelStatusText.Text = string.Empty;
            return;
        }

        GlobalSearchModelPanel.Visibility = Visibility.Visible;
        GlobalSearchModelStatusText.Text = "正在加载";
        try
        {
            (ModelRenderPart[] renderParts, int skippedParts, ModelAnimationSet? animationSet) =
                await LoadCompositeRenderPartsAsync(composite);
            if (previewGeneration != _globalDetailPreviewGeneration ||
                !ReferenceEquals(GlobalSearchResultList.SelectedItem, result))
            {
                return;
            }

            if (renderParts.Length == 0)
            {
                _globalModelPreview.SetMesh(null);
                GlobalSearchModelStatusText.Text = "没有可用部件";
                return;
            }

            _globalModelPreview.SetComposite(renderParts, composite.Name);
            _globalModelPreview.SetAnimationSet(animationSet);
            string skipped = skippedParts > 0 ? $"，跳过 {skippedParts:N0} 个异常部件" : string.Empty;
            string actions = animationSet is null ? string.Empty : $"，{animationSet.Animations.Count:N0} 个动作";
            GlobalSearchModelStatusText.Text = $"{renderParts.Length:N0} 个部件{actions}{skipped}";
        }
        catch
        {
            if (previewGeneration != _globalDetailPreviewGeneration) return;
            _globalModelPreview.SetMesh(null);
            GlobalSearchModelStatusText.Text = "模型加载失败";
        }
    }

    private Task<(ModelRenderPart[] RenderParts, int SkippedParts, ModelAnimationSet? AnimationSet)> LoadCompositeRenderPartsAsync(
        CompositeModelEntry composite) =>
        Task.Run(() =>
        {
            var parts = new List<ModelRenderPart>();
            int skipped = 0;
            foreach (CompositeModelPart part in composite.Parts)
            {
                try
                {
                    PmfMesh mesh = PmfParser.Parse(_workspace.Extract(part.MeshAsset));
                    DecodedTexture? texture = null;
                    string textureName = string.Empty;
                    if (part.TextureBinding is ModelTextureBinding binding)
                    {
                        try
                        {
                            texture = DdsDecoder.Decode(_workspace.Extract(binding.TextureAsset));
                            textureName = binding.DisplayName;
                        }
                        catch
                        {
                            // Keep the geometry visible when a single texture cannot be decoded.
                        }
                    }

                    parts.Add(new ModelRenderPart(part.MeshAsset.Name, mesh, texture, textureName));
                }
                catch
                {
                    skipped++;
                }
            }

            ModelAnimationSet? animationSet = null;
            try
            {
                animationSet = _workspace.LoadModelAnimationSet(composite);
            }
            catch
            {
                // Keep the model preview available when optional animation data is malformed.
            }

            return (parts.ToArray(), skipped, animationSet);
        });

    private async Task PreviewCompositeModelAsync(CompositeModelEntry composite, int previewGeneration)
    {
        try
        {
            (ModelRenderPart[] renderParts, int skippedParts, ModelAnimationSet? animationSet) =
                await LoadCompositeRenderPartsAsync(composite);
            if (_selectedComposite != composite || !IsPreviewCurrent(previewGeneration)) return;
            if (renderParts.Length == 0)
            {
                SelectedMetadataText.Text = "组合模型预览失败：没有可用的 PMF 部件";
                return;
            }

            _modelPreview.SetComposite(renderParts, composite.Name);
            _modelPreview.SetAnimationSet(animationSet);
            int vertices = renderParts.Sum(part => part.Mesh.Vertices.Count);
            long triangles = renderParts.Sum(part => (long)part.Mesh.DeclaredTriangleCount);
            int texturedParts = renderParts.Count(part => part.Texture is not null);
            string skippedStatus = skippedParts > 0 ? $" · 跳过 {skippedParts:N0} 个异常部件" : string.Empty;
            string animationStatus = animationSet is null
                ? string.Empty
                : $" · {animationSet.Animations.Count:N0} 个骨骼动作";
            SelectedMetadataText.Text = $"完整组合 · {renderParts.Length:N0} 个 PMF 部件 · {vertices:N0} 顶点 · {triangles:N0} 三角面 · {texturedParts:N0} 个部件已加载贴图{animationStatus}{skippedStatus}";
        }
        catch (Exception ex)
        {
            if (_selectedComposite != composite || !IsPreviewCurrent(previewGeneration)) return;
            SelectedMetadataText.Text = $"组合模型预览失败：{ex.Message}";
        }
    }

    private async void ModelTextureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingModelTextureSelection || _selectedAsset?.Kind != AssetKind.Model ||
            ModelTextureComboBox.SelectedItem is not ModelTextureBinding binding) return;
        await LoadModelTextureAsync(_selectedAsset, binding);
    }

    private async Task LoadModelTextureAsync(AssetEntry modelAsset, ModelTextureBinding binding)
    {
        try
        {
            byte[] textureBytes = await Task.Run(() => _workspace.Extract(binding.TextureAsset));
            DecodedTexture texture = await Task.Run(() => DdsDecoder.Decode(textureBytes));
            if (_selectedAsset != modelAsset || !Equals(ModelTextureComboBox.SelectedItem, binding)) return;
            _modelPreview.SetTexture(texture, binding.DisplayName);
            SelectedMetadataText.Text = $"{SelectedMetadataText.Text.Split(" · 贴图：", StringSplitOptions.None)[0]} · 贴图：{texture.Width}×{texture.Height} {texture.Format}";
        }
        catch (Exception ex)
        {
            if (_selectedAsset != modelAsset) return;
            _modelPreview.SetTexture(null, null);
            SelectedMetadataText.Text = $"贴图预览失败：{ex.Message}；可切换到实体或线框模式";
        }
    }

    private async Task PlaySoundAsync(AssetEntry asset, byte[] data)
    {
        string cacheFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XunxianDpkViewer", "preview");
        Directory.CreateDirectory(cacheFolder);
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.DisplayPath))).Substring(0, 20);
        string target = System.IO.Path.Combine(cacheFolder, hash + asset.Extension);
        await File.WriteAllBytesAsync(target, data);
        StorageFile storageFile = await StorageFile.GetFileFromPathAsync(target);
        _mediaPlayer.Source = MediaSource.CreateFromStorageFile(storageFile);
        SoundNameText.Text = asset.Name;
        _mediaPlayer.Play();
    }

    private static async Task<BitmapImage> CreateBitmapAsync(byte[] data, int decodeWidth)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e) =>
        await PickAndLoadResourceFolderAsync();

    private async void ChooseInitialPathButton_Click(object sender, RoutedEventArgs e) =>
        await PickAndLoadResourceFolderAsync();

    private async Task PickAndLoadResourceFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await LoadResourceFolderAsync(folder.Path);
    }

    private async void OpenArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".dpk");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null) await LoadArchiveAsync(file.Path);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var pathBox = new TextBox
        {
            Header = "当前资源路径",
            Text = string.IsNullOrWhiteSpace(CurrentPathText.Text) ? "尚未选择" : CurrentPathText.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var chooseButton = new Button
        {
            Content = "更换资源目录",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 9, 18, 9)
        };
        var autoUpdateToggle = new ToggleSwitch
        {
            Header = "自动检查更新",
            OffContent = "已关闭",
            OnContent = "已开启",
            IsOn = UserPreferences.LoadAutoCheckForUpdates()
        };
        var checkUpdateButton = new Button
        {
            Content = "检查更新",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 9, 18, 9)
        };
        var panel = new StackPanel { Spacing = 14, Width = 620 };
        panel.Children.Add(pathBox);
        panel.Children.Add(new TextBlock
        {
            Text = "可选择《新寻仙》安装目录，也可以直接选择其中的 res 目录。设置成功后程序会记住路径。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        panel.Children.Add(chooseButton);
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 2),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBorderBrush"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = "软件更新",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(autoUpdateToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "更新会在软件内下载并校验，完成后自动重启安装。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        panel.Children.Add(checkUpdateButton);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "设置",
            Content = panel,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };
        chooseButton.Click += async (_, _) =>
        {
            dialog.Hide();
            await PickAndLoadResourceFolderAsync();
        };
        autoUpdateToggle.Toggled += (_, _) =>
            UserPreferences.SaveAutoCheckForUpdates(autoUpdateToggle.IsOn);
        checkUpdateButton.Click += async (_, _) =>
        {
            dialog.Hide();
            await CheckForUpdatesAsync(silent: false);
        };
        await dialog.ShowAsync();
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 10, Width = 480 };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/XunxianIcon.png")),
            Width = 72,
            Height = 72,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        panel.Children.Add(new TextBlock
        {
            Text = "寻仙 DPK 资源浏览器",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock { Text = $"版本：{AppVersion}" });
        panel.Children.Add(new TextBlock { Text = $"作者：{AppAuthor}" });
        panel.Children.Add(new TextBlock
        {
            Text = "用于浏览、解释和导出《新寻仙》客户端 DPK 资源。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        var checkUpdateButton = new Button
        {
            Content = "检查更新",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 9, 18, 9)
        };
        panel.Children.Add(checkUpdateButton);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "关于",
            Content = panel,
            CloseButtonText = "关闭"
        };
        checkUpdateButton.Click += async (_, _) =>
        {
            dialog.Hide();
            await CheckForUpdatesAsync(silent: false);
        };
        await dialog.ShowAsync();
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        if (!silent) SetStatus("正在检查更新…");

        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(
                AppVersion,
                force: !silent);
            if (!result.Checked) return;

            if (result.IsUpdateAvailable && result.Manifest is not null)
            {
                await ShowUpdateDialogAsync(result.Manifest, result.ManifestUrl);
            }
            else if (!silent)
            {
                await ShowErrorAsync("检查更新", result.Message ?? "当前已是最新版本。");
            }
        }
        catch (Exception exception)
        {
            if (!silent)
                await ShowErrorAsync("检查更新失败", exception.Message);
        }
        finally
        {
            _checkingForUpdates = false;
            if (!silent) SetStatus("就绪");
        }
    }

    private async Task ShowUpdateDialogAsync(
        UpdateChannelManifest manifest,
        string? manifestUrl)
    {
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Visibility = Visibility.Collapsed
        };
        var progressText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
        };
        var panel = new StackPanel { Spacing = 12, Width = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = $"v{AppVersion}  →  v{manifest.Version}",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            panel.Children.Add(new TextBlock
            {
                Text = manifest.ReleaseNotes.Trim(),
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri? sourceUri))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"版本信息来源：{sourceUri.Host}",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }
        panel.Children.Add(progressBar);
        panel.Children.Add(progressText);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "发现新版本",
            Content = panel,
            PrimaryButtonText = "立即更新",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary
        };

        bool installing = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (installing)
            {
                args.Cancel = true;
                return;
            }

            args.Cancel = true;
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            installing = true;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = null;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            progressText.Text = "正在连接更新源…";

            try
            {
                var progress = new Progress<UpdateDownloadProgress>(value =>
                {
                    if (value.Percentage is double percentage)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = percentage;
                    }
                    string source = string.IsNullOrWhiteSpace(value.SourceLabel)
                        ? "更新源"
                        : value.SourceLabel;
                    progressText.Text = value.TotalBytes is long total
                        ? $"正在从 {source} 下载 {FormatByteSize(value.BytesReceived)} / {FormatByteSize(total)}"
                        : $"正在从 {source} 下载 {FormatByteSize(value.BytesReceived)}";
                });

                UpdateDownloadResult download = await _updateService.DownloadAsync(
                    manifest,
                    progress);
                progressBar.IsIndeterminate = true;
                progressText.Text = "下载完成，正在启动安装程序…";

                string target = UpdateInstaller.ResolveUpdateTargetPath(
                    Environment.GetCommandLineArgs());
                UpdateInstaller.StartApplyUpdate(download.FilePath, target);
                dialog.Hide();
                Application.Current.Exit();
            }
            catch (Exception exception)
            {
                installing = false;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "关闭";
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = $"更新失败：{exception.Message}";
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, bytes);
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.##} {units[unitIndex]}";
    }

    private async void ExportSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        AssetEntry[] assets = GetSelectedAssets();
        if (assets.Length == 0 || _isBusy) return;
        StorageFolder? folder = await PickOutputFolderAsync();
        if (folder is null) return;
        SetBusy(true, $"正在导出 0 / {assets.Length:N0}…");
        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < assets.Length; i++)
                {
                    _workspace.ExtractTo(assets[i], folder.Path);
                    if (i % 20 == 0 || i == assets.Length - 1)
                    {
                        int completed = i + 1;
                        DispatcherQueue.TryEnqueue(() => SetStatus($"正在导出 {completed:N0} / {assets.Length:N0}…"));
                    }
                }
            });
            SetStatus($"已导出所选 {assets.Length:N0} 个资源");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("导出失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        AssetEntry[] selected = GetSelectedAssets();
        if (selected.Length != 1 || _isBusy) return;
        AssetEntry asset = selected[0];
        SetBusy(true, "正在读取文件属性…");
        try
        {
            byte[] data = await Task.Run(() => _workspace.Extract(asset));
            var archiveInfo = new FileInfo(asset.ArchivePath);
            string sha256 = Convert.ToHexString(SHA256.HashData(data));
            string kind = asset.Kind switch
            {
                AssetKind.Image => "图像",
                AssetKind.Sound => "声音",
                AssetKind.Model => "模型",
                AssetKind.Font => "字体",
                AssetKind.MbTable => "MB表",
                _ => "其他"
            };
            string details =
                $"文件名：{asset.Name}\n" +
                $"资源类型：{kind}\n" +
                $"文件格式：{asset.Extension.TrimStart('.').ToUpperInvariant()}\n" +
                $"解包大小：{FormatBytes(data.Length)}（{data.Length:N0} 字节）\n" +
                $"所属 DPK：{asset.ArchiveName}\n" +
                $"包内路径：{asset.Entry.Path}\n" +
                $"索引根块：{asset.Entry.RootBlock:N0}（0x{asset.Entry.RootBlock:X8}）\n" +
                $"SHA-256：{sha256}\n\n" +
                $"DPK 文件：{asset.ArchivePath}\n" +
                $"DPK 大小：{FormatBytes(archiveInfo.Length)}（{archiveInfo.Length:N0} 字节）\n" +
                $"DPK 修改时间：{archiveInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}";

            SetBusy(false);
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "文件属性",
                MinWidth = 760,
                Content = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Width = 700,
                    Height = 390,
                    Padding = new Thickness(16),
                    Content = new TextBlock
                    {
                        Text = details,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    }
                },
                CloseButtonText = "关闭"
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("读取属性失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ModelPreview_AnimationExportRequested(
        object? sender,
        AnimationExportRequestedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        string sourceName = _selectedComposite?.Name
            ?? System.IO.Path.GetFileNameWithoutExtension(e.Animation.SourceAsset.Name);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SanitizeFileName($"{sourceName}_{e.Animation.Name}")
        };
        picker.FileTypeChoices.Add("Biovision BVH 骨骼动画", new List<string> { ".bvh" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        SetBusy(true, $"正在导出骨骼动画：{e.Animation.Name}…");
        try
        {
            await Task.Run(() =>
                BvhExporter.Export(
                    file.Path,
                    e.AnimationSet.Skeleton,
                    e.Animation));
            SetStatus($"BVH 骨骼动画已导出：{file.Path}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("BVH 导出失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ExportModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        AssetEntry? selectedAsset = _selectedAsset?.Kind == AssetKind.Model ? _selectedAsset : null;
        CompositeModelEntry? selectedComposite = _selectedComposite;
        if (selectedAsset is null && selectedComposite is null) return;

        string suggestedName = selectedComposite is not null
            ? SanitizeFileName(selectedComposite.Name)
            : System.IO.Path.GetFileNameWithoutExtension(selectedAsset!.Name);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add("Wavefront OBJ 模型", new List<string> { ".obj" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        ModelTextureBinding? selectedTextureBinding = ModelTextureComboBox.SelectedItem as ModelTextureBinding;

        SetBusy(true, selectedComposite is null ? "正在转换并导出 OBJ 部件…" : "正在合并并导出完整 OBJ 模型…");
        try
        {
            string outputPath = file.Path;
            await Task.Run(() =>
            {
                if (selectedComposite is not null)
                {
                    IReadOnlyList<ObjExporter.ObjPart> parts = BuildCompositeObjParts(selectedComposite, outputPath);
                    ObjExporter.Export(parts, outputPath, selectedComposite.Name);
                }
                else if (selectedAsset is not null)
                {
                    PmfMesh mesh = PmfParser.Parse(_workspace.Extract(selectedAsset));
                    string? textureFileName = selectedTextureBinding is null
                        ? null
                        : CopyObjTexture(selectedTextureBinding.TextureAsset, outputPath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    string? materialName = selectedTextureBinding?.MaterialName;
                    if (string.IsNullOrWhiteSpace(materialName)) materialName = System.IO.Path.GetFileNameWithoutExtension(selectedAsset.Name);
                    ObjExporter.Export(
                        new[] { new ObjExporter.ObjPart(System.IO.Path.GetFileNameWithoutExtension(selectedAsset.Name), mesh, materialName, textureFileName) },
                        outputPath,
                        System.IO.Path.GetFileNameWithoutExtension(selectedAsset.Name));
                }
            });
            SetStatus(selectedComposite is null
                ? $"OBJ 部件已导出：{file.Path}"
                : $"完整 OBJ 模型已导出：{file.Path}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("OBJ 导出失败", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IReadOnlyList<ObjExporter.ObjPart> BuildCompositeObjParts(CompositeModelEntry composite, string outputPath)
    {
        var parts = new List<ObjExporter.ObjPart>();
        var copiedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedTextureFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < composite.Parts.Count; i++)
        {
            CompositeModelPart part = composite.Parts[i];
            PmfMesh mesh = PmfParser.Parse(_workspace.Extract(part.MeshAsset));
            string? textureFileName = part.TextureBinding is null
                ? null
                : CopyObjTexture(part.TextureBinding.TextureAsset, outputPath, copiedTextures, usedTextureFileNames);
            string materialName = string.IsNullOrWhiteSpace(part.MaterialName)
                ? System.IO.Path.GetFileNameWithoutExtension(part.MeshAsset.Name)
                : part.MaterialName;
            string objectName = $"{i + 1:000}_{System.IO.Path.GetFileNameWithoutExtension(part.MeshAsset.Name)}";
            parts.Add(new ObjExporter.ObjPart(objectName, mesh, materialName, textureFileName));
        }

        if (parts.Count == 0) throw new InvalidDataException("完整组合没有可导出的 PMF 部件。");
        return parts;
    }

    private string CopyObjTexture(
        AssetEntry textureAsset,
        string outputPath,
        Dictionary<string, string> copiedTextures,
        HashSet<string> usedFileNames)
    {
        string key = textureAsset.DisplayPath;
        if (copiedTextures.TryGetValue(key, out string? existing)) return existing;

        string? outputDirectory = System.IO.Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        string fileName = MakeUniqueExportFileName(textureAsset.Name, usedFileNames);
        File.WriteAllBytes(System.IO.Path.Combine(outputDirectory, fileName), _workspace.Extract(textureAsset));
        copiedTextures[key] = fileName;
        return fileName;
    }

    private void ExpandModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentKind != AssetKind.Model) return;
        _modelExpanded = !_modelExpanded;
        AssetBrowserPanel.Visibility = _modelExpanded ? Visibility.Collapsed : Visibility.Visible;
        AssetListColumn.MinWidth = _modelExpanded ? 0 : 600;
        AssetListColumn.Width = _modelExpanded ? new GridLength(0) : new GridLength(650);
        ExpandModelButtonText.Text = _modelExpanded ? "恢复模型列表" : "放大模型预览";
    }

    private async void BatchExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_filteredAssets.Count == 0 || _isBusy) return;
        var confirmation = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "导出当前查询结果",
            Content = $"将按 DPK 包名和原始目录结构导出 {_filteredAssets.Count:N0} 个资源。",
            PrimaryButtonText = "开始导出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        StorageFolder? folder = await PickOutputFolderAsync();
        if (folder is null) return;

        AssetEntry[] assets = _filteredAssets.ToArray();
        SetBusy(true, $"正在导出 0 / {assets.Length:N0}…");
        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < assets.Length; i++)
                {
                    _workspace.ExtractTo(assets[i], folder.Path);
                    if (i % 20 == 0 || i == assets.Length - 1)
                    {
                        int completed = i + 1;
                        DispatcherQueue.TryEnqueue(() => SetStatus($"正在导出 {completed:N0} / {assets.Length:N0}…"));
                    }
                }
            });
            SetStatus($"导出完成：{assets.Length:N0} 个资源");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("批量导出中断", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<StorageFolder?> PickOutputFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFolderAsync();
    }

    private void CategoryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        NavigationViewItemBase? selectedItem = args.SelectedItem as NavigationViewItemBase ?? args.SelectedItemContainer;
        if (selectedItem?.Tag is not string tag) return;
        _currentKind = tag switch
        {
            "sound" => AssetKind.Sound,
            "model" => AssetKind.Model,
            "font" => AssetKind.Font,
            "mb" => AssetKind.MbTable,
            "global" => AssetKind.GlobalSearch,
            "dungeon" => AssetKind.DungeonSummary,
            "other" => AssetKind.Other,
            _ => AssetKind.Image
        };
        SearchBox.Text = string.Empty;
        ConfigureCategoryUi();
        if (_currentKind == AssetKind.GlobalSearch)
        {
            RefreshGlobalSearch();
            return;
        }
        if (_currentKind == AssetKind.DungeonSummary)
        {
            RefreshDungeonSummary();
            return;
        }
        BuildFolderTree();
        ApplyFilter();
    }

    private void ConfigureCategoryUi()
    {
        bool images = _currentKind == AssetKind.Image;
        bool sounds = _currentKind == AssetKind.Sound;
        bool models = _currentKind == AssetKind.Model;
        bool fonts = _currentKind == AssetKind.Font;
        bool mbTables = _currentKind == AssetKind.MbTable;
        bool globalSearch = _currentKind == AssetKind.GlobalSearch;
        bool dungeonSummary = _currentKind == AssetKind.DungeonSummary;
        bool others = _currentKind == AssetKind.Other;
        PageTitleText.Text = _currentKind switch
        {
            AssetKind.Image => "图标与贴图",
            AssetKind.Sound => "声音",
            AssetKind.Model => "模型",
            AssetKind.Font => "字体",
            AssetKind.MbTable => "MB 表",
            AssetKind.GlobalSearch => "全局资料",
            AssetKind.DungeonSummary => "副本怪物",
            _ => "配置与其他"
        };
        PageDescriptionText.Text = _currentKind switch
        {
            AssetKind.Image => "浏览 GUI 图标、装备图和 DDS 场景贴图",
            AssetKind.Sound => "直接试听 OGG 音乐、环境音与 WAV 音效",
            AssetKind.Model => "大尺寸实体预览 PMF 模型，并可转换导出为 OBJ",
            AssetKind.Font => "浏览 font.dpk 内的 TTF、OTF 和 TTC 字体资源",
            AssetKind.MbTable => "浏览 mb.dpk 内的玩法、物品、任务、技能等数据表",
            AssetKind.GlobalSearch => "输入名字、ID、路径或图标号，自动整理物品、套装、怪物、图标、模型和配置引用",
            AssetKind.DungeonSummary => "按副本汇总怪物头像、核心战斗属性、隐藏状态和掉落组",
            _ => "浏览特效、场景、地形、天空、影片及各类配置文件"
        };
        SearchBox.PlaceholderText = _currentKind switch
        {
            AssetKind.Image => "搜索图标名称或路径",
            AssetKind.Sound => "搜索音效、音乐或路径",
            AssetKind.Model => "搜索模型名称或路径",
            AssetKind.Font => "搜索字体名称或路径",
            AssetKind.MbTable => "搜索 MB 表名称或路径",
            AssetKind.GlobalSearch => "搜索名称、ID、图标号、路径或表内容",
            AssetKind.DungeonSummary => "搜索副本或怪物名称",
            _ => "搜索配置、特效、场景或路径"
        };
        ImageGrid.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
        AssetList.Visibility = images || mbTables ? Visibility.Collapsed : Visibility.Visible;
        MbAssetList.Visibility = mbTables ? Visibility.Visible : Visibility.Collapsed;
        ImagePreviewPanel.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
        SoundPreviewPanel.Visibility = sounds ? Visibility.Visible : Visibility.Collapsed;
        ModelPreviewHost.Visibility = models ? Visibility.Visible : Visibility.Collapsed;
        MbTableDataPanel.Visibility = mbTables ? Visibility.Visible : Visibility.Collapsed;
        GenericPreviewPanel.Visibility = fonts || others ? Visibility.Visible : Visibility.Collapsed;
        GlobalSearchPanel.Visibility = globalSearch ? Visibility.Visible : Visibility.Collapsed;
        DungeonSummaryPanel.Visibility = dungeonSummary ? Visibility.Visible : Visibility.Collapsed;
        BeginnerModeToggle.Visibility = Visibility.Collapsed;
        BatchExportButton.IsEnabled = _workspace.Assets.Count > 0 && !globalSearch;
        FolderTreeTitleText.Text = mbTables ? "MB 表目录" : "DPK 目录";
        FolderTreeHintText.Text = mbTables ? "按 mb.dpk 表目录浏览" : "按包内真实路径浏览";
        ModelTextureSelector.Visibility = Visibility.Collapsed;
        ModelTextureComboBox.ItemsSource = null;
        ExportModelButton.Visibility = models ? Visibility.Visible : Visibility.Collapsed;
        ExpandModelButton.Visibility = models ? Visibility.Visible : Visibility.Collapsed;
        SetMultiSelectMode(false);
        _modelExpanded = false;
        AssetBrowserPanel.Visibility = dungeonSummary || globalSearch ? Visibility.Collapsed : Visibility.Visible;
        PreviewPanel.Visibility = dungeonSummary || globalSearch ? Visibility.Collapsed : Visibility.Visible;
        FolderTreeColumn.Width = mbTables ? new GridLength(180) : new GridLength(230);
        AssetListColumn.MinWidth = globalSearch ? 700 : mbTables ? 360 : models ? 600 : 650;
        AssetListColumn.Width = globalSearch ? new GridLength(1, GridUnitType.Star) : mbTables ? new GridLength(500) : models ? new GridLength(650) : new GridLength(1.35, GridUnitType.Star);
        PreviewColumn.MinWidth = mbTables ? 500 : 360;
        PreviewColumn.Width = models ? new GridLength(1, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        SelectedFooterPanel.Visibility = mbTables || dungeonSummary || globalSearch ? Visibility.Collapsed : Visibility.Visible;
        ExpandModelButtonText.Text = "放大模型预览";
        if (!sounds) _mediaPlayer.Pause();
        if (_currentKind != AssetKind.Model) _modelPreview.SetMesh(null);
        PreviewImage.Source = null;
        ImageGrid.SelectedItem = null;
        AssetList.SelectedItem = null;
        MbAssetList.SelectedItem = null;
        _selectedAsset = null;
        _selectedComposite = null;
        _currentMbTableView = null;
        ExportSelectedButton.IsEnabled = false;
        MbExportButton.IsEnabled = false;
        ExportSelectedButtonText.Text = "导出原始资源";
        PropertiesButton.IsEnabled = false;
        MbPropertiesButton.IsEnabled = false;
        ExportModelButton.IsEnabled = false;
        SelectedNameText.Text = "尚未选择资源";
        SelectedPathText.Text = _currentKind switch
        {
            AssetKind.Image => "从左侧选择一张图像",
            AssetKind.Sound => "从左侧选择一个声音",
            AssetKind.Model => "从左侧选择一个 PMF 模型",
            AssetKind.Font => "从左侧选择一个字体文件",
            AssetKind.MbTable => "从左侧选择一个 MB 表文件",
            AssetKind.GlobalSearch => "输入关键词后查看反查结果",
            AssetKind.DungeonSummary => "从左侧选择一个副本",
            _ => "从左侧选择一个资源文件"
        };
        SelectedMetadataText.Text = string.Empty;
        MbTablePreviewPanel.Visibility = Visibility.Collapsed;
        MbTableSummaryText.Text = string.Empty;
        MbTablePreviewBox.Text = string.Empty;
        ClearMbTableView();
        if (!globalSearch) ClearGlobalSearchView();
        SetGenericPreviewChromeVisibility(_currentKind != AssetKind.MbTable);
        GenericTextExpander.Visibility = Visibility.Collapsed;
        GenericTextPreviewBox.Text = string.Empty;
    }

    private void BeginnerModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (FolderTree is null || SearchBox is null || _workspace.Assets.Count == 0) return;
        BuildFolderTree();
        ApplyFilter();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedIndex < 0) return;
        _sortMode = SortComboBox.SelectedIndex;
        if (ImageGrid is not null && AssetList is not null) ApplyFilter();
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_multiSelectMode) return;
        GetActiveAssetList().SelectAll();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        GetActiveAssetList().SelectedItem = null;
    }

    private void ToggleMultiSelectButton_Click(object sender, RoutedEventArgs e) =>
        SetMultiSelectMode(!_multiSelectMode);

    private void SetMultiSelectMode(bool enabled)
    {
        _multiSelectMode = enabled;
        if (ImageGrid.ItemsSource is null || AssetList.ItemsSource is null || MbAssetList.ItemsSource is null) return;
        if (!enabled)
        {
            ImageGrid.SelectedItem = null;
            AssetList.SelectedItem = null;
            MbAssetList.SelectedItem = null;
        }

        ListViewSelectionMode selectionMode = enabled ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
        ImageGrid.SelectionMode = selectionMode;
        AssetList.SelectionMode = selectionMode;
        MbAssetList.SelectionMode = selectionMode;
        ImageGrid.IsMultiSelectCheckBoxEnabled = enabled;
        AssetList.IsMultiSelectCheckBoxEnabled = enabled;
        MbAssetList.IsMultiSelectCheckBoxEnabled = enabled;
        MultiSelectButton.Content = enabled ? "完成" : "多选";
        SelectAllButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ClearSelectionButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionUi(GetActiveAssetList().SelectedItems.Count);
        if (!enabled) ResetPreviewSelection();
    }

    private ListViewBase GetActiveAssetList() => _currentKind switch
    {
        AssetKind.Image => ImageGrid,
        AssetKind.MbTable => MbAssetList,
        _ => AssetList
    };

    private AssetItemViewModel[] GetSelectedItems() => GetActiveAssetList().SelectedItems
        .OfType<AssetItemViewModel>()
        .Distinct()
        .ToArray();

    private AssetEntry[] GetSelectedAssets() => GetSelectedItems()
        .OfType<AssetItemViewModel>()
        .Select(item => item.Asset)
        .OfType<AssetEntry>()
        .Distinct()
        .ToArray();

    private void UpdateSelectionUi(int selectedCount)
    {
        AssetItemViewModel[] selectedItems = GetSelectedItems();
        AssetEntry[] selected = GetSelectedAssets();
        ExportSelectedButton.IsEnabled = selected.Length > 0;
        MbExportButton.IsEnabled = selected.Length > 0;
        ExportSelectedButtonText.Text = _multiSelectMode
            ? $"导出所选 ({selected.Length:N0})"
            : "导出原始资源";
        PropertiesButton.IsEnabled = selected.Length == 1;
        MbPropertiesButton.IsEnabled = selected.Length == 1;
        bool canExportSingleModel = selectedItems.Length == 1 &&
                                    (selectedItems[0].Composite is not null ||
                                     selectedItems[0].Asset?.Kind == AssetKind.Model);
        ExportModelButton.IsEnabled = canExportSingleModel;
        ExportModelButtonText.Text = selectedItems.Length == 1 && selectedItems[0].Composite is not null
            ? "导出完整 OBJ 模型"
            : selectedItems.Length == 1 && selectedItems[0].Asset?.Kind == AssetKind.Model
                ? "导出 OBJ 部件"
                : "导出 OBJ 模型";
    }

    private void ResetPreviewSelection()
    {
        PreviewLoadingOverlay.Visibility = Visibility.Collapsed;
        ExportModelButton.IsEnabled = false;
        ExportModelButtonText.Text = "导出 OBJ 模型";
        PropertiesButton.IsEnabled = false;
        MbPropertiesButton.IsEnabled = false;
        MbExportButton.IsEnabled = false;
        SelectedNameText.Text = "尚未选择资源";
        SelectedPathText.Text = _currentKind == AssetKind.Image
            ? "可多选图像后批量导出"
            : _currentKind == AssetKind.Sound
                ? "可多选声音后批量导出"
                : _currentKind == AssetKind.Model
                    ? "可多选模型后批量导出"
                    : "可多选资源后批量导出";
        SelectedMetadataText.Text = string.Empty;
        PreviewImage.Source = null;
        MbTablePreviewPanel.Visibility = Visibility.Collapsed;
        MbTableSummaryText.Text = string.Empty;
        MbTablePreviewBox.Text = string.Empty;
        ClearMbTableView();
        SetGenericPreviewChromeVisibility(_currentKind != AssetKind.MbTable);
        GenericTextExpander.Visibility = Visibility.Collapsed;
        GenericTextPreviewBox.Text = string.Empty;
        _selectedComposite = null;
        _currentMbTableView = null;
        if (_currentKind == AssetKind.Model) _modelPreview.SetMesh(null);
    }

    private void ClearMbTableView()
    {
        _currentMbTableView = null;
        MbTableNameText.Text = "选择一个 MB 表";
        MbTablePathText.Text = string.Empty;
        MbDataSummaryText.Text = string.Empty;
        MbRecordCountText.Text = string.Empty;
        MbRecordList.ItemsSource = null;
        MbRecordList.SelectedItem = null;
        MbRecordEmptyPanel.Visibility = Visibility.Collapsed;
        MbRecordTitleText.Text = "选择左侧记录查看字段";
        MbRecordSubtitleText.Text = string.Empty;
        MbFieldList.ItemsSource = null;
        MbRecordExtraPanel.Visibility = Visibility.Collapsed;
        MbRecordExtraText.Text = string.Empty;
    }

    private void ShowMbTableError(AssetEntry asset, string message)
    {
        _currentMbTableView = null;
        MbTableNameText.Text = CreateMbTableListName(asset);
        MbTablePathText.Text = asset.DisplayPath;
        MbDataSummaryText.Text = message;
        MbRecordCountText.Text = string.Empty;
        MbRecordList.ItemsSource = null;
        MbRecordEmptyPanel.Visibility = Visibility.Visible;
        MbRecordTitleText.Text = "无法解析表格";
        MbRecordSubtitleText.Text = message;
        MbFieldList.ItemsSource = null;
        MbRecordExtraPanel.Visibility = Visibility.Collapsed;
        MbRecordExtraText.Text = string.Empty;
    }

    private void ShowMbTableView(MbTableViewModel tableView)
    {
        _currentMbTableView = tableView;
        MbTableNameText.Text = tableView.TableName;
        MbTablePathText.Text = tableView.Path;
        MbDataSummaryText.Text = tableView.Summary;
        MbRecordCountText.Text = $"{tableView.Records.Count:N0} 条";
        MbRecordList.ItemsSource = tableView.Records;
        MbRecordEmptyPanel.Visibility = tableView.Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tableView.Records.Count > 0)
        {
            MbRecordList.SelectedIndex = 0;
            ShowMbRecordDetails(tableView.Records[0]);
        }
        else
        {
            MbRecordList.SelectedIndex = -1;
            MbRecordTitleText.Text = "没有记录";
            MbRecordSubtitleText.Text = "这个表解析成功，但没有可显示的数据行。";
            MbFieldList.ItemsSource = null;
            MbRecordExtraPanel.Visibility = Visibility.Collapsed;
            MbRecordExtraText.Text = string.Empty;
        }
    }

    private void MbRecordList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<MbRecordViewModel>().LastOrDefault() is { } record)
            ShowMbRecordDetails(record);
    }

    private void ShowMbRecordDetails(MbRecordViewModel record)
    {
        if (_currentMbTableView is not { } tableView) return;

        MbRecordTitleText.Text = record.Title;
        MbRecordSubtitleText.Text = $"{tableView.TableName} · 原始第 {record.SourceRow:N0} 行";
        MbFieldList.ItemsSource = BuildMbFieldViewModels(tableView, record.Row);

        string extraDetails = BuildMbRecordExtraDetails(tableView.Asset, record.Row);
        MbRecordExtraPanel.Visibility = string.IsNullOrWhiteSpace(extraDetails) ? Visibility.Collapsed : Visibility.Visible;
        MbRecordExtraText.Text = extraDetails.Trim();
    }

    private IReadOnlyList<MbFieldViewModel> BuildMbFieldViewModels(MbTableViewModel tableView, IReadOnlyList<string> row)
    {
        var fields = new List<MbFieldViewModel>();
        foreach (int column in tableView.ActiveColumns)
        {
            string value = column < row.Count ? row[column].Trim() : string.Empty;
            string displayValue = string.IsNullOrWhiteSpace(value) ? "空" : value;
            string name = GetColumnName(tableView.Headers, column);
            string note = GetMbFieldNote(tableView.Asset, name, column, value);
            fields.Add(new MbFieldViewModel(name, displayValue, note, column));
        }

        return fields;
    }

    private bool IsPreviewCurrent(int generation) => _previewGeneration == generation;

    private void ShowPreviewLoading(int generation, string text)
    {
        if (!IsPreviewCurrent(generation)) return;
        PreviewLoadingText.Text = text;
        PreviewLoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HidePreviewLoading(int generation)
    {
        if (IsPreviewCurrent(generation))
            PreviewLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetGenericPreviewChromeVisibility(bool visible)
    {
        Visibility visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        GenericPreviewIconBorder.Visibility = visibility;
        GenericPreviewNameText.Visibility = visibility;
        GenericPreviewRawNameText.Visibility = visibility;
        GenericExplanationCard.Visibility = visibility;
        GenericPreviewTechnicalText.Visibility = visibility;
        GenericPreviewHintText.Visibility = visibility;
    }

    private static string? TryCreateTextPreview(AssetEntry asset, byte[] data)
    {
        if (!TryDecodeTextPreview(asset, data, out string text)) return null;
        if (asset.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
            asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase) ||
            asset.Extension.Equals(".cmf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                text = XDocument.Parse(text).ToString();
            }
            catch
            {
                // Keep the decoded original when an old client file is only XML-like.
            }
        }

        const int maximumCharacters = 200_000;
        return text.Length <= maximumCharacters
            ? text
            : text[..maximumCharacters] + "\n\n……内容过长，预览到此为止；导出原始文件可查看完整内容。";
    }

    private bool TryBuildMbTableView(
        AssetEntry asset,
        byte[] data,
        IReadOnlyList<string> focusTerms,
        out MbTableViewModel? tableView,
        out string message)
    {
        tableView = null;
        message = string.Empty;
        if (asset.Kind != AssetKind.MbTable)
        {
            message = "这不是 MB 表资源。";
            return false;
        }

        if (!TryDecodeTextPreview(asset, data, out string text))
        {
            message = "这个 MB 文件暂时无法识别为可读文本表。";
            return false;
        }

        if (text.StartsWith("文件共有 ", StringComparison.Ordinal))
        {
            message = text;
            return false;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            message = "这个 MB 表没有可显示的文本行。";
            return false;
        }

        char delimiter = ChooseMbTableDelimiter(lines);
        string[][] allRows = lines
            .Select(line => SplitMbTableLine(line, delimiter))
            .Where(row => row.Length > 0)
            .ToArray();
        int maxColumns = allRows.Length == 0 ? 0 : allRows.Max(row => row.Length);
        if (maxColumns <= 1)
        {
            string[] textHeaders = { "内容" };
            int[] textColumns = { 0 };
            var indexedTextRows = lines
                .Select((line, index) => (Row: new[] { line }, SourceRow: index + 1))
                .ToArray();
            var selectedTextRows = SelectFocusedMbRows(indexedTextRows, focusTerms, out int textMatches);
            var textRecords = selectedTextRows
                .Select(row => new MbRecordViewModel(
                    row.SourceRow,
                    TrimPreviewCell(row.Row[0]),
                    row.Row[0],
                    row.Row))
                .ToList();
            string textFocus = BuildMbTableFocusText(focusTerms, textMatches, textRecords.Count, lines.Length);
            tableView = new MbTableViewModel(
                asset,
                CreateMbTableListName(asset),
                asset.DisplayPath,
                $"纯文本表 · 共 {lines.Length:N0} 行。{textFocus}",
                textHeaders,
                textColumns,
                textRecords);
            return true;
        }

        bool hasHeader = TryGetMbHeaders(asset, allRows, maxColumns, out string[] headers, out int firstDataRow);
        string[][] dataRows = allRows.Skip(firstDataRow).ToArray();
        int[] activeColumns = Enumerable.Range(0, maxColumns)
            .Where(column =>
                !string.IsNullOrWhiteSpace(GetColumnName(headers, column)) ||
                dataRows.Any(row => column < row.Length && !string.IsNullOrWhiteSpace(row[column])))
            .ToArray();
        if (activeColumns.Length == 0)
            activeColumns = Enumerable.Range(0, maxColumns).ToArray();

        var indexedRows = dataRows
            .Select((row, index) => (Row: row, SourceRow: firstDataRow + index + 1))
            .ToArray();
        var selectedRows = SelectFocusedMbRows(indexedRows, focusTerms, out int matchedRows);
        List<MbRecordViewModel> records = selectedRows
            .Select(row => new MbRecordViewModel(
                row.SourceRow,
                BuildMbRecordTitle(headers, row.Row),
                BuildMbRowPreviewText(headers, activeColumns, row.Row),
                row.Row))
            .ToList();

        bool hasNamedHeaders = headers.Any(header => !string.IsNullOrWhiteSpace(header));
        string fieldSource = hasHeader
            ? "字段来自表头"
            : hasNamedHeaders
                ? "字段来自已知模板/转译"
                : "字段按列自动识别";
        string focusText = BuildMbTableFocusText(focusTerms, matchedRows, records.Count, dataRows.Length);
        string delimiterName = delimiter == '\t' ? "制表符" : delimiter == ',' ? "逗号" : "空白";
        message = $"{fieldSource} · {delimiterName}分隔 · 共 {dataRows.Length:N0} 条记录、{activeColumns.Length:N0} 个有效字段。{focusText}";
        tableView = new MbTableViewModel(
            asset,
            CreateMbTableListName(asset),
            asset.DisplayPath,
            message,
            headers,
            activeColumns,
            records);
        return true;
    }

    private static (string[] Row, int SourceRow)[] SelectFocusedMbRows(
        IReadOnlyList<(string[] Row, int SourceRow)> rows,
        IReadOnlyList<string> focusTerms,
        out int matchedRows)
    {
        matchedRows = 0;
        string[] usableTerms = focusTerms
            .Where(term => term.Length >= 2)
            .SelectMany(GetSearchTermVariants)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (usableTerms.Length == 0) return rows.ToArray();

        (string[] Row, int SourceRow)[] matches = rows
            .Where(row => usableTerms.All(term =>
                row.Row.Any(cell => cell.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        matchedRows = matches.Length;
        return matches.Length > 0 ? matches : rows.ToArray();
    }

    private static string BuildMbTableFocusText(
        IReadOnlyList<string> focusTerms,
        int matchedRows,
        int visibleRows,
        int totalRows)
    {
        bool hasSearch = focusTerms.Any(term => term.Length >= 2);
        if (!hasSearch)
            return $"已把全部 {visibleRows:N0} 条记录放入下面的可滚动列表。";

        return matchedRows > 0
            ? $"当前搜索命中 {matchedRows:N0} 条，已全部显示；清空搜索可查看全表 {totalRows:N0} 条。"
            : $"当前搜索没有命中具体记录，下面暂时显示全表 {visibleRows:N0} 条。";
    }

    private static string BuildMbRowPreviewText(
        IReadOnlyList<string> headers,
        IReadOnlyList<int> activeColumns,
        IReadOnlyList<string> row)
    {
        string[] parts = activeColumns
            .Where(column => column < row.Count && !string.IsNullOrWhiteSpace(row[column]))
            .Take(6)
            .Select(column => $"{GetColumnName(headers, column)}: {TrimPreviewCell(row[column])}")
            .ToArray();
        return parts.Length == 0 ? "这一行没有明显字段值" : string.Join(" · ", parts);
    }

    private static string GetMbFieldNote(AssetEntry asset, string fieldName, int column, string value)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        if (normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal))
        {
            return column switch
            {
                1 => "角色/怪物的唯一编号，其他表或脚本会用它引用这个单位。",
                4 => "关联 object/cha_fight.txt，里面是生命、伤害、防御、抗性、会心等战斗属性。",
                5 => "关联 object/cha_pic.txt，里面是模型配置、部件和外观资源。",
                13 => "出生或常驻状态 ID 列表，可在 skill/state_data.txt 里查状态名称。",
                18 => "技能或效果相关 ID 列表，通常用于战斗行为、特殊机制或状态触发。",
                _ => GetGenericMbFieldNote(fieldName, value)
            };
        }

        if (normalizedPath.StartsWith("object/cha_fight", StringComparison.Ordinal))
        {
            return fieldName.Contains("生命", StringComparison.Ordinal) ||
                   fieldName.Contains("伤害", StringComparison.Ordinal) ||
                   fieldName.Contains("防御", StringComparison.Ordinal) ||
                   fieldName.Contains("抗性", StringComparison.Ordinal) ||
                   fieldName.Contains("会心", StringComparison.Ordinal)
                ? "战斗数值字段，通常会被角色/怪物表的战斗属性 ID 关联使用。"
                : GetGenericMbFieldNote(fieldName, value);
        }

        if (normalizedPath.StartsWith("life/legend_equip/legend_equip_list", StringComparison.Ordinal))
        {
            return column switch
            {
                0 => "装备或物品 ID，用于背包、掉落、奖励等系统引用。",
                1 => "装备显示名称。",
                >= 10 and <= 13 => "固定属性组 ID，可继续关联 legend_equip_atbs.txt 和 legend_equip_atb_value.txt 查看实际属性。",
                _ => GetGenericMbFieldNote(fieldName, value)
            };
        }

        return GetGenericMbFieldNote(fieldName, value);
    }

    private static string GetGenericMbFieldNote(string fieldName, string value)
    {
        if (fieldName.Contains("保留", StringComparison.Ordinal))
            return "保留或暂未转译字段，通常不是优先看的业务属性。";
        if (fieldName.Contains("名称", StringComparison.Ordinal) || fieldName.Contains("名字", StringComparison.Ordinal))
            return "显示名或内部配置名，适合用来搜索定位。";
        if (fieldName.Contains("ID", StringComparison.OrdinalIgnoreCase) || fieldName.Contains("编号", StringComparison.Ordinal))
            return "编号/关联键，常用于连接其他 MB 表或脚本配置。";
        if (fieldName.Contains("状态", StringComparison.Ordinal) || value.Contains('*', StringComparison.Ordinal))
            return "多值字段，通常是一组 ID，需要结合相关表继续解析。";
        if (fieldName.Contains("等级", StringComparison.Ordinal))
            return "等级或阶段字段，可用于判断使用门槛、怪物强度或配置区间。";
        if (fieldName.Contains("生命", StringComparison.Ordinal) ||
            fieldName.Contains("伤害", StringComparison.Ordinal) ||
            fieldName.Contains("攻击", StringComparison.Ordinal) ||
            fieldName.Contains("防御", StringComparison.Ordinal) ||
            fieldName.Contains("抗性", StringComparison.Ordinal) ||
            fieldName.Contains("评分", StringComparison.Ordinal))
            return "数值属性字段，通常直接影响强度、掉落、门槛或系统计算。";
        if (string.IsNullOrWhiteSpace(value))
            return "这一行该字段为空。";
        return "已按字段模板显示；如果它是 ID，可以用全局搜索继续追到关联表。";
    }

    private string? TryCreateMbTablePreview(AssetEntry asset, byte[] data, IReadOnlyList<string> focusTerms, out string summary)
    {
        summary = string.Empty;
        if (asset.Kind != AssetKind.MbTable) return null;
        if (!TryDecodeTextPreview(asset, data, out string text))
        {
            summary = "这个 MB 文件暂时无法识别为可读文本表。";
            return null;
        }

        if (text.StartsWith("文件共有 ", StringComparison.Ordinal))
        {
            summary = text;
            return null;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            summary = "这个 MB 表没有可显示的文本行。";
            return null;
        }

        char delimiter = ChooseMbTableDelimiter(lines);
        string[][] allRows = lines
            .Select(line => SplitMbTableLine(line, delimiter))
            .Where(row => row.Length > 0)
            .ToArray();
        int maxColumns = allRows.Length == 0 ? 0 : allRows.Max(row => row.Length);
        if (maxColumns <= 1)
        {
            summary = $"识别为文本表：共 {lines.Length:N0} 行。下方显示前 {Math.Min(lines.Length, 60):N0} 行。";
            return string.Join(Environment.NewLine, lines.Take(60));
        }

        bool hasHeader = TryGetMbHeaders(asset, allRows, maxColumns, out string[] headers, out int firstDataRow);
        string[][] dataRows = allRows.Skip(firstDataRow).ToArray();
        string[][] previewRows = GetFocusedMbRows(dataRows, focusTerms, out int matchedRows);
        int[] activeColumns = Enumerable.Range(0, maxColumns)
            .Where(column =>
                !string.IsNullOrWhiteSpace(GetColumnName(headers, column)) ||
                dataRows.Take(80).Any(row => column < row.Length && !string.IsNullOrWhiteSpace(row[column])))
            .ToArray();
        if (activeColumns.Length == 0) return null;

        const int maximumPreviewRows = 30;
        const int maximumPreviewColumns = 20;
        string delimiterName = delimiter == '\t' ? "制表符" : delimiter == ',' ? "逗号" : "空白";
        int[] visibleColumns = activeColumns.Take(maximumPreviewColumns).ToArray();
        int visibleRows = Math.Min(previewRows.Length, maximumPreviewRows);
        bool hasNamedHeaders = headers.Any(header => !string.IsNullOrWhiteSpace(header));
        summary = hasHeader
            ? $"识别到中文表头：共 {dataRows.Length:N0} 条记录、{activeColumns.Length:N0} 个有效字段；当前按字段名显示{BuildFocusSummary(matchedRows, visibleRows)}。"
            : hasNamedHeaders
                ? $"使用已知字段模板：共 {dataRows.Length:N0} 条记录、{activeColumns.Length:N0} 个有效字段；当前按字段名显示{BuildFocusSummary(matchedRows, visibleRows)}。"
                : $"解析为{delimiterName}分隔表：共 {dataRows.Length:N0} 行、约 {maxColumns:N0} 列；当前显示{BuildFocusSummary(matchedRows, visibleRows)}、{visibleColumns.Length:N0} 个有效字段。";

        var builder = new StringBuilder();
        builder.AppendLine($"表文件：{asset.Name}");
        builder.AppendLine($"包内路径：{asset.Entry.Path}");
        builder.AppendLine(hasHeader ? "字段来源：MB 表首行中文表头" : "字段来源：已知模板/数值形态推测");
        builder.AppendLine();

        builder.AppendLine("字段注释：");
        foreach (int column in visibleColumns)
        {
            builder.AppendLine($"{column + 1}. {GetColumnName(headers, column)}");
        }

        if (activeColumns.Length > visibleColumns.Length)
            builder.AppendLine($"还有 {activeColumns.Length - visibleColumns.Length:N0} 个有效字段未在当前预览中展开，可查看原始文本。");

        builder.AppendLine();
        builder.AppendLine("表格内容：");
        for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
        {
            string[] row = previewRows[rowIndex];
            builder.AppendLine($"[{rowIndex + 1}] {BuildMbRecordTitle(headers, row)}");
            foreach (int column in visibleColumns)
            {
                if (column >= row.Length) continue;
                string value = row[column].Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                builder.AppendLine($"  {GetColumnName(headers, column)}: {TrimPreviewCell(value)}");
            }

            string extraDetails = BuildMbRecordExtraDetails(asset, row);
            if (!string.IsNullOrWhiteSpace(extraDetails))
                builder.Append(extraDetails);
            builder.AppendLine();
        }

        int hiddenRows = previewRows.Length - visibleRows;
        if (hiddenRows > 0)
            builder.AppendLine($"……还有 {hiddenRows:N0} 条当前范围内的记录未显示，可展开原始文本或导出查看。");

        return builder.ToString();
    }

    private string BuildMbRecordExtraDetails(AssetEntry asset, IReadOnlyList<string> row)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        if (normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal))
            return BuildCharacterRecordExtraDetails(row);

        if (!normalizedPath.StartsWith("life/legend_equip/legend_equip_list", StringComparison.Ordinal))
            return string.Empty;

        Dictionary<string, string[]> atbs = GetLegendEquipAtbs();
        Dictionary<string, string[]> values = GetLegendEquipAtbValues();
        if (atbs.Count == 0 || values.Count == 0) return string.Empty;

        int[] fixedAttributeColumns = { 9, 10, 11, 12, 13 };
        var lines = new List<string>();
        foreach (int column in fixedAttributeColumns)
        {
            if (column >= row.Count) continue;
            string groupId = row[column].Trim();
            if (string.IsNullOrWhiteSpace(groupId) || !atbs.TryGetValue(groupId, out string[]? groupRow)) continue;

            string valueId = groupRow.Length > 3 ? groupRow[3].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(valueId) || !values.TryGetValue(valueId, out string[]? valueRow)) continue;

            string groupName = groupRow.Length > 0 ? groupRow[0].Trim() : $"属性组{groupId}";
            string attributeName = valueRow.Length > 0 ? valueRow[0].Trim() : $"属性{valueId}";
            string percentText = valueRow.Length > 3 && valueRow[3].Trim() == "1" ? "，百分比属性" : string.Empty;
            string valueText = FormatLegendAttributeValues(valueRow);
            lines.Add($"    - 属性组 {groupId}（{groupName}）: {attributeName}{percentText}{valueText}");
        }

        if (lines.Count == 0) return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("  固定属性解析:");
        foreach (string line in lines)
            builder.AppendLine(line);
        return builder.ToString();
    }

    private string BuildCharacterRecordExtraDetails(IReadOnlyList<string> row)
    {
        if (row.Count <= 4) return string.Empty;
        string fightId = row[4].Trim();
        if (string.IsNullOrWhiteSpace(fightId)) return string.Empty;

        Dictionary<string, string[]> fightRows = GetChaFightRows();
        if (!fightRows.TryGetValue(fightId, out string[]? fightRow)) return string.Empty;

        string name = row.Count > 0 ? row[0].Trim() : "角色/怪物";
        string roleId = row.Count > 1 ? row[1].Trim() : string.Empty;
        string picId = row.Count > 5 ? row[5].Trim() : string.Empty;
        string modelSummary = BuildModelSummary(picId);
        string states = row.Count > 13 ? FormatStateList(row[13], "出生/常驻状态") : string.Empty;
        string effects = row.Count > 18 ? FormatStateList(row[18], "技能/效果关联") : string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("  怪物/Boss 属性解析:");
        builder.AppendLine($"    - 名称: {name}");
        if (!string.IsNullOrWhiteSpace(roleId))
            builder.AppendLine($"    - 角色ID: {roleId}");
        builder.AppendLine($"    - 战斗属性ID: {fightId}");
        if (!string.IsNullOrWhiteSpace(modelSummary))
            builder.Append(modelSummary);
        builder.Append(BuildFightAttributeSummary(fightRow));
        if (!string.IsNullOrWhiteSpace(states))
            builder.Append(states);
        if (!string.IsNullOrWhiteSpace(effects))
            builder.Append(effects);
        return builder.ToString();
    }

    private string BuildModelSummary(string picId)
    {
        if (string.IsNullOrWhiteSpace(picId)) return string.Empty;
        Dictionary<string, string[]> picRows = GetChaPicRows();
        if (!picRows.TryGetValue(picId, out string[]? picRow)) return string.Empty;

        var builder = new StringBuilder();
        string modelName = picRow.Length > 0 ? picRow[0].Trim() : string.Empty;
        string config = picRow.Length > 2 ? picRow[2].Trim() : string.Empty;
        string parts = picRow.Length > 3 ? picRow[3].Trim() : string.Empty;
        if (!string.IsNullOrWhiteSpace(modelName))
            builder.AppendLine($"    - 外观名称: {modelName}");
        if (!string.IsNullOrWhiteSpace(config))
            builder.AppendLine($"    - 模型配置: {config}");
        if (!string.IsNullOrWhiteSpace(parts))
            builder.AppendLine($"    - 模型部件: {TrimPreviewCell(parts)}");
        return builder.ToString();
    }

    private static string BuildFightAttributeSummary(IReadOnlyList<string> fightRow)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"    - 等级: {GetCell(fightRow, 1)}");
        builder.AppendLine($"    - 生命上限: {FormatNumberCell(GetCell(fightRow, 8))}（最终 {FormatNumberCell(GetCell(fightRow, 25))}）");
        builder.AppendLine($"    - 伤害力: {FormatNumberCell(GetCell(fightRow, 11))}（最终 {FormatNumberCell(GetCell(fightRow, 28))}）");
        builder.AppendLine($"    - 防御力: {FormatNumberCell(GetCell(fightRow, 12))}（最终 {FormatNumberCell(GetCell(fightRow, 29))}）");
        builder.AppendLine($"    - 法术抗性: {FormatNumberCell(GetCell(fightRow, 13))}");
        builder.AppendLine($"    - 物理会心: {FormatNumberCell(GetCell(fightRow, 14))}（最终 {FormatNumberCell(GetCell(fightRow, 30))}）");
        builder.AppendLine($"    - 法术会心: {FormatNumberCell(GetCell(fightRow, 15))}（最终 {FormatNumberCell(GetCell(fightRow, 32))}）");
        builder.AppendLine($"    - 综合评分/战力: {FormatNumberCell(GetCell(fightRow, 49))}");
        return builder.ToString();
    }

    private string FormatStateList(string value, string label)
    {
        string[] ids = SplitIdList(value)
            .Where(id => id != "0")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (ids.Length == 0) return string.Empty;

        Dictionary<string, string[]> stateRows = GetStateDataRows();
        var parts = ids.Select(id =>
        {
            if (!stateRows.TryGetValue(id, out string[]? row) || row.Length == 0 || string.IsNullOrWhiteSpace(row[0]))
                return id;
            return $"{id}={row[0].Trim()}";
        });

        return $"    - {label}: {string.Join("；", parts)}\n";
    }

    private static IEnumerable<string> SplitIdList(string value) =>
        value.Split(new[] { '*', '|', ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Dictionary<string, string[]> GetChaFightRows() =>
        _chaFightRows ??= LoadMbTableByColumn("object/cha_fight.txt", 0);

    private Dictionary<string, string[]> GetChaPicRows() =>
        _chaPicRows ??= LoadMbTableByColumn("object/cha_pic.txt", 1);

    private Dictionary<string, string[]> GetStateDataRows() =>
        _stateDataRows ??= LoadMbTableByColumn("skill/state_data.txt", 1);

    private Dictionary<string, string[]> GetStateGroupRows() =>
        _stateGroupRows ??= LoadMbTableByColumn("skill/state_group.txt", 1);

    private Dictionary<string, string[]> GetLegendEquipAtbs() =>
        _legendEquipAtbs ??= LoadMbTableByColumn("life/legend_equip/legend_equip_atbs.txt", 1);

    private Dictionary<string, string[]> GetLegendEquipAtbValues() =>
        _legendEquipAtbValues ??= LoadMbTableByColumn("life/legend_equip/legend_equip_atb_value.txt", 1);

    private Dictionary<string, string[]> LoadMbTableByColumn(string path, int keyColumn)
    {
        AssetEntry? table = _workspace.Assets.FirstOrDefault(asset =>
            asset.Kind == AssetKind.MbTable &&
            asset.Entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (table is null) return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            byte[] data = _workspace.Extract(table);
            if (!TryDecodeTextPreview(table, data, out string text))
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] row = line.Split('\t');
                if (keyColumn >= row.Length) continue;
                string key = row[keyColumn].Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;
                result[key] = row;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FormatLegendAttributeValues(IReadOnlyList<string> row)
    {
        string[] labels = { "低档", "中档", "高档", "极品", "满值" };
        int[] columns = { 7, 10, 11, 13, 14 };
        var parts = new List<string>();
        for (int i = 0; i < columns.Length; i++)
        {
            int column = columns[i];
            if (column >= row.Count || string.IsNullOrWhiteSpace(row[column])) continue;
            parts.Add($"{labels[i]} {row[column].Trim()}");
        }

        return parts.Count == 0 ? string.Empty : $"（{string.Join("，", parts)}）";
    }

    private static string[][] GetFocusedMbRows(IReadOnlyList<string[]> rows, IReadOnlyList<string> focusTerms, out int matchedRows)
    {
        matchedRows = 0;
        string[] usableTerms = focusTerms
            .Where(term => term.Length >= 2)
            .SelectMany(GetSearchTermVariants)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (usableTerms.Length == 0) return rows.ToArray();

        string[][] matches = rows
            .Where(row => usableTerms.All(term =>
                row.Any(cell => cell.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        matchedRows = matches.Length;
        return matches.Length > 0 ? matches : rows.ToArray();
    }

    private static string BuildFocusSummary(int matchedRows, int visibleRows) =>
        matchedRows > 0
            ? $"匹配到的前 {visibleRows:N0} 条"
            : $"前 {visibleRows:N0} 条";

    private static IEnumerable<string> GetSearchTermVariants(string term)
    {
        yield return term;
        string scaleVariant = term.Replace('麟', '鳞');
        if (!scaleVariant.Equals(term, StringComparison.Ordinal))
            yield return scaleVariant;
        string unicornVariant = term.Replace('鳞', '麟');
        if (!unicornVariant.Equals(term, StringComparison.Ordinal))
            yield return unicornVariant;

        if (term.Contains("骑宠", StringComparison.OrdinalIgnoreCase) ||
            term.Contains("坐骑", StringComparison.OrdinalIgnoreCase) ||
            term.Contains("ride", StringComparison.OrdinalIgnoreCase) ||
            term.Contains("mount", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ride";
            yield return "坐骑";
            yield return "骑宠";
            yield return "pet";
            yield return "ride_list";
            yield return "fly_ride";
            yield return "car_equip";
        }
    }

    private static bool TryDecodeTextPreview(AssetEntry asset, byte[] data, out string text)
    {
        text = string.Empty;
        if (!ResourceExplanationService.IsTextPreviewSupported(asset) || data.Length == 0) return false;
        int maximumPreviewBytes = asset.Kind == AssetKind.MbTable
            ? 8 * 1024 * 1024
            : 2 * 1024 * 1024;
        if (data.Length > maximumPreviewBytes)
        {
            text = $"文件共有 {FormatBytes(data.Length)}，为避免界面卡顿，请导出后使用文本编辑器查看。";
            return true;
        }

        text = DecodeText(data).Trim('\0', '\uFEFF', ' ', '\r', '\n');
        if (text.Length == 0) return false;
        int controlCharacters = text.Count(character =>
            char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
        return controlCharacters <= Math.Max(4, text.Length / 100);
    }

    private static string DecodeText(byte[] data)
    {
        Encoding[] encodings =
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Encoding.GetEncoding(936),
            Encoding.GetEncoding("GB18030")
        };

        string bestText = string.Empty;
        int bestScore = int.MaxValue;
        foreach (Encoding encoding in encodings)
        {
            string candidate;
            try
            {
                candidate = encoding.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }

            int score = ScoreDecodedText(candidate);
            if (score >= bestScore) continue;
            bestScore = score;
            bestText = candidate;
        }

        return bestText.Length > 0 ? bestText : Encoding.UTF8.GetString(data);
    }

    private static int ScoreDecodedText(string text)
    {
        int replacementCharacters = text.Count(character => character == '\uFFFD');
        int controlCharacters = text.Count(character =>
            char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
        return replacementCharacters * 10_000 + controlCharacters * 100;
    }

    private static char ChooseMbTableDelimiter(IReadOnlyList<string> lines)
    {
        int tabCount = lines.Take(20).Sum(line => line.Count(character => character == '\t'));
        if (tabCount > 0) return '\t';
        int commaCount = lines.Take(20).Sum(line => line.Count(character => character == ','));
        return commaCount > 0 ? ',' : ' ';
    }

    private static string[] SplitMbTableLine(string line, char delimiter)
    {
        if (delimiter == ' ')
            return Regex.Split(line.Trim(), @"\s+");
        return line.Split(delimiter);
    }

    private static bool TryGetMbHeaders(
        AssetEntry asset,
        IReadOnlyList<string[]> rows,
        int maxColumns,
        out string[] headers,
        out int firstDataRow)
    {
        headers = GetKnownMbHeaders(asset, maxColumns);
        firstDataRow = 0;
        if (rows.Count == 0) return headers.Any(header => !string.IsNullOrWhiteSpace(header));

        string[] firstRow = rows[0];
        if (LooksLikeHeaderRow(firstRow, rows.Skip(1).Take(12).ToArray()))
        {
            headers = NormalizeHeaderRow(firstRow, maxColumns);
            firstDataRow = 1;
            return true;
        }

        return false;
    }

    private static string[] GetKnownMbHeaders(AssetEntry asset, int maxColumns)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        string fileName = System.IO.Path.GetFileNameWithoutExtension(asset.Name).ToLowerInvariant();
        string[] headers = new string[maxColumns];

        if (fileName.StartsWith("insect_type_", StringComparison.Ordinal))
        {
            FillHeaders(headers, "等级名称", "等级", "门槛/消耗数值", "奖励或对象ID", "类型/组ID");
        }
        else if (fileName == "insect_items")
        {
            FillHeaders(headers, "名称", "物品或技能ID", "类别/阵营", "标记值", "图标或关联ID", "关联ID列表");
        }
        else if (fileName == "insect_attributes")
        {
            FillHeaders(headers, "单位名称", "等级/序号", "生命/血量", "攻击/伤害", "比例/概率", "参数6", "参数7", "参数8", "参数9", "参数10", "参数11", "参数12", "参数13", "参数14", "参数15", "参数16", "参数17", "参数18", "参数19", "参数20");
        }
        else if (fileName == "insect_extra_attributes")
        {
            FillHeaders(headers, "效果名称", "效果ID");
        }
        else if (fileName == "insect_team")
        {
            FillHeaders(headers, "队伍/阵营名称", "队伍ID", "出生点或入口ID", "出生点名称", "关联玩法ID");
        }
        else if (normalizedPath.StartsWith("equip/", StringComparison.Ordinal))
        {
            FillHeaders(headers, "外观/装备名称", "外观ID", "男模型资源", "女模型资源", "职业限制", "性别限制", "显示ID", "备注");
        }
        else if (normalizedPath.StartsWith("life/legend_equip/legend_equip_list", StringComparison.Ordinal))
        {
            FillHeaders(headers, "物品ID", "装备名称", "保留字段", "装备模板/模型ID", "职业/系别ID", "基础生命", "基础攻击/法效", "基础防御/抗性", "基础属性", "附加属性", "固定属性组1", "固定属性组2", "固定属性组3", "固定属性组4", "套装或部位ID", "保留", "保留", "保留", "保留", "品质/阶段", "可随机属性组", "可洗练属性组", "标记");
        }
        else if (normalizedPath.StartsWith("life/legend_equip/legend_equip_atbs", StringComparison.Ordinal))
        {
            FillHeaders(headers, "属性组名称", "属性组ID", "权重", "属性值ID", "附加属性2", "附加属性3", "附加属性4", "附加属性5", "附加属性6", "备注");
        }
        else if (normalizedPath.StartsWith("life/legend_equip/legend_equip_atb_value", StringComparison.Ordinal))
        {
            FillHeaders(headers, "属性名称", "属性值ID", "属性类型ID", "是否百分比", "条件1", "条件2", "条件3", "低档值", "低档显示", "保底值", "中档值", "高档值", "浮动", "极品值", "满值", "显示倍率", "权重组");
        }
        else if (normalizedPath.StartsWith("scn/scn_info", StringComparison.Ordinal))
        {
            FillHeaders(headers, "场景代码", "场景ID", "关联区域/副本ID", "场景大类", "场景类型", "进入限制/阵营", "保留", "推荐人数", "可视距离", "地图宽", "地图高", "是否副本", "出生X", "出生Y", "缩放", "加载图", "默认出生点", "关联标记", "场景名称", "建议等级");
        }
        else if (normalizedPath.StartsWith("object/ride", StringComparison.Ordinal))
        {
            FillHeaders(headers, "骑宠名称", "骑宠ID", "角色/怪物ID", "外观分类", "模型分类", "外观组", "品质/阶位", "图标/外观ID", "保留", "移动速度", "保留", "保留", "保留", "保留", "保留", "保留", "技能1", "技能2", "技能3", "技能4", "技能5");
        }
        else if (normalizedPath.StartsWith("object/cha_list", StringComparison.Ordinal))
        {
            FillHeaders(headers, "角色/怪物名称", "角色ID", "等级显示", "角色类型", "战斗属性ID", "外观模型ID", "保留", "保留", "显示/阵营类型", "保留", "难度/玩法标记", "保留", "保留", "出生/常驻状态ID列表", "保留", "保留", "保留", "保留", "技能/效果ID列表", "保留", "保留", "保留", "保留", "保留", "保留", "保留", "保留", "视野/警戒范围", "追击/活动范围");
        }
        else if (normalizedPath.StartsWith("object/cha_fight", StringComparison.Ordinal))
        {
            FillHeaders(headers, "战斗属性ID", "等级", "类型", "攻击系/模板", "体魄基数", "力量基数", "筋骨基数", "元神基数", "生命上限", "法力/体力", "基础抗会心", "伤害力", "防御力", "法术抗性", "物理会心", "法术会心", "列16", "列17", "保留", "保留", "保留", "保留", "属性合计", "忽略防御", "移动/速度参数", "最终生命", "最终法力/体力", "最终基础抗会心", "最终伤害力", "最终防御力", "最终物理会心", "伤害倍率", "最终法术会心", "抗性倍率", "列34", "列35", "最终属性合计", "生命缩放", "速度/范围", "列39", "伤害力放大值", "防御力放大值", "会心放大值", "抗性放大值", "列44", "列45", "评分/战力分项", "总评分/战力", "额外评分", "综合评分");
        }
        else if (normalizedPath.StartsWith("object/cha_pic", StringComparison.Ordinal))
        {
            FillHeaders(headers, "外观名称", "外观模型ID", "组合配置路径", "挂件/部件列表", "缩放", "颜色/染色", "保留", "保留", "保留", "保留", "外观类型", "性别", "职业", "头部参数", "身体参数", "保留", "保留", "保留", "保留", "保留", "图标", "头像宽", "头像高");
        }
        else if (normalizedPath.StartsWith("pet/pet_list", StringComparison.Ordinal))
        {
            FillHeaders(headers, "宠物/骑宠名称", "宠物ID", "角色/怪物ID", "分类", "品质/阶位", "成长/等级", "技能组", "状态组", "关联物品", "备注");
        }
        else if (normalizedPath.StartsWith("pet/pet_type_prompt", StringComparison.Ordinal))
        {
            FillHeaders(headers, "宠物ID", "分类", "说明");
        }
        else if (normalizedPath.StartsWith("pet/car_skill", StringComparison.Ordinal))
        {
            FillHeaders(headers, "技能ID", "技能名称", "图标/状态", "说明", "消耗", "冷却", "参数");
        }

        return headers;
    }

    private static void FillHeaders(string[] headers, params string[] names)
    {
        for (int i = 0; i < headers.Length && i < names.Length; i++)
            headers[i] = names[i];
    }

    private static bool LooksLikeHeaderRow(IReadOnlyList<string> firstRow, IReadOnlyList<string[]> nextRows)
    {
        if (firstRow.Count < 3) return false;
        int namedColumns = firstRow.Count(value =>
            !string.IsNullOrWhiteSpace(value) &&
            (ContainsChinese(value) ||
             value.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("id", StringComparison.OrdinalIgnoreCase)));
        int headerKeywords = firstRow.Count(value =>
            value.Contains("名称", StringComparison.Ordinal) ||
            value.Contains("属性", StringComparison.Ordinal) ||
            value.Contains("等级", StringComparison.Ordinal) ||
            value.Contains("备注", StringComparison.Ordinal) ||
            value.Contains("说明", StringComparison.Ordinal) ||
            value.Contains("限制", StringComparison.Ordinal) ||
            value.Contains("价格", StringComparison.Ordinal));
        int mostlyNumericInNextRows = 0;
        foreach (int column in Enumerable.Range(0, Math.Min(firstRow.Count, 12)))
        {
            int filled = 0;
            int numeric = 0;
            foreach (string[] row in nextRows)
            {
                if (column >= row.Length || string.IsNullOrWhiteSpace(row[column])) continue;
                filled++;
                if (double.TryParse(row[column], out _)) numeric++;
            }

            if (filled > 0 && numeric >= filled / 2) mostlyNumericInNextRows++;
        }

        return headerKeywords >= 2 || namedColumns >= Math.Max(3, firstRow.Count / 3) && mostlyNumericInNextRows >= 2;
    }

    private static string[] NormalizeHeaderRow(IReadOnlyList<string> row, int maxColumns)
    {
        string[] headers = new string[maxColumns];
        for (int i = 0; i < headers.Length; i++)
        {
            string name = i < row.Count ? row[i].Trim() : string.Empty;
            headers[i] = string.IsNullOrWhiteSpace(name) ? $"字段{i + 1}" : name;
        }

        return headers;
    }

    private static bool ContainsChinese(string value) =>
        value.Any(character => character >= '\u4E00' && character <= '\u9FFF');

    private static string GetColumnName(IReadOnlyList<string> headers, int column)
    {
        if (column < headers.Count && !string.IsNullOrWhiteSpace(headers[column]))
            return headers[column];
        return $"字段{column + 1}";
    }

    private static string BuildMbRecordTitle(IReadOnlyList<string> headers, IReadOnlyList<string> row)
    {
        int nameColumn = FindColumn(headers, "名称", "名字", "备注", "场景名称", "装备名称", "物品名称", "单位名称");
        int idColumn = FindColumn(headers, "ID", "编号", "等级");
        string name = nameColumn >= 0 && nameColumn < row.Count && !string.IsNullOrWhiteSpace(row[nameColumn])
            ? row[nameColumn].Trim()
            : row.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "记录";
        string id = idColumn >= 0 && idColumn < row.Count && !string.IsNullOrWhiteSpace(row[idColumn])
            ? row[idColumn].Trim()
            : string.Empty;
        return string.IsNullOrWhiteSpace(id) || id == name ? name : $"{name}（ID/编号：{id}）";
    }

    private static int FindColumn(IReadOnlyList<string> headers, params string[] keywords)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i];
            if (string.IsNullOrWhiteSpace(header)) continue;
            if (keywords.Any(keyword => header.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        return -1;
    }

    private static string GuessMbColumnName(AssetEntry asset, int columnIndex, IReadOnlyList<string[]> sampleRows)
    {
        string fileName = System.IO.Path.GetFileNameWithoutExtension(asset.Name).ToLowerInvariant();
        if (fileName == "insect_items")
        {
            return columnIndex switch
            {
                0 => "名称/显示文本",
                1 => "物品或技能ID",
                2 => "类别/阵营",
                3 => "标记值",
                4 => "图标或关联ID",
                5 => "关联ID列表",
                _ => "扩展参数"
            };
        }

        if (fileName == "insect_attributes")
        {
            return columnIndex switch
            {
                0 => "单位名称",
                1 => "等级/序号",
                2 => "生命/血量类数值",
                3 => "攻击/伤害类数值",
                4 => "比例/概率参数",
                _ => "属性参数"
            };
        }

        if (fileName == "insect_extra_attributes")
        {
            return columnIndex switch
            {
                0 => "效果名称",
                1 => "效果ID",
                _ => "属性槽/效果值"
            };
        }

        if (fileName.StartsWith("insect_type_", StringComparison.Ordinal))
        {
            return columnIndex switch
            {
                0 => "等级名称",
                1 => "等级",
                2 => "门槛/消耗数值",
                3 => "奖励或对象ID",
                4 => "类型/组ID",
                _ => "扩展参数"
            };
        }

        if (fileName == "insect_team")
        {
            return columnIndex switch
            {
                0 => "队伍/阵营名称",
                1 => "队伍ID",
                _ => "队伍参数"
            };
        }

        if (columnIndex == 0) return "名称/显示文本";
        if (ColumnLooksLikeMultiValue(sampleRows, columnIndex)) return "关联ID列表/多值";
        if (ColumnLooksNumeric(sampleRows, columnIndex)) return columnIndex == 1 ? "编号ID/等级" : "数值/编号";
        return "文本/参数";
    }

    private static bool ColumnLooksNumeric(IReadOnlyList<string[]> rows, int columnIndex)
    {
        int filled = 0;
        int numeric = 0;
        foreach (string[] row in rows)
        {
            if (columnIndex >= row.Length || string.IsNullOrWhiteSpace(row[columnIndex])) continue;
            filled++;
            if (double.TryParse(row[columnIndex], out _)) numeric++;
        }

        return filled > 0 && numeric >= Math.Max(1, filled * 2 / 3);
    }

    private static bool ColumnLooksLikeMultiValue(IReadOnlyList<string[]> rows, int columnIndex)
    {
        int filled = 0;
        int multiValue = 0;
        foreach (string[] row in rows)
        {
            if (columnIndex >= row.Length || string.IsNullOrWhiteSpace(row[columnIndex])) continue;
            filled++;
            if (row[columnIndex].Contains('*') || row[columnIndex].Contains('|') || row[columnIndex].Contains(';'))
                multiValue++;
        }

        return filled > 0 && multiValue >= Math.Max(1, filled / 2);
    }

    private static string TrimPreviewCell(string value)
    {
        string normalized = value.Trim();
        return normalized.Length <= 72 ? normalized : normalized[..69] + "...";
    }

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index < row.Count ? row[index].Trim() : string.Empty;

    private static string FormatNumberCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Trim();
        if (decimal.TryParse(normalized.TrimEnd('%'), out decimal number))
        {
            string suffix = normalized.EndsWith('%') ? "%" : string.Empty;
            return number == decimal.Truncate(number)
                ? $"{number:N0}{suffix}"
                : $"{number:N2}{suffix}";
        }

        return normalized;
    }

    private static string GetMbTableDisplayName(AssetEntry asset)
    {
        string fileName = System.IO.Path.GetFileNameWithoutExtension(asset.Name);
        string normalized = fileName.ToLowerInvariant();
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').ToLowerInvariant();
        if (normalizedPath == "object/ride_list.txt") return "骑宠/坐骑总表";
        if (normalizedPath == "object/ride_connection.txt") return "骑宠挂接/连接表";
        if (normalizedPath == "object/ride_merge.txt") return "骑宠融合/合成表";
        if (normalizedPath == "object/fly_ride_energy.txt") return "飞行骑宠能量表";
        if (normalizedPath == "pet/pet_list.txt") return "宠物/侍宠总表";
        if (normalizedPath == "pet/pet_model.txt") return "宠物/骑宠模型表";
        if (normalizedPath.StartsWith("pet/car_", StringComparison.Ordinal)) return "骑宠装备/车类配置表";
        if (normalized == "insect_team") return "虫子对抗队伍表";
        if (normalized == "insect_items") return "虫子对抗道具/技能表";
        if (normalized == "insect_attributes") return "虫子属性表";
        if (normalized == "insect_extra_attributes") return "虫子额外效果表";
        if (normalized.StartsWith("insect_type_", StringComparison.Ordinal))
            return $"虫子等级配置表 {normalized["insect_type_".Length..]}";
        return fileName;
    }

    private void RefreshDungeonSummary()
    {
        if (_workspace.Assets.Count == 0)
        {
            DungeonList.ItemsSource = null;
            DungeonMonsterList.ItemsSource = null;
            DungeonSummaryStatusText.Text = "请先打开资源目录。";
            DungeonSummaryCountText.Text = string.Empty;
            ShowDungeonMonsterDetails(null);
            return;
        }

        EnsureDungeonSummaries();
        string[] terms = GetSearchTerms();
        List<DungeonSummaryViewModel> visibleDungeons = _dungeonSummaries
            .Where(dungeon => terms.Length == 0 || DungeonMatchesSearch(dungeon, terms))
            .ToList();

        DungeonList.ItemsSource = visibleDungeons;
        DungeonSummaryCountText.Text = $"{visibleDungeons.Sum(dungeon => GetVisibleDungeonMonsters(dungeon, terms).Count):N0} 个怪物";
        DungeonSummaryStatusText.Text = terms.Length == 0
            ? "已汇总副本怪物、头像、核心属性、隐藏状态和掉落组。"
            : $"当前搜索：{string.Join(" ", terms)}";

        DungeonSummaryViewModel? preferred = visibleDungeons.FirstOrDefault(dungeon =>
            _selectedDungeonSummary is not null && dungeon.Name == _selectedDungeonSummary.Name) ??
            visibleDungeons.FirstOrDefault();
        DungeonList.SelectedItem = preferred;
        if (preferred is not null)
            ShowDungeonSummary(preferred);
        else
        {
            DungeonMonsterList.ItemsSource = null;
            DungeonMonsterListTitleText.Text = "怪物";
            DungeonMonsterListCountText.Text = "0 个";
            ShowDungeonMonsterDetails(null);
        }
    }

    private void EnsureDungeonSummaries()
    {
        if (_dungeonSummariesBuilt) return;
        _dungeonSummaries = DungeonDefinitions
            .Select(BuildDungeonSummary)
            .ToList();
        _dungeonSummariesBuilt = true;
        _ = LoadDungeonPortraitsAsync(_dungeonSummaries.SelectMany(dungeon => dungeon.Monsters).ToArray());
    }

    private DungeonSummaryViewModel BuildDungeonSummary(DungeonDefinition definition)
    {
        Dictionary<string, string[]> chaRows = GetChaListRows();
        Dictionary<string, string[]> fightRows = GetChaFightRows();
        Dictionary<string, string[]> picRows = GetChaPicRows();
        Dictionary<string, List<string[]>> skillRowsByPic = GetChaSkillRowsByPicId();
        Dictionary<string, List<string>> rewardProxyIdsByName = GetDungeonRewardProxyIds(definition);
        HashSet<string> rewardProxyRoleIds = rewardProxyIdsByName.Values
            .SelectMany(ids => ids)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bossRoleIds = new HashSet<string>(definition.BossMonsterIds, StringComparer.OrdinalIgnoreCase);
        var candidateRoleIds = new HashSet<string>(definition.KnownMonsterIds, StringComparer.OrdinalIgnoreCase);

        var candidatePicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string[]>> pair in skillRowsByPic)
        {
            if (pair.Value.Any(row => definition.Aliases.Any(alias =>
                    row.Any(cell => cell.Contains(alias, StringComparison.OrdinalIgnoreCase)))))
            {
                candidatePicIds.Add(pair.Key);
            }
        }

        foreach (KeyValuePair<string, string[]> pair in picRows)
        {
            if (definition.Aliases.Any(alias => pair.Value.Any(cell =>
                    cell.Contains(alias, StringComparison.OrdinalIgnoreCase))))
            {
                candidatePicIds.Add(pair.Key);
            }
        }

        var monsters = new List<DungeonMonsterViewModel>();
        foreach (string[] row in chaRows.Values)
        {
            string name = CleanMonsterName(GetCell(row, 0));
            string roleId = GetCell(row, 1);
            string fightId = GetCell(row, 4);
            string picId = GetCell(row, 5);
            if (definition.ExcludedRoleIds?.Contains(roleId) == true) continue;
            if (string.IsNullOrWhiteSpace(roleId) ||
                string.IsNullOrWhiteSpace(fightId) ||
                !fightRows.TryGetValue(fightId, out string[]? fightRow))
            {
                continue;
            }

            bool rowMentionsDungeon = definition.Aliases.Any(alias =>
                row.Any(cell => cell.Contains(alias, StringComparison.OrdinalIgnoreCase)));
            bool picMentionsDungeon = candidatePicIds.Contains(picId);
            bool knownRole = candidateRoleIds.Contains(roleId);
            bool inKnownRange = IsInKnownRange(roleId, definition.RoleIdRanges);
            bool directCandidate = knownRole || inKnownRange;
            bool aliasCandidate = (rowMentionsDungeon || picMentionsDungeon) &&
                                  IsCredibleAliasDungeonMonster(row, fightRow, definition);
            if (!directCandidate && !aliasCandidate) continue;

            if (rewardProxyRoleIds.Contains(roleId) && IsRewardProxyOnly(name)) continue;
            if (ShouldSkipDungeonMonsterRow(name, row, directCandidate)) continue;

            picRows.TryGetValue(picId, out string[]? picRow);
            monsters.Add(BuildDungeonMonster(row, fightRow, picRow, definition, bossRoleIds, rewardProxyIdsByName, chaRows));
        }

        List<DungeonMonsterViewModel> deduped = monsters
            .GroupBy(monster => $"{monster.Name}|{monster.FightId}|{monster.PicId}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                DungeonMonsterViewModel chosen = group
                    .OrderBy(monster => monster.SortRank)
                    .ThenBy(monster =>
                        definition.KnownMonsterIds.Contains(monster.RoleId, StringComparer.OrdinalIgnoreCase) ||
                        IsInKnownRange(monster.RoleId, definition.RoleIdRanges) ? 0 : 1)
                    .ThenByDescending(HasResolvedDrops)
                    .ThenByDescending(monster => ParseIntOrDefault(monster.RoleId))
                    .First();
                chosen.SourceIds = string.Join("、", group
                    .Select(monster => monster.RoleId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => ParseIntOrDefault(id)));
                return chosen;
            })
            .OrderBy(monster => monster.SortRank)
            .ThenBy(monster => monster.Name, NaturalStringComparer.Instance)
            .ToList();

        return new DungeonSummaryViewModel(definition.Name, $"{definition.Subtitle} · {deduped.Count:N0} 个可识别怪物", deduped);
    }

    private DungeonMonsterViewModel BuildDungeonMonster(
        IReadOnlyList<string> chaRow,
        IReadOnlyList<string> fightRow,
        IReadOnlyList<string>? picRow,
        DungeonDefinition definition,
        IReadOnlySet<string> bossRoleIds,
        IReadOnlyDictionary<string, List<string>> rewardProxyIdsByName,
        IReadOnlyDictionary<string, string[]> chaRows)
    {
        string name = CleanMonsterName(GetCell(chaRow, 0));
        string roleId = GetCell(chaRow, 1);
        string fightId = GetCell(chaRow, 4);
        string picId = GetCell(chaRow, 5);
        string iconName = picRow is not null ? NormalizePortraitIconName(GetCell(picRow, 26)) : string.Empty;
        string roleLabel = definition.RoleLabels is not null &&
                           definition.RoleLabels.TryGetValue(roleId, out string? label)
            ? label
            : string.Empty;
        bool isMechanism = definition.MechanismRoleIds?.Contains(roleId) == true ||
                           name.Contains("宝箱", StringComparison.Ordinal) ||
                           name.Contains("首饰盒", StringComparison.Ordinal) ||
                           name.Contains("蛛囊", StringComparison.Ordinal) ||
                           name.Contains("尖刺", StringComparison.Ordinal);
        bool isBoss = !isMechanism && IsBossMonster(name, roleId, bossRoleIds);
        string kind = !string.IsNullOrWhiteSpace(roleLabel)
            ? roleLabel
            : isBoss ? "首领" : isMechanism ? "机制/箱子" : "小怪";
        FightStatSnapshot adjustedStats = BuildAdjustedFightStats(fightRow, chaRow);
        IReadOnlyList<StatLineViewModel> stats = BuildDungeonMonsterStats(fightRow, chaRow);
        IReadOnlyList<string> hiddenStates = BuildHiddenStateSummaries(chaRow);
        IReadOnlyList<string[]> rewardRows = ResolveRewardProxyRows(name, roleId, rewardProxyIdsByName, chaRows);
        IReadOnlyList<DropLineViewModel> drops = BuildMonsterDrops(chaRow, rewardRows);
        string hiddenSummary = hiddenStates.Count == 0
            ? string.Empty
            : string.Join("\n", hiddenStates.Select(state => "· " + state));
        string phaseSummary = BuildPhaseSummary(picId);
        if (!string.IsNullOrWhiteSpace(phaseSummary))
            hiddenSummary = string.IsNullOrWhiteSpace(hiddenSummary)
                ? "· " + phaseSummary
                : hiddenSummary + "\n· " + phaseSummary;

        return new DungeonMonsterViewModel
        {
            Name = name,
            RoleId = roleId,
            FightId = fightId,
            PicId = picId,
            IconName = iconName,
            KindText = kind,
            Subtitle = $"{definition.Name} · {kind} · ID {roleId} · 战斗属性 {fightId}",
            CompactStats = $"等级 {adjustedStats.Level} · 生命 {adjustedStats.Health} · 伤害 {adjustedStats.Damage} · 物防 {adjustedStats.PhysicalDefense} · 法抗 {adjustedStats.MagicResistance}",
            Explanation = string.Empty,
            HiddenSummary = hiddenSummary,
            SourceIds = roleId,
            Stats = stats,
            Drops = drops,
            SortRank = isBoss ? 0 : isMechanism ? 20 : 10
        };
    }

    private static bool HasResolvedDrops(DungeonMonsterViewModel monster) =>
        monster.Drops.Any(drop => !drop.Name.Contains("未识别", StringComparison.Ordinal));

    private static int ParseIntOrDefault(string value) =>
        int.TryParse(value, out int parsed) ? parsed : 0;

    private static bool IsInKnownRange(string roleId, IReadOnlyList<IntRange> ranges)
    {
        if (!int.TryParse(roleId, out int id)) return false;
        return ranges.Any(range => id >= range.Start && id <= range.End);
    }

    private static bool IsCredibleAliasDungeonMonster(
        IReadOnlyList<string> chaRow,
        IReadOnlyList<string> fightRow,
        DungeonDefinition definition)
    {
        bool hasCuratedIds = definition.KnownMonsterIds.Count > 0 ||
                             definition.BossMonsterIds.Count > 0 ||
                             definition.RoleIdRanges.Count > 0;
        if (hasCuratedIds) return false;
        if (IsLikelyNonMonsterName(CleanMonsterName(GetCell(chaRow, 0)))) return false;
        if (GetCell(chaRow, 4) == "1" && !HasDropGroups(chaRow)) return false;
        if (!int.TryParse(GetCell(fightRow, 1), out int level)) return false;

        int minimumLevel = ExtractMinimumDungeonLevel(definition.Subtitle);
        return minimumLevel <= 0 || level >= Math.Max(1, minimumLevel - 30);
    }

    private static bool ShouldSkipDungeonMonsterRow(string name, IReadOnlyList<string> row, bool directCandidate)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (IsLikelyNonMonsterName(name)) return true;
        if (GetCell(row, 4) == "1" && !HasDropGroups(row)) return true;
        if (!directCandidate && GetCell(row, 8) == "110") return true;
        return false;
    }

    private static bool IsLikelyNonMonsterName(string name)
    {
        string[] blockedTerms =
        {
            "演出用", "透明", "占位", "测试", "传送", "入口", "出口", "返回", "离开",
            "出生点", "光柱", "特效", "镜头", "寻路", "空气墙", "无敌"
        };
        return blockedTerms.Any(term => name.Contains(term, StringComparison.Ordinal));
    }

    private static int ExtractMinimumDungeonLevel(string subtitle)
    {
        Match match = Regex.Match(subtitle, @"(\d+)\s*级");
        return match.Success && int.TryParse(match.Groups[1].Value, out int level) ? level : 0;
    }

    private Dictionary<string, List<string>> GetDungeonRewardProxyIds(DungeonDefinition definition)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in LoadMbRows("etc/recomdaily/recomdaily_boss.txt"))
        {
            string line = string.Join('\t', row);
            if (!definition.Aliases.Any(alias => line.Contains(alias, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (Match match in Regex.Matches(line, @"<help_bank:7,(\d+),([^>]+)>"))
            {
                string roleId = match.Groups[1].Value.Trim();
                string monsterName = ExtractRecommendationMonsterName(match.Groups[2].Value);
                if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(monsterName))
                    continue;

                if (!result.TryGetValue(monsterName, out List<string>? ids))
                {
                    ids = new List<string>();
                    result[monsterName] = ids;
                }

                if (!ids.Contains(roleId, StringComparer.OrdinalIgnoreCase))
                    ids.Add(roleId);
            }
        }

        return result;
    }

    private static string ExtractRecommendationMonsterName(string text)
    {
        string result = text.Trim();
        int index = result.LastIndexOf('的');
        if (index >= 0 && index + 1 < result.Length)
            result = result[(index + 1)..];
        result = result.Replace("（简单难度）", string.Empty)
            .Replace("（正常难度）", string.Empty)
            .Replace("副本", string.Empty)
            .Trim();
        return CleanMonsterName(result);
    }

    private static bool MonsterNameMatches(string name, string expected)
    {
        string left = CleanMonsterName(name);
        string right = CleanMonsterName(expected);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        if (right.Length >= 2 && left.Contains(right, StringComparison.OrdinalIgnoreCase)) return true;
        return left.Length >= 3 && right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRewardProxyOnly(string name) =>
        name.Contains("宝箱", StringComparison.Ordinal) ||
        name.Contains("首饰盒", StringComparison.Ordinal);

    private static IReadOnlyList<string[]> ResolveRewardProxyRows(
        string monsterName,
        string roleId,
        IReadOnlyDictionary<string, List<string>> rewardProxyIdsByName,
        IReadOnlyDictionary<string, string[]> chaRows)
    {
        var rows = new List<string[]>();
        foreach (KeyValuePair<string, List<string>> pair in rewardProxyIdsByName)
        {
            if (!MonsterNameMatches(monsterName, pair.Key)) continue;
            foreach (string proxyId in pair.Value)
            {
                if (proxyId.Equals(roleId, StringComparison.OrdinalIgnoreCase)) continue;
                if (chaRows.TryGetValue(proxyId, out string[]? row))
                    rows.Add(row);
            }
        }

        return rows;
    }

    private string BuildPhaseSummary(string picId)
    {
        if (string.IsNullOrWhiteSpace(picId)) return string.Empty;
        if (!GetChaSkillRowsByPicId().TryGetValue(picId, out List<string[]>? skillRows))
            return string.Empty;

        var ranges = new List<(decimal Low, decimal High)>();
        foreach (string[] row in skillRows)
        {
            for (int column = 0; column + 2 < row.Length; column++)
            {
                if (GetCell(row, column) != "10") continue;
                if (!decimal.TryParse(GetCell(row, column + 1), out decimal low) ||
                    !decimal.TryParse(GetCell(row, column + 2), out decimal high))
                {
                    continue;
                }

                if (low < 0) low = 0;
                if (high <= 0 || high > 100) continue;
                ranges.Add((low, high));
            }
        }

        var texts = ranges
            .Distinct()
            .OrderByDescending(range => range.High)
            .ThenByDescending(range => range.Low)
            .Take(8)
            .Select(range => $"{range.Low:0}-{range.High:0}%")
            .ToArray();
        return texts.Length == 0 ? string.Empty : $"血量阶段：{string.Join("、", texts)}";
    }

    private IReadOnlyList<StatLineViewModel> BuildDungeonMonsterStats(IReadOnlyList<string> fightRow, IReadOnlyList<string> chaRow)
    {
        string lockBlood = BuildLockBloodSummary(chaRow);
        FightStatSnapshot adjustedStats = BuildAdjustedFightStats(fightRow, chaRow);
        Dictionary<string, List<StateAttributeAdjustment>> directAttributes = GetDirectStateAttributes(chaRow);
        var stats = new List<StatLineViewModel>
        {
            new("等级", adjustedStats.Level, string.Empty),
            new("生命", adjustedStats.Health, string.Empty),
            new("锁血", lockBlood, string.Empty),
            new("物理/普通伤害", adjustedStats.Damage, string.Empty),
            new("物理防御", adjustedStats.PhysicalDefense, string.Empty),
            new("法术抗性", adjustedStats.MagicResistance, string.Empty),
            new("抵抗会心几率", adjustedStats.CritResistance, string.Empty),
            new("会心伤害减免", FormatDirectAttribute(directAttributes, "68"), string.Empty),
            new("伤害/幸运减免", FormatDirectAttribute(directAttributes, "32"), string.Empty),
            new("忽略物防", FormatDirectAttribute(directAttributes, "45"), string.Empty),
            new("忽略法抗", FormatDirectAttribute(directAttributes, "46"), string.Empty),
            new("物理会伤上限", FormatDirectAttribute(directAttributes, "19"), string.Empty),
            new("法术会伤上限", FormatDirectAttribute(directAttributes, "20"), string.Empty),
            new("综合评分/战力", adjustedStats.FightValue, string.Empty)
        };
        return stats.Where(stat => !string.IsNullOrWhiteSpace(stat.Value)).ToList();
    }

    private FightStatSnapshot BuildAdjustedFightStats(IReadOnlyList<string> fightRow, IReadOnlyList<string> chaRow)
    {
        Dictionary<string, List<StateAttributeAdjustment>> attributes = GetDirectStateAttributes(chaRow);

        decimal physicalDefense = CalculateAdjustedAttribute(ParseDecimalCell(GetCell(fightRow, 30)), attributes, "15");
        decimal magicResistance = CalculateAdjustedAttribute(ParseDecimalCell(GetCell(fightRow, 32)), attributes, "29");
        decimal critResistance = CalculateAdjustedAttribute(ParseDecimalCell(GetCell(fightRow, 39)), attributes, "21");

        return new FightStatSnapshot(
            FormatNumberCell(GetCell(fightRow, 1)),
            FormatNumberCell(GetCell(fightRow, 25)),
            FormatNumberCell(GetCell(fightRow, 28)),
            FormatDecimalCell(physicalDefense),
            FormatDecimalCell(magicResistance),
            FormatDecimalCell(critResistance),
            FormatNumberCell(GetCell(fightRow, 49)));
    }

    private static decimal CalculateAdjustedAttribute(
        decimal baseValue,
        IReadOnlyDictionary<string, List<StateAttributeAdjustment>> attributes,
        string attributeId)
    {
        if (!attributes.TryGetValue(attributeId, out List<StateAttributeAdjustment>? values))
            return baseValue;

        decimal flat = 0m;
        decimal percent = 0m;
        foreach (StateAttributeAdjustment adjustment in values)
        {
            if (adjustment.Mode == 0)
                flat += adjustment.Value;
            else if (adjustment.Mode == 1)
                percent += adjustment.Value;
        }

        return (baseValue + flat) * (1m + percent / 10000m);
    }

    private string BuildLockBloodSummary(IReadOnlyList<string> chaRow)
    {
        string[] locks = GetStateRowsForMonster(chaRow)
            .Select(row => GetCell(row, 0))
            .Where(name => name.Contains("锁血", StringComparison.Ordinal))
            .Select(name =>
            {
                Match match = Regex.Match(name, @"锁血\s*(\d+)%");
                return match.Success ? $"{match.Groups[1].Value}%" : "锁血";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return locks.Length == 0 ? string.Empty : $"锁血 {string.Join("、", locks)}";
    }

    private Dictionary<string, List<StateAttributeAdjustment>> GetDirectStateAttributes(IReadOnlyList<string> chaRow)
    {
        var attributes = new Dictionary<string, List<StateAttributeAdjustment>>(StringComparer.OrdinalIgnoreCase);
        foreach (StateDataReference state in GetEffectiveStateDataRowsForMonster(chaRow))
        {
            foreach (StateAttributeAdjustment adjustment in ParseStateAttributePack(
                         GetCell(state.Row, 8),
                         state.DataStateId,
                         GetCell(state.Row, 0)))
            {
                if (!attributes.TryGetValue(adjustment.AttributeId, out List<StateAttributeAdjustment>? values))
                {
                    values = new List<StateAttributeAdjustment>();
                    attributes[adjustment.AttributeId] = values;
                }

                values.Add(adjustment);
            }
        }

        return attributes;
    }

    private IEnumerable<string[]> GetStateRowsForMonster(IReadOnlyList<string> chaRow)
    {
        return GetEffectiveStateDataRowsForMonster(chaRow).Select(state => state.Row);
    }

    private IEnumerable<StateDataReference> GetEffectiveStateDataRowsForMonster(IReadOnlyList<string> chaRow)
    {
        Dictionary<string, string[]> stateRows = GetStateDataRows();
        var yieldedDataIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (EffectiveStateGroup stateGroup in GetEffectiveStateGroupsForMonster(chaRow))
        {
            bool yieldedFromGroup = false;
            IReadOnlyList<string> dataIds = stateGroup.GroupRow is not null
                ? SplitIdList(GetCell(stateGroup.GroupRow, 3)).ToArray()
                : new[] { stateGroup.DirectStateId };

            foreach (string rawDataId in dataIds)
            {
                string dataId = rawDataId.TrimStart('-');
                if (string.IsNullOrWhiteSpace(dataId) || dataId == "0") continue;
                if (!stateRows.TryGetValue(dataId, out string[]? row)) continue;
                if (!yieldedDataIds.Add(dataId)) continue;

                yieldedFromGroup = true;
                yield return new StateDataReference(stateGroup.DirectStateId, dataId, row);
            }

            if (!yieldedFromGroup ||
                yieldedDataIds.Contains(stateGroup.DirectStateId) ||
                !ShouldUseDirectStateDataFallback(stateGroup.DirectStateId, stateRows))
            {
                continue;
            }

            if (stateRows.TryGetValue(stateGroup.DirectStateId, out string[]? directRow) &&
                yieldedDataIds.Add(stateGroup.DirectStateId))
            {
                yield return new StateDataReference(stateGroup.DirectStateId, stateGroup.DirectStateId, directRow);
            }
        }
    }

    private IEnumerable<EffectiveStateGroup> GetEffectiveStateGroupsForMonster(IReadOnlyList<string> chaRow)
    {
        Dictionary<string, string[]> stateGroupRows = GetStateGroupRows();
        var bestByGroup = new Dictionary<string, EffectiveStateGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (string directStateId in SplitIdList(GetCell(chaRow, 13))
                     .Where(id => id != "0")
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            stateGroupRows.TryGetValue(directStateId, out string[]? groupRow);
            string groupKey = groupRow is not null && !string.IsNullOrWhiteSpace(GetCell(groupRow, 6))
                ? GetCell(groupRow, 6)
                : directStateId;
            int priority = groupRow is not null ? ParseIntOrDefault(GetCell(groupRow, 7)) : 0;
            var candidate = new EffectiveStateGroup(directStateId, groupKey, priority, groupRow);

            if (!bestByGroup.TryGetValue(groupKey, out EffectiveStateGroup? existing) ||
                candidate.Priority > existing.Priority)
            {
                bestByGroup[groupKey] = candidate;
            }
        }

        return bestByGroup.Values;
    }

    private static bool ShouldUseDirectStateDataFallback(
        string directStateId,
        IReadOnlyDictionary<string, string[]> stateRows)
    {
        if (directStateId == "2720") return false;
        return stateRows.TryGetValue(directStateId, out string[]? row) &&
               ParseStateAttributePack(GetCell(row, 8), directStateId, GetCell(row, 0)).Count > 0;
    }

    private static IReadOnlyList<StateAttributeAdjustment> ParseStateAttributePack(
        string value,
        string sourceId,
        string sourceName)
    {
        string[] parts = SplitIdList(value).ToArray();
        var result = new List<StateAttributeAdjustment>();
        for (int index = 0; index + 2 < parts.Length; index += 3)
        {
            if (!int.TryParse(parts[index], out int mode)) continue;
            string attributeId = parts[index + 1];
            if (string.IsNullOrWhiteSpace(attributeId)) continue;
            if (!decimal.TryParse(parts[index + 2], out decimal rawValue)) continue;
            result.Add(new StateAttributeAdjustment(sourceId, sourceName, attributeId, mode, rawValue));
        }

        return result;
    }

    private static string FormatDirectAttribute(
        IReadOnlyDictionary<string, List<StateAttributeAdjustment>> attributes,
        string attributeId)
    {
        return attributes.TryGetValue(attributeId, out List<StateAttributeAdjustment>? values) && values.Count > 0
            ? FormatAttributeAdjustments(values)
            : string.Empty;
    }

    private static string FormatAttributePack(string value)
    {
        IReadOnlyList<StateAttributeAdjustment> attributes = ParseStateAttributePack(value, string.Empty, string.Empty);
        if (attributes.Count == 0) return string.Empty;
        return string.Join("；", attributes
            .GroupBy(attribute => attribute.AttributeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{GetFightAttributeDisplayName(group.Key)}：{FormatAttributeAdjustments(group)}"));
    }

    private static string FormatAttributeAdjustments(IEnumerable<StateAttributeAdjustment> adjustments)
    {
        return string.Join("、", adjustments.Select(FormatAttributeAdjustment).Distinct());
    }

    private static string FormatAttributeAdjustment(StateAttributeAdjustment adjustment)
    {
        string raw = FormatSignedDecimal(adjustment.Value);
        return adjustment.Mode switch
        {
            1 => $"{FormatSignedDecimal(adjustment.Value / 100m)}%",
            0 => raw,
            _ => raw
        };
    }

    private static string FormatSignedDecimal(decimal value)
    {
        string sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{FormatDecimalCell(value)}";
    }

    private static string GetFightAttributeDisplayName(string attributeId) =>
        attributeId switch
        {
            "4" => "生命值上限",
            "14" => "伤害",
            "15" => "防御",
            "17" => "物理会心几率",
            "18" => "法术会心几率",
            "19" => "物理会心伤害上限",
            "20" => "法术会心伤害上限",
            "21" => "抵抗会心几率",
            "22" => "法术效果",
            "28" => "移动速度",
            "29" => "法术抗性",
            "32" => "幸运一击伤害减免",
            "36" => "承受伤害",
            "38" => "最终伤害和治疗输出",
            "39" => "最终防御效果",
            "45" => "忽略物理防御",
            "46" => "忽略法术抗性",
            "68" => "会心伤害减免",
            "69" => "对怪物伤害",
            _ => $"属性 {attributeId}"
        };

    private static decimal ParseDecimalCell(string value)
    {
        return decimal.TryParse(value.TrimEnd('%'), out decimal number) ? number : 0m;
    }

    private static string FormatDecimalCell(decimal number) =>
        number == decimal.Truncate(number)
            ? $"{number:N0}"
            : $"{number:N2}";

    private IReadOnlyList<string> BuildHiddenStateSummaries(IReadOnlyList<string> chaRow)
    {
        string[] ids = new[] { GetCell(chaRow, 13) }
            .SelectMany(SplitIdList)
            .Where(id => id != "0")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<string>();
        foreach (StateDataReference state in GetEffectiveStateDataRowsForMonster(chaRow))
        {
            string name = CleanStateText(GetCell(state.Row, 0));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string attributePack = FormatAttributePack(GetCell(state.Row, 8));
            if (!string.IsNullOrWhiteSpace(attributePack))
                result.Add($"{name}：{attributePack}");
        }

        AppendKnownDungeonMechanismSummaries(result, ids);
        return result;
    }

    private void AppendKnownDungeonMechanismSummaries(ICollection<string> result, IReadOnlyCollection<string> directStateIds)
    {
        if (directStateIds.Contains("59725", StringComparer.OrdinalIgnoreCase))
        {
            result.Add("蓝晶蛛活化：防御/法抗 -100%、-115%、-130%、-145%、-160%");
            result.Add("碎晶护盾：伤害吸收 2,000,000,000");
        }

        if (directStateIds.Contains("59681", StringComparer.OrdinalIgnoreCase))
        {
            result.Add("玄冥破煞符：削弱蛛王战斗力");
        }
    }

    private static bool IsUsefulStateDescription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] keywords = { "锁血", "免疫", "无敌", "护盾", "吸收", "降低", "提高", "减免", "削弱", "伤害" };
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private static string CleanStateText(string text) =>
        Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    private IReadOnlyList<DropLineViewModel> BuildMonsterDrops(
        IReadOnlyList<string> chaRow,
        IReadOnlyList<string[]> rewardRows)
    {
        Dictionary<string, string[]> itemRandRows = GetItemRandRows();
        Dictionary<string, string> itemNames = GetItemNameById();
        string[] groupIds = new[] { chaRow }.Concat(rewardRows)
            .SelectMany(row => SplitIdList(GetCell(row, 18)))
            .Where(id => id != "0")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var drops = new List<DropLineViewModel>();
        foreach (string groupId in groupIds)
        {
            if (!itemRandRows.TryGetValue(groupId, out string[]? groupRow)) continue;
            string groupName = GetCell(groupRow, 0);
            var itemParts = new List<string>();
            for (int column = 4; column + 1 < groupRow.Length; column += 3)
            {
                string itemId = GetCell(groupRow, column);
                if (string.IsNullOrWhiteSpace(itemId) || itemId == "0") continue;
                string chance = GetCell(groupRow, column + 1);
                itemNames.TryGetValue(itemId, out string? itemName);
                itemParts.Add($"{(string.IsNullOrWhiteSpace(itemName) ? itemId : itemName)} {FormatDropChance(chance)}");
                if (itemParts.Count >= 8) break;
            }

            string detail = itemParts.Count == 0
                ? $"掉落组 ID：{groupId}"
                : $"掉落组 ID：{groupId} · {string.Join("，", itemParts)}";
            drops.Add(new DropLineViewModel(groupName, detail));
        }

        return drops;
    }

    private void DungeonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<DungeonSummaryViewModel>().LastOrDefault() is { } dungeon)
            ShowDungeonSummary(dungeon);
    }

    private void ShowDungeonSummary(DungeonSummaryViewModel dungeon)
    {
        _selectedDungeonSummary = dungeon;
        string[] terms = GetSearchTerms();
        List<DungeonMonsterViewModel> monsters = GetVisibleDungeonMonsters(dungeon, terms);
        DungeonMonsterListTitleText.Text = dungeon.Name;
        DungeonMonsterListCountText.Text = $"{monsters.Count:N0} 个";
        DungeonMonsterList.ItemsSource = monsters;
        DungeonMonsterList.SelectedItem = monsters.FirstOrDefault();
        ShowDungeonMonsterDetails(monsters.FirstOrDefault());
    }

    private void DungeonMonsterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowDungeonMonsterDetails(e.AddedItems.OfType<DungeonMonsterViewModel>().LastOrDefault());
    }

    private void ShowDungeonMonsterDetails(DungeonMonsterViewModel? monster)
    {
        if (monster is null)
        {
            DungeonMonsterPortraitImage.Source = null;
            DungeonMonsterNameText.Text = "选择一个怪物";
            DungeonMonsterMetaText.Text = string.Empty;
            DungeonMonsterExplainText.Text = string.Empty;
            DungeonMonsterStatList.ItemsSource = null;
            DungeonMonsterHiddenText.Text = "暂无数据。";
            DungeonMonsterDropList.ItemsSource = null;
            return;
        }

        DungeonMonsterPortraitImage.Source = monster.Portrait;
        DungeonMonsterNameText.Text = monster.Name;
        string sourceIds = string.IsNullOrWhiteSpace(monster.SourceIds) ? monster.RoleId : monster.SourceIds;
        DungeonMonsterMetaText.Text = $"{monster.KindText} · 关联记录 {sourceIds} · 外观 ID {monster.PicId} · 战斗属性 ID {monster.FightId}";
        DungeonMonsterExplainText.Text = monster.Explanation;
        DungeonMonsterStatList.ItemsSource = monster.Stats;
        DungeonMonsterHiddenText.Text = monster.HiddenSummary;
        DungeonMonsterDropList.ItemsSource = monster.Drops;
    }

    private List<DungeonMonsterViewModel> GetVisibleDungeonMonsters(DungeonSummaryViewModel dungeon, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || terms.All(term => DungeonTextMatches(dungeon.Name + " " + dungeon.Subtitle, term)))
            return dungeon.Monsters.ToList();

        return dungeon.Monsters
            .Where(monster => terms.All(term =>
                DungeonTextMatches(monster.Name, term) ||
                DungeonTextMatches(monster.Subtitle, term) ||
                DungeonTextMatches(monster.CompactStats, term) ||
                DungeonTextMatches(monster.HiddenSummary, term)))
            .ToList();
    }

    private static bool DungeonMatchesSearch(DungeonSummaryViewModel dungeon, IReadOnlyList<string> terms) =>
        terms.All(term =>
            DungeonTextMatches(dungeon.Name, term) ||
            DungeonTextMatches(dungeon.Subtitle, term) ||
            dungeon.Monsters.Any(monster =>
                DungeonTextMatches(monster.Name, term) ||
                DungeonTextMatches(monster.Subtitle, term) ||
                DungeonTextMatches(monster.HiddenSummary, term)));

    private static bool DungeonTextMatches(string text, string term) =>
        GetSearchTermVariants(term).Any(variant => text.Contains(variant, StringComparison.OrdinalIgnoreCase));

    private async Task LoadDungeonPortraitsAsync(IReadOnlyList<DungeonMonsterViewModel> monsters)
    {
        foreach (DungeonMonsterViewModel monster in monsters)
        {
            if (string.IsNullOrWhiteSpace(monster.IconName)) continue;
            AssetEntry? image = FindPortraitAsset(monster.IconName);
            if (image is null) continue;
            try
            {
                byte[] data = await Task.Run(() => _workspace.Extract(image));
                monster.Portrait = await CreateBitmapAsync(data, 96);
                if (Equals(DungeonMonsterList.SelectedItem, monster))
                    DungeonMonsterPortraitImage.Source = monster.Portrait;
            }
            catch
            {
                // Keep the card readable even when a portrait image is missing or in an unsupported format.
            }
        }
    }

    private AssetEntry? FindPortraitAsset(string iconName)
    {
        string normalized = iconName.Replace('\\', '/');
        string fileName = System.IO.Path.GetFileName(normalized);
        return _workspace.Assets
            .Where(asset => asset.Kind == AssetKind.Image &&
                            (asset.Entry.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                             asset.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                             asset.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(asset => asset.Entry.Path.Contains("portrait", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset => asset.Entry.Path.Contains("head", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset => asset.Entry.Path.Contains("icon", StringComparison.OrdinalIgnoreCase))
            .ThenBy(asset => asset.Entry.Path, NaturalStringComparer.Instance)
            .FirstOrDefault();
    }

    private Dictionary<string, string[]> GetChaListRows() =>
        _chaListRows ??= LoadMbTableByColumn("object/cha_list.txt", 1);

    private Dictionary<string, string[]> GetItemRandRows() =>
        _itemRandRows ??= LoadMbTableByColumn("item/item_rand.txt", 1);

    private Dictionary<string, List<string[]>> GetChaSkillRowsByPicId()
    {
        if (_chaSkillRowsByPicId is not null) return _chaSkillRowsByPicId;
        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in LoadMbRows("object/cha_skill_choose.txt"))
        {
            string picId = GetCell(row, 1);
            if (string.IsNullOrWhiteSpace(picId)) continue;
            if (!result.TryGetValue(picId, out List<string[]>? rows))
            {
                rows = new List<string[]>();
                result[picId] = rows;
            }

            rows.Add(row);
        }

        _chaSkillRowsByPicId = result;
        return _chaSkillRowsByPicId;
    }

    private Dictionary<string, string> GetItemNameById()
    {
        if (_itemNameById is not null) return _itemNameById;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tablePath in new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" })
        {
            foreach (string[] row in LoadMbRows(tablePath))
            {
                string id = GetCell(row, 0);
                string name = GetCell(row, 1);
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    result[id] = name;
            }
        }

        foreach (string[] row in LoadMbRows("item/raw_list.txt"))
        {
            string name = GetCell(row, 0);
            string id = GetCell(row, 1);
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                result.TryAdd(id, name);
        }

        _itemNameById = result;
        return _itemNameById;
    }

    private AssetEntry? FindItemIconAsset(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        Dictionary<string, string> iconReferences = GetItemIconReferenceById();
        foreach (string token in GetPotentialItemLookupKeys(idOrName))
        {
            if (iconReferences.TryGetValue(token, out string? iconReference))
            {
                AssetEntry? image = FindGlobalImageAsset(iconReference);
                if (image is not null) return image;
            }

            AssetEntry? directImage = FindGlobalImageAsset(token);
            if (directImage is not null) return directImage;
        }

        string normalizedName = CleanGlobalTitle(idOrName);
        if (normalizedName.Length > 0)
        {
            foreach (KeyValuePair<string, string> pair in GetItemNameById())
            {
                if (!pair.Value.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)) continue;
                if (iconReferences.TryGetValue(pair.Key, out string? iconReference))
                {
                    AssetEntry? image = FindGlobalImageAsset(iconReference);
                    if (image is not null) return image;
                }
            }
        }

        return null;
    }

    private Dictionary<string, string> GetItemIconReferenceById()
    {
        if (_itemIconReferenceById is not null) return _itemIconReferenceById;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tablePath in new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" })
            AddItemIconReferencesFromRows(result, tablePath, 0, 1);
        AddItemIconReferencesFromRows(result, "item/raw_list.txt", 1, 0);
        AddItemIconReferencesFromRows(result, "help_bank/bank_tz.txt", 1, 0);
        AddItemIconReferencesFromRows(result, "item/item_set.txt", 0, 1);

        _itemIconReferenceById = result;
        return _itemIconReferenceById;
    }

    private void AddItemIconReferencesFromRows(
        IDictionary<string, string> result,
        string tablePath,
        int idColumn,
        int nameColumn)
    {
        foreach (string[] row in LoadMbRows(tablePath))
        {
            string id = GetCell(row, idColumn);
            if (string.IsNullOrWhiteSpace(id)) continue;
            string name = GetCell(row, nameColumn);
            if (TryFindItemIconReference(row, id, name, out string? reference))
                result.TryAdd(id, reference);
        }
    }

    private bool TryFindItemIconReference(
        IReadOnlyList<string> row,
        string id,
        string name,
        out string reference)
    {
        string[] skippedValues = { id, name };

        foreach (string cell in row)
        {
            foreach (Match match in Regex.Matches(cell, @"<ic:(\d+)>", RegexOptions.IgnoreCase))
            {
                reference = match.Groups[1].Value;
                if (FindGlobalImageAsset(reference) is not null) return true;
            }
        }

        foreach (string cell in row)
        {
            foreach (Match match in Regex.Matches(cell, @"(?i)(?:[\w.-]+/)*[\w.-]+\.(?:png|jpg|jpeg|dds|tga)"))
            {
                reference = match.Value;
                if (FindGlobalImageAsset(reference) is not null) return true;
            }
        }

        foreach (string cell in row)
        {
            if (skippedValues.Any(value => !string.IsNullOrWhiteSpace(value) &&
                                           value.Equals(cell.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (string token in ExtractLooseNumericTokens(cell))
            {
                if (token.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                if (token.Length is < 2 or > 6) continue;
                reference = token;
                if (FindGlobalImageAsset(reference) is not null) return true;
            }
        }

        reference = string.Empty;
        return false;
    }

    private static IEnumerable<string> GetPotentialItemLookupKeys(string value)
    {
        string normalized = value.Trim();
        if (normalized.Length == 0) yield break;
        yield return normalized;

        string cleaned = CleanGlobalTitle(normalized);
        if (!cleaned.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            yield return cleaned;

        foreach (string token in ExtractLooseNumericTokens(normalized))
            yield return token;
    }

    private static IEnumerable<string> ExtractLooseNumericTokens(string value)
    {
        foreach (Match match in Regex.Matches(value, @"\d+"))
            yield return match.Value;
    }

    private IReadOnlyList<string[]> LoadMbRows(string path)
    {
        if (_mbRowsByPath.TryGetValue(path, out IReadOnlyList<string[]>? cachedRows))
            return cachedRows;

        AssetEntry? table = _workspace.Assets.FirstOrDefault(asset =>
            asset.Kind == AssetKind.MbTable &&
            asset.Entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            _mbRowsByPath[path] = Array.Empty<string[]>();
            return _mbRowsByPath[path];
        }

        try
        {
            byte[] data = _workspace.Extract(table);
            if (!TryDecodeTextPreview(table, data, out string text))
            {
                _mbRowsByPath[path] = Array.Empty<string[]>();
                return _mbRowsByPath[path];
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            char delimiter = ChooseMbTableDelimiter(lines);
            _mbRowsByPath[path] = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => SplitMbTableLine(line, delimiter))
                .ToArray();
            return _mbRowsByPath[path];
        }
        catch
        {
            _mbRowsByPath[path] = Array.Empty<string[]>();
            return _mbRowsByPath[path];
        }
    }

    private static bool HasDropGroups(IReadOnlyList<string> row) =>
        SplitIdList(GetCell(row, 18)).Any(id => id != "0");

    private static string NormalizePortraitIconName(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        string fileName = System.IO.Path.GetFileName(normalized.Replace('\\', '/'));
        if (fileName.Equals("unknown.png", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("unknown.dds", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
    }

    private static bool IsBossMonster(string name, string roleId, IReadOnlySet<string> bossRoleIds)
    {
        if (bossRoleIds.Contains(roleId)) return true;
        if (name.Contains("将军", StringComparison.Ordinal) ||
            name.Contains("蛇王", StringComparison.Ordinal) ||
            name.Contains("蛛王", StringComparison.Ordinal) ||
            name.Contains("纤丝", StringComparison.Ordinal) ||
            name.Contains("长老", StringComparison.Ordinal) ||
            name.Contains("笑千山", StringComparison.Ordinal) ||
            name.Contains("首领", StringComparison.Ordinal) ||
            name.Contains("BOSS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string CleanMonsterName(string name)
    {
        string result = Regex.Replace(name, @"^\((NPC|BOSS|怪|静|骑)[^)]*\)", string.Empty, RegexOptions.IgnoreCase).Trim();
        result = result.Replace("（简单难度）", string.Empty).Replace("（正常难度）", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(result) ? name : result;
    }

    private static string FormatDropChance(string value)
    {
        if (!decimal.TryParse(value, out decimal raw)) return string.Empty;
        if (raw <= 0) return string.Empty;
        decimal percent = raw / 10000m;
        return percent >= 100 ? "必掉" : $"约 {percent:0.##}%";
    }

    private sealed record DungeonDefinition(
        string Name,
        string Subtitle,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> KnownMonsterIds,
        IReadOnlyList<string> BossMonsterIds,
        IReadOnlyList<IntRange> RoleIdRanges,
        IReadOnlyDictionary<string, string>? RoleLabels = null,
        IReadOnlySet<string>? ExcludedRoleIds = null,
        IReadOnlySet<string>? MechanismRoleIds = null);

    private sealed record IntRange(int Start, int End);

    private sealed record FightStatSnapshot(
        string Level,
        string Health,
        string Damage,
        string PhysicalDefense,
        string MagicResistance,
        string CritResistance,
        string FightValue);

    private sealed record EffectiveStateGroup(
        string DirectStateId,
        string GroupKey,
        int Priority,
        string[]? GroupRow);

    private sealed record StateDataReference(
        string DirectStateId,
        string DataStateId,
        string[] Row);

    private sealed record StateAttributeAdjustment(
        string SourceId,
        string SourceName,
        string AttributeId,
        int Mode,
        decimal Value);

    private sealed record GlobalMbDisplay(
        string Title,
        string Category,
        string PreviewText,
        string MatchReason,
        int SortRank,
        IReadOnlyList<GlobalSearchLinkViewModel> Links);

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy;
        BusyRing.IsActive = busy;
        if (status is not null) SetStatus(status);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
    }

    internal Task ShowUpdateRecoveryNoticeAsync() => ShowErrorAsync(
        "更新未完成",
        "新版本替换失败，软件已自动恢复并重新启动旧版本。请稍后重试；详细原因已记录到本机更新日志中。");

    private static string SanitizeFileName(string value)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);
        string result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "xunxian_model" : result;
    }

    private static string MakeUniqueExportFileName(string fileName, HashSet<string> usedFileNames)
    {
        string safeName = SanitizeFileName(fileName);
        string name = System.IO.Path.GetFileNameWithoutExtension(safeName);
        string extension = System.IO.Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".dds";
        string candidate = name + extension;
        int suffix = 2;
        while (!usedFileNames.Add(candidate))
            candidate = $"{name}_{suffix++}{extension}";
        return candidate;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    int leftEnd = leftIndex;
                    int rightEnd = rightIndex;
                    while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                    while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;

                    int leftSignificant = leftIndex;
                    int rightSignificant = rightIndex;
                    while (leftSignificant < leftEnd - 1 && left[leftSignificant] == '0') leftSignificant++;
                    while (rightSignificant < rightEnd - 1 && right[rightSignificant] == '0') rightSignificant++;
                    int leftDigits = leftEnd - leftSignificant;
                    int rightDigits = rightEnd - rightSignificant;
                    if (leftDigits != rightDigits) return leftDigits.CompareTo(rightDigits);

                    int digitComparison = string.CompareOrdinal(
                        left, leftSignificant,
                        right, rightSignificant,
                        leftDigits);
                    if (digitComparison != 0) return digitComparison;

                    int runLengthComparison = (leftEnd - leftIndex).CompareTo(rightEnd - rightIndex);
                    if (runLengthComparison != 0) return runLengthComparison;
                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                int characterComparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0) return characterComparison;
                leftIndex++;
                rightIndex++;
            }
            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }
    }
}
