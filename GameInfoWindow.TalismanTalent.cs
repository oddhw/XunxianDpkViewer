using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow
{
    private const string TalismanFont = "ms-appx:///Assets/Fonts/fzlth_gb18030.ttf#FZLanTingHei-R-GB18030";
    private int _selectedTalismanTalentId;
    private readonly Dictionary<int, Button> _talismanTalentButtons = new();
    private TextBlock? _talismanPointsText;
    private ContentPresenter? _talismanTalentEffect;
    private Grid? _talismanNativeWindow;
    private ScrollViewer? _talismanEffectScroll;
    private Task<WriteableBitmap>? _talismanTreeBackground;

    private void BuildTalismanTalentView()
    {
        _talismanTalentButtons.Clear();
        if (_selectedTalisman is null || _repository is null)
        {
            TalismanTalentHost.Content = null;
            return;
        }
        GameTalismanCatalog catalog = _repository.Talismans;
        GameInfoRecord record = _selectedTalisman.Record;
        GameTalismanInfo info = record.Talisman!;
        GameTalismanSelection state = TalismanState(record);
        var root = new Grid { Width = 572, Height = 430, UseLayoutRounding = true };
        _talismanNativeWindow = root;
        var background = new Grid
        {
            Name = "TalismanClientBackground",
            Margin = new Thickness(13, 35, 13, 13),
            Background = GameItemViewModel.BrushFromColor("#FF0F0A08")
        };
        background.Children.Add(TalismanTiles("image/cmn/pic_background.png", 128, 128, 0, 0, 128, 128));
        root.Children.Add(background);
        Grid foot = TalismanFrame("image/cmn/frm_border_foot.png", 128, 128, 128, 128, new Thickness(33), false);
        foot.Margin = new Thickness(0, 40, 0, 0);
        root.Children.Add(foot);
        Grid innerFrame = TalismanFrame("image/cmn/frm_border_inner.png", 128, 128, 128, 128, new Thickness(30, 20, 30, 30), false);
        innerFrame.Margin = new Thickness(15, 40, 15, 15);
        root.Children.Add(innerFrame);
        FrameworkElement bottom = TalismanSprite("image/cmn/pic_border_bottom.png", 64, 32, 0, 0, 51, 25);
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(bottom);
        Grid head = TalismanFrame("image/cmn/frm_border_head.png", 256, 64, 242, 46, new Thickness(54, 46, 54, 0), false);
        head.Height = 46;
        head.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(head);
        FrameworkElement highlight = TalismanSprite("image/cmn/pic_title_hi.png", 256, 4, 0, 0, 256, 4);
        highlight.HorizontalAlignment = HorizontalAlignment.Center;
        highlight.VerticalAlignment = VerticalAlignment.Top;
        highlight.Margin = new Thickness(0, 9, 0, 0);
        root.Children.Add(highlight);
        Grid title = TalismanOutlinedText(catalog.Text("title"), 16, "#FFFFEEDD");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Top;
        title.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(title);

        var content = new Grid { Margin = new Thickness(22, 46, 22, 22) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(content);
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(left);
        Grid summary = TalismanFrame("image/cmn/frm_lace_0.png", 64, 64, 64, 64, new Thickness(14));
        left.Children.Add(summary);
        var identity = new Grid { Width = 190, Height = 60, Margin = new Thickness(8, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        identity.Children.Add(TalismanFrame("image/cmn/frm_card.png", 64, 64, 64, 64, new Thickness(12)));
        var identityContent = new Canvas();
        FrameworkElement itemFrame = TalismanSprite("image/cmn/pic_card.png", 64, 64, 0, 0, 49, 50);
        Canvas.SetLeft(itemFrame, 5);
        Canvas.SetTop(itemFrame, 5);
        identityContent.Children.Add(itemFrame);
        var itemIcon = new Image { Source = _selectedTalisman.ImageSource, Width = 40, Height = 40 };
        Canvas.SetLeft(itemIcon, 9);
        Canvas.SetTop(itemIcon, 9);
        identityContent.Children.Add(itemIcon);
        TextBlock name = TalismanText(record.Name, 16);
        name.TextWrapping = TextWrapping.NoWrap;
        var nameView = new Viewbox { Width = 135, Height = 21, Child = name, Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
        Canvas.SetLeft(nameView, 55);
        Canvas.SetTop(nameView, 8);
        identityContent.Children.Add(nameView);
        if (info.InternalRank > 5 && catalog.AppearanceNames.Count > 0)
        {
            Grid appearance = TalismanFrame("image/cmn/frm_edit.png", 64, 32, 64, 29, new Thickness(5));
            appearance.Name = "TalismanAppearanceSelector";
            appearance.Width = 130;
            appearance.Height = 25;
            var appearanceBackground = new Viewbox { Stretch = Stretch.Fill, Margin = new Thickness(3, 3, 19, 3),
                Child = TalismanSprite("image/map/image_server.png", 128, 32, 1, 1, 91, 17) };
            appearance.Children.Insert(0, appearanceBackground);
            TextBlock appearanceText = TalismanText(catalog.AppearanceNames.ElementAtOrDefault(state.AppearanceIndex) ?? catalog.AppearanceNames[0], 16);
            appearanceText.TextWrapping = TextWrapping.NoWrap;
            appearanceText.Margin = new Thickness(4, 0, 20, 0);
            appearanceText.VerticalAlignment = VerticalAlignment.Center;
            appearance.Children.Add(appearanceText);
            Button dropdown = TalismanSpriteButton("image/cmn/btn_dropdown.png", 32, 128, 1, 3, 19, 22, true);
            dropdown.Width = 22;
            dropdown.Height = 22;
            dropdown.HorizontalAlignment = HorizontalAlignment.Right;
            dropdown.VerticalAlignment = VerticalAlignment.Center;
            var menu = new MenuFlyout();
            foreach ((string label, int index) in catalog.AppearanceNames.Select((label, index) => (label, index)))
            {
                var option = new ToggleMenuFlyoutItem { Text = label, IsChecked = index == state.AppearanceIndex,
                    FontFamily = new FontFamily(TalismanFont) };
                option.Click += (_, _) =>
                {
                    state.AppearanceIndex = index;
                    appearanceText.Text = label;
                    foreach (ToggleMenuFlyoutItem entry in menu.Items.OfType<ToggleMenuFlyoutItem>())
                    {
                        entry.IsChecked = ReferenceEquals(entry, option);
                    }
                };
                menu.Items.Add(option);
            }
            dropdown.Flyout = menu;
            AutomationProperties.SetName(dropdown, catalog.AppearanceNames[0]);
            appearance.Children.Add(dropdown);
            Canvas.SetLeft(appearance, 52);
            Canvas.SetTop(appearance, 30);
            identityContent.Children.Add(appearance);
        }
        identity.Children.Add(identityContent);
        summary.Children.Add(identity);
        var values = new StackPanel { Margin = new Thickness(10, 70, 0, 0) };
        values.Children.Add(TalismanText(info.DisplayRank > 0
            ? TalismanFormat(catalog.Text("rank"), info.DisplayRank) : catalog.Text("novice"), 16));
        var growthRow = new Grid { Height = 18 };
        TextBlock growthText = TalismanText(TalismanFormat(catalog.Text("growth"), state.Growth, info.MaxGrowth) + $"({info.MaxGrowth})", 16);
        growthRow.Children.Add(growthText);
        Button plus = TalismanSpriteButton("image/cmn/btn_plus.png", 16, 64, 0, 0, 16, 16, true);
        plus.Width = 15;
        plus.Height = 15;
        plus.Margin = new Thickness(0, 0, 20, 0);
        plus.HorizontalAlignment = HorizontalAlignment.Right;
        plus.VerticalAlignment = VerticalAlignment.Center;
        plus.IsEnabled = state.Growth < info.MaxGrowth;
        plus.Click += (_, _) => TalismanGrowthBox.Value = Math.Min(info.MaxGrowth, state.Growth + 1);
        AutomationProperties.SetName(plus, TalismanGrowthLabel.Text);
        growthRow.Children.Add(plus);
        values.Children.Add(growthRow);
        _talismanPointsText = TalismanText(TalismanFormat(catalog.Text("points"), state.Growth - state.UsedPoints), 16);
        values.Children.Add(_talismanPointsText);
        // Spirit reserve is character state; the supplied client screenshot provides this preview value.
        values.Children.Add(TalismanText(TalismanFormat(catalog.Text("reserve"), 0, 19600), 16));
        values.Children.Add(TalismanText(TalismanFormat(catalog.Text("conversion"), info.SpiritConversionRate), 16));
        foreach (FrameworkElement row in values.Children.Cast<FrameworkElement>())
        {
            row.Height = 18;
        }
        summary.Children.Add(values);

        Grid effectFrame = TalismanFrame("image/cmn/frm_lace_2.png", 128, 64, 128, 64, new Thickness(20, 5, 20, 20), false);
        Grid.SetRow(effectFrame, 1);
        left.Children.Add(effectFrame);
        _talismanTalentEffect = new ContentPresenter();
        Grid effectScroll = TalismanClientScroll(_talismanTalentEffect);
        effectScroll.Margin = new Thickness(7, 10, 5, 10);
        effectFrame.Children.Add(effectScroll);

        Grid treeFrame = TalismanFrame("image/cmn/frm_lace_3.png", 64, 64, 60, 58, new Thickness(12), false);
        Grid.SetColumn(treeFrame, 1);
        content.Children.Add(treeFrame);
        var treeArea = new Grid { Margin = new Thickness(10, 6, 9, 10) };
        treeArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        treeArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        treeFrame.Children.Add(treeArea);
        var progress = new Grid { Name = "TalismanProficiency", Margin = new Thickness(0, 0, 23, 0) };
        Grid progressFrame = TalismanFrame("image/cmn/frm_progress.png", 128, 32, 128, 26,
            new Thickness(23, 26, 23, 0), false, sourceY: 3);
        progressFrame.Margin = new Thickness(0, 2, 0, 2);
        progress.Children.Add(progressFrame);
        var progressFill = new Viewbox { Width = 240, Height = 9, Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = TalismanSprite("image/cmn/pic_progres.png", 64, 32, 0, 0, 64, 9) };
        progress.Children.Add(progressFill);
        Grid proficiency = TalismanOutlinedText(TalismanFormat(catalog.Text("proficiency"), info.MaxProficiency, info.MaxProficiency), 16);
        proficiency.HorizontalAlignment = HorizontalAlignment.Center;
        proficiency.VerticalAlignment = VerticalAlignment.Center;
        progress.Children.Add(proficiency);
        treeArea.Children.Add(progress);
        var tree = new Grid();
        Grid.SetRow(tree, 1);
        treeArea.Children.Add(tree);
        var treeBackground = new Image { Stretch = Stretch.Fill };
        treeBackground.Loaded += async (_, _) =>
            treeBackground.Source = await (_talismanTreeBackground ??= LoadTalismanTreeBackgroundAsync());
        tree.Children.Add(treeBackground);
        var treeCanvas = new Canvas { Width = 304, Height = 316, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        tree.Children.Add(treeCanvas);
        TextBlock treeTitle = TalismanText(catalog.Text("talents"), 16);
        treeTitle.Width = 304;
        treeTitle.TextAlignment = TextAlignment.Center;
        Canvas.SetTop(treeTitle, 5);
        treeCanvas.Children.Add(treeTitle);

        foreach (GameTalismanTalent talent in info.Talents)
        {
            Button button = CreateTalismanTalentButton(talent);
            Canvas.SetLeft(button, talent.X + 5);
            Canvas.SetTop(button, talent.Y + 5);
            treeCanvas.Children.Add(button);
            _talismanTalentButtons.Add(talent.Id, button);
        }
        var reset = new Button
        {
            Content = catalog.Text("reset"),
            Tag = GameItemViewModel.LoadBitmap(catalog.Images.GetValueOrDefault("image/common/btn_common.png")),
            Style = (Style)((FrameworkElement)Content).Resources["WanxiangUpgradeButtonStyle"]
        };
        reset.Click += (_, _) =>
        {
            state.TalentLevels.Clear();
            _selectedTalismanTalentId = 0;
            RefreshTalismanTalents();
        };
        Canvas.SetLeft(reset, 102);
        Canvas.SetTop(reset, 282);
        treeCanvas.Children.Add(reset);
        TalismanTalentHost.Content = new Viewbox
        {
            Child = root,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        RefreshTalismanTalents();
    }

    private Button CreateTalismanTalentButton(GameTalismanTalent talent)
    {
        var icon = new Grid { Width = 40, Height = 40 };
        icon.Children.Add(new Image { Source = GameItemViewModel.LoadBitmap(talent.IconPath), Stretch = Stretch.Fill });
        var outline = new Border { BorderBrush = GameItemViewModel.BrushFromColor("#FFFFFF00"), BorderThickness = new Thickness(1), Opacity = 0 };
        icon.Children.Add(outline);
        foreach ((int x, int y) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1), (0, 0) })
        {
            TextBlock number = TalismanText("0", 20, x == 0 && y == 0 ? "#FF00FF00" : "#FF000000");
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;
            number.Margin = new Thickness(x, y, -x, -y);
            number.IsHitTestVisible = false;
            icon.Children.Add(number);
        }
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Content = icon,
            Tag = talent,
            Style = (Style)((FrameworkElement)Content).Resources["WanxiangImageButtonStyle"]
        };
        AutomationProperties.SetName(button, talent.Name);
        button.PointerEntered += (_, _) => outline.Opacity = 1;
        button.PointerExited += (_, _) => { outline.Opacity = 0; icon.Opacity = 1; };
        button.PointerPressed += (_, _) => icon.Opacity = 0.7;
        button.PointerReleased += (_, _) => icon.Opacity = 1;
        button.Click += (_, _) => { _selectedTalismanTalentId = talent.Id; RefreshTalismanTalents(); };
        button.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler((_, args) =>
        {
            ChangeTalismanTalent(talent, 1);
            args.Handled = true;
        }), true);
        button.RightTapped += (_, args) =>
        {
            ChangeTalismanTalent(talent, -1);
            args.Handled = true;
        };
        button.KeyDown += (_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Add or Windows.System.VirtualKey.Up)
            {
                ChangeTalismanTalent(talent, 1);
                args.Handled = true;
            }
            else if (args.Key is Windows.System.VirtualKey.Subtract or Windows.System.VirtualKey.Down)
            {
                ChangeTalismanTalent(talent, -1);
                args.Handled = true;
            }
        };
        return button;
    }

    private void ChangeTalismanTalent(GameTalismanTalent talent, int delta)
    {
        if (_selectedTalisman?.Record.Talisman is not { } info)
        {
            return;
        }
        GameTalismanSelection state = TalismanState(_selectedTalisman.Record);
        if (delta > 0)
        {
            state.Learn(info, talent);
        }
        else
        {
            state.TalentLevels[talent.Id] = Math.Max(0, state.TalentLevels.GetValueOrDefault(talent.Id) - 1);
            state.Normalize(info);
        }
        _selectedTalismanTalentId = talent.Id;
        RefreshTalismanTalents();
    }

    private void RefreshTalismanTalents()
    {
        if (_selectedTalisman is null || _repository is null || _talismanTalentEffect is null)
        {
            return;
        }
        GameTalismanCatalog catalog = _repository.Talismans;
        GameTalismanInfo info = _selectedTalisman.Record.Talisman!;
        GameTalismanSelection state = TalismanState(_selectedTalisman.Record);
        if (_talismanPointsText is not null)
        {
            _talismanPointsText.Text = TalismanFormat(catalog.Text("points"), state.Growth - state.UsedPoints);
        }
        foreach (GameTalismanTalent talent in info.Talents)
        {
            if (!_talismanTalentButtons.TryGetValue(talent.Id, out Button? button) || button.Content is not Grid icon)
            {
                continue;
            }
            int level = state.TalentLevels.GetValueOrDefault(talent.Id);
            TextBlock[] numbers = icon.Children.OfType<TextBlock>().ToArray();
            foreach (TextBlock number in numbers)
            {
                number.Text = level.ToString(CultureInfo.InvariantCulture);
            }
            numbers[^1].Foreground = GameItemViewModel.BrushFromColor(level == talent.MaxLevel
                ? GameTooltipFormatter.FlagColor
                : state.IsUnlocked(info, talent) ? GameTooltipFormatter.EquipColor : GameTooltipFormatter.HintColor);
            ToolTipService.SetToolTip(button, new ToolTip
            {
                Content = GameItemViewModel.CreateTooltipContent(TalismanTalentLines(talent, state)),
                MaxWidth = 360,
                FontFamily = new FontFamily(TalismanFont),
                Background = GameItemViewModel.BrushFromColor("#FF080D0B"),
                BorderBrush = GameItemViewModel.BrushFromColor("#FF94773C"),
                Padding = new Thickness(7)
            });
        }
        GameTalismanTalent? selected = info.Talents.FirstOrDefault(talent => talent.Id == _selectedTalismanTalentId);
        RichTextBlock effect = GameItemViewModel.CreateTooltipContent(selected is not null
            ? TalismanTalentLines(selected, state)
            : info.RefinementRows.Select((row, index) => new GameTooltipLine(GameTooltipAlignment.Left,
                new[] { new GameTooltipSpan($"[{index + 1}]{row}", index < state.ActivatedAttributes
                    ? GameTooltipFormatter.EquipColor : GameTooltipFormatter.HintColor) })).ToArray());
        effect.FontSize = 16;
        effect.LineHeight = 18;
        effect.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _talismanTalentEffect.Content = effect;
    }

    private IReadOnlyList<GameTooltipLine> TalismanTalentLines(GameTalismanTalent talent, GameTalismanSelection state)
    {
        GameTalismanCatalog catalog = _repository!.Talismans;
        int level = state.TalentLevels.GetValueOrDefault(talent.Id);
        var lines = new List<GameTooltipLine>();
        Add(talent.Name, GameTooltipFormatter.FlagColor);
        Add($"{catalog.Text("level")} {level}/{talent.MaxLevel}");
        Add(talent.Description);
        if (level > 0)
        {
            Effect(level);
        }
        if (level < talent.MaxLevel)
        {
            Effect(level + 1);
        }
        else if (!string.IsNullOrWhiteSpace(talent.FullLevelBonus))
        {
            Add(catalog.Text("bonus"), GameTooltipFormatter.FlagColor);
            Add(talent.FullLevelBonus);
        }
        GameTalismanInfo info = _selectedTalisman!.Record.Talisman!;
        if (state.Growth < talent.RequiredGrowth)
        {
            Add(catalog.Text("requireGrowth"), "#FFFF0000");
        }
        else if (!state.IsUnlocked(info, talent))
        {
            Add(TalismanFormat(catalog.Text("requirePoints"), talent.RequiredPoints), GameTooltipFormatter.FlagColor);
        }
        else if (state.CanLearn(info, talent))
        {
            Add(catalog.Text("learn"), GameTooltipFormatter.EquipColor);
        }
        return lines;

        void Effect(int rank)
        {
            Add($"{rank}{catalog.Text("effect")}", GameTooltipFormatter.FlagColor);
            Add(talent.EffectAt(rank));
        }
        void Add(string text, string color = GameTooltipFormatter.DefaultColor)
        {
            foreach (string line in text.Replace("\\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                lines.Add(new GameTooltipLine(GameTooltipAlignment.Left, new[] { new GameTooltipSpan(line, color) }));
            }
        }
    }

    private static string TalismanFormat(string format, params int[] values)
    {
        int index = 0;
        return Regex.Replace(format, "%[d%]", match => match.Value == "%%" ? "%" :
            values.ElementAtOrDefault(index++).ToString(CultureInfo.InvariantCulture));
    }

    private static TextBlock TalismanText(string text, double size = 14, string color = GameTooltipFormatter.DefaultColor) => new()
    {
        Text = text,
        FontFamily = new FontFamily(TalismanFont),
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = GameItemViewModel.BrushFromColor(color)
    };

    private static Grid TalismanOutlinedText(string text, double size, string color = GameTooltipFormatter.DefaultColor)
    {
        var result = new Grid { IsHitTestVisible = false };
        foreach ((int x, int y) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1), (0, 0) })
        {
            TextBlock layer = TalismanText(text, size, x == 0 && y == 0 ? color : "#FF000000");
            layer.TextWrapping = TextWrapping.NoWrap;
            layer.Margin = new Thickness(x, y, -x, -y);
            result.Children.Add(layer);
        }
        return result;
    }

    private Grid TalismanFrame(string path, int bitmapWidth, int bitmapHeight, int width, int height, Thickness caps,
        bool fillCenter = true, int sourceX = 0, int sourceY = 0)
    {
        var frame = new Grid();
        var patches = new Grid { IsHitTestVisible = false };
        frame.Children.Add(patches);
        double[] x = { 0, caps.Left, width - caps.Right, width };
        double[] y = { 0, caps.Top, height - caps.Bottom, height };
        for (int index = 0; index < 3; index++)
        {
            patches.ColumnDefinitions.Add(new ColumnDefinition { Width = index == 1
                ? new GridLength(1, GridUnitType.Star) : new GridLength(x[index + 1] - x[index]) });
            patches.RowDefinitions.Add(new RowDefinition { Height = index == 1
                ? new GridLength(1, GridUnitType.Star) : new GridLength(y[index + 1] - y[index]) });
        }
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1 && !fillCenter)
                {
                    continue;
                }
                double patchWidth = x[column + 1] - x[column];
                double patchHeight = y[row + 1] - y[row];
                if (patchWidth <= 0 || patchHeight <= 0)
                {
                    continue;
                }
                FrameworkElement patch = TalismanTiles(path, bitmapWidth, bitmapHeight,
                    sourceX + x[column], sourceY + y[row], patchWidth, patchHeight);
                Grid.SetRow(patch, row);
                Grid.SetColumn(patch, column);
                patches.Children.Add(patch);
            }
        }
        return frame;
    }

    private Canvas TalismanTiles(string path, int bitmapWidth, int bitmapHeight,
        double sourceX, double sourceY, double tileWidth, double tileHeight)
    {
        var tiles = new Canvas { IsHitTestVisible = false };
        tiles.SizeChanged += (_, args) =>
        {
            tiles.Children.Clear();
            tiles.Clip = new RectangleGeometry { Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
            for (double y = 0; y < args.NewSize.Height; y += tileHeight)
            {
                for (double x = 0; x < args.NewSize.Width; x += tileWidth)
                {
                    FrameworkElement tile = TalismanSprite(path, bitmapWidth, bitmapHeight, sourceX, sourceY, tileWidth, tileHeight);
                    Canvas.SetLeft(tile, x);
                    Canvas.SetTop(tile, y);
                    tiles.Children.Add(tile);
                }
            }
        };
        return tiles;
    }

    private Button TalismanSpriteButton(string path, int bitmapWidth, int bitmapHeight,
        int sourceX, int sourceY, int width, int height, bool vertical, bool repeat = false)
    {
        var presenter = new ContentPresenter { IsHitTestVisible = false };
        var button = new Button
        {
            Width = width, Height = height, Content = presenter,
            Style = (Style)((FrameworkElement)Content).Resources["WanxiangImageButtonStyle"]
        };
        bool over = false;
        bool pressed = false;
        void Refresh()
        {
            int state = !button.IsEnabled ? 3 : pressed ? 2 : over ? 1 : 0;
            int x = sourceX + (vertical ? 0 : state * width);
            int y = sourceY + (vertical ? state * height : 0);
            presenter.Content = repeat
                ? TalismanFrame(path, bitmapWidth, bitmapHeight, width, height, new Thickness(4), sourceX: x, sourceY: y)
                : new Viewbox { Child = TalismanSprite(path, bitmapWidth, bitmapHeight, x, y, width, height), Stretch = Stretch.Fill };
        }
        button.PointerEntered += (_, _) => { over = true; Refresh(); };
        button.PointerExited += (_, _) => { over = false; pressed = false; Refresh(); };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { pressed = true; Refresh(); }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => { pressed = false; Refresh(); }), true);
        button.PointerCanceled += (_, _) => { pressed = false; Refresh(); };
        button.IsEnabledChanged += (_, _) => Refresh();
        Refresh();
        return button;
    }

    private Grid TalismanClientScroll(ContentPresenter content)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(17) });
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Enabled
        };
        _talismanEffectScroll = scroll;
        layout.Children.Add(scroll);
        var slider = new Grid { Name = "TalismanClientScrollBar", Margin = new Thickness(1, 0, 1, 0) };
        Grid.SetColumn(slider, 1);
        layout.Children.Add(slider);
        Grid frame = TalismanFrame("image/cmn/frm_slider.png", 32, 64, 15, 45, new Thickness(5), false, 1, 1);
        frame.Margin = new Thickness(0, 9, 0, 9);
        slider.Children.Add(frame);
        var track = new Canvas { Margin = new Thickness(0, 10, 0, 10) };
        slider.Children.Add(track);
        Button thumb = TalismanSpriteButton("image/cmn/btn_scroll.png", 64, 32, 0, 0, 11, 18, false, true);
        Canvas.SetLeft(thumb, 2);
        track.Children.Add(thumb);
        Button up = TalismanSpriteButton("image/cmn/btn_up.png", 64, 16, 0, 0, 15, 10, false);
        up.VerticalAlignment = VerticalAlignment.Top;
        up.Click += (_, _) => scroll.ChangeView(null, Math.Max(0, scroll.VerticalOffset - 18), null, true);
        slider.Children.Add(up);
        Button down = TalismanSpriteButton("image/cmn/btn_down.png", 64, 16, 0, 0, 15, 10, false);
        down.VerticalAlignment = VerticalAlignment.Bottom;
        down.Click += (_, _) => scroll.ChangeView(null, Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + 18), null, true);
        slider.Children.Add(down);
        void Refresh()
        {
            double height = Math.Max(0, track.ActualHeight);
            thumb.Height = Math.Clamp(scroll.ExtentHeight > 0 ? height * scroll.ViewportHeight / scroll.ExtentHeight : height,
                Math.Min(18, height), height);
            Canvas.SetTop(thumb, scroll.ScrollableHeight > 0
                ? (height - thumb.Height) * scroll.VerticalOffset / scroll.ScrollableHeight : 0);
            up.IsEnabled = scroll.VerticalOffset > 0;
            down.IsEnabled = scroll.VerticalOffset < scroll.ScrollableHeight;
            thumb.IsEnabled = scroll.ScrollableHeight > 0;
        }
        double? dragY = null;
        double dragOffset = 0;
        thumb.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            dragY = args.GetCurrentPoint(track).Position.Y;
            dragOffset = scroll.VerticalOffset;
            thumb.CapturePointer(args.Pointer);
            args.Handled = true;
        }), true);
        thumb.PointerMoved += (_, args) =>
        {
            if (dragY is not { } start || track.ActualHeight <= thumb.Height)
            {
                return;
            }
            double offset = dragOffset + (args.GetCurrentPoint(track).Position.Y - start) *
                scroll.ScrollableHeight / (track.ActualHeight - thumb.Height);
            scroll.ChangeView(null, Math.Clamp(offset, 0, scroll.ScrollableHeight), null, true);
            args.Handled = true;
        };
        thumb.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            dragY = null;
            thumb.ReleasePointerCapture(args.Pointer);
        }), true);
        thumb.PointerCaptureLost += (_, _) => dragY = null;
        scroll.ViewChanged += (_, _) => Refresh();
        scroll.SizeChanged += (_, _) => Refresh();
        content.SizeChanged += (_, _) => Refresh();
        track.SizeChanged += (_, _) => Refresh();
        return layout;
    }

    private FrameworkElement TalismanSprite(string path, int bitmapWidth, int bitmapHeight,
        double x, double y, double width, double height)
    {
        var sprite = new Canvas { Width = width, Height = height, IsHitTestVisible = false,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) } };
        var image = new Image
        {
            Source = GameItemViewModel.LoadBitmap(_repository!.Talismans.Images.GetValueOrDefault(path)),
            Width = bitmapWidth, Height = bitmapHeight, Stretch = Stretch.Fill
        };
        Canvas.SetLeft(image, -x);
        Canvas.SetTop(image, -y);
        sprite.Children.Add(image);
        return sprite;
    }

    private async Task<WriteableBitmap> LoadTalismanTreeBackgroundAsync()
    {
        const int width = 302;
        const int height = 336;
        var pixels = new byte[width * height * 4];
        foreach ((int part, int partWidth, int partHeight, int x, int y) in new[]
                 { (1, 256, 256, 0, 0), (2, 46, 256, 256, 0), (3, 256, 80, 0, 256), (4, 46, 80, 256, 256) })
        {
            using var stream = File.OpenRead(_repository!.Talismans.Images[$"image/talent_frm/t{part}.png"]);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            byte[] source = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage))
                .DetachPixelData();
            for (int row = 0; row < partHeight; row++)
            {
                Buffer.BlockCopy(source, row * (int)decoder.PixelWidth * 4,
                    pixels, ((row + y) * width + x) * 4, partWidth * 4);
            }
        }
        var result = new WriteableBitmap(width, height);
        using Stream target = result.PixelBuffer.AsStream();
        await target.WriteAsync(pixels);
        result.Invalidate();
        return result;
    }
}
