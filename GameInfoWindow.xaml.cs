using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow : Window
{
    private const string NoWanxiangSealText = "没有八卦封印";
    private readonly string? _resourceFolder;
    private IReadOnlyList<GameInfoCategory> _categories = Array.Empty<GameInfoCategory>();
    private GameInfoRepository? _repository;
    private GameInfoCategory? _selectedCategory;
    private IReadOnlyList<GameItemViewModel> _weaponItems = Array.Empty<GameItemViewModel>();
    private IReadOnlyList<GameItemViewModel> _equipmentItems = Array.Empty<GameItemViewModel>();
    private IReadOnlyList<GameWeaponEnhancementRow> _wuhunRows = Array.Empty<GameWeaponEnhancementRow>();
    private GameItemViewModel? _selectedWeapon;
    private GameItemViewModel? _selectedEquipment;
    private GameYinYangCatalog _yinYangCatalog = GameYinYangCatalog.Empty;
    private GameWanxiangCatalog _wanxiangCatalog = GameWanxiangCatalog.Empty;
    private readonly Dictionary<string, YinYangSelectionState> _yinYangStates =
        new(StringComparer.Ordinal);
    private string _activeYinYangSide = "阴";
    private bool _updatingWeaponFilters;
    private bool _updatingEquipmentFilters;
    private bool _updatingYinYang;
    private bool _updatingWanxiang;
    private bool _syncingWeaponSelection;
    private bool _syncingEquipmentSelection;
    private bool _syncingWashSelection;
    private bool _syncingManualWashSelection;
    private bool _syncingManualCandidateSelection;
    private bool _syncingFireSelection;
    private bool _loaded;
    private readonly Random _equipmentRandom = new();
    private readonly Dictionary<string, GameEquipmentSimulator> _equipmentSimulators =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _equipmentFireTimer;
    private GameEquipmentSimulator? _activeEquipmentSimulator;
    private GameEquipmentEnhancementInfo? _activeEquipmentEnhancement;
    private int _selectedWashSlotIndex;
    private int _selectedManualWashSlotIndex;
    private int _selectedFireId = 1;
    private int _equipmentFireProgress;
    private int _equipmentFireTarget;
    private int _equipmentFireSpeed = 1;
    private readonly Random _wanxiangRandom = new();
    private readonly GameWanxiangGemSeries?[] _wanxiangSlots = new GameWanxiangGemSeries?[8];
    private int _selectedWanxiangSlotIndex;
    private int _wanxiangLevelIndex;
    private int _wanxiangExp;

    public GameInfoWindow(string? resourceFolder = null)
    {
        _resourceFolder = resourceFolder;
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;

        _equipmentFireTimer = DispatcherQueue.CreateTimer();
        _equipmentFireTimer.Interval = TimeSpan.FromMilliseconds(20);
        _equipmentFireTimer.Tick += EquipmentFireTimer_Tick;
        Closed += (_, _) => _equipmentFireTimer.Stop();

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Xunxian.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int width = Math.Min(1560, Math.Max(1180, displayArea.WorkArea.Width - 70));
        int height = Math.Min(960, Math.Max(760, displayArea.WorkArea.Height - 70));
        AppWindow.Resize(new SizeInt32(width, height));
    }

    private async void GameInfoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LoadingState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            _repository = await Task.Run(() => GameInfoRepository.Load(_resourceFolder));
            _categories = _repository.Categories;
            _yinYangCatalog = _repository.YinYang;
            _wanxiangCatalog = _repository.Wanxiang;
            CategoryList.ItemsSource = _categories;
            if (_categories.Count > 0)
            {
                CategoryList.SelectedIndex = 0;
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            CategoryTitleText.Text = "游戏资料读取失败";
            CategoryDescriptionText.Text = exception.Message;
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingState.Visibility = Visibility.Collapsed;
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not GameInfoCategory category)
        {
            return;
        }

        _selectedCategory = category;
        CategoryTitleText.Text = category.Name;
        CategoryDescriptionText.Text = category.Description;
        CategoryCountText.Text = category.CountText;
        InfoSearchBox.Text = string.Empty;
        bool isWeapon = category.Id.Equals("weapon", StringComparison.Ordinal);
        bool isEquipment = category.Id.Equals("equipment", StringComparison.Ordinal);
        bool isYinYang = category.Id.Equals("yinyang", StringComparison.Ordinal);
        bool isWanxiang = category.Id.Equals("wanxiang", StringComparison.Ordinal);
        TimelineView.Visibility = isWeapon || isEquipment || isYinYang || isWanxiang
            ? Visibility.Collapsed
            : Visibility.Visible;
        WeaponView.Visibility = isWeapon ? Visibility.Visible : Visibility.Collapsed;
        EquipmentView.Visibility = isEquipment ? Visibility.Visible : Visibility.Collapsed;
        YinYangView.Visibility = isYinYang ? Visibility.Visible : Visibility.Collapsed;
        WanxiangView.Visibility = isWanxiang ? Visibility.Visible : Visibility.Collapsed;
        InfoSearchBox.Visibility = isYinYang || isWanxiang ? Visibility.Collapsed : Visibility.Visible;
        CategoryCountBadge.Visibility = isYinYang || isWanxiang ? Visibility.Collapsed : Visibility.Visible;
        InfoSearchBox.PlaceholderText = string.Empty;
        EmptyState.Visibility = Visibility.Collapsed;

        if (isWeapon)
        {
            ConfigureWeaponFilters(category);
        }
        else if (isEquipment)
        {
            ConfigureEquipmentFilters(category);
        }
        else if (isYinYang)
        {
            ConfigureYinYangView();
        }
        else if (isWanxiang)
        {
            ConfigureWanxiangView();
        }
        else
        {
            ApplyFilter();
        }
    }

    private void InfoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_selectedCategory is null)
        {
            return;
        }

        if (_selectedCategory.Id.Equals("weapon", StringComparison.Ordinal))
        {
            ApplyWeaponFilter();
            return;
        }

        if (_selectedCategory.Id.Equals("equipment", StringComparison.Ordinal))
        {
            ApplyEquipmentFilter();
            return;
        }

        if (_selectedCategory.Id.Equals("yinyang", StringComparison.Ordinal))
        {
            return;
        }

        if (_selectedCategory.Id.Equals("wanxiang", StringComparison.Ordinal))
        {
            return;
        }

        string query = InfoSearchBox.Text.Trim();
        var stages = new List<GameStageViewModel>();

        foreach (GameInfoStage stage in _selectedCategory.Stages)
        {
            IEnumerable<GameInfoRecord> records = stage.Records;
            if (!string.IsNullOrWhiteSpace(query))
            {
                records = records.Where(record => Matches(record, query));
            }

            GameItemViewModel[] items = records.Select(record => new GameItemViewModel(record)).ToArray();
            if (items.Length > 0)
            {
                stages.Add(new GameStageViewModel(stage, items));
            }
        }

        TimelineList.ItemsSource = stages;
        EmptyState.Visibility = stages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureWanxiangView()
    {
        if (_wanxiangCatalog.Levels.Count == 0 || _wanxiangCatalog.GemSeries.Count == 0)
        {
            return;
        }

        _updatingWanxiang = true;
        try
        {
            GameWanxiangNameOption[] guaOptions = _wanxiangCatalog.GuaNames
                .Select(name => new GameWanxiangNameOption(name))
                .ToArray();
            string? guaName = (WanxiangGuaBox.SelectedItem as GameWanxiangNameOption)?.Name;
            WanxiangGuaBox.ItemsSource = guaOptions;
            WanxiangGuaBox.SelectedItem = guaOptions.FirstOrDefault(option =>
                option.Name.Equals(guaName, StringComparison.Ordinal)) ?? guaOptions[0];

            GameWanxiangLevelState currentLevel = CurrentWanxiangLevel();
            GameWanxiangQualityOption[] qualities = _wanxiangCatalog.Levels
                .GroupBy(level => level.QualityIndex)
                .OrderBy(group => group.Key)
                .Select(group => new GameWanxiangQualityOption(group.Key, group.First().QualityName))
                .ToArray();
            WanxiangQualityBox.ItemsSource = qualities;
            WanxiangQualityBox.SelectedItem = qualities.First(option =>
                option.Index == currentLevel.QualityIndex);
            SetWanxiangSegmentOptions(currentLevel.QualityIndex, currentLevel.Segment);

            string selectedGuaName = (WanxiangGuaBox.SelectedItem as GameWanxiangNameOption)?.Name ??
                                     _wanxiangCatalog.GuaNames[0];
            SetWanxiangGemOptions(selectedGuaName);

            BitmapImage? emptySlot = GameItemViewModel.LoadBitmap(_wanxiangCatalog.EmptySlotPath);
            foreach (Image frame in WanxiangSlotFrames())
            {
                frame.Source = emptySlot;
            }

            BitmapImage? selectionFrame = GameItemViewModel.LoadBitmap(_wanxiangCatalog.SelectionFramePath);
            foreach (Image frame in WanxiangSlotSelectionFrames())
            {
                frame.Source = selectionFrame;
            }

            WanxiangInlayBackground.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.InlayBackgroundPath);
            WanxiangPlateSlotFrame.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.PlateSlotFramePath);
            WanxiangPlateIcon.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.Plates.FirstOrDefault()?.ImagePath);
            WanxiangWindowHeader.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.WindowHeaderPath);
            WanxiangWindowFoot.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.WindowFootPath);
            WanxiangWindowInner.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.WindowInnerPath);
            WanxiangWindowBackground.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.WindowBackgroundPath);
            WanxiangWindowBottom.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.WindowBottomPath);
            BitmapImage? upgradeButtonSprite = GameItemViewModel.LoadBitmap(_wanxiangCatalog.ButtonSpritePath);
            WanxiangFireButton.Tag = upgradeButtonSprite;
            WanxiangFairyButton.Tag = upgradeButtonSprite;
            WanxiangTooltipFrame.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.TooltipFramePath);
            WanxiangGuaStateFrame.Source = WanxiangTooltipFrame.Source;
            WanxiangLevelCenterBottom.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.LevelCenterBottomPath);
            WanxiangLevelCenterTop.Source = GameItemViewModel.LoadBitmap(_wanxiangCatalog.LevelCenterTopPath);
        }
        finally
        {
            _updatingWanxiang = false;
        }

        UpdateWanxiangView();
    }

    private void SetWanxiangGemOptions(string guaName)
    {
        GameWanxiangGemSeriesOption[] options = new[]
            {
                new GameWanxiangGemSeriesOption(null, "无", null)
            }
            .Concat(_wanxiangCatalog.GemSeries
                .Where(series => !series.IsBound)
                .OrderByDescending(series => series.Id)
                .Select(series => new GameWanxiangGemSeriesOption(
                    series,
                    series.Name,
                    series.Resolve(guaName)?.Record.ImagePath)))
            .ToArray();
        WanxiangGemBox.ItemsSource = options;
        WanxiangGemBox.SelectedItem = FindWanxiangGemOption(
            options,
            _wanxiangSlots[_selectedWanxiangSlotIndex]);
    }

    private void WanxiangGuaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingWanxiang || WanxiangGuaBox.SelectedItem is not GameWanxiangNameOption gua)
        {
            return;
        }

        _updatingWanxiang = true;
        try
        {
            SetWanxiangGemOptions(gua.Name);
        }
        finally
        {
            _updatingWanxiang = false;
        }

        UpdateWanxiangView();
    }

    private void WanxiangQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingWanxiang || WanxiangQualityBox.SelectedItem is not GameWanxiangQualityOption quality)
        {
            return;
        }

        _updatingWanxiang = true;
        try
        {
            GameWanxiangLevelState level = _wanxiangCatalog.Levels
                .First(candidate => candidate.QualityIndex == quality.Index);
            _wanxiangLevelIndex = FindWanxiangLevelIndex(level);
            _wanxiangExp = 0;
            SetWanxiangSegmentOptions(level.QualityIndex, level.Segment);
        }
        finally
        {
            _updatingWanxiang = false;
        }

        UpdateWanxiangView();
    }

    private void WanxiangSegmentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingWanxiang ||
            WanxiangQualityBox.SelectedItem is not GameWanxiangQualityOption quality ||
            WanxiangSegmentBox.SelectedItem is not GameWanxiangSegmentOption segment)
        {
            return;
        }

        GameWanxiangLevelState? level = _wanxiangCatalog.Levels.FirstOrDefault(candidate =>
            candidate.QualityIndex == quality.Index && candidate.Segment == segment.Value);
        if (level is null)
        {
            return;
        }

        _wanxiangLevelIndex = FindWanxiangLevelIndex(level);
        _wanxiangExp = 0;
        UpdateWanxiangView();
    }

    private void WanxiangSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            !int.TryParse(element.Tag?.ToString(), out int slotIndex) ||
            slotIndex < 0 || slotIndex >= _wanxiangSlots.Length)
        {
            return;
        }

        _selectedWanxiangSlotIndex = slotIndex;
        _updatingWanxiang = true;
        try
        {
            GameWanxiangGemSeriesOption[] options = (WanxiangGemBox.ItemsSource as IEnumerable<GameWanxiangGemSeriesOption>)
                ?.ToArray() ?? Array.Empty<GameWanxiangGemSeriesOption>();
            WanxiangGemBox.SelectedItem = FindWanxiangGemOption(options, _wanxiangSlots[slotIndex]);
        }
        finally
        {
            _updatingWanxiang = false;
        }

        UpdateWanxiangView();
    }

    private void WanxiangGemBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingWanxiang || WanxiangGemBox.SelectedItem is not GameWanxiangGemSeriesOption option)
        {
            return;
        }

        _wanxiangSlots[_selectedWanxiangSlotIndex] = option.Series;
        UpdateWanxiangView();
    }

    private void WanxiangRemoveGemButton_Click(object sender, RoutedEventArgs e)
    {
        _wanxiangSlots[_selectedWanxiangSlotIndex] = null;
        _updatingWanxiang = true;
        try
        {
            GameWanxiangGemSeriesOption[] options = (WanxiangGemBox.ItemsSource as IEnumerable<GameWanxiangGemSeriesOption>)
                ?.ToArray() ?? Array.Empty<GameWanxiangGemSeriesOption>();
            WanxiangGemBox.SelectedItem = FindWanxiangGemOption(options, null);
        }
        finally
        {
            _updatingWanxiang = false;
        }

        UpdateWanxiangView();
    }

    private void WanxiangFireButton_Click(object sender, RoutedEventArgs e) => ApplyWanxiangUpgrade(1);

    private void WanxiangFairyButton_Click(object sender, RoutedEventArgs e) => ApplyWanxiangUpgrade(2);

    private void ApplyWanxiangUpgrade(int methodKind)
    {
        if (_wanxiangCatalog.Levels.Count == 0 || _wanxiangLevelIndex >= _wanxiangCatalog.Levels.Count - 1)
        {
            return;
        }

        GameWanxiangLevelState level = CurrentWanxiangLevel();
        GameWanxiangUpgradeMethod? method = _wanxiangCatalog.ResolveUpgradeMethod(
            level.QualityIndex,
            methodKind);
        if (method is null)
        {
            return;
        }

        _wanxiangExp += method.RollExp(_wanxiangRandom);
        while (_wanxiangLevelIndex < _wanxiangCatalog.Levels.Count - 1)
        {
            level = CurrentWanxiangLevel();
            if (_wanxiangExp < level.MaxExp)
            {
                break;
            }

            _wanxiangExp -= level.MaxExp;
            _wanxiangLevelIndex++;
        }

        if (_wanxiangLevelIndex >= _wanxiangCatalog.Levels.Count - 1)
        {
            _wanxiangExp = 0;
        }

        SyncWanxiangLevelSelectors();
        UpdateWanxiangView();
    }

    private void UpdateWanxiangView()
    {
        if (_wanxiangCatalog.Levels.Count == 0)
        {
            return;
        }

        GameWanxiangLevelState level = CurrentWanxiangLevel();
        string guaName = (WanxiangGuaBox.SelectedItem as GameWanxiangNameOption)?.Name ??
                         _wanxiangCatalog.GuaNames.FirstOrDefault() ?? string.Empty;
        Image[] slotImages = WanxiangSlotImages();
        Image[] selectionFrames = WanxiangSlotSelectionFrames();
        var equippedGems = new List<GameWanxiangGem>();
        int stoneCount = 0;

        for (int index = 0; index < _wanxiangSlots.Length; index++)
        {
            GameWanxiangGem? gem = _wanxiangSlots[index]?.Resolve(guaName);
            slotImages[index].Source = GameItemViewModel.LoadBitmap(gem?.Record.ImagePath);
            slotImages[index].Visibility = gem is null ? Visibility.Collapsed : Visibility.Visible;
            selectionFrames[index].Visibility = index == _selectedWanxiangSlotIndex
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (gem is not null)
            {
                stoneCount++;
                equippedGems.Add(gem);
            }
        }

        GameWanxiangGem? selectedGem = _wanxiangSlots[_selectedWanxiangSlotIndex]?.Resolve(guaName);
        WanxiangSelectedGemDetailHost.Content = CreateWanxiangGemContent(selectedGem);
        WanxiangRemoveGemButton.IsEnabled = selectedGem is not null;

        GameWanxiangState? guaState = level.ResolveStates(stoneCount, guaName).FirstOrDefault();
        WanxiangGuaText.Text = stoneCount >= 8 ? guaName : _wanxiangCatalog.NoGuaText;
        WanxiangLevelText.Text = level.DisplayName;
        bool isFinalLevel = _wanxiangLevelIndex >= _wanxiangCatalog.Levels.Count - 1;
        string expText = isFinalLevel
            ? "炼卦值：---/---"
            : $"炼卦值：{_wanxiangExp}/{level.MaxExp}";
        WanxiangExpText.Text = expText;

        BitmapImage? guaStateIcon = GameItemViewModel.LoadBitmap(guaState?.IconPath);
        WanxiangGuaStateIcon.Source = guaStateIcon;
        WanxiangGuaStateIcon.Visibility = guaStateIcon is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        WanxiangGuaStateHost.Content = CreateWanxiangRichText(
            string.IsNullOrWhiteSpace(guaState?.Description)
                ? NoWanxiangSealText
                : guaState.Description);

        GameInfoRecord? plate = _wanxiangCatalog.Plates.FirstOrDefault();
        if (plate is not null)
        {
            IReadOnlyList<string> attributes = GameWanxiangAttributeAggregator.Aggregate(equippedGems)
                .Select(attribute => attribute.Text)
                .ToArray();
            string[] gemIconPaths = equippedGems
                .Select(gem => gem.Record.ImagePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray();
            var overlay = new GameWanxiangTooltipOverlay(
                stoneCount >= 8 ? $"{guaName}卦" : "无",
                level.ClientDisplayText,
                expText,
                attributes,
                gemIconPaths,
                _wanxiangCatalog.PlateSlotFramePath);
            WanxiangTooltipHost.Content = GameItemViewModel.CreateWanxiangTooltipContent(plate, overlay);
        }

        UpdateWanxiangLevelArt(level);
        UpdateWanxiangUpgradeMethod(level, 1, WanxiangFireButton, isFinalLevel);
        UpdateWanxiangUpgradeMethod(level, 2, WanxiangFairyButton, isFinalLevel);
    }

    private void UpdateWanxiangUpgradeMethod(
        GameWanxiangLevelState level,
        int methodKind,
        Button button,
        bool isFinalLevel)
    {
        GameWanxiangUpgradeMethod? method = _wanxiangCatalog.ResolveUpgradeMethod(
            level.QualityIndex,
            methodKind);
        button.IsEnabled = method is not null && !isFinalLevel;
        ToolTipService.SetToolTip(
            button,
            method is null
                ? null
                : CreateWanxiangRichText(
                    (methodKind == 1
                        ? _wanxiangCatalog.FireUpgradeTipFormat
                        : _wanxiangCatalog.FairyUpgradeTipFormat)
                    .Replace("%s", method.Material.DisplayName, StringComparison.Ordinal)
                    .Replace("%d", method.Material.Count.ToString(), StringComparison.Ordinal)));
    }

    private void UpdateWanxiangLevelArt(GameWanxiangLevelState level)
    {
        WanxiangLevelArtCanvas.Children.Clear();
        AddWanxiangLevelImage(_wanxiangCatalog.LevelBasePath);

        int currentOffset = level.QualityIndex * 8;
        int previousOffset = level.QualityIndex > 1 ? (level.QualityIndex - 1) * 8 : 0;
        for (int petal = 1; petal <= 8; petal++)
        {
            int imageIndex = 0;
            if (petal <= level.Segment)
            {
                imageIndex = currentOffset + petal;
            }
            else if (level.Segment > 0 && level.Segment < 8 && level.QualityIndex > 0)
            {
                imageIndex = previousOffset + petal;
            }

            if (imageIndex > 0 && _wanxiangCatalog.LevelPetalPaths.TryGetValue(imageIndex, out string? path))
            {
                AddWanxiangLevelImage(path);
            }
        }

        WanxiangLevelExpFill.Source = _wanxiangCatalog.LevelExpPaths.TryGetValue(
            level.QualityIndex,
            out string? expPath)
            ? GameItemViewModel.LoadBitmap(expPath)
            : null;
        bool isFinalLevel = _wanxiangLevelIndex >= _wanxiangCatalog.Levels.Count - 1;
        double progress = isFinalLevel
            ? 1d
            : level.MaxExp > 0 ? Math.Clamp((double)_wanxiangExp / level.MaxExp, 0d, 1d) : 0d;
        double fillHeight = Math.Floor(progress * 48d);
        WanxiangLevelExpClip.Rect = new Windows.Foundation.Rect(
            8,
            58 - fillHeight,
            48,
            fillHeight);
    }

    private void AddWanxiangLevelImage(string? path)
    {
        BitmapImage? source = GameItemViewModel.LoadBitmap(path);
        if (source is null)
        {
            return;
        }

        var image = new Image
        {
            Source = source,
            Width = 512,
            Height = 512,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(image, -100);
        Canvas.SetTop(image, -120);
        WanxiangLevelArtCanvas.Children.Add(image);
    }

    private void SetWanxiangSegmentOptions(int qualityIndex, int preferredSegment)
    {
        GameWanxiangSegmentOption[] segments = _wanxiangCatalog.Levels
            .Where(level => level.QualityIndex == qualityIndex)
            .OrderBy(level => level.Segment)
            .Select(level => new GameWanxiangSegmentOption(level.Segment, $"{level.Segment}段"))
            .ToArray();
        WanxiangSegmentBox.ItemsSource = segments;
        WanxiangSegmentBox.SelectedItem = segments.FirstOrDefault(segment => segment.Value == preferredSegment) ??
                                          segments.FirstOrDefault();
    }

    private void SyncWanxiangLevelSelectors()
    {
        GameWanxiangLevelState level = CurrentWanxiangLevel();
        _updatingWanxiang = true;
        try
        {
            if (WanxiangQualityBox.ItemsSource is IEnumerable<GameWanxiangQualityOption> qualities)
            {
                WanxiangQualityBox.SelectedItem = qualities.FirstOrDefault(quality =>
                    quality.Index == level.QualityIndex);
            }
            SetWanxiangSegmentOptions(level.QualityIndex, level.Segment);
        }
        finally
        {
            _updatingWanxiang = false;
        }
    }

    private GameWanxiangLevelState CurrentWanxiangLevel()
    {
        _wanxiangLevelIndex = Math.Clamp(_wanxiangLevelIndex, 0, Math.Max(0, _wanxiangCatalog.Levels.Count - 1));
        return _wanxiangCatalog.Levels[_wanxiangLevelIndex];
    }

    private int FindWanxiangLevelIndex(GameWanxiangLevelState level)
    {
        for (int index = 0; index < _wanxiangCatalog.Levels.Count; index++)
        {
            if (_wanxiangCatalog.Levels[index].Id == level.Id)
            {
                return index;
            }
        }

        return 0;
    }

    private static GameWanxiangGemSeriesOption? FindWanxiangGemOption(
        IEnumerable<GameWanxiangGemSeriesOption> options,
        GameWanxiangGemSeries? series) =>
        options.FirstOrDefault(option => option.Series?.Id == series?.Id);

    private static UIElement CreateWanxiangGemContent(GameWanxiangGem? gem)
    {
        if (gem is null)
        {
            return new TextBlock
            {
                Text = "无",
                Foreground = (Brush)Application.Current.Resources["MutedTextBrush"]
            };
        }

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = gem.Record.Name,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateWanxiangRichText(gem.AttributeRaw));
        return panel;
    }

    private static RichTextBlock CreateWanxiangRichText(string raw)
    {
        var block = new RichTextBlock
        {
            FontFamily = new FontFamily("ms-appx:///Assets/Fonts/fzlth_gb18030.ttf#FZLanTingHei-R-GB18030"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = false
        };
        var paragraph = new Paragraph();
        foreach (GameRichTextSpan span in GameRichTextParser.Parse(raw))
        {
            paragraph.Inlines.Add(new Run
            {
                Text = span.Text,
                Foreground = GameItemViewModel.BrushFromColor(span.Color)
            });
        }
        block.Blocks.Add(paragraph);
        return block;
    }

    private Image[] WanxiangSlotSelectionFrames() =>
    new[]
    {
        WanxiangSlot0Selection,
        WanxiangSlot1Selection,
        WanxiangSlot2Selection,
        WanxiangSlot3Selection,
        WanxiangSlot4Selection,
        WanxiangSlot5Selection,
        WanxiangSlot6Selection,
        WanxiangSlot7Selection
    };

    private Image[] WanxiangSlotFrames() =>
    new[]
    {
        WanxiangSlot0Frame,
        WanxiangSlot1Frame,
        WanxiangSlot2Frame,
        WanxiangSlot3Frame,
        WanxiangSlot4Frame,
        WanxiangSlot5Frame,
        WanxiangSlot6Frame,
        WanxiangSlot7Frame
    };

    private Image[] WanxiangSlotImages() =>
    new[]
    {
        WanxiangSlot0Image,
        WanxiangSlot1Image,
        WanxiangSlot2Image,
        WanxiangSlot3Image,
        WanxiangSlot4Image,
        WanxiangSlot5Image,
        WanxiangSlot6Image,
        WanxiangSlot7Image
    };

    private void ConfigureYinYangView()
    {
        _updatingYinYang = true;
        try
        {
            if (_yinYangCatalog.Jades.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }

            GameYinYangGroupOptionViewModel[] enchantOptions = _yinYangCatalog.EnchantGroups
                .Select(group => new GameYinYangGroupOptionViewModel(group))
                .ToArray();
            GameYinYangGroupOptionViewModel[] spiritOptions = _yinYangCatalog.SpiritGroups
                .Select(group => new GameYinYangGroupOptionViewModel(group))
                .ToArray();
            GameYinYangGroupOptionViewModel[] soulOptions = _yinYangCatalog.SoulGroups
                .Select(group => new GameYinYangGroupOptionViewModel(group))
                .ToArray();

            ConfigureYinYangSide("阴", YinYangControls("阴"), enchantOptions, spiritOptions, soulOptions);
            ConfigureYinYangSide("阳", YinYangControls("阳"), enchantOptions, spiritOptions, soulOptions);

            string framePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Client", "frm_tip.png");
            YinYangYinTooltipFrame.Source = GameItemViewModel.LoadBitmap(framePath);
            YinYangYangTooltipFrame.Source = YinYangYinTooltipFrame.Source;

            GameYinYangJade? yinJade = _yinYangCatalog.Jades.FirstOrDefault(jade =>
                jade.Side.Equals("阴", StringComparison.Ordinal));
            GameYinYangJade? yangJade = _yinYangCatalog.Jades.FirstOrDefault(jade =>
                jade.Side.Equals("阳", StringComparison.Ordinal));
            YinYangYinEditorTitle.Text = YinYangYinPreviewTitle.Text = yinJade?.Record.Name ?? "曲灵玉·阴";
            YinYangYangEditorTitle.Text = YinYangYangPreviewTitle.Text = yangJade?.Record.Name ?? "曲灵玉·阳";
        }
        finally
        {
            _updatingYinYang = false;
        }

        UpdateYinYangPreview();
    }

    private void ConfigureYinYangSide(
        string side,
        YinYangSideControls controls,
        IReadOnlyList<GameYinYangGroupOptionViewModel> enchantOptions,
        IReadOnlyList<GameYinYangGroupOptionViewModel> spiritOptions,
        IReadOnlyList<GameYinYangGroupOptionViewModel> soulOptions)
    {
        YinYangSelectionState state = YinYangState(side);
        state.EnchantGroup1Key ??= enchantOptions.FirstOrDefault()?.Key;
        state.EnchantGroup2Key ??= enchantOptions.FirstOrDefault()?.Key;
        state.SpiritGroupKey ??= spiritOptions.FirstOrDefault()?.Key;
        state.SoulGroupKey ??= soulOptions.FirstOrDefault()?.Key;
        state.EnchantQuality1 = ResolveQualityId(state.EnchantQuality1);
        state.EnchantQuality2 = ResolveQualityId(state.EnchantQuality2);
        state.SpiritQuality = ResolveQualityId(state.SpiritQuality);
        state.SoulQuality = ResolveQualityId(state.SoulQuality);

        controls.EnchantGroup1.ItemsSource = enchantOptions;
        controls.EnchantGroup2.ItemsSource = enchantOptions;
        controls.SpiritGroup.ItemsSource = spiritOptions;
        controls.SoulGroup.ItemsSource = soulOptions;
        controls.EnchantQuality1.ItemsSource = _yinYangCatalog.Qualities;
        controls.EnchantQuality2.ItemsSource = _yinYangCatalog.Qualities;
        controls.SpiritQuality.ItemsSource = _yinYangCatalog.Qualities;
        controls.SoulQuality.ItemsSource = _yinYangCatalog.Qualities;

        controls.EnchantGroup1.SelectedItem = FindGroupOption(enchantOptions, state.EnchantGroup1Key);
        controls.EnchantGroup2.SelectedItem = FindGroupOption(enchantOptions, state.EnchantGroup2Key);
        controls.SpiritGroup.SelectedItem = FindGroupOption(spiritOptions, state.SpiritGroupKey);
        controls.SoulGroup.SelectedItem = FindGroupOption(soulOptions, state.SoulGroupKey);
        controls.EnchantQuality1.SelectedItem = FindQuality(state.EnchantQuality1);
        controls.EnchantQuality2.SelectedItem = FindQuality(state.EnchantQuality2);
        controls.SpiritQuality.SelectedItem = FindQuality(state.SpiritQuality);
        controls.SoulQuality.SelectedItem = FindQuality(state.SoulQuality);

        SetYinYangAttributeSource(controls.EnchantAttribute1, "enchant", state.EnchantGroup1Key, 1, state.EnchantQuality1, state.EnchantAttribute1Name);
        SetYinYangAttributeSource(controls.EnchantAttribute2, "enchant", state.EnchantGroup2Key, 2, state.EnchantQuality2, state.EnchantAttribute2Name);
        SetYinYangAttributeSource(controls.SpiritAttribute, "spirit", state.SpiritGroupKey, 3, state.SpiritQuality, state.SpiritAttributeName);
        SetYinYangAttributeSource(controls.SoulAttribute, "soul", state.SoulGroupKey, 0, state.SoulQuality, state.SoulAttributeName);
    }

    private void YinYangJadeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingYinYang || sender is not FrameworkElement { Tag: string side } ||
            !_yinYangCatalog.Jades.Any(jade => jade.Side.Equals(side, StringComparison.Ordinal)))
        {
            return;
        }

        _activeYinYangSide = side;
        ConfigureYinYangView();
    }

    private void YinYangEnchantGroup1Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.EnchantGroup1Key = SelectedGroupKey((ComboBox)sender);
        state.EnchantAttribute1Name = null;
        SetYinYangAttributeSource(controls.EnchantAttribute1, "enchant", state.EnchantGroup1Key, 1, state.EnchantQuality1, null);
        UpdateYinYangPreview();
    }

    private void YinYangEnchantGroup2Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.EnchantGroup2Key = SelectedGroupKey((ComboBox)sender);
        state.EnchantAttribute2Name = null;
        SetYinYangAttributeSource(controls.EnchantAttribute2, "enchant", state.EnchantGroup2Key, 2, state.EnchantQuality2, null);
        UpdateYinYangPreview();
    }

    private void YinYangSpiritGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.SpiritGroupKey = SelectedGroupKey((ComboBox)sender);
        state.SpiritAttributeName = null;
        SetYinYangAttributeSource(controls.SpiritAttribute, "spirit", state.SpiritGroupKey, 3, state.SpiritQuality, null);
        UpdateYinYangPreview();
    }

    private void YinYangSoulGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.SoulGroupKey = SelectedGroupKey((ComboBox)sender);
        state.SoulAttributeName = null;
        SetYinYangAttributeSource(controls.SoulAttribute, "soul", state.SoulGroupKey, 0, state.SoulQuality, null);
        UpdateYinYangPreview();
    }

    private void YinYangEnchantQuality1Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.EnchantQuality1 = SelectedQualityId((ComboBox)sender);
        SetYinYangAttributeSource(controls.EnchantAttribute1, "enchant", state.EnchantGroup1Key, 1, state.EnchantQuality1, state.EnchantAttribute1Name);
        UpdateYinYangPreview();
    }

    private void YinYangEnchantQuality2Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.EnchantQuality2 = SelectedQualityId((ComboBox)sender);
        SetYinYangAttributeSource(controls.EnchantAttribute2, "enchant", state.EnchantGroup2Key, 2, state.EnchantQuality2, state.EnchantAttribute2Name);
        UpdateYinYangPreview();
    }

    private void YinYangSpiritQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.SpiritQuality = SelectedQualityId((ComboBox)sender);
        SetYinYangAttributeSource(controls.SpiritAttribute, "spirit", state.SpiritGroupKey, 3, state.SpiritQuality, state.SpiritAttributeName);
        UpdateYinYangPreview();
    }

    private void YinYangSoulQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        string side = YinYangSide(sender);
        YinYangSideControls controls = YinYangControls(side);
        YinYangSelectionState state = YinYangState(side);
        state.SoulQuality = SelectedQualityId((ComboBox)sender);
        SetYinYangAttributeSource(controls.SoulAttribute, "soul", state.SoulGroupKey, 0, state.SoulQuality, state.SoulAttributeName);
        UpdateYinYangPreview();
    }

    private void YinYangEnchantAttribute1Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        YinYangState(YinYangSide(sender)).EnchantAttribute1Name =
            (((ComboBox)sender).SelectedItem as GameYinYangAttribute)?.Name;
        UpdateYinYangPreview();
    }

    private void YinYangEnchantAttribute2Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        YinYangState(YinYangSide(sender)).EnchantAttribute2Name =
            (((ComboBox)sender).SelectedItem as GameYinYangAttribute)?.Name;
        UpdateYinYangPreview();
    }

    private void YinYangSpiritAttributeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        YinYangState(YinYangSide(sender)).SpiritAttributeName =
            (((ComboBox)sender).SelectedItem as GameYinYangAttribute)?.Name;
        UpdateYinYangPreview();
    }

    private void YinYangSoulAttributeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingYinYang) return;
        YinYangState(YinYangSide(sender)).SoulAttributeName =
            (((ComboBox)sender).SelectedItem as GameYinYangAttribute)?.Name;
        UpdateYinYangPreview();
    }

    private void YinYangExtractCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked == true)
        {
            YinYangOperationStatusText.Text = "已选择要提取的属性。";
        }
    }

    private void YinYangExtractEnchantButton_Click(object sender, RoutedEventArgs e)
    {
        YinYangSelectionState state = ActiveYinYangState();
        if (YinYangExtractEnchantCheckBox.IsChecked != true)
        {
            YinYangOperationStatusText.Text = "请先选择“提取附魔属性”。";
            return;
        }

        state.EnchantAttribute1Name = null;
        state.EnchantAttribute2Name = null;
        YinYangExtractEnchantCheckBox.IsChecked = false;
        YinYangOperationStatusText.Text = "附魔属性已提取，曲灵玉恢复为未激活。";
        ConfigureYinYangView();
    }

    private void YinYangExtractSpiritButton_Click(object sender, RoutedEventArgs e)
    {
        YinYangSelectionState state = ActiveYinYangState();
        if (YinYangExtractSpiritCheckBox.IsChecked != true)
        {
            YinYangOperationStatusText.Text = "请先选择“提取附灵属性”。";
            return;
        }

        state.SpiritAttributeName = null;
        YinYangExtractSpiritCheckBox.IsChecked = false;
        YinYangOperationStatusText.Text = "附灵属性已提取，曲灵玉恢复为未激活。";
        ConfigureYinYangView();
    }

    private void SetYinYangAttributeSource(
        ComboBox attributeBox,
        string role,
        string? groupKey,
        int slot,
        int qualityId,
        string? preferredName)
    {
        GameYinYangAffixGroup? group = FindYinYangGroup(role, groupKey);
        GameYinYangAttribute[] options = group?.Attributes
            .Where(attribute => attribute.QualityId == qualityId && (slot == 0 || attribute.Slot == slot))
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Select(attributes => attributes.First())
            .ToArray() ?? Array.Empty<GameYinYangAttribute>();
        attributeBox.ItemsSource = options;
        GameYinYangAttribute? selected = options.FirstOrDefault(attribute =>
            !string.IsNullOrWhiteSpace(preferredName) &&
            attribute.Name.Equals(preferredName, StringComparison.Ordinal));
        attributeBox.SelectedItem = selected;
    }

    private GameYinYangAffixGroup? FindYinYangGroup(string role, string? groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return null;
        }

        IEnumerable<GameYinYangAffixGroup> groups = role switch
        {
            "enchant" => _yinYangCatalog.EnchantGroups,
            "spirit" => _yinYangCatalog.SpiritGroups,
            "soul" => _yinYangCatalog.SoulGroups,
            _ => Array.Empty<GameYinYangAffixGroup>()
        };
        return groups.FirstOrDefault(group => YinYangGroupKey(group).Equals(groupKey, StringComparison.Ordinal));
    }

    private static string YinYangSide(object sender) =>
        sender is FrameworkElement element && element.Name.StartsWith("YinYangYang", StringComparison.Ordinal)
            ? "阳"
            : "阴";

    private YinYangSideControls YinYangControls(string side) =>
        side.Equals("阳", StringComparison.Ordinal)
            ? new YinYangSideControls(
                YinYangYangEnchantGroup1Box,
                YinYangYangEnchantGroup2Box,
                YinYangYangSpiritGroupBox,
                YinYangYangSoulGroupBox,
                YinYangYangEnchantQuality1Box,
                YinYangYangEnchantQuality2Box,
                YinYangYangSpiritQualityBox,
                YinYangYangSoulQualityBox,
                YinYangYangEnchantAttribute1Box,
                YinYangYangEnchantAttribute2Box,
                YinYangYangSpiritAttributeBox,
                YinYangYangSoulAttributeBox)
            : new YinYangSideControls(
                YinYangEnchantGroup1Box,
                YinYangEnchantGroup2Box,
                YinYangSpiritGroupBox,
                YinYangSoulGroupBox,
                YinYangEnchantQuality1Box,
                YinYangEnchantQuality2Box,
                YinYangSpiritQualityBox,
                YinYangSoulQualityBox,
                YinYangEnchantAttribute1Box,
                YinYangEnchantAttribute2Box,
                YinYangSpiritAttributeBox,
                YinYangSoulAttributeBox);

    private YinYangSelectionState YinYangState(string side)
    {
        if (!_yinYangStates.TryGetValue(side, out YinYangSelectionState? state))
        {
            state = new YinYangSelectionState();
            _yinYangStates[side] = state;
        }

        return state;
    }

    private YinYangSelectionState ActiveYinYangState()
    {
        return YinYangState(_activeYinYangSide);
    }

    private void UpdateYinYangPreview()
    {
        UpdateYinYangPreview("阴", YinYangYinTooltipHost);
        UpdateYinYangPreview("阳", YinYangYangTooltipHost);
    }

    private void UpdateYinYangPreview(string side, ContentPresenter tooltipHost)
    {
        if (_yinYangCatalog.Jades.Count == 0 ||
            !_yinYangStates.TryGetValue(side, out YinYangSelectionState? state))
        {
            return;
        }

        GameYinYangJade? jade = _yinYangCatalog.Jades.FirstOrDefault(item =>
            item.Side.Equals(side, StringComparison.Ordinal));
        if (jade is null)
        {
            tooltipHost.Content = null;
            return;
        }

        GameYinYangAttribute? enchant1 = FindYinYangAttribute("enchant", state.EnchantGroup1Key, 1, state.EnchantQuality1, state.EnchantAttribute1Name);
        GameYinYangAttribute? enchant2 = FindYinYangAttribute("enchant", state.EnchantGroup2Key, 2, state.EnchantQuality2, state.EnchantAttribute2Name);
        GameYinYangAttribute? spirit = FindYinYangAttribute("spirit", state.SpiritGroupKey, 3, state.SpiritQuality, state.SpiritAttributeName);
        GameYinYangAttribute? soul = FindYinYangAttribute("soul", state.SoulGroupKey, 0, state.SoulQuality, state.SoulAttributeName);

        var applied = new List<GameTooltipSupplementLine>();
        if (enchant1 is not null) applied.Add(new GameTooltipSupplementLine("附魔属性", enchant1.Text));
        if (enchant2 is not null) applied.Add(new GameTooltipSupplementLine("附魔属性", enchant2.Text));
        if (spirit is not null) applied.Add(new GameTooltipSupplementLine("附灵属性", spirit.Text));
        if (soul is not null) applied.Add(new GameTooltipSupplementLine($"{soul.QualityName}附魂", soul.Text));

        var activeRoles = new HashSet<string>(StringComparer.Ordinal);
        if (enchant1 is not null || enchant2 is not null) activeRoles.Add("enchant");
        if (spirit is not null) activeRoles.Add("spirit");
        if (soul is not null) activeRoles.Add("soul");

        GameTooltipHintLine[] emptyAttributeHints = _yinYangCatalog.EmptyAttributeHints
            .Where(hint => !activeRoles.Contains(hint.Role))
            .Select(hint => new GameTooltipHintLine(hint.Text))
            .ToArray();

        tooltipHost.Content = GameItemViewModel.CreateTooltipContent(
            jade.Record,
            supplementLines: applied,
            hintLines: emptyAttributeHints,
            yinYangLayout: true);
    }

    private GameYinYangAttribute? FindYinYangAttribute(
        string role,
        string? groupKey,
        int slot,
        int qualityId,
        string? attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return null;
        }

        return FindYinYangGroup(role, groupKey)?.Attributes.FirstOrDefault(attribute =>
            attribute.QualityId == qualityId &&
            (slot == 0 || attribute.Slot == slot) &&
            attribute.Name.Equals(attributeName, StringComparison.Ordinal));
    }

    private static string YinYangGroupKey(GameYinYangAffixGroup group) =>
        string.Concat(group.Role, ":", group.Prefix);

    private static string? SelectedGroupKey(ComboBox box) =>
        (box.SelectedItem as GameYinYangGroupOptionViewModel)?.Key;

    private static int SelectedQualityId(ComboBox box) =>
        (box.SelectedItem as GameYinYangQuality)?.Id ?? 1;

    private static int ResolveQualityId(int qualityId) =>
        qualityId is >= 1 and <= 5 ? qualityId : 1;

    private GameYinYangQuality? FindQuality(int id) =>
        _yinYangCatalog.Qualities.FirstOrDefault(quality => quality.Id == id);

    private static GameYinYangGroupOptionViewModel? FindGroupOption(
        IReadOnlyList<GameYinYangGroupOptionViewModel> options,
        string? key) =>
        options.FirstOrDefault(option => option.Key.Equals(key, StringComparison.Ordinal));

    private sealed record YinYangSideControls(
        ComboBox EnchantGroup1,
        ComboBox EnchantGroup2,
        ComboBox SpiritGroup,
        ComboBox SoulGroup,
        ComboBox EnchantQuality1,
        ComboBox EnchantQuality2,
        ComboBox SpiritQuality,
        ComboBox SoulQuality,
        ComboBox EnchantAttribute1,
        ComboBox EnchantAttribute2,
        ComboBox SpiritAttribute,
        ComboBox SoulAttribute);

    private sealed class YinYangSelectionState
    {
        public string? EnchantGroup1Key { get; set; }
        public string? EnchantGroup2Key { get; set; }
        public string? SpiritGroupKey { get; set; }
        public string? SoulGroupKey { get; set; }
        public int EnchantQuality1 { get; set; } = 1;
        public int EnchantQuality2 { get; set; } = 1;
        public int SpiritQuality { get; set; } = 1;
        public int SoulQuality { get; set; } = 1;
        public string? EnchantAttribute1Name { get; set; }
        public string? EnchantAttribute2Name { get; set; }
        public string? SpiritAttributeName { get; set; }
        public string? SoulAttributeName { get; set; }
    }

    private void ConfigureEquipmentFilters(GameInfoCategory category)
    {
        _updatingEquipmentFilters = true;
        try
        {
            GameInfoRecord[] records = category.Stages
                .SelectMany(stage => stage.Records)
                .ToArray();

            GameFilterOption[] stages = new[] { new GameFilterOption(string.Empty, "全部阶段") }
                .Concat(category.Stages
                    .Select(stage => new GameFilterOption(stage.Id, stage.Name)))
                .ToArray();
            GameFilterOption[] professions = new[] { new GameFilterOption(string.Empty, "全部职业") }
                .Concat(records
                    .Select(GetEquipmentProfession)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(EquipmentProfessionOrder)
                    .ThenBy(value => value, StringComparer.CurrentCulture)
                    .Select(value => new GameFilterOption(value, value)))
                .ToArray();
            GameFilterOption[] parts = new[] { new GameFilterOption(string.Empty, "全部部位") }
                .Concat(records
                    .Select(GetEquipmentPart)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(EquipmentPartOrder)
                    .ThenBy(value => value, StringComparer.CurrentCulture)
                    .Select(value => new GameFilterOption(value, value)))
                .ToArray();

            EquipmentStageBox.ItemsSource = stages;
            EquipmentProfessionBox.ItemsSource = professions;
            EquipmentPartBox.ItemsSource = parts;
            EquipmentStageBox.SelectedIndex = 0;
            EquipmentProfessionBox.SelectedIndex = 0;
            EquipmentPartBox.SelectedIndex = 0;

            EquipmentProgressionSummaryText.Text = string.Empty;
        }
        finally
        {
            _updatingEquipmentFilters = false;
        }

        ApplyEquipmentFilter();
    }

    private void ApplyEquipmentFilter()
    {
        if (_selectedCategory is null ||
            !_selectedCategory.Id.Equals("equipment", StringComparison.Ordinal))
        {
            return;
        }

        string selectedId = _selectedEquipment?.Record.Id ?? string.Empty;
        string stage = (EquipmentStageBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string profession = (EquipmentProfessionBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string part = (EquipmentPartBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string query = InfoSearchBox.Text.Trim();

        IEnumerable<GameInfoRecord> records = _selectedCategory.Stages
            .SelectMany(stageInfo => stageInfo.Records)
            .Where(record => string.IsNullOrWhiteSpace(stage) ||
                             _selectedCategory.Stages.Any(stageInfo =>
                                 stageInfo.Id.Equals(stage, StringComparison.Ordinal) &&
                                 stageInfo.Records.Contains(record)))
            .Where(record => string.IsNullOrWhiteSpace(profession) ||
                             string.Equals(GetEquipmentProfession(record), profession, StringComparison.Ordinal))
            .Where(record => string.IsNullOrWhiteSpace(part) ||
                             string.Equals(GetEquipmentPart(record), part, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(query))
        {
            records = records.Where(record => Matches(record, query) ||
                                              (FindEquipmentStage(_selectedCategory, record)?.Name ?? string.Empty)
                                                  .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              GetEquipmentProfession(record).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              GetEquipmentPart(record).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _equipmentItems = records
            .OrderBy(record => EquipmentStageOrder(_selectedCategory, record))
            .ThenBy(record => EquipmentPartOrder(GetEquipmentPart(record)))
            .ThenBy(record => GetEquipmentProfession(record), StringComparer.CurrentCulture)
            .ThenBy(record => record.Name, StringComparer.CurrentCulture)
            .Select(record => new GameItemViewModel(
                record,
                FindEquipmentStage(_selectedCategory, record)?.Name))
            .ToArray();

        _syncingEquipmentSelection = true;
        try
        {
            EquipmentList.ItemsSource = _equipmentItems;
            GameItemViewModel? selection = _equipmentItems.FirstOrDefault(item =>
                                                       item.Record.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                                                   ?? _equipmentItems.FirstOrDefault();
            EquipmentList.SelectedItem = selection;
            _selectedEquipment = selection;
        }
        finally
        {
            _syncingEquipmentSelection = false;
        }

        EquipmentResultText.Text = $"{_equipmentItems.Count:N0} 件";
        EquipmentListHintText.Text = $"{_equipmentItems.Count:N0} 件";
        EquipmentEmptyState.Visibility = _equipmentItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectedEquipment();
    }

    private void EquipmentFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingEquipmentFilters)
        {
            ApplyEquipmentFilter();
        }
    }

    private void EquipmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingEquipmentSelection && EquipmentList.SelectedItem is GameItemViewModel item)
        {
            _selectedEquipment = item;
            UpdateSelectedEquipment();
        }
    }

    private void EquipmentTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EquipmentTabView.SelectedItem is TabViewItem tab && tab.Header is string header)
        {
            EquipmentResultText.Text = _equipmentItems.Count > 0
                ? $"{_equipmentItems.Count:N0} 件 · {header}"
                : "0 件";
        }
    }

    private void UpdateSelectedEquipment()
    {
        if (_selectedEquipment is null)
        {
            SelectedEquipmentHeader.Visibility = Visibility.Collapsed;
            SelectedEquipmentTooltipHost.Content = null;
            EquipmentCurrentAttributeList.ItemsSource = null;
            EquipmentWashSlotList.ItemsSource = null;
            EquipmentManualCandidateBox.ItemsSource = null;
            EquipmentManualTierBox.ItemsSource = null;
            EquipmentManualApplyButton.IsEnabled = false;
            EquipmentManualStatusText.Text = string.Empty;
            EquipmentStrengthAttributeList.ItemsSource = null;
            EquipmentFireOptionList.ItemsSource = null;
            EquipmentStrengthStatusText.Text = string.Empty;
            SelectedEquipmentStageText.Text = string.Empty;
            SelectedEquipmentFormsText.Text = string.Empty;
            SelectedEquipmentUpgradeList.ItemsSource = null;
            SelectedEquipmentUpgradeEmptyText.Visibility = Visibility.Collapsed;
            _activeEquipmentSimulator = null;
            _activeEquipmentEnhancement = null;
            _equipmentFireTimer.Stop();
            return;
        }

        GameInfoRecord record = _selectedEquipment.Record;
        GameInfoStage? stage = FindEquipmentStage(_selectedCategory, record);
        SelectedEquipmentHeader.Visibility = Visibility.Visible;
        SelectedEquipmentNameText.Text = record.Name;
        SelectedEquipmentNameText.Foreground = _selectedEquipment.QualityBrush;
        SelectedEquipmentMetaText.Text = string.Join(
            " · ",
            new[]
            {
                stage?.Name,
                GetEquipmentProfession(record),
                GetEquipmentPart(record),
                string.IsNullOrWhiteSpace(record.Tooltip?.Level) ? null : $"等级 {record.Tooltip.Level}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        SelectedEquipmentImage.Source = _selectedEquipment.ImageSource;
        SelectedEquipmentImage.Visibility = _selectedEquipment.ImageVisibility;
        SelectedEquipmentFallback.Visibility = _selectedEquipment.FallbackVisibility;
        SelectedEquipmentTooltipFrame.Source = _selectedEquipment.TooltipFrameSource;

        SelectedEquipmentStageText.Text = stage?.Name ?? string.Empty;
        SelectedEquipmentFormsText.Text = stage is { OfficialForms.Count: > 0 }
            ? string.Join(" / ", stage.OfficialForms)
            : string.Empty;
        GameEquipmentUpgradeViewModel[] upgrades = _repository?.EquipmentUpgrades
            .Get(record)
            .Select(upgrade => new GameEquipmentUpgradeViewModel(upgrade, record.Id))
            .ToArray() ?? Array.Empty<GameEquipmentUpgradeViewModel>();
        SelectedEquipmentUpgradeList.ItemsSource = upgrades;
        SelectedEquipmentUpgradeEmptyText.Visibility = Visibility.Collapsed;

        GameEquipmentWashRecord? wash = _repository?.EquipmentWash.Get(record);
        _activeEquipmentSimulator = wash is null
            ? null
            : _equipmentSimulators.GetValueOrDefault(record.Id);
        if (wash is not null && _activeEquipmentSimulator is null)
        {
            _activeEquipmentSimulator = new GameEquipmentSimulator(wash);
            _equipmentSimulators[record.Id] = _activeEquipmentSimulator;
        }

        EquipmentWashSummaryText.Text = string.Empty;

        GameEquipmentEnhancementInfo? enhancement = _repository?.EquipmentEnhancement.Get(record);
        _activeEquipmentEnhancement = enhancement;
        if (enhancement is null)
        {
            EquipmentStrengthRuleText.Text = string.Empty;
            EquipmentFireOptionList.ItemsSource = null;
            EquipmentStrengthStatusText.Text = string.Empty;
            EquipmentStrengthButton.IsEnabled = false;
            _equipmentFireTimer.Stop();
        }
        else
        {
            EquipmentStrengthRuleText.Text = enhancement.Rule;
            ConfigureEquipmentStrength(enhancement);
            EquipmentStrengthStatusText.Text = "火力达到最佳火力标识时，引发所选火焰的效果";
            EquipmentStrengthButton.IsEnabled = _activeEquipmentSimulator is not null;
        }

        RefreshEquipmentSimulation();
    }

    private void RefreshEquipmentSimulation()
    {
        GameEquipmentSimulator? simulator = _activeEquipmentSimulator;
        if (simulator is null)
        {
            EquipmentCurrentAttributeList.ItemsSource = null;
            EquipmentWashSlotList.ItemsSource = null;
            EquipmentManualCandidateBox.ItemsSource = null;
            EquipmentManualTierBox.ItemsSource = null;
            EquipmentManualApplyButton.IsEnabled = false;
            EquipmentManualStatusText.Text = string.Empty;
            EquipmentStrengthAttributeList.ItemsSource = null;
            EquipmentWashButton.IsEnabled = false;
            EquipmentWashReplaceButton.IsEnabled = false;
            EquipmentWashResultText.Text = string.Empty;
            EquipmentWashResultTierText.Text = string.Empty;
            ShowWashCost(null);
            UpdateSelectedEquipmentTooltip();
            return;
        }

        GameEquipmentSimulationSlotViewModel[] slots = simulator.Slots
            .Select(slot => new GameEquipmentSimulationSlotViewModel(slot, simulator.Wash.MaxTierMarker))
            .ToArray();
        int preferredRandomSlot = slots.Any(slot => slot.Index == _selectedWashSlotIndex)
            ? _selectedWashSlotIndex
            : slots.FirstOrDefault(slot => slot.IsWashable)?.Index ?? slots.FirstOrDefault()?.Index ?? 0;
        int preferredManualSlot = slots.Any(slot => slot.Index == _selectedManualWashSlotIndex)
            ? _selectedManualWashSlotIndex
            : slots.FirstOrDefault(slot => slot.IsWashable)?.Index ?? slots.FirstOrDefault()?.Index ?? 0;

        _syncingWashSelection = true;
        try
        {
            EquipmentCurrentAttributeList.ItemsSource = slots;
            EquipmentCurrentAttributeList.SelectedItem = slots.FirstOrDefault(slot => slot.Index == preferredRandomSlot);
        }
        finally
        {
            _syncingWashSelection = false;
        }

        _syncingManualWashSelection = true;
        try
        {
            EquipmentWashSlotList.ItemsSource = slots;
            EquipmentWashSlotList.SelectedItem = slots.FirstOrDefault(slot => slot.Index == preferredManualSlot);
        }
        finally
        {
            _syncingManualWashSelection = false;
        }

        _selectedWashSlotIndex = preferredRandomSlot;
        _selectedManualWashSlotIndex = preferredManualSlot;
        EquipmentStrengthAttributeList.ItemsSource = slots;
        UpdateWashSelection();
        UpdateManualWashSelection();
        UpdateSelectedEquipmentTooltip();
    }

    private void UpdateSelectedEquipmentTooltip()
    {
        if (_selectedEquipment?.Record is not { } record)
        {
            SelectedEquipmentTooltipHost.Content = null;
            return;
        }

        if (record.Tooltip is not { } tooltip || _activeEquipmentSimulator is not { } simulator)
        {
            SelectedEquipmentTooltipHost.Content = GameItemViewModel.CreateTooltipContent(record);
            return;
        }

        GameEquipmentSimulationSlot[] orderedSlots = simulator.Slots
            .OrderBy(slot => slot.Definition.Index)
            .ToArray();
        GameItemTooltip currentTooltip = tooltip with
        {
            EquipRows = orderedSlots
                .Where(slot => !slot.Definition.UsesFeatureColor)
                .Select(simulator.FormatSlotText)
                .ToArray(),
            FeatureRows = orderedSlots
                .Where(slot => slot.Definition.UsesFeatureColor)
                .Select(simulator.FormatSlotText)
                .ToArray(),
            RandomRows = Array.Empty<string>()
        };
        SelectedEquipmentTooltipHost.Content = GameItemViewModel.CreateTooltipContent(
            record with { Tooltip = currentTooltip });
    }

    private async void EquipmentCurrentAttributeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingWashSelection ||
            EquipmentCurrentAttributeList.SelectedItem is not GameEquipmentSimulationSlotViewModel selected)
        {
            return;
        }

        GameEquipmentWashOutcome? pending = _activeEquipmentSimulator?.PendingWash;
        if (pending is not null && pending.SlotIndex != selected.Index)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = "此装备其他属性已进行了洗炼，点击确认将会清除其他属性洗炼结果",
                PrimaryButtonText = "确认",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = (Content as FrameworkElement)?.XamlRoot
            };
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                _syncingWashSelection = true;
                EquipmentCurrentAttributeList.SelectedItem = (EquipmentCurrentAttributeList.ItemsSource as IEnumerable<GameEquipmentSimulationSlotViewModel>)?
                    .FirstOrDefault(slot => slot.Index == _selectedWashSlotIndex);
                _syncingWashSelection = false;
                return;
            }

            _activeEquipmentSimulator?.DiscardPendingWash();
        }

        _selectedWashSlotIndex = selected.Index;
        UpdateWashSelection();
    }

    private void EquipmentWashSlotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingManualWashSelection ||
            EquipmentWashSlotList.SelectedItem is not GameEquipmentSimulationSlotViewModel selected)
        {
            return;
        }

        _selectedManualWashSlotIndex = selected.Index;
        UpdateManualWashSelection();
    }

    private void EquipmentWashButton_Click(object sender, RoutedEventArgs e)
    {
        GameEquipmentWashOutcome? outcome = _activeEquipmentSimulator?.WashSlot(_selectedWashSlotIndex);
        if (outcome is null)
        {
            EquipmentWashResultText.Text = string.Empty;
            EquipmentWashResultTierText.Text = string.Empty;
            return;
        }

        EquipmentWashResultText.Text = _activeEquipmentSimulator?.FormatOutcomeText(outcome) ?? outcome.Text;
        EquipmentWashResultTierText.Text = $"第 {outcome.TierIndex + 1}/{Math.Max(1, outcome.Candidate.TierTexts.Count)} 档";
        EquipmentWashReplaceButton.IsEnabled = true;
    }

    private void EquipmentWashReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEquipmentSimulator?.ApplyPendingWash() == true)
        {
            RefreshEquipmentSimulation();
            EquipmentWashResultText.Text = string.Empty;
            EquipmentWashResultTierText.Text = string.Empty;
        }
    }

    private void UpdateWashSelection()
    {
        GameEquipmentSimulationSlot? slot = _activeEquipmentSimulator?.Slots.FirstOrDefault(value =>
            value.Definition.Index == _selectedWashSlotIndex);
        EquipmentWashButton.IsEnabled = slot?.Definition.IsWashable == true;
        GameEquipmentWashOutcome? pending = _activeEquipmentSimulator?.PendingWash;
        EquipmentWashReplaceButton.IsEnabled = pending is not null;
        if (pending is not null)
        {
            EquipmentWashResultText.Text = _activeEquipmentSimulator?.FormatOutcomeText(pending) ?? pending.Text;
            EquipmentWashResultTierText.Text = $"第 {pending.TierIndex + 1}/{Math.Max(1, pending.Candidate.TierTexts.Count)} 档";
        }
        else if (slot is not null)
        {
            EquipmentWashResultText.Text = string.Empty;
            EquipmentWashResultTierText.Text = string.Empty;
        }
        else
        {
            EquipmentWashResultText.Text = string.Empty;
            EquipmentWashResultTierText.Text = string.Empty;
        }

        GameEquipmentWashCost? cost = _activeEquipmentSimulator?.Wash.Costs.FirstOrDefault(value =>
            value.SlotIndex == _selectedWashSlotIndex);
        ShowWashCost(cost);
    }

    private void UpdateManualWashSelection()
    {
        GameEquipmentSimulator? simulator = _activeEquipmentSimulator;
        GameEquipmentSimulationSlot? slot = simulator?.Slots.FirstOrDefault(value =>
            value.Definition.Index == _selectedManualWashSlotIndex);

        _syncingManualCandidateSelection = true;
        try
        {
            if (simulator is null || slot is null || !slot.Definition.IsWashable)
            {
                EquipmentManualCandidateBox.ItemsSource = null;
                EquipmentManualTierBox.ItemsSource = null;
                EquipmentManualApplyButton.IsEnabled = false;
                EquipmentManualStatusText.Text = string.Empty;
                return;
            }

            GameEquipmentManualCandidateViewModel[] candidates = slot.Definition.Candidates
                .Select(candidate => new GameEquipmentManualCandidateViewModel(candidate))
                .ToArray();
            EquipmentManualCandidateBox.ItemsSource = candidates;
            GameEquipmentManualCandidateViewModel? selectedCandidate = candidates.FirstOrDefault(candidate =>
                candidate.Id.Equals(slot.Candidate.Id, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
            EquipmentManualCandidateBox.SelectedItem = selectedCandidate;
            UpdateManualTierOptions(
                selectedCandidate?.Candidate,
                selectedCandidate?.Id.Equals(slot.Candidate.Id, StringComparison.OrdinalIgnoreCase) == true
                    ? slot.TierIndex
                    : 0);
            EquipmentManualStatusText.Text = string.Empty;
        }
        finally
        {
            _syncingManualCandidateSelection = false;
        }
    }

    private void EquipmentManualCandidateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingManualCandidateSelection ||
            EquipmentManualCandidateBox.SelectedItem is not GameEquipmentManualCandidateViewModel selectedCandidate)
        {
            return;
        }

        GameEquipmentSimulationSlot? slot = _activeEquipmentSimulator?.Slots.FirstOrDefault(value =>
            value.Definition.Index == _selectedManualWashSlotIndex);
        int selectedTier = slot is not null &&
                           selectedCandidate.Id.Equals(slot.Candidate.Id, StringComparison.OrdinalIgnoreCase)
            ? slot.TierIndex
            : 0;
        UpdateManualTierOptions(selectedCandidate.Candidate, selectedTier);
        EquipmentManualStatusText.Text = string.Empty;
    }

    private void UpdateManualTierOptions(GameEquipmentWashCandidate? candidate, int selectedTier)
    {
        if (candidate is null || _activeEquipmentSimulator is not { } simulator)
        {
            EquipmentManualTierBox.ItemsSource = null;
            EquipmentManualApplyButton.IsEnabled = false;
            return;
        }

        GameEquipmentManualTierViewModel[] tiers = candidate.TierTexts
            .Select((_, index) => new GameEquipmentManualTierViewModel(
                index,
                GameEquipmentSimulator.FormatTierText(candidate, index, simulator.Wash.MaxTierMarker)))
            .ToArray();
        EquipmentManualTierBox.ItemsSource = tiers;
        EquipmentManualTierBox.SelectedItem = tiers.ElementAtOrDefault(
            Math.Clamp(selectedTier, 0, Math.Max(0, tiers.Length - 1)));
        EquipmentManualApplyButton.IsEnabled = tiers.Length > 0;
    }

    private void EquipmentManualApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEquipmentSimulator is not { } simulator ||
            EquipmentManualCandidateBox.SelectedItem is not GameEquipmentManualCandidateViewModel candidate ||
            EquipmentManualTierBox.SelectedItem is not GameEquipmentManualTierViewModel tier ||
            !simulator.ReplaceSlot(_selectedManualWashSlotIndex, candidate.Id, tier.Index))
        {
            EquipmentManualStatusText.Text = string.Empty;
            return;
        }

        RefreshEquipmentSimulation();
        GameEquipmentSimulationSlot? updated = simulator.Slots.FirstOrDefault(value =>
            value.Definition.Index == _selectedManualWashSlotIndex);
        EquipmentManualStatusText.Text = string.Empty;
    }

    private void ShowWashCost(GameEquipmentWashCost? cost)
    {
        if (cost?.Material is { } material)
        {
            BitmapImage? image = GameItemViewModel.LoadBitmap(material.ImagePath);
            EquipmentWashCostImage.Source = image;
            EquipmentWashCostImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
            EquipmentWashCostFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
            EquipmentWashCostNameText.Text = string.IsNullOrWhiteSpace(material.DisplayName)
                ? string.Join(" / ", material.Names)
                : material.DisplayName;
            EquipmentWashCostDetailText.Text = material.Names.Count > 1
                ? string.Join(" / ", material.Names)
                : string.Empty;
            EquipmentWashCostAmountText.Text = $"×{material.Count}";
            return;
        }

        EquipmentWashCostImage.Source = null;
        EquipmentWashCostImage.Visibility = Visibility.Collapsed;
        EquipmentWashCostFallback.Visibility = Visibility.Visible;
        if (cost is { Money: > 0 })
        {
            EquipmentWashCostNameText.Text = cost.BoundMoney ? "绑定金" : "流通金";
            EquipmentWashCostDetailText.Text = string.Empty;
            EquipmentWashCostAmountText.Text = cost.Money.ToString("N0");
        }
        else
        {
            EquipmentWashCostNameText.Text = string.Empty;
            EquipmentWashCostDetailText.Text = string.Empty;
            EquipmentWashCostAmountText.Text = string.Empty;
        }
    }

    private void ConfigureEquipmentStrength(GameEquipmentEnhancementInfo enhancement)
    {
        _syncingFireSelection = true;
        try
        {
            EquipmentFireOptionList.ItemsSource = enhancement.FireOptions
                .Select(option => new GameEquipmentFireOptionViewModel(option, option.Id == 1))
                .ToArray();
            _selectedFireId = enhancement.FireOptions.FirstOrDefault()?.Id ?? 1;
        }
        finally
        {
            _syncingFireSelection = false;
        }

        GameEquipmentMaterial? selectedMaterial = enhancement.Materials.FirstOrDefault(material =>
            material.Id.Equals(enhancement.BoundMaterialId, StringComparison.OrdinalIgnoreCase))
            ?? enhancement.Materials.FirstOrDefault();
        BitmapImage? image = GameItemViewModel.LoadBitmap(selectedMaterial?.ImagePath);
        EquipmentStrengthMaterialImage.Source = image;
        EquipmentStrengthMaterialImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        EquipmentStrengthMaterialFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        EquipmentStrengthMaterialNameText.Text = enhancement.Materials.Count > 0
            ? string.Join(" / ", enhancement.Materials.Select(material => material.Name))
            : string.Empty;
        EquipmentStrengthMaterialCountText.Text = enhancement.MaterialCount > 0
            ? $"×{enhancement.MaterialCount}"
            : string.Empty;
        EquipmentStrengthMoneyText.Text = enhancement.Money > 0
            ? $"需要流通金：{enhancement.Money:N0}"
            : string.Empty;

        ResetEquipmentFire();
        _equipmentFireTimer.Start();
    }

    private void EquipmentFireOption_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingFireSelection ||
            sender is not RadioButton radioButton ||
            !int.TryParse(radioButton.Tag?.ToString(), out int fireId))
        {
            return;
        }

        _selectedFireId = fireId;
        ResetEquipmentFire();
    }

    private void EquipmentFireTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_activeEquipmentEnhancement is null || _activeEquipmentSimulator is null)
        {
            sender.Stop();
            return;
        }

        _equipmentFireProgress += _equipmentFireSpeed;
        if (_equipmentFireProgress > 100)
        {
            _equipmentFireProgress = 0;
            _equipmentFireSpeed = _equipmentRandom.Next(1, 3);
        }

        UpdateEquipmentFireTrack();
    }

    private void EquipmentStrengthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEquipmentSimulator is null || _activeEquipmentEnhancement is null)
        {
            return;
        }

        _equipmentFireTimer.Stop();
        bool perfectHit = Math.Abs(_equipmentFireProgress - _equipmentFireTarget) <=
                          _activeEquipmentEnhancement.FireAccuracy;
        GameEquipmentEnhancementOutcome? outcome = _activeEquipmentSimulator.Enhance(
            _activeEquipmentEnhancement,
            _selectedFireId,
            perfectHit);
        if (outcome is null)
        {
            EquipmentStrengthStatusText.Text = string.Empty;
            return;
        }

        RefreshEquipmentSimulation();
        string result = outcome.Event.Name;
        if (perfectHit)
        {
            result += " · 命中最佳火力";
        }

        if (outcome.ChangedSlotIndexes.Count > 0)
        {
            result += $" · 第 {string.Join("、", outcome.ChangedSlotIndexes)} 条属性提升";
        }
        else
        {
            result += " · 所选属性已达最高档次，效果不变";
        }

        if (!outcome.ConsumesMaterial)
        {
            result += " · 本次无消耗";
        }

        EquipmentStrengthStatusText.Text = result;
        ResetEquipmentFire();
        _equipmentFireTimer.Start();
    }

    private void ResetEquipmentFire()
    {
        _equipmentFireProgress = 0;
        _equipmentFireTarget = _equipmentRandom.Next(5, 96);
        _equipmentFireSpeed = _equipmentRandom.Next(1, 3);
        UpdateEquipmentFireTrack();
    }

    private void EquipmentFireTrackGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateEquipmentFireTrack();

    private void UpdateEquipmentFireTrack()
    {
        EquipmentFireProgress.Value = _equipmentFireProgress;
        EquipmentFireValueText.Text = $"{_equipmentFireProgress} / 100";
        double availableWidth = Math.Max(0, EquipmentFireTrackGrid.ActualWidth - EquipmentFireTargetMarker.Width);
        double markerLeft = availableWidth * _equipmentFireTarget / 100d;
        EquipmentFireTargetMarker.Margin = new Thickness(markerLeft, 0, 0, 9);
        double labelLeft = Math.Clamp(markerLeft - 24, 0, Math.Max(0, availableWidth - 48));
        EquipmentFireTargetLabel.Margin = new Thickness(labelLeft, 0, 0, 0);
        EquipmentFireTargetLabel.Text = "最佳火力";
    }

    private void ConfigureWeaponFilters(GameInfoCategory category)
    {
        _updatingWeaponFilters = true;
        try
        {
            GameFilterOption[] levels = new[] { new GameFilterOption(string.Empty, "全部等级") }
                .Concat(category.Stages
                    .OrderBy(stage => stage.Records.FirstOrDefault()?.Weapon?.StageOrder ?? int.MaxValue)
                    .Select(stage => new GameFilterOption(stage.Name, stage.Name)))
                .ToArray();
            GameFilterOption[] professions = new[] { new GameFilterOption(string.Empty, "全部职业") }
                .Concat(category.Stages
                    .SelectMany(stage => stage.Records)
                    .Select(record => record.Group)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(ProfessionOrder)
                    .Select(value => new GameFilterOption(value, value)))
                .ToArray();

            WeaponLevelBox.ItemsSource = levels;
            WeaponProfessionBox.ItemsSource = professions;
            WeaponLevelBox.SelectedIndex = Math.Max(0, levels.Length - 1);
            WeaponProfessionBox.SelectedIndex = 0;
        }
        finally
        {
            _updatingWeaponFilters = false;
        }

        ApplyWeaponFilter();
    }

    private void ApplyWeaponFilter()
    {
        if (_selectedCategory is null ||
            !_selectedCategory.Id.Equals("weapon", StringComparison.Ordinal))
        {
            return;
        }

        string selectedId = _selectedWeapon?.Record.Id ?? string.Empty;
        string level = (WeaponLevelBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string profession = (WeaponProfessionBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string query = InfoSearchBox.Text.Trim();

        IEnumerable<GameInfoRecord> records = _selectedCategory.Stages
            .SelectMany(stage => stage.Records)
            .Where(record => string.IsNullOrWhiteSpace(level) ||
                             string.Equals(record.Weapon?.LevelLabel, level, StringComparison.Ordinal))
            .Where(record => string.IsNullOrWhiteSpace(profession) ||
                             string.Equals(record.Group, profession, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(query))
        {
            records = records.Where(record => Matches(record, query) ||
                                              (record.Weapon?.LevelLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        _weaponItems = records
            .OrderBy(record => record.Weapon?.StageOrder ?? int.MaxValue)
            .ThenBy(record => ProfessionOrder(record.Group))
            .ThenBy(record => record.Name, StringComparer.CurrentCulture)
            .Select(record => new GameItemViewModel(record))
            .ToArray();

        _syncingWeaponSelection = true;
        try
        {
            WeaponList.ItemsSource = _weaponItems;
            WeaponChoiceBox.ItemsSource = _weaponItems;
            GameItemViewModel? selection = _weaponItems.FirstOrDefault(item =>
                                                    item.Record.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                                                ?? _weaponItems.FirstOrDefault();
            WeaponList.SelectedItem = selection;
            WeaponChoiceBox.SelectedItem = selection;
            _selectedWeapon = selection;
        }
        finally
        {
            _syncingWeaponSelection = false;
        }

        WeaponResultText.Text = $"{_weaponItems.Count:N0} 把";
        WeaponEmptyState.Visibility = _weaponItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectedWeapon();
    }

    private void WeaponFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingWeaponFilters)
        {
            ApplyWeaponFilter();
        }
    }

    private void WeaponList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingWeaponSelection && WeaponList.SelectedItem is GameItemViewModel item)
        {
            SelectWeapon(item);
        }
    }

    private void WeaponChoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingWeaponSelection && WeaponChoiceBox.SelectedItem is GameItemViewModel item)
        {
            SelectWeapon(item);
        }
    }

    private void SelectWeapon(GameItemViewModel item)
    {
        _selectedWeapon = item;
        _syncingWeaponSelection = true;
        try
        {
            WeaponList.SelectedItem = item;
            WeaponChoiceBox.SelectedItem = item;
            WeaponList.ScrollIntoView(item);
        }
        finally
        {
            _syncingWeaponSelection = false;
        }

        UpdateSelectedWeapon();
    }

    private void UpdateSelectedWeapon()
    {
        if (_selectedWeapon is null)
        {
            SelectedWeaponHeader.Visibility = Visibility.Collapsed;
            SelectedWeaponTooltipHost.Content = null;
            WuhunPanel.Visibility = Visibility.Collapsed;
            return;
        }

        GameInfoRecord record = _selectedWeapon.Record;
        SelectedWeaponHeader.Visibility = Visibility.Visible;
        SelectedWeaponNameText.Text = record.Name;
        SelectedWeaponNameText.Foreground = _selectedWeapon.QualityBrush;
        SelectedWeaponMetaText.Text = string.Join(
            " · ",
            new[] { record.Weapon?.LevelLabel, record.Group }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        SelectedWeaponImage.Source = _selectedWeapon.ImageSource;
        SelectedWeaponImage.Visibility = _selectedWeapon.ImageVisibility;
        SelectedWeaponFallback.Visibility = _selectedWeapon.FallbackVisibility;
        SelectedWeaponTooltipFrame.Source = _selectedWeapon.TooltipFrameSource;
        RefreshSelectedWeaponTooltip(null);
        WuhunPanel.Visibility = Visibility.Visible;
        ConfigureWuhun(record);
    }

    private void ConfigureWuhun(GameInfoRecord record)
    {
        GameWeaponInfo? weapon = record.Weapon;
        GameWeaponEnhancementCatalog catalog = _repository?.WeaponEnhancement ?? GameWeaponEnhancementCatalog.Empty;
        _wuhunRows = catalog.GetRows(record);
        WuhunCapText.Text = string.IsNullOrWhiteSpace(weapon?.SoulCap)
            ? $"{weapon?.System}"
            : $"{weapon.System} · 武魂上限：{weapon.SoulCap}";

        if (weapon is null || !weapon.SupportsWuhun)
        {
            ShowWuhunUnavailable(string.Empty);
            return;
        }

        if (_wuhunRows.Count == 0)
        {
            ShowWuhunUnavailable(string.Empty);
            return;
        }

        WuhunUnavailableText.Visibility = Visibility.Collapsed;
        WuhunComparisonPanel.Visibility = Visibility.Visible;
        WuhunCostPanel.Visibility = Visibility.Visible;
        WuhunLevelBox.Visibility = Visibility.Visible;
        WuhunAdvanceButton.Visibility = Visibility.Visible;
        WuhunResetButton.Visibility = Visibility.Visible;
        WuhunLevelBox.ItemsSource = new[] { new GameWeaponEnhancementOption(null, "武魂未激活") }
            .Concat(_wuhunRows.Select(row => new GameWeaponEnhancementOption(
                row,
                row.ClientDisplayName)))
            .ToArray();
        WuhunLevelBox.SelectedIndex = 0;
        UpdateWuhunComparison();
    }

    private void ShowWuhunUnavailable(string message)
    {
        WuhunUnavailableText.Text = string.Empty;
        WuhunUnavailableText.Visibility = Visibility.Collapsed;
        WuhunComparisonPanel.Visibility = Visibility.Collapsed;
        WuhunCostPanel.Visibility = Visibility.Collapsed;
        WuhunLevelBox.Visibility = Visibility.Collapsed;
        WuhunAdvanceButton.Visibility = Visibility.Collapsed;
        WuhunResetButton.Visibility = Visibility.Collapsed;
        WuhunLevelBox.ItemsSource = null;
        _wuhunRows = Array.Empty<GameWeaponEnhancementRow>();
        RefreshSelectedWeaponTooltip(null);
    }

    private void WuhunLevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWuhunComparison();
    }

    private void WuhunAdvanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (WuhunLevelBox.SelectedIndex < WuhunLevelBox.Items.Count - 1)
        {
            WuhunLevelBox.SelectedIndex++;
        }
    }

    private void WuhunResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (WuhunLevelBox.Items.Count > 0)
        {
            WuhunLevelBox.SelectedIndex = 0;
        }
    }

    private void UpdateWuhunComparison()
    {
        if (_selectedWeapon is null || _wuhunRows.Count == 0)
        {
            RefreshSelectedWeaponTooltip(null);
            return;
        }

        GameWeaponEnhancementOption? option = WuhunLevelBox.SelectedItem as GameWeaponEnhancementOption;
        int currentIndex = option?.Row is null
            ? -1
            : FindWuhunRowIndex(option.Row);
        GameWeaponEnhancementRow? current = currentIndex >= 0 ? _wuhunRows[currentIndex] : null;
        GameWeaponEnhancementRow? target = currentIndex + 1 < _wuhunRows.Count
            ? _wuhunRows[currentIndex + 1]
            : null;

        WuhunCurrentTitleText.Text = current is null
            ? "武魂未激活"
            : current.ClientDisplayName;
        WuhunCurrentTraitsList.ItemsSource = current is null
            ? new[] { "武魂未激活" }
            : current.Traits.Select(value => $"强化：{value}").ToArray();
        WuhunTargetTitleText.Text = target?.ClientDisplayName ?? string.Empty;
        WuhunTargetTraitsList.ItemsSource = target is null
            ? Array.Empty<string>()
            : target.Traits.Select(value => $"强化：{value}").ToArray();

        GameWeaponEnhancementCost? cost = target is null
            ? null
            : (_repository?.WeaponEnhancement ?? GameWeaponEnhancementCatalog.Empty).FindCost(target);
        UpdateWuhunCost(cost, target is not null);
        RefreshSelectedWeaponTooltip(current);
        WuhunAdvanceButton.IsEnabled = target is not null;
        WuhunResetButton.IsEnabled = current is not null;
    }

    private int FindWuhunRowIndex(GameWeaponEnhancementRow row)
    {
        for (int index = 0; index < _wuhunRows.Count; index++)
        {
            if (_wuhunRows[index].StepIndex == row.StepIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private void RefreshSelectedWeaponTooltip(GameWeaponEnhancementRow? current)
    {
        if (_selectedWeapon is null)
        {
            SelectedWeaponTooltipHost.Content = null;
            return;
        }

        GameWeaponTooltipOverlay? overlay = current is null
            ? null
            : new GameWeaponTooltipOverlay(
                $"武魂等级：{current.ClientDisplayName}",
                current.Traits);
        SelectedWeaponTooltipHost.Content = GameItemViewModel.CreateTooltipContent(
            _selectedWeapon.Record,
            overlay);
    }

    private void UpdateWuhunCost(GameWeaponEnhancementCost? cost, bool hasTarget)
    {
        if (!hasTarget)
        {
            WuhunMaterialImage.Source = null;
            WuhunMaterialImage.Visibility = Visibility.Collapsed;
            WuhunMaterialFallback.Visibility = Visibility.Visible;
            WuhunMaterialNameText.Text = string.Empty;
            WuhunCostText.Text = string.Empty;
            WuhunSuccessText.Text = string.Empty;
            return;
        }

        WuhunMaterialNameText.Text = cost?.MaterialText ?? string.Empty;
        WuhunCostText.Text = cost is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(cost.BoundGold)
                ? string.Empty
                : $"绑定金：{cost.BoundGold}";
        WuhunSuccessText.Text = cost?.SuccessValue is int success ? $"成功率：{success}%" : string.Empty;

        BitmapImage? materialImage = GameItemViewModel.LoadBitmap(cost?.MaterialIcon?.ImagePath);
        WuhunMaterialImage.Source = materialImage;
        WuhunMaterialImage.Visibility = materialImage is null ? Visibility.Collapsed : Visibility.Visible;
        WuhunMaterialFallback.Visibility = materialImage is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetEquipmentProfession(GameInfoRecord record)
    {
        if (record.Tooltip?.RequiredProfessions.Count > 0)
        {
            return GameProfessionFormatter.NormalizeRequirement(record.Tooltip.RequiredProfessions);
        }

        return GameProfessionFormatter.NormalizeRequirement(record.Group);
    }

    private static string GetEquipmentPart(GameInfoRecord record) =>
        string.IsNullOrWhiteSpace(record.Tooltip?.Part) ? record.Summary : record.Tooltip.Part;

    private static GameInfoStage? FindEquipmentStage(GameInfoCategory? category, GameInfoRecord record)
    {
        return category?.Stages.FirstOrDefault(stage => stage.Records.Contains(record));
    }

    private static int EquipmentProfessionOrder(string value)
    {
        string[] order = { "力士", "游侠", "法师", "符咒师" };
        string first = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? value;
        int index = Array.IndexOf(order, first);
        return index < 0 ? int.MaxValue : index;
    }

    private static int EquipmentPartOrder(string value)
    {
        string[] order = { "头部", "头冠", "法帽", "衣服", "战袍", "法袍", "腰带", "束腰", "围腰", "裤子", "长裤", "绸裤", "鞋子", "皮靴", "丝履", "戒指/护腕", "戒指", "项链/坠饰", "项链", "灵坠" };
        int index = Array.IndexOf(order, value);
        return index < 0 ? int.MaxValue : index;
    }

    private static int EquipmentStageOrder(GameInfoCategory category, GameInfoRecord record)
    {
        for (int index = 0; index < category.Stages.Count; index++)
        {
            if (category.Stages[index].Records.Contains(record))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static int ProfessionOrder(string value)
    {
        string[] order = { "游侠系", "力士系", "法师系", "符咒系" };
        int index = Array.IndexOf(order, value);
        return index < 0 ? int.MaxValue : index;
    }

    private static bool Matches(GameInfoRecord record, string query)
    {
        if (record.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            record.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            record.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            record.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        GameItemTooltip? tooltip = record.Tooltip;
        if (tooltip is null)
        {
            return false;
        }

        return tooltip.RequiredProfessions.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               tooltip.BaseRows.Any(value => value.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || value.Value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               tooltip.EquipRows.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               tooltip.FeatureRows.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               tooltip.RandomRows.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class GameStageViewModel
{
    public GameStageViewModel(GameInfoStage stage, IReadOnlyList<GameItemViewModel> records)
    {
        Name = stage.Name;
        Period = stage.Period;
        Summary = stage.Summary;
        Records = records;
        PeriodVisibility = string.IsNullOrWhiteSpace(Period) ? Visibility.Collapsed : Visibility.Visible;
        SummaryVisibility = string.IsNullOrWhiteSpace(Summary) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string Name { get; }
    public string Period { get; }
    public string Summary { get; }
    public IReadOnlyList<GameItemViewModel> Records { get; }
    public Visibility PeriodVisibility { get; }
    public Visibility SummaryVisibility { get; }
}

public sealed record GameFilterOption(string Id, string Name);

public sealed record GameWanxiangNameOption(string Name);

public sealed record GameWanxiangQualityOption(int Index, string Name);

public sealed record GameWanxiangSegmentOption(int Value, string Name);

public sealed class GameWanxiangGemSeriesOption
{
    public GameWanxiangGemSeriesOption(
        GameWanxiangGemSeries? series,
        string name,
        string? imagePath)
    {
        Series = series;
        Name = name;
        ImageSource = GameItemViewModel.LoadBitmap(imagePath);
    }

    public GameWanxiangGemSeries? Series { get; }
    public string Name { get; }
    public BitmapImage? ImageSource { get; }
}

public sealed class GameWanxiangSocketViewModel
{
    public GameWanxiangSocketViewModel(GameWanxiangGem gem)
    {
        Name = gem.Record.Name;
        ImageSource = GameItemViewModel.LoadBitmap(gem.Record.ImagePath);
    }

    public string Name { get; }
    public BitmapImage? ImageSource { get; }
}

public sealed class GameYinYangGroupOptionViewModel
{
    public GameYinYangGroupOptionViewModel(GameYinYangAffixGroup group)
    {
        Key = string.Concat(group.Role, ":", group.Prefix);
        Name = group.Name;
        Detail = string.Concat("第", group.GenerationId, "代 · ", group.RoleLabel, "诀");
    }

    public string Key { get; }
    public string Name { get; }
    public string Detail { get; }
}

public sealed record GameYinYangAppliedAttributeViewModel(string RoleLabel, string Text);

public sealed class GameEquipmentSimulationSlotViewModel
{
    private static readonly Brush EquipmentBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0));
    private static readonly Brush FeatureBrush = new SolidColorBrush(Color.FromArgb(255, 255, 100, 0));

    public GameEquipmentSimulationSlotViewModel(
        GameEquipmentSimulationSlot slot,
        string maxTierMarker)
    {
        Index = slot.Definition.Index;
        SlotText = slot.Definition.Name;
        CurrentText = GameEquipmentSimulator.FormatTierText(
            slot.Candidate,
            slot.TierIndex,
            maxTierMarker);
        TierText = $"{slot.TierIndex + 1}/{Math.Max(1, slot.Candidate.TierTexts.Count)} 档";
        IsWashable = slot.Definition.IsWashable;
        StateText = string.Empty;
        TextBrush = slot.Definition.UsesFeatureColor ? FeatureBrush : EquipmentBrush;
        CandidateTip = string.Join(
            Environment.NewLine,
            slot.Definition.Candidates
                .Select(candidate => candidate.Name)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCulture));
    }

    public int Index { get; }
    public string SlotText { get; }
    public string CurrentText { get; }
    public string TierText { get; }
    public bool IsWashable { get; }
    public string StateText { get; }
    public Brush TextBrush { get; }
    public string CandidateTip { get; }
}

public sealed class GameEquipmentManualCandidateViewModel
{
    public GameEquipmentManualCandidateViewModel(GameEquipmentWashCandidate candidate)
    {
        Candidate = candidate;
        Id = candidate.Id;
        Name = candidate.Name;
    }

    public GameEquipmentWashCandidate Candidate { get; }
    public string Id { get; }
    public string Name { get; }
}

public sealed record GameEquipmentManualTierViewModel(int Index, string Name);

public sealed class GameEquipmentFireOptionViewModel
{
    public GameEquipmentFireOptionViewModel(GameEquipmentFireOption option, bool isSelected)
    {
        Id = option.Id;
        Description = option.Description;
        IsSelected = isSelected;
    }

    public int Id { get; }
    public string Description { get; }
    public bool IsSelected { get; }
}

public sealed class GameEquipmentWashOptionViewModel
{
    public GameEquipmentWashOptionViewModel(GameEquipmentWashOption option)
    {
        Slot = option.Slot;
        Label = option.Label;
        ShortName = option.ShortName;
        RawName = string.IsNullOrWhiteSpace(option.RawName) ? option.Label : option.RawName;
        RangeText = string.Empty;
    }

    public string Slot { get; }
    public string Label { get; }
    public string ShortName { get; }
    public string RawName { get; }
    public string RangeText { get; }
}

public sealed class GameEquipmentWashMaterialViewModel
{
    public GameEquipmentWashMaterialViewModel(GameEquipmentWashMaterial material)
    {
        Name = string.IsNullOrWhiteSpace(material.DisplayName)
            ? string.Join(" / ", material.Names)
            : material.DisplayName;
        NamesText = material.Names.Count > 0 ? string.Join(" / ", material.Names) : string.Empty;
        CountText = material.Count > 0 ? $"×{material.Count}" : string.Empty;
        ImageSource = GameItemViewModel.LoadBitmap(material.ImagePath);
        ImageVisibility = ImageSource is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackVisibility = ImageSource is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Name { get; }
    public string NamesText { get; }
    public string CountText { get; }
    public BitmapImage? ImageSource { get; }
    public Visibility ImageVisibility { get; }
    public Visibility FallbackVisibility { get; }
}

public sealed class GameEquipmentMaterialViewModel
{
    public GameEquipmentMaterialViewModel(GameEquipmentMaterial material, string boundMaterialId)
    {
        Name = material.Name;
        IdText = material.Id.Equals(boundMaterialId, StringComparison.OrdinalIgnoreCase) ? "绑定材料" : "可用材料";
        RoleText = material.Id.Equals(boundMaterialId, StringComparison.OrdinalIgnoreCase) ? "绑定" : "可选";
        ImageSource = GameItemViewModel.LoadBitmap(material.ImagePath);
        ImageVisibility = ImageSource is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackVisibility = ImageSource is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Name { get; }
    public string IdText { get; }
    public string RoleText { get; }
    public BitmapImage? ImageSource { get; }
    public Visibility ImageVisibility { get; }
    public Visibility FallbackVisibility { get; }
}

public sealed class GameEquipmentUpgradeViewModel
{
    public GameEquipmentUpgradeViewModel(GameEquipmentUpgrade upgrade, string currentItemId)
    {
        bool isIncoming = upgrade.TargetItemId.Equals(currentItemId, StringComparison.OrdinalIgnoreCase);
        DirectionText = isIncoming ? "前置装备" : "可升级到";
        RouteText = $"{upgrade.SourceName}  →  {upgrade.TargetName}";

        var details = new List<string>();
        if (upgrade.Materials.Count > 0)
        {
            details.Add($"材料：{string.Join(" + ", upgrade.Materials.Select(material => material.Name))}");
        }

        if (upgrade.GoldCost is long goldCost && goldCost > 0)
        {
            details.Add($"流通金：{goldCost:N0}");
        }

        if (upgrade.IsBoundRecipe)
        {
            details.Add("绑定配方升级");
        }

        CostText = details.Count > 0
            ? string.Join(" · ", details)
            : string.Empty;
    }

    public string DirectionText { get; }
    public string RouteText { get; }
    public string CostText { get; }
}

public sealed record GameWeaponEnhancementOption(
    GameWeaponEnhancementRow? Row,
    string DisplayName);

public sealed class GameItemViewModel
{
    private const string ClientDefaultColor = "#FFFFFFFF";
    private const string ClientFontFamily = "ms-appx:///Assets/Fonts/fzlth_gb18030.ttf#FZLanTingHei-R-GB18030";
    private static readonly Brush ClientDefaultBrush = Brush(ClientDefaultColor);
    private static readonly BitmapImage? ClientTooltipFrame = LoadBitmap(
        Path.Combine(AppContext.BaseDirectory, "Assets", "Client", "frm_tip.png"));

    public GameItemViewModel(GameInfoRecord record, string? equipmentStageName = null)
    {
        Record = record;
        Name = record.Name;
        Group = record.Group;
        Summary = record.Summary;
        CardMetaText = string.Join(
            " · ",
            new[] { record.Weapon?.LevelLabel, record.Group }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        string equipmentProfession = GameProfessionFormatter.NormalizeRequirement(
            record.Tooltip?.RequiredProfessions.Count > 0
                ? record.Tooltip.RequiredProfessions
                : new[] { record.Group });
        EquipmentCardMetaText = string.Join(
            " · ",
            new[]
            {
                equipmentStageName,
                record.Tooltip?.Part ?? record.Summary,
                equipmentProfession,
                string.IsNullOrWhiteSpace(record.Tooltip?.Level) ? null : $"等级 {record.Tooltip.Level}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        ImageSource = LoadBitmap(record.ImagePath);
        ImageVisibility = ImageSource is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackVisibility = ImageSource is null ? Visibility.Visible : Visibility.Collapsed;

        GameItemTooltip? tooltip = record.Tooltip;
        QualityBrush = Brush(GameTooltipFormatter.ResolveQualityColor(tooltip?.Quality));
        TooltipFrameSource = ClientTooltipFrame;
        TooltipContent = CreateTooltipContent(record);
    }

    public GameInfoRecord Record { get; }
    public string Name { get; }
    public string Group { get; }
    public string Summary { get; }
    public string CardMetaText { get; }
    public string EquipmentCardMetaText { get; }
    public BitmapImage? ImageSource { get; }
    public Visibility ImageVisibility { get; }
    public Visibility FallbackVisibility { get; }
    public Brush QualityBrush { get; }
    public BitmapImage? TooltipFrameSource { get; }
    public RichTextBlock TooltipContent { get; }

    public static RichTextBlock CreateTooltipContent(
        GameInfoRecord record,
        GameWeaponTooltipOverlay? weaponOverlay = null,
        IReadOnlyList<GameTooltipSupplementLine>? supplementLines = null,
        IReadOnlyList<GameTooltipHintLine>? hintLines = null,
        bool yinYangLayout = false)
    {
        IReadOnlyList<GameTooltipLine> tooltipLines = yinYangLayout
            ? GameTooltipFormatter.FormatYinYang(record, supplementLines, hintLines)
            : GameTooltipFormatter.Format(record, weaponOverlay, supplementLines, hintLines);
        return CreateTooltipContent(tooltipLines);
    }

    public static RichTextBlock CreateWanxiangTooltipContent(
        GameInfoRecord record,
        GameWanxiangTooltipOverlay overlay) =>
        CreateTooltipContent(GameTooltipFormatter.FormatWanxiang(record, overlay));

    private static RichTextBlock CreateTooltipContent(IReadOnlyList<GameTooltipLine> tooltipLines)
    {
        var content = new RichTextBlock
        {
            FontFamily = new FontFamily(ClientFontFamily),
            FontSize = 14,
            Foreground = ClientDefaultBrush,
            IsTextSelectionEnabled = false,
            TextWrapping = TextWrapping.Wrap
        };

        foreach (GameTooltipLine line in tooltipLines)
        {
            var paragraph = new Paragraph
            {
                TextAlignment = line.Alignment == GameTooltipAlignment.Center
                    ? TextAlignment.Center
                    : TextAlignment.Left
            };

            if (line.TrailingSpans.Count > 0)
            {
                var splitLine = new Grid
                {
                    Width = 408
                };
                splitLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                splitLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock leadingText = CreateTooltipTextBlock(line.Spans);
                TextBlock trailingText = CreateTooltipTextBlock(line.TrailingSpans);
                trailingText.HorizontalAlignment = HorizontalAlignment.Right;
                trailingText.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(trailingText, 1);
                splitLine.Children.Add(leadingText);
                splitLine.Children.Add(trailingText);
                paragraph.Inlines.Add(new InlineUIContainer { Child = splitLine });
            }
            else
            {
                foreach (GameTooltipSpan span in line.Spans)
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = span.Text,
                        Foreground = Brush(span.Color)
                    });
                }

                BitmapImage? iconFrame = LoadBitmap(line.IconFramePath);
                foreach (string iconPath in line.IconPaths)
                {
                    var icon = new Grid
                    {
                        Width = 20,
                        Height = 20,
                        Margin = new Thickness(1, 0, 0, 0)
                    };
                    if (iconFrame is not null)
                    {
                        icon.Children.Add(new Image
                        {
                            Source = iconFrame,
                            Stretch = Stretch.Fill,
                            IsHitTestVisible = false
                        });
                    }
                    icon.Children.Add(new Image
                    {
                        Source = LoadBitmap(iconPath),
                        Margin = new Thickness(2),
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    });
                    paragraph.Inlines.Add(new InlineUIContainer { Child = icon });
                }
            }

            content.Blocks.Add(paragraph);
        }

        return content;
    }

    private static TextBlock CreateTooltipTextBlock(IReadOnlyList<GameTooltipSpan> spans)
    {
        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily(ClientFontFamily),
            FontSize = 14,
            Foreground = ClientDefaultBrush,
            TextWrapping = TextWrapping.NoWrap
        };
        foreach (GameTooltipSpan span in spans)
        {
            textBlock.Inlines.Add(new Run
            {
                Text = span.Text,
                Foreground = Brush(span.Color)
            });
        }

        return textBlock;
    }

    public static BitmapImage? LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    public static SolidColorBrush BrushFromColor(string color) => Brush(color);

    private static SolidColorBrush Brush(string color)
    {
        string value = color.Trim().TrimStart('#');
        byte alpha = 255;
        int offset = 0;

        if (value.Length == 8 && value.All(Uri.IsHexDigit))
        {
            alpha = Convert.ToByte(value[..2], 16);
            offset = 2;
        }
        else if (value.Length != 6 || !value.All(Uri.IsHexDigit))
        {
            value = ClientDefaultColor.TrimStart('#');
            alpha = Convert.ToByte(value[..2], 16);
            offset = 2;
        }

        byte red = Convert.ToByte(value.Substring(offset, 2), 16);
        byte green = Convert.ToByte(value.Substring(offset + 2, 2), 16);
        byte blue = Convert.ToByte(value.Substring(offset + 4, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
