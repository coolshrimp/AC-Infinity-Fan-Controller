using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace ACInfinityDesktop;

public sealed record DiscoveredController(ulong Address, string Name, short Rssi, int? Type)
{
    public bool Verified {get;init;}
    public bool SupportsControls => Verified && (Type is null or 1);
    public string Verification => !Verified?"Not checked":SupportsControls?"Verified controller":"AC Infinity service verified; model not supported yet";
    public string Mac => string.Join(":", Enumerable.Range(0, 6).Select(i => ((Address >> (8 * (5 - i))) & 255).ToString("X2")));
    public string Display => $"{Name}  ·  {Mac}  ·  {Rssi} dBm";
}

public sealed class BluetoothController
{
    private static readonly Guid ServiceId = new("70d51000-2c7f-4e75-ae8a-d758951ce4e0");
    private static readonly Guid WriteId = new("70d51001-2c7f-4e75-ae8a-d758951ce4e0");
    private static readonly Guid NotifyId = new("70d51002-2c7f-4e75-ae8a-d758951ce4e0");
    private BluetoothLEDevice? device;
    private GattDeviceService? service;
    private GattCharacteristic? writer, reader;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object rxLock = new();
    private TaskCompletionSource<byte[]>? pending;
    private readonly List<byte> receiveBuffer = new();
    private ushort sequence;
    private byte expectedCommand;
    private bool verified;
    public bool Connected => verified && device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public ControllerState State { get; private set; } = new();

    internal static bool IsAciName(string name)=>name.StartsWith("ACI",StringComparison.OrdinalIgnoreCase)||name.StartsWith("AC Infinity",StringComparison.OrdinalIgnoreCase);
    public static async Task<List<DiscoveredController>> ScanAsync(string target)
    {
        if (target.Length > 0 && !Regex.IsMatch(target, "^(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")) throw new ArgumentException("Enter a Bluetooth MAC address, or leave it empty.");
        ulong? wanted = target.Length == 0 ? null : Convert.ToUInt64(target.Replace(":", ""), 16);
        var found = new Dictionary<ulong, DiscoveredController>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        BluetoothError scanError = BluetoothError.Success;
        watcher.Received += (_, e) =>
        {
            if (wanted.HasValue && e.BluetoothAddress != wanted.Value) return;
            var data = e.Advertisement.ManufacturerData.FirstOrDefault(x => x.CompanyId == 2306);
            string name = e.Advertisement.LocalName;
            if (!wanted.HasValue && data == null && !IsAciName(name)) return;
            int? type = null;
            if (data != null)
            {
                using var r = DataReader.FromBuffer(data.Data); var b = new byte[r.UnconsumedBufferLength]; r.ReadBytes(b);
                if (b.Length >= 19) type = b[12];
            }
            lock (found)
            {
                found.TryGetValue(e.BluetoothAddress, out var previous);
                found[e.BluetoothAddress] = new(e.BluetoothAddress, string.IsNullOrEmpty(name) ? previous?.Name ?? "AC Infinity device" : name, e.RawSignalStrengthInDBm, type ?? previous?.Type);
            }
        };
        watcher.Stopped += (_, e) => scanError = e.Error;
        try { watcher.Start(); await Task.Delay(10000); }
        finally { watcher.Stop(); }
        if (scanError != BluetoothError.Success) throw new IOException($"Bluetooth scan failed: {scanError}. Check the PC's Bluetooth adapter.");
        lock (found) return found.Values.OrderByDescending(x => x.Rssi).ToList();
    }

