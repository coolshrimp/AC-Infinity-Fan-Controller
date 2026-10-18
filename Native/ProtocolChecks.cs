using System;
using System.IO;
using System.Collections.Generic;
namespace ACInfinityDesktop;
internal static class ProtocolChecks
{
    public static object Run()
    {
        int passed=0;
        void Check(bool ok){if(!ok)throw new Exception($"Protocol test {passed+1} failed");passed++;}
        void Reject(Action action){try{action();}catch(InvalidDataException){passed++;return;}throw new Exception("Invalid packet accepted");}
        var model=Convert.FromHexString("a510002e000215e6000110010211010a12010a13070f581f3c1050321404000007081504000007081608000007800000070817040a000f004fb3");
        var fields=Protocol.Fields(Protocol.Response(model,2,1));Check(fields[16][0]==2 && fields[17][0]==10 && fields[18][0]==10);
        Reject(()=>Protocol.Response(model,3,1));Reject(()=>Protocol.Response(model,2,3));
        var corrupt=(byte[])model.Clone();corrupt[13]^=1;Reject(()=>Protocol.Response(corrupt,2,1));
        Reject(()=>Protocol.Response(model[..^1],2,1));Reject(()=>Protocol.Fields(new byte[]{16,3,1}));
        var live=Convert.FromHexString("a5100014000657060001100102020506091612c00305000a000000200101b59f");
        var state=new ControllerState();state.ApplyLive(Protocol.Fields(Protocol.Response(live,6,1)));
        Check(Math.Abs(state.TemperatureC!.Value-23.26)<.001);Check(state.Humidity==48 && state.Output==10 && state.Mode==2);
        Check(Convert.ToHexString(Protocol.Request(1,2,new byte[]{16,17,18,19,20,21,22,23}))=="A5000008000225DA00011011121314151617713E");
        var ack=Convert.FromHexString("a5130006769585f2000310001200ff011629");Check(Protocol.Response(ack,0x7695,3).Length==6);
        Protocol.ValidateAck(new byte[]{16,0,18,0},16,18);passed++;
        Reject(()=>Protocol.ValidateAck(new byte[]{16,1},16));Reject(()=>Protocol.ValidateAck(new byte[]{16,0},16,18));
        foreach(var level in new[]{0,11}){try{Protocol.Speed(level);throw new Exception("Invalid speed accepted");}catch(ArgumentOutOfRangeException){passed++;}}
        var retry=new ReconnectPolicy();var now=DateTime.UtcNow;
        Check(retry.Due(true,now));Check(!retry.Due(false,now));
        retry.Failed(now);Check(!retry.Due(true,now.AddSeconds(29))&&retry.Due(true,now.AddSeconds(30)));
        retry.Failed(now);Check(retry.Next==now.AddSeconds(60));
        for(int i=0;i<8;i++)retry.Failed(now);Check(retry.Next==now.AddSeconds(120));
        retry.Paused=true;Check(!retry.Due(true,now.AddDays(1)));retry.Reset();Check(retry.Due(true,now)&&retry.Failures==0);
        string dir=Path.Combine(Path.GetTempPath(),"aci-tests-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"preferences.json");
        try {
            File.WriteAllText(path,"{\"address\":\"AA:BB:CC:DD:EE:FF\",\"unit\":\"F\"}");
            var pref=Preferences.Read(path);Check(pref.address=="AA:BB:CC:DD:EE:FF"&&pref.unit=="F"&&pref.AutoConnect);
            pref.PanelColor="#164E63";pref.Pinned=true;pref.Transparency=65;pref.Theme="Forest";pref.AutoConnect=false;pref.Save(path);
            var saved=Preferences.Read(path);Check(saved.PanelColor=="#164E63"&&saved.Pinned&&saved.Transparency==65&&saved.Theme=="Forest"&&!saved.AutoConnect);
            Check(saved.DisplayName()=="AC Infinity");
            saved.Rename("  Workshop fan  ");saved.Save(path);
            var renamed=Preferences.Read(path);Check(renamed.DisplayName()=="Workshop fan");
            renamed.address="11:22:33:44:55:66";Check(renamed.DisplayName()=="AC Infinity");
            renamed.address="aa:bb:cc:dd:ee:ff";Check(renamed.DisplayName()=="Workshop fan");
            renamed.Rename(" ");Check(renamed.DisplayName()=="AC Infinity");
            File.WriteAllText(path,"broken");Check(Preferences.Read(path).AutoConnect);
            File.WriteAllText(path,"{\"address\":null,\"DisplayNames\":null,\"HistoryHours\":-1,\"WidgetView\":null,\"Transparency\":200}");
            var repaired=Preferences.Read(path);
            Check(repaired.address=="" && repaired.DisplayNames!=null && repaired.HistoryHours==6 && repaired.WidgetView=="Status" && repaired.Transparency==100);
            File.WriteAllText(path,"{\"WidgetView\":\"Readings\"}");
            Check(Preferences.Read(path).WidgetView=="Status");
        } finally {File.Delete(path);Directory.Delete(dir);}
        var testId=Guid.NewGuid();
        using(var owner=new SingleInstance("ACInfinityDesktop.Test."+testId))
        using(var activated=new System.Threading.ManualResetEventSlim())
        {
            Check(owner.IsPrimary);
            // Signal before registering the listener, covering simultaneous startup.
            using var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,"--instance-client "+testId){UseShellExecute=false,CreateNoWindow=true});
            Check(child!=null && child.WaitForExit(10000) && child.ExitCode==0);
            owner.Listen(()=>activated.Set());Check(activated.Wait(5000));
        }
        using(var replacement=new SingleInstance("ACInfinityDesktop.Test."+testId))Check(replacement.IsPrimary);
        Check(BluetoothController.IsAciName("ACI-UniversalController"));
        Check(BluetoothController.IsAciName("aci Controller"));
        Check(!BluetoothController.IsAciName("Headphones"));
        var candidate=new DiscoveredController(0xAABBCCDDEEFF,"ACI",-60,1);
        Check(!candidate.SupportsControls);
        Check((candidate with {Verified=true}).SupportsControls);
        Check(!(candidate with {Verified=true,Type=2}).SupportsControls);
        var history=new SensorHistory();var time=DateTimeOffset.Now;
        Check(history.Add(new SensorSample(time.AddHours(-2),22,45)));
        Check(!history.Add(new SensorSample(time.AddHours(-2),24,50)));
        Check(!history.Add(new SensorSample(time,23,120)));
        Check(history.Add(new SensorSample(time,23,48)));
        Check(history.Range(time,1).Count==1 && history.Range(time,6).Count==2);
        Check(history.Range(time,1)[0].Vpd>1 && history.Range(time,1)[0].Vpd<2);
        history.SelectDevice(Path.GetTempPath(),"",true);Check(history.Range(time,24).Count==0);
        var historyDir=Path.Combine(Path.GetTempPath(),"aci-history-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(historyDir,"history"));
            var sampleJson=System.Text.Json.JsonSerializer.Serialize(new SensorSample(time,22,45));
            File.WriteAllText(Path.Combine(historyDir,"history","AABBCCDDEEFF.json"),"[null,"+sampleJson+"]");
            history.SelectDevice(historyDir,"AA:BB:CC:DD:EE:FF",false);
            Check(history.Range(time,1).Count==1);
            history.SelectDevice(historyDir,"11:22:33:44:55:66",false);
            Check(history.Range(time,1).Count==0); // Never mix two controllers' histories.
            history.SelectDevice(historyDir,"AA:BB:CC:DD:EE:FF",false);
            Check(history.Add(new SensorSample(time.AddSeconds(1),23,46)));history.Save();
            var reopened=new SensorHistory();reopened.SelectDevice(historyDir,"AA:BB:CC:DD:EE:FF",false);
            Check(reopened.Range(time.AddSeconds(1),1).Count==2);
            Check(reopened.Add(new SensorSample(time.AddSeconds(2),24,47))&&reopened.Range(time.AddSeconds(2),1).Count==3);
        }
        finally {Directory.Delete(historyDir,true);}
        var work=new System.Windows.Rect(0,0,1920,1080);
        var size=new System.Windows.Size(500,400);
        Check(PanelDocking.Place(new System.Windows.Rect(100,100,450,300),size,work)==new System.Windows.Point(560,100));
        Check(PanelDocking.Place(new System.Windows.Rect(1350,100,450,300),size,work)==new System.Windows.Point(840,100));
        Check(PanelDocking.Place(new System.Windows.Rect(600,200,450,300),new System.Windows.Size(1000,400),work)==new System.Windows.Point(600,510));
        Check(PanelDocking.Place(new System.Windows.Rect(600,700,450,300),new System.Windows.Size(1000,400),work)==new System.Windows.Point(600,290));
        Check(PanelDocking.Place(new System.Windows.Rect(100,900,450,300),size,work)==new System.Windows.Point(100,490));
        Check(PanelDocking.Place(new System.Windows.Rect(-900,100,450,300),size,new System.Windows.Rect(-1920,0,1920,1080))==new System.Windows.Point(-1410,100));
        var crampedArea=new System.Windows.Rect(0,0,900,600);
        Check(PanelDocking.Place(new System.Windows.Rect(0,0,450,400),size,crampedArea)==new System.Windows.Point(400,0)&&
            PanelDocking.Place(new System.Windows.Rect(20,0,450,400),size,crampedArea)==new System.Windows.Point(400,0));
        var polling=new SpeedPolling();var clock=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
        polling.Observe(4,clock,clock,true);Check(polling.Interval.TotalSeconds==10);
        polling.Request(8,clock);Check(polling.Fast&&polling.Target==8);
        polling.Observe(6,clock.AddSeconds(2),clock.AddSeconds(2),true);Check(polling.Fast);
        polling.Observe(6,clock.AddSeconds(2),clock.AddSeconds(40),true);Check(polling.Fast); // Same reply cannot establish stability.
        polling.Observe(8,clock.AddSeconds(4),clock.AddSeconds(4),true);Check(polling.Fast);
        polling.Observe(8,clock.AddSeconds(6),clock.AddSeconds(6),true);Check(!polling.Fast&&polling.Target==null);
        polling.Observe(7,clock.AddSeconds(16),clock.AddSeconds(16),true);Check(polling.Fast); // External/Auto changes also speed up reads.
        polling.Observe(7,clock.AddSeconds(26),clock.AddSeconds(26),true);Check(!polling.Fast);
        polling.Request(10,clock.AddSeconds(26));
        polling.Observe(7,clock.AddSeconds(28),clock.AddSeconds(28),true);Check(polling.Fast);
        polling.Observe(7,clock.AddSeconds(38),clock.AddSeconds(38),true);Check(!polling.Fast); // Settled short of requested output.
        polling.Request(10,clock.AddSeconds(40));polling.Observe(null,clock.AddSeconds(132),clock.AddSeconds(132),true);Check(!polling.Fast);
        polling.Request(10,clock.AddSeconds(140));polling.Observe(null,clock.AddSeconds(142),clock.AddSeconds(142),false);Check(!polling.Fast&&polling.Target==null);
        Check(ColorChoices.Hsv(0,1,1)==System.Windows.Media.Colors.Red);
        Check(ColorChoices.Hsv(120,1,1)==System.Windows.Media.Colors.Lime);
        Check(ColorChoices.Hsv(240,1,1)==System.Windows.Media.Colors.Blue);
        Check(ColorChoices.Hsv(300,0,1)==System.Windows.Media.Colors.White);
        Check(ColorChoices.Hsv(300,1,0)==System.Windows.Media.Colors.Black);
        Check(ColorChoices.TryHex("#91C7F2",out var sky)&&sky==System.Windows.Media.Color.FromRgb(145,199,242));
        Check(!ColorChoices.TryHex("#GG7788",out _));Check(!ColorChoices.TryHex("#12345",out _));
        var hsv=ColorChoices.ToHsv(sky);Check(ColorChoices.Hsv(hsv.H,hsv.S,hsv.V)==sky);
        return new {passed,framework="native .NET",hardwareWrites=false};
    }
}
