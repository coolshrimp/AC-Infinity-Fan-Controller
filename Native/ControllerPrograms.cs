using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACInfinityDesktop;

public sealed class ControllerPresetSnapshot
{
    public int Mode {get;set;}
    public int On {get;set;}
    public int Off {get;set;}
    public bool IsValid => Mode is >=1 and <=7 && On is >=0 and <=10 && Off is >=0 and <=10;
    public static ControllerPresetSnapshot Capture(ControllerState state)
    {
        var snapshot=new ControllerPresetSnapshot{Mode=state.Mode,On=state.On,Off=state.Off};
        if(!snapshot.IsValid)throw new InvalidDataException("Cannot preserve this controller mode or speed presets.");
        return snapshot;
    }
}

/// <summary>Editable Auto thresholds, retaining bytes that the user has not changed.</summary>
public sealed class ControllerAutoSettings
{
    private readonly byte[] baselineRaw;
    public bool Fahrenheit {get;}
    public bool HighTemperatureEnabled {get;set;}
    public bool LowTemperatureEnabled {get;set;}
    public bool HighHumidityEnabled {get;set;}
    public bool LowHumidityEnabled {get;set;}
    public int HighTemperature {get;set;}
    public int LowTemperature {get;set;}
    public int HighHumidity {get;set;}
    public int LowHumidity {get;set;}

    private ControllerAutoSettings(byte[] raw,bool fahrenheit)
    {
        baselineRaw=(byte[])raw.Clone();Fahrenheit=fahrenheit;
        HighTemperatureEnabled=(raw[0]&8)!=0;LowTemperatureEnabled=(raw[0]&4)!=0;
        HighHumidityEnabled=(raw[0]&2)!=0;LowHumidityEnabled=(raw[0]&1)!=0;
        HighTemperature=raw[fahrenheit?1:2];LowTemperature=raw[fahrenheit?3:4];
        HighHumidity=raw[5];LowHumidity=raw[6];
    }
    public static ControllerAutoSettings FromRaw(byte[] seven,bool fahrenheit)
    {
        ArgumentNullException.ThrowIfNull(seven);
        if(seven.Length!=7)throw new ArgumentException("Controller Auto settings must contain exactly seven bytes.",nameof(seven));
        return new ControllerAutoSettings(seven,fahrenheit);
    }
    public string Validate()
    {
        int min=Fahrenheit?32:0,max=Fahrenheit?194:90;
        bool highTempEdited=HighTemperature!=baselineRaw[Fahrenheit?1:2];
        bool lowTempEdited=LowTemperature!=baselineRaw[Fahrenheit?3:4];
        // Existing bytes may come from firmware with a different range or rounding rule.
        // Preserve them untouched; validate any edited value or newly enabled trigger.
        if((highTempEdited||HighTemperatureEnabled&&(baselineRaw[0]&8)==0)&&(HighTemperature<min||HighTemperature>max))
            return $"High temperature must be {min}–{max} °{(Fahrenheit?'F':'C')}.";
        if((lowTempEdited||LowTemperatureEnabled&&(baselineRaw[0]&4)==0)&&(LowTemperature<min||LowTemperature>max))
            return $"Low temperature must be {min}–{max} °{(Fahrenheit?'F':'C')}.";
        if((HighHumidity!=baselineRaw[5]||HighHumidityEnabled&&(baselineRaw[0]&2)==0)&&HighHumidity is <0 or >100)
            return "High humidity must be 0–100%.";
        if((LowHumidity!=baselineRaw[6]||LowHumidityEnabled&&(baselineRaw[0]&1)==0)&&LowHumidity is <0 or >100)
            return "Low humidity must be 0–100%.";
        return "";
    }
    public byte[] Encode()
    {
        string error=Validate();if(error.Length>0)throw new ArgumentException(error);
        var raw=(byte[])baselineRaw.Clone();
        raw[0]=(byte)((baselineRaw[0]&0xf0)|(HighTemperatureEnabled?8:0)|(LowTemperatureEnabled?4:0)|(HighHumidityEnabled?2:0)|(LowHumidityEnabled?1:0));
        // A toggle-only edit must never normalize a previously stored F/C pair.
        if(HighTemperature!=baselineRaw[Fahrenheit?1:2])WriteTemperature(raw,1,HighTemperature);
        if(LowTemperature!=baselineRaw[Fahrenheit?3:4])WriteTemperature(raw,3,LowTemperature);
        raw[5]=(byte)HighHumidity;raw[6]=(byte)LowHumidity;
        return raw;
    }
    private void WriteTemperature(byte[] raw,int fahrenheitIndex,int selected)
    {
        raw[fahrenheitIndex]=(byte)(Fahrenheit?selected:Math.Round(selected*9d/5+32,MidpointRounding.AwayFromZero));
        raw[fahrenheitIndex+1]=(byte)(Fahrenheit?Math.Round((selected-32)*5d/9,MidpointRounding.AwayFromZero):selected);
    }
}

