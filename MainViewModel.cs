using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using MvvmCross.Commands;
using MvvmCross.ViewModels;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MinimalBleApp;

public class MainViewModel : MvxViewModel
{
    private enum BleState
    {
        Idle,
        Scanning,
        Connecting,
        Connected,
    }

    private BleState _state;
    private BleState State
    {
        get => _state;
        set
        {
            if (value == _state)
            {
                return;
            }

            _state = value;
            RaisePropertyChanged(nameof(ButtonText));
            RaisePropertyChanged(nameof(StatusText));
        }
    }

    private readonly IBluetoothLE _bluetoothLe;

    public ObservableCollection<IDevice> DiscoveredDevices { get; } = [];
    public ICommand ActionCommand { get; }

    public string StatusText => State switch
    {
        BleState.Scanning => "Scanning...",
        BleState.Connecting => "Connecting...",
        BleState.Connected => $"Connected to {SelectedDevice?.Name}",
        _ => ""
    };
    
    public string ButtonText => State switch
    {
        BleState.Idle when SelectedDevice == null => "Start Scan",
        BleState.Scanning when SelectedDevice == null => "Stop Scan",
        BleState.Connecting => "Connecting...",
        BleState.Connected => "Disconnect",
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

        _bluetoothLe.Adapter.ScanTimeout = 30000;

        _bluetoothLe.StateChanged += OnBleStateChanged;
        _bluetoothLe.Adapter.DeviceDiscovered += OnDeviceDiscovered;
        _bluetoothLe.Adapter.DeviceConnected += OnDeviceConnected;
        _bluetoothLe.Adapter.DeviceDisconnected += OnDeviceDisconnected;
        _bluetoothLe.Adapter.DeviceConnectionLost += OnDeviceDisconnected;
        
        ActionCommand = new MvxAsyncCommand(DoActionAsync);
    }

    private async void OnBleStateChanged(object? sender, BluetoothStateChangedArgs args)
    {
        if (args.OldState == BluetoothState.On && args.NewState != BluetoothState.On)
        {
            State = BleState.Connecting;
        }
        else if (args.OldState != BluetoothState.On && args.NewState == BluetoothState.On)
        {
            await Connect();
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
        if (State != BleState.Scanning || DiscoveredDevices.Any(d => d.Id == args.Device.Id))
        {
            return;
        }
        
        DiscoveredDevices.Add(args.Device);
    }

    private async void OnDeviceConnected(object? sender, DeviceEventArgs args)
    {
        State = BleState.Connected;

        try
        {
            foreach (var service in await SelectedDevice!.GetServicesAsync())
            {
                foreach (var characteristic in await service.GetCharacteristicsAsync())
                {
                    if (!characteristic.CanRead)
                    {
                        continue;
                    }

                    var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var result = await characteristic.ReadAsync(tokenSource.Token);

                    if (result.resultCode != 0)
                    {
                        await ShowAlert("Error", "Unable to read from paired device");
                    }
                    else
                    {
                        await ShowAlert("Success", $"Read {result.data.Length} bytes from \"{characteristic.Name}\" characteristic");
                    }
                    
                    return;
                }
            }
        }
        catch (Exception)
        {
            await ShowAlert("Error", "Exception while reading from paired device");
        }
    }

    private async void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        if (State != BleState.Connected || SelectedDevice == null)
        {
            return;
        }

        State = BleState.Connecting;
        await Connect();
    }

    private async Task DoActionAsync()
    {
        switch (State)
        {
            case BleState.Idle when SelectedDevice == null:
                await StartScanning();
                break;
                
            case BleState.Scanning when SelectedDevice == null:
                State = BleState.Idle;
                await _bluetoothLe.Adapter.StopScanningForDevicesAsync();
                break;
            
            case BleState.Idle when SelectedDevice != null:
            case BleState.Scanning when SelectedDevice != null:
                await Connect();
                break;
            
            case BleState.Connected:
                State = BleState.Idle;
                await _bluetoothLe.Adapter.DisconnectDeviceAsync(SelectedDevice);
                SelectedDevice = null;
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
            await ShowAlert("Error", "No BLE permissions!");
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
                
                State = BleState.Idle;
            });
    }

    private async Task Connect()
    {
        State = BleState.Connecting;
        DiscoveredDevices.Clear();
        
        if (SelectedDevice == null)
        {
            return;
        }
        
        SelectedDevice = await _bluetoothLe.Adapter.ConnectToKnownDeviceAsync(SelectedDevice.Id);
    }

    private static Task ShowAlert(string title, string message) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var mainPage = Application.Current?.MainPage;

        return mainPage == null
            ? Task.CompletedTask
            : mainPage.DisplayAlert(title, message, "OK");
    });
}