using System.Collections.ObjectModel;
using System.Windows.Input;
using MvvmCross.Commands;
using MvvmCross.ViewModels;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MinimalBleApp;

public enum BleState
{
    Unpaired,
    Scanning,
}

public class MainViewModel : MvxViewModel
{
    private readonly IBluetoothLE _bluetoothLe;

    public ObservableCollection<IDevice> DiscoveredDevices { get; } = [];
    public ICommand ActionCommand { get; }

    private BleState _state;
    public BleState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ButtonText));
        }
    }

    public string ButtonText => State switch
    {
        BleState.Unpaired when SelectedDevice == null => "Start Scan",
        BleState.Scanning when SelectedDevice == null => "Stop Scan",
        _ => "Connect"
    };

    private IDevice? _selectedDevice;
    public IDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ButtonText));
        }
    }
    
    public MainViewModel()
    {
        _bluetoothLe = CrossBluetoothLE.Current;
        
        _bluetoothLe.Adapter.DeviceDiscovered += OnDeviceDiscovered;
        
        ActionCommand = new MvxAsyncCommand(DoActionAsync);
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
        if (DiscoveredDevices.Any(d => d.Id == args.Device.Id))
        {
            return;
        }
        
        DiscoveredDevices.Add(args.Device);
    }

    private async Task DoActionAsync()
    {
        switch (State)
        {
            case BleState.Unpaired:
                await StartScanning();
                break;
                
            case BleState.Scanning:
                State = BleState.Unpaired;
                await _bluetoothLe.Adapter.StopScanningForDevicesAsync();
                break;
        }
    }

    private async Task StartScanning()
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

        State = BleState.Scanning;
        _bluetoothLe.Adapter
            .StartScanningForDevicesAsync(deviceFilter: d => !string.IsNullOrEmpty(d.Name))
            .ContinueWith(_ =>
            {
                if (State != BleState.Scanning)
                {
                    return;
                }
                
                State = BleState.Unpaired;
            });
    }

    private static Task<bool> ShowAlert(string title, string message, string confirm, string? cancel = null)
    {
        var mainPage = Application.Current?.MainPage;

        return mainPage == null
            ? Task.FromResult(false)
            : mainPage.DisplayAlert(title, message, confirm, cancel);
    }
}