    public Task ConnectAsync(DiscoveredController target)=>ConnectCoreAsync(target,false);
    public static async Task<DiscoveredController> VerifyAsync(DiscoveredController target)
    {
        var probe=new BluetoothController();
        try{await probe.ConnectCoreAsync(target,true);return target with {Verified=true};}
        finally{await probe.DisconnectAsync();}
    }
    private async Task ConnectCoreAsync(DiscoveredController target,bool identifyOnly)
    {
        await gate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            if (!identifyOnly && target.Type.HasValue && target.Type != 1) throw new NotSupportedException("This build supports Controller 67 (type 1).");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(target.Address).AsTask(token) ?? throw new IOException("Windows could not open the controller.");
            var services = await device.GetGattServicesForUuidAsync(ServiceId, BluetoothCacheMode.Uncached).AsTask(token);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) throw new IOException("Controller Bluetooth service is unavailable. Move closer and close other controller apps.");
            service = services.Services[0];
            var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(token);
            if (chars.Status != GattCommunicationStatus.Success) throw new IOException("Could not read Bluetooth characteristics.");
            writer = chars.Characteristics.FirstOrDefault(x => x.Uuid == WriteId);
            reader = chars.Characteristics.FirstOrDefault(x => x.Uuid == NotifyId);
            if (writer == null || reader == null || !reader.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) throw new IOException("Expected AC Infinity characteristics were not found.");
            // Other advertised models can be identified by the vendor service, but their
            // control schema is not assumed to match Controller 67.
            if(identifyOnly && target.Type.HasValue && target.Type!=1)return;
            reader.ValueChanged += Received;
            Check(await reader.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token));
            State = new();
            var fields = Protocol.Fields(await SendAsync(1, new byte[] { 16, 17, 18, 19, 20, 21, 22, 23 }));
            if (!fields.TryGetValue(17, out var off) || off.Length != 1 || !fields.TryGetValue(18, out var on) || on.Length != 1 || off[0] > 10 || on[0] > 10) throw new InvalidDataException("Invalid Controller 67 speed presets.");
            State.Settings = fields; State.On = on[0]; State.Off = off[0];
            await ReadLiveCoreAsync();
            verified = true;
        }
        catch { await DisconnectCoreAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task RefreshAsync()
    {
        await gate.WaitAsync();
        try { RequireConnection(); await ReadLiveCoreAsync(); }
        catch { await DisconnectCoreAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task SetSpeedAsync(int level)
    {
        var payload = Protocol.Speed(level);
        await ChangeAsync(payload, new byte[] { 16, 18 });
    }
    public Task SetModeAsync(bool on) => ChangeAsync(new byte[] {16,1,(byte)(on ? 2 : 1)}, new byte[] {16});
    private async Task ChangeAsync(byte[] payload, byte[] required)
    {
        await gate.WaitAsync();
        try
        {
            RequireConnection();
            Protocol.ValidateAck(await SendAsync(3, payload), required);
            var presets = Protocol.Fields(await SendAsync(1, new byte[] {17,18}));
            State.Off = presets[17][0]; State.On = presets[18][0];
            await ReadLiveCoreAsync();
        }
        catch { await DisconnectCoreAsync(); throw; }
        finally { gate.Release(); }
    }
    private async Task ReadLiveCoreAsync() => State.ApplyLive(Protocol.Fields(await SendAsync(1, new byte[] {16,2,3,32})));
    private void RequireConnection() { if (!Connected) throw new IOException("Connect to your controller first."); }
    private static void Check(GattCommunicationStatus status) { if (status != GattCommunicationStatus.Success) throw new IOException($"Bluetooth communication failed: {status}. Move closer and reconnect."); }
    private async Task<byte[]> SendAsync(byte command, byte[] payload)
    {
        if (writer == null) throw new IOException("Bluetooth is disconnected.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        TaskCompletionSource<byte[]> completion;
        ushort current;
        lock (rxLock)
        {
            sequence = sequence == ushort.MaxValue ? (ushort)1 : (ushort)(sequence + 1);
            current = sequence; expectedCommand = command; receiveBuffer.Clear();
            completion = pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            using var bytes = new DataWriter(); bytes.WriteBytes(Protocol.Request(command, current, payload));
            var option = writer.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
            Check(await writer.WriteValueAsync(bytes.DetachBuffer(), option).AsTask(deadline.Token));
            return await completion.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) { throw new IOException("Controller did not respond within 12 seconds. Move closer and reconnect. A write may have reached the controller; check its current mode before retrying."); }
        finally { lock (rxLock) { pending = null; receiveBuffer.Clear(); } }
    }
    private void Received(GattCharacteristic sender, GattValueChangedEventArgs e)
    {
        using var r = DataReader.FromBuffer(e.CharacteristicValue); var data = new byte[r.UnconsumedBufferLength]; r.ReadBytes(data);
        lock (rxLock)
        {
            if (sender != reader || pending == null) return;
            if (data.Length >= 2 && data[0] == 0xa5 && (data[1] == 0x10 || data[1] == 0x13)) receiveBuffer.Clear();
            else if (receiveBuffer.Count == 0) return;
            receiveBuffer.AddRange(data);
            if (receiveBuffer.Count < 4) return;
            int length = (receiveBuffer[2] << 8 | receiveBuffer[3]) + 12;
            if (length > 4096 || receiveBuffer.Count > length) { receiveBuffer.Clear(); return; }
            if (receiveBuffer.Count != length) return;
            try { pending.TrySetResult(Protocol.Response(receiveBuffer.ToArray(), sequence, expectedCommand)); }
            catch (InvalidDataException) { }
            receiveBuffer.Clear();
        }
    }
    public async Task DisconnectAsync()
    {
        await gate.WaitAsync(); try { await DisconnectCoreAsync(); } finally { gate.Release(); }
    }
    private async Task DisconnectCoreAsync()
    {
        verified = false;
        var oldReader = reader; reader = null; writer = null;
        if (oldReader != null)
        {
            oldReader.ValueChanged -= Received;
            try { await oldReader.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        service?.Dispose(); service = null; device?.Dispose(); device = null;
        lock (rxLock) { pending?.TrySetException(new IOException("Disconnected.")); pending = null; receiveBuffer.Clear(); }
    }
}
