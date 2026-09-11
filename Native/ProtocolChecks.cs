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
            pref.Pinned=true;pref.Transparency=65;pref.Theme="Forest";pref.AutoConnect=false;pref.Save(path);
            var saved=Preferences.Read(path);Check(saved.Pinned&&saved.Transparency==65&&saved.Theme=="Forest"&&!saved.AutoConnect);
            File.WriteAllText(path,"broken");Check(Preferences.Read(path).AutoConnect);
        } finally {File.Delete(path);Directory.Delete(dir);}
        return new {passed,framework="native .NET",hardwareWrites=false};
    }
}
