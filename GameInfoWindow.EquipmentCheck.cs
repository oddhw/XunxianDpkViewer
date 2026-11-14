#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using XunxianDpkViewer.Core;

namespace XunxianDpkViewer;

public sealed partial class GameInfoWindow
{
    internal async Task VerifyEquipmentUpdatesAsync(string outputFolder)
    {
        string logPath = Path.Combine(outputFolder, "equipment-update-check.log");
        try
        {
            for (int attempt = 0; attempt < 600 &&
                 (_repository is null || LoadingState.Visibility == Visibility.Visible); attempt++)
            {
                await Task.Delay(100);
            }
            Require(_repository is not null, "Game information did not load.");
            _clientDataTimer.Stop();
            File.WriteAllText(logPath, SelfTest.VerifyEquipmentUpdates(_repository!, _resourceFolder!));
            File.AppendAllText(logPath, SelfTest.VerifyEquipmentProfessions(_repository!, _resourceFolder!));

            GameInfoCategory equipment = _categories.Single(category => category.Id == "equipment");
            CategoryList.SelectedItem = equipment;
            GameInfoStage newest = equipment.Stages[^1];
            Require(newest.Name == "裂空装备" && newest.Records.Count == 96,
                "The current client expansion is not displayed in the equipment stages.");
            EquipmentStageBox.SelectedItem = EquipmentStageBox.Items.OfType<GameFilterOption>()
                .Single(option => option.Id == newest.Id);
            Require(EquipmentList.Items.Count == 96 && EquipmentList.Items.Cast<GameItemViewModel>()
                    .All(item => item.Record.Name.StartsWith("裂空【", StringComparison.Ordinal)),
                "The new equipment stage does not contain all client items.");
            foreach (string family in new[] { "力士", "游侠", "法师", "符咒师" })
            {
                EquipmentProfessionBox.SelectedItem = EquipmentProfessionBox.Items.OfType<GameFilterOption>()
                    .Single(option => option.Id == family + " / 天狐");
                Require(EquipmentList.Items.Count == 24 && EquipmentList.Items.Cast<GameItemViewModel>()
                        .All(item => item.Record.Tooltip!.RequiredProfessions[0] == family),
                    "A profession is missing or mixed with other professions in the new stage.");
            }
            GameInfoRecord sample = newest.Records.Single(record => record.Id == "31584");
            Require(sample.Tooltip?.Level == "270" && sample.ImagePath is not null && File.Exists(sample.ImagePath) &&
                    _repository!.EquipmentWash.Get(sample) is not null &&
                    _repository.EquipmentEnhancement.Get(sample) is not null &&
                    _repository.EquipmentUpgrades.Get(sample).Any(step => step.TargetItemId == sample.Id),
                "The new equipment is missing its client level, icon, attributes or upgrade route.");

            EquipmentProfessionBox.SelectedItem = EquipmentProfessionBox.Items.OfType<GameFilterOption>()
                .Single(option => option.Id == "力士 / 天狐");
            EquipmentList.SelectedItem = EquipmentList.Items.Cast<GameItemViewModel>()
                .Single(item => item.Record.Id == sample.Id);
            await CaptureEquipmentUpdateViewAsync(outputFolder, "equipment-new-stage.png", 1920, 1080);
            await CaptureEquipmentUpdateViewAsync(outputFolder, "equipment-new-stage-compact.png", 1180, 820);

            GameInfoCategory oldEquipment = equipment with { Stages = equipment.Stages.Take(equipment.Stages.Count - 1).ToArray() };
            _categories = _categories.Select(category => category.Id == "equipment" ? oldEquipment : category).ToArray();
            CategoryList.ItemsSource = _categories;
            CategoryList.SelectedItem = oldEquipment;
            EquipmentStageBox.SelectedItem = EquipmentStageBox.Items.OfType<GameFilterOption>()
                .Single(option => option.Id == "xingyun");
            EquipmentProfessionBox.SelectedItem = EquipmentProfessionBox.Items.OfType<GameFilterOption>()
                .Single(option => option.Id == "法师 / 天狐");
            InfoSearchBox.Text = "31468";
            await Task.Delay(150);
            Require(EquipmentList.Items.Count == 1, "The initial equipment query did not select its item.");
            GameInfoRepository previous = _repository!;
            string mbPath = Path.Combine(outputFolder, "client", "mb.dpk");
            Require(Path.GetFullPath(_resourceFolder!) == Path.Combine(Path.GetFullPath(outputFolder), "client", "res"),
                "The update check must use a disposable client copy.");
            File.SetLastWriteTimeUtc(mbPath, File.GetLastWriteTimeUtc(mbPath).AddSeconds(2));
            await RefreshClientDataIfChangedAsync();
            Require(ReferenceEquals(previous, _repository), "Client resources were reloaded before stabilizing.");
            _clientDataTimer.Start();
            for (int attempt = 0; attempt < 600 && ReferenceEquals(previous, _repository); attempt++)
            {
                await Task.Delay(100);
            }
            _clientDataTimer.Stop();
            Require(!ReferenceEquals(previous, _repository) &&
                    EquipmentStageBox.Items.OfType<GameFilterOption>().Any(option => option.Id == newest.Id),
                "Client file changes did not automatically restore the new equipment stage.");
            Require(_selectedCategory?.Id == "equipment" && InfoSearchBox.Text == "31468" &&
                    (EquipmentStageBox.SelectedItem as GameFilterOption)?.Id == "xingyun" &&
                    (EquipmentProfessionBox.SelectedItem as GameFilterOption)?.Id == "法师 / 天狐" &&
                    EquipmentList.Items.Count == 1 && _selectedEquipment?.Record.Id == "31468",
                "Automatic client reload reset the category, filters, query or selected item.");
            previous = _repository!;
            await RefreshClientDataIfChangedAsync();
            Require(ReferenceEquals(previous, _repository), "Unchanged client files triggered another reload.");

            File.Move(mbPath, mbPath + ".saved");
            try
            {
                await RefreshClientDataIfChangedAsync();
                Require(ReferenceEquals(previous, _repository) && EquipmentList.Items.Count == 1,
                    "Temporarily unavailable client resources cleared the loaded data.");
                File.WriteAllBytes(mbPath, new byte[1024]);
                await RefreshClientDataIfChangedAsync();
                await RefreshClientDataIfChangedAsync();
                Require(ReferenceEquals(previous, _repository), "An incomplete client patch replaced valid data.");
            }
            finally
            {
                File.Delete(mbPath);
                File.Move(mbPath + ".saved", mbPath);
            }
            File.AppendAllText(logPath,
                "Client change detection, stable-file debounce, automatic reload, preserved query and filters, " +
                "missing/corrupt archive recovery and desktop/compact rendering passed.\nEQUIPMENT VIEW CHECK PASSED\n");
        }
        catch (Exception exception)
        {
            File.AppendAllText(logPath, exception.ToString());
        }
        finally
        {
            Close();
            Application.Current.Exit();
        }
    }

    private async Task CaptureEquipmentUpdateViewAsync(string outputFolder, string fileName, int width, int height)
    {
        AppWindow.Resize(new SizeInt32(width, height));
        await Task.Delay(500);
        Require(SelectedEquipmentTooltipHost.Content is not null && EquipmentView.ActualWidth > 0,
            "The equipment detail is not displayed.");
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync((FrameworkElement)Content);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Require(bitmap.PixelWidth > 0 && pixels.Distinct().Take(12).Count() >= 12, "The equipment view is blank.");
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(outputFolder);
        StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
#endif
