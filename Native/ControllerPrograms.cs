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

public sealed class ControllerProgram
{
    public int Mode {get;set;}=7;
    public int StartMinute {get;set;}=480;
    public int EndMinute {get;set;}=1200;
    public int OnMinutes {get;set;}=5;
    public int OffMinutes {get;set;}=10;
    public int RunSpeed {get;set;}=5;
    public int IdleSpeed {get;set;}
    public string Validate()
    {
        if(Mode is not (6 or 7))return "Choose a daily schedule or repeating cycle.";
        if(RunSpeed is <1 or >10 || IdleSpeed is <0 or >10)return "Run speed must be 1–10; idle speed must be 0–10.";
        if(Mode==7 && (StartMinute is <0 or >=1440 || EndMinute is <0 or >=1440 || StartMinute==EndMinute))return "Use different start and end times in HH:mm format.";
        if(Mode==6 && (OnMinutes is <1 or >1440 || OffMinutes is <1 or >1440))return "Cycle durations must be 1–1440 minutes.";
        return "";
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
    internal static Dictionary<byte,byte[]> ProgramFields(ControllerProgram program)
    {
        string error=program.Validate();if(error.Length>0)throw new ArgumentException(error);
        var fields=new Dictionary<byte,byte[]>{{17,new[]{(byte)program.IdleSpeed}},{18,new[]{(byte)program.RunSpeed}}};
        if(program.Mode==6)
        {
            var cycle=new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(cycle,(uint)(program.OnMinutes*60));
            BinaryPrimitives.WriteUInt32BigEndian(cycle.AsSpan(4),(uint)(program.OffMinutes*60));
            fields[22]=cycle;
        }
        else fields[23]=new[]{(byte)(program.StartMinute/60),(byte)(program.StartMinute%60),(byte)(program.EndMinute/60),(byte)(program.EndMinute%60)};
        fields[16]=new[]{(byte)program.Mode};return fields;
    }
    internal static byte[] Encode(Dictionary<byte,byte[]> fields) => fields.SelectMany(p=>new[]{p.Key,(byte)p.Value.Length}.Concat(p.Value)).ToArray();
    internal static void Verify(Dictionary<byte,byte[]> expected,Dictionary<byte,byte[]> actual)
    {
        foreach(var field in expected)
            if(!actual.TryGetValue(field.Key,out var value)||!value.SequenceEqual(field.Value))
                throw new InvalidDataException($"Controller did not retain setting {field.Key}. Read its current settings before retrying.");
    }
}