public sealed class ControllerProgram
{
    public int Mode {get;set;}=7;
    public int StartMinute {get;set;}=480;
    public int EndMinute {get;set;}=1200;
    public double TimerOnMinutes {get;set;}=5;
    public double TimerOffMinutes {get;set;}=5;
    public double OnMinutes {get;set;}=5;
    public double OffMinutes {get;set;}=10;
    public int RunSpeed {get;set;}=5;
    public int IdleSpeed {get;set;}
    public ControllerAutoSettings? Auto {get;set;}
    public string Validate()
    {
        if(Mode is <1 or >7)return "Choose a controller mode from 1 to 7.";
        if(RunSpeed is <0 or >10 || IdleSpeed is <0 or >10)return "Run and idle speeds must be 0–10.";
        if(Mode==3)return Auto?.Validate()??"Read the controller's Auto settings before editing them.";
        if(Mode==4&&!TrySeconds(TimerOnMinutes,out _))return "Timer to On must be a whole number of seconds between 1 second and 24 hours.";
        if(Mode==5&&!TrySeconds(TimerOffMinutes,out _))return "Timer to Off must be a whole number of seconds between 1 second and 24 hours.";
        if(Mode==7 && (StartMinute is <0 or >=1440 || EndMinute is <0 or >=1440 || StartMinute==EndMinute))return "Use different start and end times in HH:mm format.";
        if(Mode==6 && (!TrySeconds(OnMinutes,out _) || !TrySeconds(OffMinutes,out _)))return "Cycle durations must be whole numbers of seconds between 1 second and 24 hours.";
        return "";
    }
    internal static bool TrySeconds(double minutes,out uint seconds)
    {
        seconds=0;if(!double.IsFinite(minutes))return false;
        double exact=minutes*60,rounded=Math.Round(exact,MidpointRounding.AwayFromZero);
        // Small floating-point roundoff from seconds/60 is acceptable; subsecond input is not.
        if(!double.IsFinite(exact)||rounded<1||rounded>86400||Math.Abs(exact-rounded)>0.0000001)return false;
        seconds=(uint)rounded;return true;
    }
}

internal static class ControllerPrograms
{
    internal static byte[] Presets(ControllerPresetSnapshot snapshot)
    {
        if(!snapshot.IsValid)throw new ArgumentException("Invalid controller mode or presets.");
        // Configure presets before selecting the mode that will use them.
        return new byte[]{17,1,(byte)snapshot.Off,18,1,(byte)snapshot.On,16,1,(byte)snapshot.Mode};
    }
    internal static Dictionary<byte,byte[]> ProgramFields(ControllerProgram program,bool activate=true)
    {
        ArgumentNullException.ThrowIfNull(program);
        string error=program.Validate();if(error.Length>0)throw new ArgumentException(error);
        var fields=new Dictionary<byte,byte[]>{{17,new[]{(byte)program.IdleSpeed}},{18,new[]{(byte)program.RunSpeed}}};
        switch(program.Mode)
        {
            case 3: fields[19]=program.Auto!.Encode();break;
            case 4: fields[20]=Duration(program.TimerOnMinutes);break;
            case 5: fields[21]=Duration(program.TimerOffMinutes);break;
            case 6: fields[22]=Duration(program.OnMinutes).Concat(Duration(program.OffMinutes)).ToArray();break;
            case 7: fields[23]=new[]{(byte)(program.StartMinute/60),(byte)(program.StartMinute%60),(byte)(program.EndMinute/60),(byte)(program.EndMinute%60)};break;
        }
        // Save-only intentionally leaves the controller's active mode unchanged.
        if(activate)fields[16]=new[]{(byte)program.Mode};return fields;
    }
    private static byte[] Duration(double minutes)
    {
        if(!ControllerProgram.TrySeconds(minutes,out uint seconds))throw new ArgumentException("Invalid controller duration.");
        var raw=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(raw,seconds);return raw;
    }
    internal static byte[] Encode(Dictionary<byte,byte[]> fields) => fields.SelectMany(p=>new[]{p.Key,(byte)p.Value.Length}.Concat(p.Value)).ToArray();
    internal static void Verify(Dictionary<byte,byte[]> expected,Dictionary<byte,byte[]> actual)
    {
        foreach(var field in expected)
            if(!actual.TryGetValue(field.Key,out var value)||!value.SequenceEqual(field.Value))
                throw new InvalidDataException($"Controller did not retain setting {field.Key}. Read its current settings before retrying.");
    }
}
