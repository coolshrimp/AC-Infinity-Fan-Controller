using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace ACInfinityDesktop;

internal static class ControllerEditingChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(bool condition,string description)
        {
            if(!condition)throw new InvalidOperationException($"Controller editor test {passed+1} failed: {description}");
            passed++;
        }
        void Reject(Action action,string description)
        {
            bool rejected=false;
            try{action();}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}
            Check(rejected,description);
        }
        byte[] baseline={0xa5,76,24,61,16,65,35};
        foreach(bool fahrenheit in new[]{false,true})
        {
            var auto=ControllerAutoSettings.FromRaw(baseline,fahrenheit);
            Check(auto.Fahrenheit==fahrenheit&&auto.HighTemperature==(fahrenheit?76:24)&&auto.LowTemperature==(fahrenheit?61:16),"selected temperature unit reads its exact stored byte");
            Check(!auto.HighTemperatureEnabled&&auto.LowTemperatureEnabled&&!auto.HighHumidityEnabled&&auto.LowHumidityEnabled,"Auto mask uses high-temp08 low-temp04 high-RH02 low-RH01");
            Check(auto.HighHumidity==65&&auto.LowHumidity==35&&auto.Validate()=="","humidity thresholds load independently of temperature unit");
            Check(auto.Encode().SequenceEqual(baseline),"no-op Auto edit preserves every original byte in both units");
            for(int mask=0;mask<16;mask++)
            {
                auto.HighTemperatureEnabled=(mask&8)!=0;auto.LowTemperatureEnabled=(mask&4)!=0;
                auto.HighHumidityEnabled=(mask&2)!=0;auto.LowHumidityEnabled=(mask&1)!=0;
                var encoded=auto.Encode();
                Check(encoded[0]==(0xa0|mask),"edited trigger combinations retain reserved upper mask bits");
                Check(encoded.Skip(1).SequenceEqual(baseline.Skip(1)),"toggle-only edits preserve both temperature pairs and disabled thresholds");
            }
        }
        var celsius=ControllerAutoSettings.FromRaw(baseline,false);celsius.HighTemperature=25;
        var raw=celsius.Encode();
        Check(raw[1]==77&&raw[2]==25&&raw[3]==61&&raw[4]==16,"editing Celsius high converts its pair and leaves low bytes exact");
        Check(raw[0]==0xa5&&raw[5]==65&&raw[6]==35,"temperature edit preserves trigger mask and humidity settings");
        celsius=ControllerAutoSettings.FromRaw(baseline,false);celsius.LowTemperature=14;raw=celsius.Encode();
        Check(raw[3]==57&&raw[4]==14&&raw[1]==76&&raw[2]==24,"editing Celsius low rounds Fahrenheit and preserves high pair");
        var fahrenheitAuto=ControllerAutoSettings.FromRaw(baseline,true);fahrenheitAuto.HighTemperature=73;raw=fahrenheitAuto.Encode();
        Check(raw[1]==73&&raw[2]==23&&raw[3]==61&&raw[4]==16,"editing Fahrenheit high rounds Celsius and preserves low pair");
        fahrenheitAuto=ControllerAutoSettings.FromRaw(baseline,true);fahrenheitAuto.LowTemperature=50;raw=fahrenheitAuto.Encode();
        Check(raw[3]==50&&raw[4]==10&&raw[1]==76&&raw[2]==24,"editing Fahrenheit low updates only its pair");
        var humidityOnly=ControllerAutoSettings.FromRaw(baseline,false);humidityOnly.HighHumidity=99;raw=humidityOnly.Encode();
        Check(raw.Take(5).SequenceEqual(baseline.Take(5))&&raw[5]==99&&raw[6]==35,"editing one humidity threshold leaves both temperature pairs unchanged");
        foreach(var edge in new[]{(F:false,Value:0,ExpectedF:32,ExpectedC:0),(F:false,Value:90,ExpectedF:194,ExpectedC:90),(F:true,Value:32,ExpectedF:32,ExpectedC:0),(F:true,Value:194,ExpectedF:194,ExpectedC:90)})
        {
            var auto=ControllerAutoSettings.FromRaw(baseline,edge.F);auto.HighTemperature=edge.Value;
            raw=auto.Encode();Check(raw[1]==edge.ExpectedF&&raw[2]==edge.ExpectedC,"editable temperature range endpoints encode safely");
        }
        var inverted=ControllerAutoSettings.FromRaw(baseline,false);
        inverted.HighTemperature=10;inverted.LowTemperature=40;inverted.HighHumidity=10;inverted.LowHumidity=90;
        inverted.HighTemperatureEnabled=inverted.LowTemperatureEnabled=inverted.HighHumidityEnabled=inverted.LowHumidityEnabled=true;
        Check(inverted.Validate()==""&&inverted.Encode()[0]==0xaf,"independent triggers allow inverted thresholds without imposing unrelated ordering");
        foreach(byte mask in new byte[]{0xf0,0xff})
        {
            byte[] unusual={mask,255,255,254,254,253,252};
            var auto=ControllerAutoSettings.FromRaw(unusual,false);
            Check(auto.Validate()==""&&auto.Encode().SequenceEqual(unusual),"untouched firmware threshold bytes remain round-trip safe even outside editor limits");
            auto.HighTemperature=20;raw=auto.Encode();
            Check(raw[1]==68&&raw[2]==20&&raw.Skip(3).SequenceEqual(unusual.Skip(3)),"editing one unusual threshold does not normalize the other stored values");
        }
        var invalidEnabled=ControllerAutoSettings.FromRaw(new byte[]{0xf0,255,255,255,255,255,255},false);invalidEnabled.HighTemperatureEnabled=true;
        Check(invalidEnabled.Validate().Length>0,"newly enabling an out-of-range stored trigger requires a valid threshold");
        Reject(()=>invalidEnabled.Encode(),"invalid newly enabled trigger cannot be encoded");
        foreach(var invalid in new[]{(F:false,High:true,Value:-1),(F:false,High:false,Value:91),(F:true,High:true,Value:31),(F:true,High:false,Value:195)})
        {
            var auto=ControllerAutoSettings.FromRaw(baseline,invalid.F);
            if(invalid.High)auto.HighTemperature=invalid.Value;else auto.LowTemperature=invalid.Value;
            Check(auto.Validate().Length>0,"edited temperature outside conservative range is rejected");
            Reject(()=>auto.Encode(),"invalid disabled temperature cannot leak through encoding");
        }
        foreach(int badHumidity in new[]{-1,101,int.MaxValue})
        {
            var auto=ControllerAutoSettings.FromRaw(baseline,false);auto.HighHumidity=badHumidity;
            Check(auto.Validate().Length>0,"invalid edited high humidity rejected even while trigger is disabled");
            Reject(()=>auto.Encode(),"invalid high humidity cannot wrap into a byte");
            auto=ControllerAutoSettings.FromRaw(baseline,false);auto.LowHumidity=badHumidity;
            Check(auto.Validate().Length>0,"invalid edited low humidity rejected");
        }
        var input=(byte[])baseline.Clone();var independent=ControllerAutoSettings.FromRaw(input,false);input[0]=0;input[1]=0;
        Check(independent.Encode().SequenceEqual(baseline),"Auto model copies its input instead of retaining a mutable readback buffer");
        var output=independent.Encode();output[0]=0;output[2]=0;
        Check(independent.Encode().SequenceEqual(baseline),"encoded arrays cannot mutate the retained restore baseline");
        foreach(int length in new[]{0,6,8})Reject(()=>ControllerAutoSettings.FromRaw(new byte[length],false),"Auto rejects malformed seven-byte layout");
        Reject(()=>ControllerAutoSettings.FromRaw(null!,false),"Auto rejects missing payload");

        for(int mode=1;mode<=7;mode++)
        foreach(int speed in new[]{0,10})
        {
            var program=new ControllerProgram{Mode=mode,RunSpeed=speed,IdleSpeed=10-speed,Auto=ControllerAutoSettings.FromRaw(baseline,false)};
            Check(program.Validate()=="","all seven native controller modes accept speed preset endpoints");
            var active=ControllerPrograms.ProgramFields(program);
            Check(active[17][0]==10-speed&&active[18][0]==speed&&active[16][0]==mode&&active.Keys.Last()==16,"native activation writes exact presets and mode last");
            byte? selected=mode switch{3=>19,4=>20,5=>21,6=>22,7=>23,_=>null};
            Check(active.Keys.SequenceEqual(selected.HasValue?new byte[]{17,18,selected.Value,16}:new byte[]{17,18,16}),"native mode writes only its own configuration field");
            var saveOnly=ControllerPrograms.ProgramFields(program,false);
            Check(!saveOnly.ContainsKey(16)&&saveOnly.Keys.SequenceEqual(active.Keys.Where(key=>key!=16)),"save-only omits active-mode field for every native mode");
            var readback=Protocol.Fields(ControllerPrograms.Encode(saveOnly));readback[16]=new byte[]{2};
            ControllerPrograms.Verify(saveOnly,readback);
            Check(true,"save-only readback allows the previously active mode to remain unchanged");
        }
        var autoProgram=new ControllerProgram{Mode=3,Auto=ControllerAutoSettings.FromRaw(baseline,false),RunSpeed=0,IdleSpeed=10};
        Check(Convert.ToHexString(ControllerPrograms.Encode(ControllerPrograms.ProgramFields(autoProgram)))=="11010A1201001307A54C183D104123100103","Auto TLV uses exact seven-byte body after presets and before mode");
        Check(new ControllerProgram{Mode=3}.Validate().Length>0,"Auto mode requires a read baseline before editing");
        Reject(()=>ControllerPrograms.ProgramFields(new ControllerProgram{Mode=3}),"missing Auto baseline cannot produce a settings command");
        var invalidAuto=ControllerAutoSettings.FromRaw(baseline,false);invalidAuto.HighHumidity=101;
        Reject(()=>ControllerPrograms.ProgramFields(new ControllerProgram{Mode=3,Auto=invalidAuto}),"Auto validation propagates through native program encoding");
        var timerOn=new ControllerProgram{Mode=4,TimerOnMinutes=59d/60,RunSpeed=0,IdleSpeed=10};
        Check(Convert.ToHexString(ControllerPrograms.Encode(ControllerPrograms.ProgramFields(timerOn)))=="11010A12010014040000003B100104","timer-to-on encodes fractional minutes as exact big-endian seconds");
        var timerOff=new ControllerProgram{Mode=5,TimerOffMinutes=1.5};
        Check(Convert.ToHexString(ControllerPrograms.ProgramFields(timerOff)[21])=="0000005A","timer-to-off stores its independent seconds field");
        var fractionalCycle=new ControllerProgram{Mode=6,OnMinutes=5.5,OffMinutes=7d/60};
        Check(Convert.ToHexString(ControllerPrograms.ProgramFields(fractionalCycle)[22])=="0000014A00000007","cycle retains fractional-minute durations to whole-second precision");
        foreach(var duration in new[]{(Minutes:1d/60,Seconds:1u),(Minutes:59d/60,Seconds:59u),(Minutes:1d,Seconds:60u),(Minutes:1.5,Seconds:90u),(Minutes:60.25,Seconds:3615u),(Minutes:1440d,Seconds:86400u)})
        {
            var onFields=ControllerPrograms.ProgramFields(new ControllerProgram{Mode=4,TimerOnMinutes=duration.Minutes});
            var offFields=ControllerPrograms.ProgramFields(new ControllerProgram{Mode=5,TimerOffMinutes=duration.Minutes});
            Check(BinaryPrimitives.ReadUInt32BigEndian(onFields[20])==duration.Seconds&&BinaryPrimitives.ReadUInt32BigEndian(offFields[21])==duration.Seconds,"timer endpoints and second-precision values encode without truncation");
            var cycle=ControllerPrograms.ProgramFields(new ControllerProgram{Mode=6,OnMinutes=duration.Minutes,OffMinutes=duration.Minutes})[22];
            Check(BinaryPrimitives.ReadUInt32BigEndian(cycle)==duration.Seconds&&BinaryPrimitives.ReadUInt32BigEndian(cycle.AsSpan(4))==duration.Seconds,"both cycle durations retain the same accepted precision and bounds");
        }
        foreach(double invalidDuration in new[]{0d,-1d,0.5/60,1.001/60,1440+1d/60,double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue})
        foreach(int mode in new[]{4,5,6})
        {
            var program=new ControllerProgram{Mode=mode,TimerOnMinutes=invalidDuration,TimerOffMinutes=invalidDuration,OnMinutes=invalidDuration,OffMinutes=invalidDuration};
            Check(program.Validate().Length>0,"invalid, fractional-second, or overflowing native duration rejected");
            Reject(()=>ControllerPrograms.ProgramFields(program),"invalid duration cannot overflow or truncate during encoding");
        }
        foreach(var invalidCycle in new[]{new ControllerProgram{Mode=6,OnMinutes=5,OffMinutes=0},new ControllerProgram{Mode=6,OnMinutes=double.NaN,OffMinutes=10}})
            Reject(()=>ControllerPrograms.ProgramFields(invalidCycle),"each cycle duration is validated independently");
        foreach(int invalidMode in new[]{-1,0,8,int.MaxValue})
            Reject(()=>ControllerPrograms.ProgramFields(new ControllerProgram{Mode=invalidMode}),"unsupported controller mode rejected");
        foreach(int invalidSpeed in new[]{-1,11})
        {
            Reject(()=>ControllerPrograms.ProgramFields(new ControllerProgram{Mode=2,RunSpeed=invalidSpeed}),"invalid native run speed rejected");
            Reject(()=>ControllerPrograms.ProgramFields(new ControllerProgram{Mode=1,IdleSpeed=invalidSpeed}),"invalid native idle speed rejected");
        }
        Check(new ControllerProgram{Mode=1,TimerOnMinutes=double.NaN,OnMinutes=0}.Validate()=="","manual modes ignore unrelated hidden duration editors");
        Check(new ControllerProgram{Mode=4,TimerOnMinutes=1,TimerOffMinutes=double.NaN}.Validate()=="","editing timer-to-on does not validate an unrelated timer-to-off value");
        var dailySave=ControllerPrograms.ProgramFields(new ControllerProgram{Mode=7,StartMinute=1425,EndMinute=377},false);
        Check(Convert.ToHexString(dailySave[23])=="172D0611"&&!dailySave.ContainsKey(16),"save-only overnight daily schedule retains hour/minute order without activating");
        var unchangedMode=Protocol.Fields(ControllerPrograms.Encode(dailySave));unchangedMode[16]=new byte[]{3};
        ControllerPrograms.Verify(dailySave,unchangedMode);Check(true,"save-only verification confirms stored daily settings while retaining Auto mode");
        Reject(()=>ControllerPrograms.Verify(ControllerPrograms.ProgramFields(new ControllerProgram{Mode=7,StartMinute=1425,EndMinute=377}),unchangedMode),"activate readback still rejects a controller that did not change its mode");
        return passed;
    }
}
