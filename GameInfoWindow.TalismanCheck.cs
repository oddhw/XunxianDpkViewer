#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow
{
    internal async Task VerifyTalismanViewAsync(string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        string logPath = Path.Combine(outputFolder, "talisman-view-check.log");
        try
        {
            for (int attempt = 0; attempt < 600 &&
                 (_repository is null || LoadingState.Visibility == Visibility.Visible); attempt++)
            {
                await Task.Delay(100);
            }
            Require(_repository?.Talismans.Items.Count > 0, "Client talismans were not loaded.");
            CategoryList.SelectedItem = _categories.Single(category => category.Id == "talisman");
            await Task.Delay(600);
            Require(TalismanView.Visibility == Visibility.Visible && WeaponView.Visibility == Visibility.Collapsed,
                "Talisman category did not switch to its own view.");
            Require(TalismanList.Items.Count == _repository!.Talismans.Items.Count,
                "Default talisman list is incomplete.");
            File.WriteAllText(Path.Combine(outputFolder, "talisman-data-check.log"),
                SelfTest.VerifyTalismans(_repository.Talismans, _resourceFolder!));
            await VerifyTalismanStageSortingAsync(outputFolder);
            await CaptureTalismanViewAsync(outputFolder, "talisman-wide.png", 2560, 1200);

            InfoSearchBox.Text = "32399";
            await Task.Delay(250);
            Require(TalismanList.Items.Count == 1 && _selectedTalisman?.Record.Id == "32399",
                "Search did not select the exact talisman.");
            TalismanActivatedBox.Value = 11;
            Require(SelectedTalismanTooltipHost.Content is not null &&
                    TalismanState(_selectedTalisman!.Record).ActivatedAttributes == 11,
                "Selection did not display the integrated talisman attributes.");
            await CaptureTalismanViewAsync(outputFolder, "talisman-selected.png", 2560, 1200);
            Require(TalismanTalentHost.ActualWidth > 650,
                "The talent pane remains fixed at the client source width on a wide screen.");
            Require(_repository.Talismans.AppearanceNames.FirstOrDefault() == "默认外形" &&
                    _selectedTalisman!.Record.Talisman!.MaxProficiency == 100000 &&
                    Descendants(TalismanTalentHost).OfType<FrameworkElement>().Any(element => element.Name == "TalismanProficiency") &&
                    Descendants(TalismanTalentHost).OfType<FrameworkElement>().Any(element => element.Name == "TalismanClientBackground"),
                "The original client appearance selector, progress row or tiled background is missing.");
            await Task.Delay(150);
            Require(_talismanEffectScroll!.ScrollableHeight > 0,
                "The full client refinement text does not have a working scroll region.");
            Button effectDown = Descendants(TalismanTalentHost).OfType<Grid>()
                .Single(grid => grid.Name == "TalismanClientScrollBar").Children.OfType<Button>()
                .Single(button => button.VerticalAlignment == VerticalAlignment.Bottom);
            ((IInvokeProvider)new ButtonAutomationPeer(effectDown).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(150);
            Require(_talismanEffectScroll.VerticalOffset > 0, "The original client scroll arrow does not move the text.");
            _talismanEffectScroll.ChangeView(null, 0, null, true);
            await CaptureTalismanViewAsync(outputFolder, "talisman-ultrawide.png", 3440, 1440);
            Require(TalismanTalentHost.ActualWidth > 800,
                "The talent pane does not use the additional available window space.");
            await CaptureTalismanViewAsync(outputFolder, "talisman-desktop.png", 1920, 1080);
            Require(TalismanSideBySideScroll.ScrollableWidth <= 1,
                "A standard desktop window still needs horizontal scrolling to show both panes.");
            double talentOffset = TalismanTalentScroll.VerticalOffset;
            TalismanDetailScroll.ChangeView(null, TalismanDetailScroll.ScrollableHeight, null, disableAnimation: true);
            await CaptureTalismanViewAsync(outputFolder, "talisman-refinement.png", 2560, 1200);
            Require(TalismanTalentScroll.VerticalOffset == talentOffset,
                "Scrolling attributes also moves the adjacent talent pane.");
            TalismanDetailScroll.ChangeView(null, 0, null, disableAnimation: true);

            Require(TalismanTalentScroll.Visibility == Visibility.Visible && TalismanDetailScroll.Visibility == Visibility.Visible &&
                    _talismanTalentButtons.Count == 13,
                "Attributes and the selected talent tree are not visible together.");
            GameTalismanInfo bow = _selectedTalisman!.Record.Talisman!;
            GameTalismanSelection bowState = TalismanState(_selectedTalisman.Record);
            ChangeTalismanTalent(bow.Talents[^1], 1);
            Require(bowState.UsedPoints == 0, "Locked talent can be learned before its prerequisite investment.");
            foreach ((int id, int count) in new[] { (2693, 10), (2697, 10), (2694, 10), (2691, 5), (2695, 5),
                         (2699, 5), (2692, 5), (2696, 5), (2700, 5), (2707, 1) })
            {
                GameTalismanTalent talent = bow.Talents.Single(node => node.Id == id);
                for (int point = 0; point < count; point++)
                {
                    ChangeTalismanTalent(talent, 1);
                }
            }
            Require(bowState.UsedPoints == 61 && bowState.TalentLevels.GetValueOrDefault(2707) == 1 &&
                    _talismanPointsText!.Text == "剩余能力点：0", "Learning does not update talent ranks and remaining points.");
            ChangeTalismanTalent(bow.Talents[0], 1);
            Require(bowState.UsedPoints == 61, "Talent learning exceeds the point budget.");
            _selectedTalismanTalentId = 0;
            RefreshTalismanTalents();
            await CaptureTalismanViewAsync(outputFolder, "talisman-talents-allocated.png", 2560, 1200);
            ChangeTalismanTalent(bow.Talents.Single(node => node.Id == 2693), -1);
            Require(!bowState.TalentLevels.ContainsKey(2707), "Removing invested points keeps an invalid final talent.");
            Button reset = Descendants(TalismanTalentHost).OfType<Button>()
                .Single(button => button.Content as string == "法宝重置");
            ((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            Require(bowState.UsedPoints == 0, "The visible reset button does not clear talent allocations.");
            ChangeTalismanTalent(bow.Talents[0], 1);
            InfoSearchBox.Text = "32408";
            await Task.Delay(150);
            Require(_selectedTalisman!.Record.Id == "32408" &&
                    _talismanTalentButtons.Keys.SequenceEqual(_selectedTalisman.Record.Talisman!.Talents.Select(node => node.Id)) &&
                    TalismanState(_selectedTalisman.Record).UsedPoints == 0,
                "Switching talismans mixes trees or point allocations.");
            InfoSearchBox.Text = "32399";
            await Task.Delay(150);
            Require(TalismanState(_selectedTalisman!.Record).UsedPoints == 1,
                "Returning to a talisman loses its query state.");
            TalismanGrowthBox.Value = 60;
            Require(TalismanState(_selectedTalisman.Record).Growth == 60 &&
                    _talismanPointsText!.Text == "剩余能力点：59" && TalismanDetailScroll.Visibility == Visibility.Visible,
                "Changing growth does not update the adjacent talent pane.");
            TalismanGrowthBox.Value = 61;
            await CaptureTalismanViewAsync(outputFolder, "talisman-talents-short.png", 2560, 760);
            Require(TalismanTalentScroll.ScrollableHeight > 0,
                "A short viewport clips the enlarged talent pane without vertical scrolling.");
            TalismanTalentScroll.ChangeView(null, TalismanTalentScroll.ScrollableHeight, null, true);
            await Task.Delay(150);
            Require(TalismanTalentScroll.VerticalOffset > 0, "The bottom of the enlarged talent pane is unreachable.");
            TalismanTalentScroll.ChangeView(null, 0, null, true);
            await CaptureTalismanViewAsync(outputFolder, "talisman-talents-compact.png", 1180, 760);
            Require(TalismanSideBySideScroll.ScrollableWidth > 0,
                "A narrow viewport clips the native talent pane without horizontal scrolling.");
            TalismanSideBySideScroll.ChangeView(TalismanSideBySideScroll.ScrollableWidth, null, null, disableAnimation: true);
            await CaptureTalismanViewAsync(outputFolder, "talisman-talents-compact-scrolled.png", 1180, 760);
            Require(TalismanSideBySideScroll.HorizontalOffset > 0,
                "The adjacent talent pane is unreachable in a narrow viewport.");
            TalismanSideBySideScroll.ChangeView(0, null, null, disableAnimation: true);

            InfoSearchBox.Text = string.Empty;
            TalismanProfessionBox.SelectedItem = ((IEnumerable<GameFilterOption>)TalismanProfessionBox.ItemsSource)
                .Single(option => option.Id == "13");
            await Task.Delay(200);
            Require(TalismanList.Items.Count > 0 && TalismanList.Items.Cast<GameItemViewModel>()
                .All(item => item.Record.Talisman!.ProfessionId == 13), "Profession filter mixes other classes.");
            TalismanLevelBox.SelectedItem = ((IEnumerable<GameFilterOption>)TalismanLevelBox.ItemsSource)
                .Single(option => option.Id == "14:260");
            await Task.Delay(200);
            Require(TalismanList.Items.Count > 0 && TalismanList.Items.Cast<GameItemViewModel>()
                .All(item => item.Record.Talisman is { DisplayRank: 14, Level: 260, ProfessionId: 13 }),
                "Stage and profession filters do not combine correctly.");
            await CaptureTalismanViewAsync(outputFolder, "talisman-compact.png", 1180, 760);

            InfoSearchBox.Text = "__no_talisman_match__";
            await Task.Delay(150);
            Require(TalismanList.Items.Count == 0 && TalismanEmptyState.Visibility == Visibility.Visible &&
                    TalismanDetails.Visibility == Visibility.Collapsed && TalismanTalentScroll.Visibility == Visibility.Collapsed,
                "Empty search results leave a stale talisman detail visible.");
            InfoSearchBox.Text = string.Empty;
            TalismanProfessionBox.SelectedIndex = 0;
            TalismanLevelBox.SelectedIndex = 0;
            TalismanList.SelectedIndex = TalismanList.Items.Count - 1;
            await Task.Delay(200);
            Require(_selectedTalisman?.Record.Id == (TalismanList.SelectedItem as GameItemViewModel)?.Record.Id &&
                    SelectedTalismanTooltipHost.Content is not null,
                "Changing the list selection did not refresh the detail.");
            CategoryList.SelectedItem = _categories.Single(category => category.Id == "equipment");
            Require(TalismanView.Visibility == Visibility.Collapsed && EquipmentView.Visibility == Visibility.Visible,
                "Switching back to equipment leaves the talisman view visible.");
            await VerifyEquipmentProfessionFiltersAsync(outputFolder);
            CategoryList.SelectedItem = _categories.Single(category => category.Id == "talisman");
            Require(TalismanList.Items.Count == _repository.Talismans.Items.Count,
                "Returning to the talisman category loses records.");
            Close();
            Require(_talismanViewClosed, "Closing the window does not stop pending talisman refreshes.");
            ApplyTalismanFilter();
            UpdateSelectedTalisman();
            RefreshTalismanState();
            File.WriteAllText(logPath, "TALISMAN VIEW CHECK PASSED\nResponsive adjacent panes at 1180, 1920, 2560 and 3440 pixels, uniform enlargement, original tiled textures, appearance and proficiency rows, native scroll arrows, independent scrolling, short-window scrolling, growth synchronization, point allocation, reset, selection, filters, empty results and category switching checked.\n");
        }
        catch (Exception exception)
        {
            File.WriteAllText(logPath, exception.ToString());
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    private async Task VerifyEquipmentProfessionFiltersAsync(string outputFolder)
    {
        File.WriteAllText(Path.Combine(outputFolder, "equipment-profession-client-check.log"),
            SelfTest.VerifyEquipmentProfessions(_repository!, _resourceFolder!));
        EquipmentStageBox.SelectedItem = ((IEnumerable<GameFilterOption>)EquipmentStageBox.ItemsSource)
            .Single(option => option.Id == "xingyun");
        foreach ((string family, string itemId, string fileName) in new[]
                 { ("法师", "31468", "equipment-mage.png"), ("符咒师", "31476", "equipment-talismanist.png") })
        {
            EquipmentProfessionBox.SelectedItem = ((IEnumerable<GameFilterOption>)EquipmentProfessionBox.ItemsSource)
                .Single(option => option.Id == family + " / 天狐");
            await Task.Delay(150);
            Require(EquipmentList.Items.Count > 0 && EquipmentList.Items.Cast<GameItemViewModel>()
                        .All(item => item.Record.Tooltip!.RequiredProfessions[0] == family),
                $"Equipment filter mixes another family into {family}.");
            EquipmentList.SelectedItem = EquipmentList.Items.Cast<GameItemViewModel>().Single(item => item.Record.Id == itemId);
            await Task.Delay(100);
            Require(SelectedEquipmentMetaText.Text.Contains(family + " / 天狐", StringComparison.Ordinal) &&
                    SelectedEquipmentTooltipHost.Content is RichTextBlock tooltip && tooltip.Blocks.OfType<Paragraph>()
                        .Any(paragraph => string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)) == "需要：" + family + " 天狐"),
                $"Equipment header or visible tooltip uses the wrong family for client item {itemId}.");
            AppWindow.Resize(new SizeInt32(1920, 1080));
            await Task.Delay(400);
            SelectedEquipmentTooltipHost.StartBringIntoView(new BringIntoViewOptions
                { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync((FrameworkElement)Content);
            byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(outputFolder);
            StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
        }
    }

    private async Task VerifyTalismanStageSortingAsync(string outputFolder)
    {
        GameFilterOption[] options = ((IEnumerable<GameFilterOption>)TalismanLevelBox.ItemsSource).ToArray();
        Require(options[0].Id.Length == 0 &&
                options.Single(option => option.Id == "0").Name == _repository!.Talismans.Text("novice") &&
                options.Single(option => option.Id == "1:10").Name == "1阶10级法宝" &&
                options.Single(option => option.Id == "1:20").Name == "1阶20级法宝" &&
                options.Single(option => option.Id == "3:80").Name == "3阶80级法宝" &&
                options.Single(option => option.Id == "4:80").Name == "4阶80级法宝",
            "Stage filters do not distinguish the client rank and level.");
        Require(Array.FindIndex(options, option => option.Id == "1:10") < Array.FindIndex(options, option => option.Id == "1:20") &&
                Array.FindIndex(options, option => option.Id == "3:80") < Array.FindIndex(options, option => option.Id == "4:80"),
            "Stage filter choices are in the wrong progression order.");
        GameTalismanInfo[] stages = TalismanList.Items.Cast<GameItemViewModel>().Select(item => item.Record.Talisman!)
            .DistinctBy(info => (info.DisplayRank, info.Level)).ToArray();
        for (int index = 1; index < stages.Length; index++)
        {
            Require(stages[index - 1].DisplayRank < stages[index].DisplayRank ||
                    stages[index - 1].DisplayRank == stages[index].DisplayRank && stages[index - 1].Level < stages[index].Level,
                "The talisman list still mixes stages with a shared level.");
        }
        TalismanProfessionBox.SelectedItem = ((IEnumerable<GameFilterOption>)TalismanProfessionBox.ItemsSource)
            .Single(option => option.Id == "6");
        foreach ((string stage, string itemId, string file) in new[]
                 { ("3:80", "8224", "talisman-rank3-level80.png"), ("4:80", "8248", "talisman-rank4-level80.png") })
        {
            TalismanLevelBox.SelectedItem = options.Single(option => option.Id == stage);
            await Task.Delay(150);
            GameInfoRecord[] records = TalismanList.Items.Cast<GameItemViewModel>().Select(item => item.Record).ToArray();
            Require(records.Length > 0 && records.Any(record => record.Id == itemId) &&
                    records.All(record => TalismanStageId(record.Talisman!) == stage && record.Talisman!.ProfessionId == 6),
                $"Stage {stage} excludes a client item or includes another stage or profession.");
            await CaptureTalismanViewAsync(outputFolder, file, 1920, 1080);
        }
        TalismanLevelBox.SelectedIndex = 0;
        TalismanProfessionBox.SelectedIndex = 0;
        string[] clientStages = options.Skip(1).Select(option => option.Name).ToArray();
        File.WriteAllLines(Path.Combine(outputFolder, "talisman-stage-check.log"),
            new[] { "TALISMAN STAGE CHECK PASSED", "Rank 3 level 80 / rank 4 level 80 and profession filters verified against client items 8224 / 8248." }
                .Concat(clientStages));
    }

    private async Task CaptureTalismanViewAsync(string outputFolder, string fileName, int width, int height)
    {
        string? levelBefore = (TalismanLevelBox.SelectedItem as GameFilterOption)?.Id;
        string? professionBefore = (TalismanProfessionBox.SelectedItem as GameFilterOption)?.Id;
        int countBefore = TalismanList.Items.Count;
        AppWindow.Resize(new SizeInt32(width, height));
        await Task.Delay(700);
        var root = (FrameworkElement)Content;
        root.UpdateLayout();
        Require(levelBefore == (TalismanLevelBox.SelectedItem as GameFilterOption)?.Id &&
                professionBefore == (TalismanProfessionBox.SelectedItem as GameFilterOption)?.Id &&
                countBefore == TalismanList.Items.Count,
            $"Resize changed the talisman filters: {levelBefore}/{professionBefore}/{countBefore} -> " +
            $"{(TalismanLevelBox.SelectedItem as GameFilterOption)?.Id}/{(TalismanProfessionBox.SelectedItem as GameFilterOption)?.Id}/{TalismanList.Items.Count}.");
        Require(TalismanList.ActualWidth >= 180 && TalismanDetailArea.ActualWidth >= 280,
            "Talisman list or detail column is clipped.");
        double listRight = TalismanList.TransformToVisual(TalismanView)
            .TransformPoint(new Windows.Foundation.Point(TalismanList.ActualWidth, 0)).X;
        double detailLeft = TalismanDetailArea.TransformToVisual(TalismanView)
            .TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        double detailRight = TalismanDetailArea.TransformToVisual(TalismanView)
            .TransformPoint(new Windows.Foundation.Point(TalismanDetailArea.ActualWidth, 0)).X;
        Require(listRight <= detailLeft && detailRight <= TalismanView.ActualWidth + 1,
            "Talisman columns overlap or extend beyond the viewport.");
        if (TalismanTalentScroll.Visibility == Visibility.Visible)
        {
            Require(_talismanNativeWindow is not null, "Native talent window is missing.");
            Windows.Foundation.Point nativeTop = _talismanNativeWindow!.TransformToVisual(TalismanSideBySideLayout)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            Windows.Foundation.Point nativeBottom = _talismanNativeWindow.TransformToVisual(TalismanSideBySideLayout)
                .TransformPoint(new Windows.Foundation.Point(572, 430));
            double attributesRight = TalismanDetailScroll.TransformToVisual(TalismanSideBySideLayout)
                .TransformPoint(new Windows.Foundation.Point(TalismanDetailScroll.ActualWidth, 0)).X;
            double attributeTop = TalismanTooltipFrameBounds.TransformToVisual(TalismanSideBySideLayout)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y + TalismanDetailScroll.VerticalOffset;
            double scale = (nativeBottom.X - nativeTop.X) / 572;
            Require(nativeTop.X >= attributesRight + 17 &&
                    Math.Abs(nativeTop.Y + TalismanTalentScroll.VerticalOffset - attributeTop) <= 1 &&
                    scale >= 0.99 && Math.Abs(nativeBottom.Y - nativeTop.Y - 430 * scale) <= 1 &&
                    Math.Abs(nativeBottom.X - nativeTop.X - TalismanTalentHost.ActualWidth) <= 1,
                "The talent pane is not uniformly scaled to the available width beside the item tooltip.");
            if (TalismanSideBySideScroll.ActualWidth >= TalismanSideBySideLayout.ActualWidth)
            {
                Require(TalismanSideBySideScroll.ScrollableWidth <= 1 && nativeBottom.X <= TalismanSideBySideScroll.ActualWidth + 1,
                    "Wide layout leaves part of the adjacent talent pane outside the visible area.");
            }
            foreach (Button button in _talismanTalentButtons.Values)
            {
                Windows.Foundation.Point top = button.TransformToVisual(_talismanNativeWindow)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                Require(top.X >= 0 && top.Y >= 0 && top.X + button.ActualWidth <= 572 &&
                        top.Y + button.ActualHeight <= 430 && button.IsHitTestVisible,
                    "A native talent icon is clipped or cannot receive input.");
            }
        }
        Require(TalismanDetailScroll.Visibility == Visibility.Collapsed ||
                (Math.Abs(SelectedTalismanTooltipFrame.Width - TalismanTooltipFrameBounds.ActualWidth) <= 1 &&
                Math.Abs(SelectedTalismanTooltipFrame.Height - TalismanTooltipFrameBounds.ActualHeight) <= 1),
            "Talisman tooltip background did not resize with the text.");
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && pixels.Distinct().Take(12).Count() >= 12,
            "Talisman view rendered a blank bitmap.");
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(outputFolder);
        StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        File.AppendAllText(Path.Combine(outputFolder, "layout.log"),
            $"{fileName}: root={root.ActualWidth}x{root.ActualHeight}; view={TalismanView.ActualWidth}; " +
            $"list={TalismanList.ActualWidth}; detail={TalismanDetailScroll.ActualWidth}; talent={TalismanTalentHost.ActualWidth}; horizontalScroll={TalismanSideBySideScroll.ScrollableWidth}; frame={TalismanTooltipFrameBounds.ActualWidth}x{TalismanTooltipFrameBounds.ActualHeight}; " +
            $"image={SelectedTalismanTooltipFrame.ActualWidth}x{SelectedTalismanTooltipFrame.ActualHeight}; level={levelBefore}; profession={professionBefore}; count={countBefore}\n");
        Require(TalismanDetailScroll.Visibility == Visibility.Collapsed ||
                Math.Abs(TalismanTooltipFrameBounds.ActualHeight - SelectedTalismanTooltipHost.ActualHeight - 13) <= 2,
            $"Talisman tooltip frame does not follow the text height: frame={TalismanTooltipFrameBounds.ActualHeight}, content={SelectedTalismanTooltipHost.ActualHeight}.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
#endif
