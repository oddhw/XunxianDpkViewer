using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow
{
    private readonly DispatcherQueueTimer _clientDataTimer;
    private GameClientDataVersion? _pendingClientVersion;
    private bool _clientDataLoading;
    private bool _clientDataClosed;

    private async Task RefreshClientDataIfChangedAsync()
    {
        if (!_loaded || _clientDataLoading || _clientDataClosed)
        {
            return;
        }

        try
        {
            GameClientDataVersion? version = GameClientInfoReader.GetDataVersion(_resourceFolder);
            if (version is null || version == _repository?.ClientDataVersion)
            {
                _pendingClientVersion = null;
                return;
            }

            // Wait for two matching observations so an active client patch is not loaded halfway.
            if (version != _pendingClientVersion)
            {
                _pendingClientVersion = version;
                return;
            }

            await ReloadClientDataAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private async Task ReloadClientDataAsync()
    {
        if (_clientDataLoading || _clientDataClosed)
        {
            return;
        }

        _clientDataLoading = true;
        try
        {
            GameInfoRepository repository = await Task.Run(() => GameInfoRepository.Load(_resourceFolder));
            if (_clientDataClosed ||
                repository.ClientDataVersion != GameClientInfoReader.GetDataVersion(_resourceFolder) ||
                (repository.Categories.Count == 0 && _categories.Count > 0))
            {
                return;
            }

            ApplyClientData(repository);
            _pendingClientVersion = null;
        }
        finally
        {
            _clientDataLoading = false;
        }
    }

    private void ApplyClientData(GameInfoRepository repository)
    {
        string? selectedCategory = _selectedCategory?.Id;
        string query = InfoSearchBox.Text;
        GameItemViewModel? selectedWeapon = _selectedWeapon;
        GameItemViewModel? selectedEquipment = _selectedEquipment;
        GameItemViewModel? selectedTalisman = _selectedTalisman;
        ComboBox[] filters = selectedCategory switch
        {
            "equipment" => new[] { EquipmentStageBox, EquipmentProfessionBox, EquipmentPartBox },
            "weapon" => new[] { WeaponLevelBox, WeaponProfessionBox },
            "talisman" => new[] { TalismanLevelBox, TalismanProfessionBox },
            _ => Array.Empty<ComboBox>()
        };
        string?[] filterIds = filters.Select(box => (box.SelectedItem as GameFilterOption)?.Id).ToArray();

        _equipmentFireTimer.Stop();
        _equipmentSimulators.Clear();
        _activeEquipmentSimulator = null;
        _activeEquipmentEnhancement = null;
        _talismanItems = Array.Empty<GameItemViewModel>();
        _repository = repository;
        _categories = repository.Categories;
        _yinYangCatalog = repository.YinYang;
        _wanxiangCatalog = repository.Wanxiang;
        for (int index = 0; index < _wanxiangSlots.Length; index++)
        {
            int? seriesId = _wanxiangSlots[index]?.Id;
            _wanxiangSlots[index] = _wanxiangCatalog.GemSeries.FirstOrDefault(series => series.Id == seriesId);
        }
        _wanxiangLevelIndex = Math.Clamp(_wanxiangLevelIndex, 0, Math.Max(0, _wanxiangCatalog.Levels.Count - 1));

        CategoryList.ItemsSource = _categories;
        CategoryList.SelectedItem = _categories.FirstOrDefault(category => category.Id == selectedCategory)
                                    ?? _categories.FirstOrDefault();
        EmptyState.Visibility = _categories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _updatingEquipmentFilters = true;
        _updatingWeaponFilters = true;
        _updatingTalismanFilters = true;
        try
        {
            for (int index = 0; index < filters.Length; index++)
            {
                if (filters[index].Items.OfType<GameFilterOption>().FirstOrDefault(option =>
                        option.Id == filterIds[index]) is { } option)
                {
                    filters[index].SelectedItem = option;
                }
            }
        }
        finally
        {
            _updatingEquipmentFilters = false;
            _updatingWeaponFilters = false;
            _updatingTalismanFilters = false;
        }

        _selectedWeapon = selectedWeapon;
        _selectedEquipment = selectedEquipment;
        _selectedTalisman = selectedTalisman;
        InfoSearchBox.Text = query;
        ApplyFilter();
    }
}
