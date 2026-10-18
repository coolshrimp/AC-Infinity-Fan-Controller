using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACInfinityDesktop;

// Opt-in, bounded hardware checks. Private recovery data stays in ignored diagnostics.
internal static class ProgramDiagnostics
{
    private sealed record OptionalSettingRead(byte Field,bool Available,string? Hex,string? Note);
    internal sealed class Backup
    {
        public string Address {get;set;}="";
        public int? Type {get;set;}
        public string Name {get;set;}="Controller 67";
        public Dictionary<byte,byte[]> Fields {get;set;}=new();
    }
    internal static async Task RunAsync(string[] args,string output)
    {
        var controller=new BluetoothController();Backup? original=null;DiscoveredController? target=null;
        bool mutated=false;var checks=new List<string>();string? error=null;bool restored=false;
        var optionalSettings=new List<OptionalSettingRead>();
        bool inspect=args.Contains("--inspect-programs");
        bool readKnownSettings=args.Contains("--read-known-settings");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        try
        {
            var prefs=Preferences.Read(Path.Combine(MainWindow.DataDirectory,"preferences.json"));
            var devices=await BluetoothController.ScanAsync(prefs.address);
            var supported=devices.Where(d=>d.Type is null or 1).ToArray();
            if(supported.Length!=1)throw new IOException("Hardware checks require exactly one selected Controller 67.");
            target=supported[0];
            checks.Add($"Discovered controller signal: {target.Rssi} dBm.");
            for(int attempt=1;;attempt++)
            {
                File.WriteAllText(output+".progress.txt",$"Opening Bluetooth service, attempt {attempt} of 3.");
                try {await controller.ConnectAsync(target);break;}
                catch(IOException) when(attempt<3){await Task.Delay(2000);}
            }
            File.WriteAllText(output+".progress.txt","Connected. Reading and preserving original settings.");
            await controller.ReadProgramsAsync();
            var fields=controller.State.Settings;
            ValidateBackupFields(fields);
            var cycle=fields[22];var daily=fields[23];
            var presets=ControllerPresetSnapshot.Capture(controller.State);
            original=new Backup{Address=target.Mac,Type=target.Type,Name=target.Name,
                Fields=CloneFields(fields)};
            // Recovery snapshot must reach disk before the first hardware write.
            File.WriteAllText(output+".restore.json",JsonSerializer.Serialize(original,new JsonSerializerOptions{WriteIndented=true}));
            if(inspect)
            {
                checks.Add($"Read only: mode {presets.Mode}, run preset {presets.On}, idle preset {presets.Off}, output {controller.State.Output}.");
                foreach(var field in original.Fields)checks.Add($"Stored field {field.Key}: {Convert.ToHexString(field.Value)}.");
                return;
            }
            mutated=true;
            // Exact configuration comparisons belong in a manual mode: a selected timer
            // may legitimately count down while a later Bluetooth read is in flight.
            File.WriteAllText(output+".progress.txt","Testing unchanged settings round trip in manual mode.");
            await controller.SelectModeAsync(2);RequireMode(controller,2);
            var manualBaseline=CloneFields(original.Fields);manualBaseline[16]=new byte[]{2};
            await controller.RestoreProgramsAsync(manualBaseline);
            ControllerPrograms.Verify(manualBaseline,controller.State.Settings);
            checks.Add("All stored fields 16–23 round-tripped unchanged with manual mode selected.");

            File.WriteAllText(output+".progress.txt","Testing save-only Auto thresholds and enable flags.");
            await CheckAutoEditorAsync(controller,original,presets,checks);
            File.WriteAllText(output+".progress.txt","Testing save-only timers and timer mode selection.");
            await CheckTimerEditorAsync(controller,original,presets,checks);
            // Return the test configuration to its baseline before checking that the
            // previously verified cycle, daily, and PC-session paths leave it intact.
            await controller.RestoreProgramsAsync(manualBaseline);
            ControllerPrograms.Verify(manualBaseline,controller.State.Settings);
            checks.Add("Auto thresholds and both timers restored exactly before the remaining checks.");
            var untouched=original.Fields.Where(p=>p.Key is 19 or 20 or 21).ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
            // Distinct values prove this is a retained write, not a cached read of old settings.
            File.WriteAllText(output+".progress.txt","Testing controller cycle and daily schedule writes.");
            int onMinutes=BinaryPrimitives.ReadUInt32BigEndian(cycle)==300?6:5;
            await controller.ApplyProgramAsync(new ControllerProgram{Mode=6,OnMinutes=onMinutes,OffMinutes=10,RunSpeed=Math.Max(1,presets.On),IdleSpeed=presets.Off});
            checks.Add("Controller accepted and retained repeating-cycle settings and mode 6.");
            await Task.Delay(1500);
            int start=daily[0]==8&&daily[1]==0?9*60:8*60;
            await controller.ApplyProgramAsync(new ControllerProgram{Mode=7,StartMinute=start,EndMinute=20*60,RunSpeed=Math.Max(1,presets.On),IdleSpeed=presets.Off});
            checks.Add("Controller accepted and retained daily-window settings and mode 7.");
            // Exercise the same recovery/session code as the tray scheduler. Injecting
            // phase times tests both boundaries without changing the PC's actual clock.
            File.WriteAllText(output+".progress.txt","Testing PC-managed on/off phases and restoration.");
            var testPrefs=new Preferences{address=target.Mac};
            var session=new AppScheduleSession(testPrefs,controller,()=>testPrefs.Save(output+".session.json"));
            await session.StartAsync(new AppFanSchedule{StartMinute=0,EndMinute=0,OnMinutes=1,OffMinutes=1,RunSpeed=Math.Max(1,presets.On),IdleSpeed=0});
            var onTime=new DateTimeOffset(DateTime.Today.AddHours(8));
            await session.TickAsync(onTime);await WaitForOutputAsync(controller,Math.Max(1,presets.On));
            checks.Add($"PC schedule on phase reached reported output {controller.State.Output} in mode {controller.State.Mode}.");
            await session.TickAsync(onTime.AddMinutes(1));await WaitForOutputAsync(controller,0);
            checks.Add($"PC schedule off phase reached reported output {controller.State.Output} in mode {controller.State.Mode}.");
            await session.StopAsync();
            checks.Add("PC schedule stop restored its captured controller mode and presets.");
            ControllerPrograms.Verify(untouched,controller.State.Settings);
            checks.Add("Unrelated stored Auto and timer settings unchanged.");
        }
        catch(Exception ex){error=ex.Message;}
        finally
        {
            if(mutated&&original!=null&&target!=null)
            {
                try
                {
                    File.WriteAllText(output+".progress.txt","Restoring original controller settings and verifying reconnect.");
                    if(!controller.Connected)await controller.ConnectAsync(target);
                    await controller.RestoreProgramsAsync(original.Fields);
                    await controller.DisconnectAsync();await Task.Delay(1000);await controller.ConnectAsync(target);
                    var stable=CloneFields(original.Fields);
                    int originalMode=original.Fields[16][0];
                    if(originalMode is 4 or 5)stable.Remove(originalMode==4?(byte)20:(byte)21);
                    ControllerPrograms.Verify(stable,controller.State.Settings);RequireMode(controller,originalMode);restored=true;
                    checks.Add("Original Auto, timers, cycle, daily window, On/Off presets and mode restored; stable fields verified after reconnect.");
                    if(originalMode is 4 or 5)checks.Add("The originally active timer was verified before reactivation; elapsed countdown bytes are not required to remain equal after reconnect.");
                }
                catch(Exception ex){error=(error==null?"":error+" ")+"RESTORE REQUIRED: "+ex.Message;}
            }
            // Optional inspection is deliberately last and read-only. An absent or
            // unresponsive optional selector must not invalidate a completed restore.
            if(readKnownSettings&&error==null&&controller.Connected)
            {
                File.WriteAllText(output+".progress.txt","Reading optional known device settings without writing them.");
                optionalSettings=await InspectKnownSettingsAsync(controller);
            }
            await controller.DisconnectAsync();
            File.WriteAllText(output,JsonSerializer.Serialize(new{success=error==null,inspect,restored,checks,optionalSettings,error},new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(output+".progress.txt",error==null?"Completed.":"Check failed; see result JSON.");
        }
        if(error!=null)throw new IOException(error);
    }
    private static void ValidateBackupFields(Dictionary<byte,byte[]> fields)
    {
        foreach(var expected in new (byte Key,int Length)[]{(16,1),(17,1),(18,1),(19,7),(20,4),(21,4),(22,8),(23,4)})
            if(!fields.TryGetValue(expected.Key,out var value)||value.Length!=expected.Length)
                throw new InvalidDataException($"Stored setting {expected.Key} was missing or had an unexpected length; no writes attempted.");
    }
    private static Dictionary<byte,byte[]> CloneFields(Dictionary<byte,byte[]> fields)=>
        fields.Where(p=>p.Key is >=16 and <=23).ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
    private static void RequireMode(BluetoothController controller,int expected)
    {
        if(!controller.Connected||controller.State.Mode!=expected||!controller.State.Settings.TryGetValue(16,out var mode)||mode.Length!=1||mode[0]!=expected)
            throw new InvalidDataException($"Controller mode {expected} was not confirmed by settings and live readback.");
    }
    private static ControllerProgram AutoProgram(byte[] raw,ControllerPresetSnapshot presets)=>new()
    {
        Mode=3,RunSpeed=presets.On,IdleSpeed=presets.Off,Auto=ControllerAutoSettings.FromRaw(raw,false)
    };
    private static async Task CheckAutoEditorAsync(BluetoothController controller,Backup original,ControllerPresetSnapshot presets,List<string> checks)
    {
        byte[] raw=original.Fields[19];
        var unchanged=AutoProgram(raw,presets);
        await controller.ApplyProgramAsync(unchanged,false);RequireMode(controller,2);
        ControllerPrograms.Verify(new(){{19,raw}},controller.State.Settings);
        checks.Add("Save-only Auto retained the exact original mask and F/C pairs without changing manual mode.");

        var changed=AutoProgram(raw,presets);
        changed.Auto!.HighTemperature=raw[2]>=90?89:raw[2]+1;
        byte[] changedRaw=changed.Auto.Encode();
        await controller.ApplyProgramAsync(changed,false);RequireMode(controller,2);
        ControllerPrograms.Verify(new(){{19,changedRaw}},controller.State.Settings);
        if(changedRaw.SequenceEqual(raw)||changedRaw[0]!=raw[0]||!changedRaw.AsSpan(3).SequenceEqual(raw.AsSpan(3)))
            throw new InvalidDataException("The Auto temperature edit changed unrelated settings.");
        checks.Add("Save-only Auto retained a changed high-temperature F/C pair and preserved every unrelated byte.");

        var toggled=AutoProgram(changedRaw,presets);
        toggled.Auto!.HighTemperatureEnabled=!toggled.Auto.HighTemperatureEnabled;
        byte[] toggledRaw=toggled.Auto.Encode();
        await controller.ApplyProgramAsync(toggled,false);RequireMode(controller,2);
        ControllerPrograms.Verify(new(){{19,toggledRaw}},controller.State.Settings);
        if(toggledRaw[0]!=(byte)(changedRaw[0]^8)||!toggledRaw.AsSpan(1).SequenceEqual(changedRaw.AsSpan(1)))
            throw new InvalidDataException("The Auto enable toggle changed more than its single flag.");
        checks.Add("Auto high-temperature enable toggled exactly bit 0x08, preserving reserved bits and all threshold bytes.");

        await controller.ApplyProgramAsync(AutoProgram(raw,presets),false);RequireMode(controller,2);
        ControllerPrograms.Verify(new(){{19,raw}},controller.State.Settings);
        await controller.SelectModeAsync(3);RequireMode(controller,3);
        checks.Add("Mode selector activated Auto mode 3 with the original trigger settings restored.");
        await controller.SelectModeAsync(2);RequireMode(controller,2);
    }
    private static async Task CheckTimerEditorAsync(BluetoothController controller,Backup original,ControllerPresetSnapshot presets,List<string> checks)
    {
        foreach(int mode in new[]{4,5})
        {
            byte field=mode==4?(byte)20:(byte)21;
            uint preferred=mode==4?61u:67u;
            uint seconds=BinaryPrimitives.ReadUInt32BigEndian(original.Fields[field])==preferred?preferred+1:preferred;
            var program=new ControllerProgram{Mode=mode,RunSpeed=presets.On,IdleSpeed=presets.Off,
                TimerOnMinutes=seconds/60d,TimerOffMinutes=seconds/60d};
            var expected=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(expected,seconds);
            await controller.ApplyProgramAsync(program,false);RequireMode(controller,2);
            ControllerPrograms.Verify(new(){{field,expected}},controller.State.Settings);
            checks.Add($"Save-only timer field {field} retained exactly {seconds} seconds without changing manual mode.");
            await controller.SelectModeAsync(mode);RequireMode(controller,mode);
            checks.Add($"Mode selector activated timer mode {mode}; no exact countdown comparison is made while it runs.");
            await controller.SelectModeAsync(2);RequireMode(controller,2);
        }
    }
    private static async Task<List<OptionalSettingRead>> InspectKnownSettingsAsync(BluetoothController controller)
    {
        var result=new List<OptionalSettingRead>();
        foreach(byte field in new byte[]{32,33,34,36,1})
        {
            if(!controller.Connected)
            {
                result.Add(new(field,false,null,"Not attempted after the optional read connection ended."));continue;
            }
            try
            {
                var values=await controller.ReadKnownSettingsAsync(new[]{field});
                result.Add(values.TryGetValue(field,out var raw)?new(field,true,Convert.ToHexString(raw),"Read only; no write capability inferred."):
                    new(field,false,null,"Controller did not return this optional field."));
            }
            catch(Exception ex){result.Add(new(field,false,null,"Optional read unavailable: "+ex.Message));}
        }
        return result;
    }
    private static async Task WaitForOutputAsync(BluetoothController controller,int target)
    {
        var limit=DateTime.UtcNow.AddSeconds(45);
        while(DateTime.UtcNow<limit)
        {
            await Task.Delay(2000);await controller.RefreshAsync();
            if(controller.State.Output==target)return;
        }
        throw new IOException($"Command was retained but reported output did not reach {target} within 45 seconds.");
    }

}
