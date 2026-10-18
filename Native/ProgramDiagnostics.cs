using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACInfinityDesktop;

// Opt-in, bounded hardware checks. Private recovery data stays in ignored diagnostics.
internal static class ProgramDiagnostics
{
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
        bool inspect=args.Contains("--inspect-programs");
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
            if(!fields.TryGetValue(22,out var cycle)||cycle.Length!=8||!fields.TryGetValue(23,out var daily)||daily.Length!=4)
                throw new InvalidDataException("Stored cycle/daily settings were not available; no writes attempted.");
            var presets=ControllerPresetSnapshot.Capture(controller.State);
            original=new Backup{Address=target.Mac,Type=target.Type,Name=target.Name,
                Fields=new byte[]{17,18,22,23,16}.ToDictionary(k=>k,k=>(byte[])fields[k].Clone())};
            // Recovery snapshot must reach disk before the first hardware write.
            File.WriteAllText(output+".restore.json",JsonSerializer.Serialize(original,new JsonSerializerOptions{WriteIndented=true}));
            if(inspect)
            {
                checks.Add($"Read only: mode {presets.Mode}, run preset {presets.On}, idle preset {presets.Off}, output {controller.State.Output}.");
                checks.Add("Cycle bytes: "+Convert.ToHexString(cycle)+"; daily bytes: "+Convert.ToHexString(daily));return;
            }
            mutated=true;
            File.WriteAllText(output+".progress.txt","Testing unchanged program round trip.");
            await controller.RestoreProgramsAsync(original.Fields);
            checks.Add("Unchanged native program round trip accepted and read back.");
            var untouched=fields.Where(p=>p.Key is 19 or 20 or 21).ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
            // Distinct values prove this is a retained write, not a cached read of old settings.
            File.WriteAllText(output+".progress.txt","Testing controller cycle and daily schedule writes.");
            int onMinutes=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(cycle)==300?6:5;
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
                    ControllerPrograms.Verify(original.Fields,controller.State.Settings);restored=true;
                    checks.Add("Original cycle, daily window, On/Off presets and mode restored, then verified after reconnect.");
                }
                catch(Exception ex){error=(error==null?"":error+" ")+"RESTORE REQUIRED: "+ex.Message;}
            }
            await controller.DisconnectAsync();
            File.WriteAllText(output,JsonSerializer.Serialize(new{success=error==null,inspect,restored,checks,error},new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(output+".progress.txt",error==null?"Completed.":"Check failed; see result JSON.");
        }
        if(error!=null)throw new IOException(error);
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
