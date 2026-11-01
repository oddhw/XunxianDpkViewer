using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow
{
    private IReadOnlyList<GameItemViewModel> _talismanItems = Array.Empty<GameItemViewModel>();
    private GameItemViewModel? _selectedTalisman;
    private bool _updatingTalismanFilters;
    private bool _updatingTalismanState;
    private bool _talismanViewClosed;
    private readonly Dictionary<string, GameTalismanSelection> _talismanStates = new(StringComparer.Ordinal);

    private void ConfigureTalismanView()
    {
        if (_talismanViewClosed || _repository is null)
        {
            return;
        }

        GameTalismanCatalog catalog = _repository.Talismans;
        if (_talismanItems.Count == 0)
        {
            _talismanItems = catalog.Items.Select(record => new GameItemViewModel(record, deferTooltip: true)).ToArray();
        }

        TalismanLevelLabel.Text = catalog.LevelLabel;
        TalismanProfessionLabel.Text = catalog.ProfessionLabel;
        TalismanGrowthLabel.Text = catalog.Text("growth").Split('：')[0];
        TalismanActivatedLabel.Text = catalog.Text("activated");
        _updatingTalismanFilters = true;
        try
        {
            TalismanLevelBox.ItemsSource = new[] { new GameFilterOption(string.Empty, catalog.AllText) }
                .Concat(catalog.Items.Select(record => record.Talisman!)
                    .Where(info => info.Level > 0)
                    .DistinctBy(TalismanStageId)
                    .OrderBy(info => info.DisplayRank)
                    .ThenBy(info => info.Level)
                    .Select(info => new GameFilterOption(TalismanStageId(info),
                        info.DisplayRank > 0 ? $"{info.DisplayRank}阶{info.Level}级{catalog.Name}" : catalog.Text("novice"))))
                .ToArray();
            TalismanProfessionBox.ItemsSource = new[] { new GameFilterOption(string.Empty, catalog.AllText) }
                .Concat(catalog.Items.Select(record => record.Talisman!)
                    .Where(info => !string.IsNullOrWhiteSpace(info.Profession))
                    .DistinctBy(info => info.ProfessionId)
                    .OrderBy(info => info.ProfessionId)
                    .Select(info => new GameFilterOption(info.ProfessionId.ToString(), info.Profession)))
                .ToArray();
            TalismanLevelBox.SelectedIndex = 0;
            TalismanProfessionBox.SelectedIndex = 0;
        }
        finally
        {
            _updatingTalismanFilters = false;
        }

        ApplyTalismanFilter();
    }

    private void ApplyTalismanFilter()
    {
        if (_talismanViewClosed || _updatingTalismanFilters || _selectedCategory?.Id != "talisman")
        {
            return;
        }

        string stage = (TalismanLevelBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string profession = (TalismanProfessionBox.SelectedItem as GameFilterOption)?.Id ?? string.Empty;
        string query = InfoSearchBox.Text.Trim();
        GameItemViewModel[] matches = _talismanItems
            .Where(item => string.IsNullOrEmpty(stage) || TalismanStageId(item.Record.Talisman!) == stage)
            .Where(item => string.IsNullOrEmpty(profession) || item.Record.Talisman!.ProfessionId.ToString() == profession)
            .Where(item => string.IsNullOrEmpty(query) || MatchesTalisman(item.Record, query))
            .OrderBy(item => item.Record.Talisman!.DisplayRank)
            .ThenBy(item => item.Record.Talisman!.Level)
            .ThenBy(item => item.Record.Talisman!.ProfessionId)
            .ThenByDescending(item => int.TryParse(item.Record.Id, out int id) ? id : 0)
            .ToArray();
        GameItemViewModel? selection = matches.FirstOrDefault(item => item.Record.Id == _selectedTalisman?.Record.Id)
                                       ?? matches.FirstOrDefault();
        TalismanList.ItemsSource = matches;
        TalismanList.SelectedItem = selection;
        _selectedTalisman = selection;
        CategoryCountText.Text = matches.Length.ToString("N0");
        TalismanEmptyState.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectedTalisman();
    }

    private static string TalismanStageId(GameTalismanInfo info) =>
        info.DisplayRank > 0 ? FormattableString.Invariant($"{info.DisplayRank}:{info.Level}") : "0";

    private static bool MatchesTalisman(GameInfoRecord record, string query) =>
        Matches(record, query) ||
        record.Talisman!.RankText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        record.Talisman.RefinementRows.Any(row => row.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        record.Talisman.Talents.Any(talent => talent.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            talent.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        record.Tooltip!.Level.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void TalismanFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyTalismanFilter();

    private void TalismanTooltipFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } grid &&
            grid.Children[0] is Canvas { Children.Count: > 0 } canvas && canvas.Children[0] is Image frame)
        {
            frame.Width = e.NewSize.Width;
            frame.Height = e.NewSize.Height;
        }
    }

    private void TalismanList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTalisman = TalismanList.SelectedItem as GameItemViewModel;
        UpdateSelectedTalisman();
    }

    private void UpdateSelectedTalisman()
    {
        if (_talismanViewClosed)
        {
            return;
        }
        TalismanDetails.Visibility = _selectedTalisman is null ? Visibility.Collapsed : Visibility.Visible;
        TalismanTalentScroll.Visibility = _selectedTalisman is null ? Visibility.Collapsed : Visibility.Visible;
        TalismanGrowthBox.IsEnabled = _selectedTalisman is not null;
        TalismanActivatedBox.IsEnabled = _selectedTalisman is not null;
        if (_selectedTalisman is null)
        {
            SelectedTalismanTooltipHost.Content = null;
            TalismanTalentHost.Content = null;
            return;
        }

        GameTalismanInfo info = _selectedTalisman.Record.Talisman!;
        GameTalismanSelection state = TalismanState(_selectedTalisman.Record);
        _updatingTalismanState = true;
        try
        {
            TalismanGrowthBox.Maximum = info.MaxGrowth;
            TalismanGrowthBox.Value = state.Growth;
            TalismanActivatedBox.Maximum = info.RefinementRows.Count;
            TalismanActivatedBox.Value = state.ActivatedAttributes;
        }
        finally
        {
            _updatingTalismanState = false;
        }
        SelectedTalismanImage.Source = _selectedTalisman.ImageSource;
        SelectedTalismanTooltipFrame.Source = _selectedTalisman.TooltipFrameSource;
        RefreshTalismanState();
        TalismanDetailScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private GameTalismanSelection TalismanState(GameInfoRecord record)
    {
        if (!_talismanStates.TryGetValue(record.Id, out GameTalismanSelection? state))
        {
            state = new GameTalismanSelection
            {
                Growth = record.Talisman!.MaxGrowth,
                ActivatedAttributes = record.Talisman.RefinementRows.Count
            };
            _talismanStates.Add(record.Id, state);
        }
        return state;
    }

    private void TalismanItemLayout_SizeChanged(object sender, SizeChangedEventArgs e) =>
        TalismanListColumn.Width = new GridLength(Math.Clamp(e.NewSize.Width * 0.16, 180, 260));

    private void TalismanSideBySideScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double comparisonWidth = Math.Max(950, e.NewSize.Width);
        double attributeWidth = Math.Clamp((comparisonWidth - 18) * 0.42, 360, 672);
        attributeWidth = Math.Min(attributeWidth, comparisonWidth - 18 - 572);
        TalismanAttributeColumn.Width = new GridLength(attributeWidth);
        double talentWidth = Math.Max(572, comparisonWidth - attributeWidth - 18);
        TalismanTalentColumn.Width = new GridLength(talentWidth);
        double heightLimitedWidth = Math.Max(572, (e.NewSize.Height - 54) * 572 / 430);
        TalismanTalentHost.Width = Math.Min(talentWidth, heightLimitedWidth);
    }

    private void TalismanState_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_updatingTalismanState || _selectedTalisman is null || double.IsNaN(e.NewValue))
        {
            return;
        }
        GameTalismanSelection state = TalismanState(_selectedTalisman.Record);
        if (ReferenceEquals(sender, TalismanGrowthBox))
        {
            state.Growth = (int)Math.Round(e.NewValue);
        }
        else
        {
            state.ActivatedAttributes = (int)Math.Round(e.NewValue);
        }
        state.Normalize(_selectedTalisman.Record.Talisman!);
        RefreshTalismanState();
    }

    private void RefreshTalismanState()
    {
        if (_talismanViewClosed || _selectedTalisman is null)
        {
            return;
        }
        SelectedTalismanTooltipHost.Content = GameItemViewModel.CreateTooltipContent(
            GameTooltipFormatter.FormatTalisman(_selectedTalisman.Record, TalismanState(_selectedTalisman.Record)));
        BuildTalismanTalentView();
    }

    private void TalismanItemTooltip_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip { Tag: GameItemViewModel item, Content: Grid grid } &&
            grid.Children.LastOrDefault() is ContentPresenter presenter)
        {
            presenter.Content = GameItemViewModel.CreateTooltipContent(
                GameTooltipFormatter.FormatTalisman(item.Record, TalismanState(item.Record)));
        }
    }
}
