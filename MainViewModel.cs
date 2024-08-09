using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Animations;
using Microsoft.Maui.ApplicationModel;
using MvvmCross.Commands;
using MvvmCross.ViewModels;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MinimalBleApp;

public class MainViewModel : MvxViewModel
{
    private readonly IBluetoothLE _bluetoothLe;

    public ObservableCollection<IDevice> DiscoveredDevices { get; } = [];
    public ICommand ScanCommand { get; }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }
    
    
    public MainViewModel()
    {
        _bluetoothLe = CrossBluetoothLE.Current;
        _bluetoothLe.Adapter.DeviceDiscovered += OnDeviceDiscovered;
        
        ScanCommand = new MvxAsyncCommand(ToggleScanForDevicesAsync);
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
        if (DiscoveredDevices.Any(d => d.Id == args.Device.Id))
        {
            return;
        }
        
        DiscoveredDevices.Add(args.Device);
    }

    private async Task ToggleScanForDevicesAsync()
    {
        if (IsScanning)
        {
            IsScanning = false;
            await _bluetoothLe.Adapter.StopScanningForDevicesAsync();
        }
        else
        {
            DiscoveredDevices.Clear();
            
            var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Bluetooth>();
            }

            if (status != PermissionStatus.Granted)
            {
                await ShowAlert("Error", "No BLE permissions!", "OK");
                return;
            }

            IsScanning = true;
            _bluetoothLe.Adapter
                .StartScanningForDevicesAsync(deviceFilter: d => !string.IsNullOrEmpty(d.Name))
                .ContinueWith(_ => { IsScanning = false; });
        }
    }

    private static Task<bool> ShowAlert(string title, string message, string confirm, string? cancel = null)
    {
        var mainPage = Microsoft.Maui.Controls.Application.Current?.MainPage;

        return mainPage == null
            ? Task.FromResult(false)
            : mainPage.DisplayAlert(title, message, confirm, cancel);
    }
}