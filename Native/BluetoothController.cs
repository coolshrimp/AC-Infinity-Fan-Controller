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

public sealed class BluetoothController : IScheduleController
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
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) throw new IOException($"Controller Bluetooth service is unavailable (Windows: {services.Status}, matching services: {services.Services.Count}). Move closer and close other controller apps.");
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
            // Acknowledging a write does not guarantee a complete subsequent read.
            // Reject malformed presets before replacing either displayed value.
            if (!presets.TryGetValue(17, out var off) || off.Length != 1 || off[0] > 10 ||
                !presets.TryGetValue(18, out var on) || on.Length != 1 || on[0] > 10)
                throw new InvalidDataException("Invalid Controller 67 speed presets.");
            State.Off = off[0]; State.On = on[0];
            await ReadLiveCoreAsync();
        }
        catch { await DisconnectCoreAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task ReadProgramsAsync()
    {
        await gate.WaitAsync();
        try {RequireConnection();await ReadSettingsCoreAsync();}
        catch {await DisconnectCoreAsync();throw;}
        finally {gate.Release();}
    }
    public Task ApplyProgramAsync(ControllerProgram program,bool activate=true)
    {
        var configuration=ControllerPrograms.ProgramFields(program,false);
        return WriteConfigurationAsync(configuration,activate?(byte)program.Mode:null,false);
    }
    public Task SelectModeAsync(int mode)
    {
        if(mode is <1 or >7)throw new ArgumentOutOfRangeException(nameof(mode),"Controller mode must be 1–7.");
        return WriteConfigurationAsync(new Dictionary<byte,byte[]>(),(byte)mode,false);
    }
    public Task SetPresetsAsync(ControllerPresetSnapshot presets)=>WriteVerifiedAsync(Protocol.Fields(ControllerPrograms.Presets(presets)));
    // Used by bounded checks to restore exact saved bytes, including untouched Auto/timers.
    internal Task RestoreProgramsAsync(Dictionary<byte,byte[]> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if(fields.Count==0||fields.Keys.Any(k=>k is <16 or >23))throw new ArgumentException("Unexpected program field.");
        foreach(var field in fields)
        {
            int length=field.Key switch{16 or 17 or 18=>1,19=>7,20 or 21 or 23=>4,22=>8,_=>0};
            if(field.Value==null||field.Value.Length!=length)throw new ArgumentException("Malformed saved program field.");
            if(field.Key==16&&field.Value[0] is <1 or >7)throw new ArgumentException("Invalid saved controller mode.");
            if(field.Key is 17 or 18&&field.Value[0]>10)throw new ArgumentException("Invalid saved controller preset.");
        }
        var configuration=fields.Where(p=>p.Key!=16).ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
        byte? mode=fields.TryGetValue(16,out var originalMode)?originalMode[0]:null;
        return WriteConfigurationAsync(configuration,mode,true);
    }
    private async Task WriteConfigurationAsync(Dictionary<byte,byte[]> configuration,byte? activateMode,bool restoring)
    {
        await gate.WaitAsync();
        bool blockedActiveTimer=false;
        try
        {
            RequireConnection();
            byte? preserveMode=null;
            if(!restoring&&!activateMode.HasValue&&configuration.Count>0)
            {
                // Some Controller 67 firmware activates a mode when its settings change.
                // Save-only must preserve the fresh pre-write mode, not a cached editor value.
                await ReadLiveCoreAsync();
                if(State.Mode is <1 or >7)throw new InvalidDataException("Cannot preserve an unknown controller mode.");
                preserveMode=(byte)State.Mode;
            }
            if(configuration.ContainsKey(20)||configuration.ContainsKey(21))
            {
                if(!preserveMode.HasValue)await ReadLiveCoreAsync();
                int current=State.Mode;
                if(current==4&&configuration.ContainsKey(20)||current==5&&configuration.ContainsKey(21))
                {
                    if(!restoring)
                    {
                        // Until a firmware-specific live check proves these values static,
                        // avoid racing or resetting a timer the user is currently running.
                        blockedActiveTimer=true;
                        throw new InvalidOperationException("Select On or Off before editing the currently running timer, then save its settings.");
                    }
                    // An active timer may expose a changing counter. Freeze only the timer
                    // being restored; preserve its pre-expiry idle/run behavior meanwhile.
                    activateMode??=(byte)current;
                    await SelectModeCoreAsync((byte)(current==4?1:2));
                }
            }
            if(configuration.Count>0)await WriteFieldsCoreAsync(configuration);
            // Verify stored configuration before activating any timer or automatic mode.
            // Exact timer readback after activation could race its legitimate countdown.
            if(activateMode.HasValue)await SelectModeCoreAsync(activateMode.Value);
            else if(preserveMode.HasValue&&State.Mode!=preserveMode.Value)
            {
                // Restore only when firmware switched modes; reselecting an unchanged
                // cycle or timer could otherwise restart its current phase unnecessarily.
                await SelectModeCoreAsync(preserveMode.Value);
            }
        }
        catch {if(!blockedActiveTimer)await DisconnectCoreAsync();throw;}
        finally {gate.Release();}
    }
    private async Task SelectModeCoreAsync(byte mode)
    {
        var fields=new Dictionary<byte,byte[]>{{16,new[]{mode}}};
        Protocol.ValidateAck(await SendAsync(3,ControllerPrograms.Encode(fields)),16);
        var readback=Protocol.Fields(await SendAsync(1,new byte[]{16}));
        ControllerPrograms.Verify(fields,readback);
        State.Settings[16]=(byte[])readback[16].Clone();
        await ReadLiveCoreAsync();
    }
    private async Task WriteVerifiedAsync(Dictionary<byte,byte[]> fields)
    {
        await gate.WaitAsync();
        try {RequireConnection();await WriteFieldsCoreAsync(fields);}
        catch {await DisconnectCoreAsync();throw;}
        finally {gate.Release();}
    }
    private async Task WriteFieldsCoreAsync(Dictionary<byte,byte[]> fields)
    {
        Protocol.ValidateAck(await SendAsync(3,ControllerPrograms.Encode(fields)),fields.Keys.ToArray());
        await ReadSettingsCoreAsync();
        ControllerPrograms.Verify(fields,State.Settings);
    }
    // Only documented identity/clock/calibration/status parameters may be inspected.
    // Query individually so an unsupported parameter cannot hide another parameter's reply.
    internal async Task<Dictionary<byte,byte[]>> ReadKnownSettingsAsync(byte[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if(ids.Any(id=>id is not (1 or 32 or 33 or 34 or 36)))throw new ArgumentException("This setting is not in the read-only inspection list.",nameof(ids));
        var requested=ids.Distinct().ToArray();
        var result=new Dictionary<byte,byte[]>();
        await gate.WaitAsync();
        try
        {
            RequireConnection();
            foreach(byte id in requested)
            {
                var fields=Protocol.Fields(await SendAsync(1,new[]{id}));
                if(fields.TryGetValue(id,out var value)&&value.Length>0)result[id]=value;
            }
            return result;
        }
        catch {await DisconnectCoreAsync();throw;}
        finally {gate.Release();}
    }
    private async Task ReadSettingsCoreAsync()
    {
        var fields=Protocol.Fields(await SendAsync(1,new byte[]{16,17,18,19,20,21,22,23}));
        if(!fields.TryGetValue(17,out var off)||off.Length!=1||off[0]>10||
           !fields.TryGetValue(18,out var on)||on.Length!=1||on[0]>10)throw new InvalidDataException("Invalid Controller 67 presets.");
        State.Settings=fields;State.On=on[0];State.Off=off[0];await ReadLiveCoreAsync();
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
            var frame=Protocol.Request(command,current,payload);
            var option = writer.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
            // Controller 67 accepts a framed command over consecutive ATT writes.
            // Twenty-byte fragments also work with the minimum Bluetooth ATT MTU.
            for(int offset=0;offset<frame.Length;offset+=20)
            {
                using var bytes=new DataWriter();bytes.WriteBytes(frame.AsSpan(offset,Math.Min(20,frame.Length-offset)).ToArray());
                Check(await writer.WriteValueAsync(bytes.DetachBuffer(),option).AsTask(deadline.Token));
            }
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
