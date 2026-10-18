using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace ACInfinityDesktop;
public sealed record SensorSample(DateTimeOffset Time,double TemperatureC,double Humidity)
{
    public double Vpd=>0.6108*Math.Exp(17.27*TemperatureC/(TemperatureC+237.3))*(1-Humidity/100);
}
internal sealed class SensorHistory
{
    private readonly List<SensorSample> samples=new();
    private string? file;
    public void SelectDevice(string directory,string address,bool ephemeral)
    {
        samples.Clear();file=null;
        if(ephemeral||!System.Text.RegularExpressions.Regex.IsMatch(address,"^(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$"))return;
        file=Path.Combine(directory,"history",address.Replace(":","")+".json");
        try{samples.AddRange((JsonSerializer.Deserialize<List<SensorSample>>(File.ReadAllText(file))??new()).Where(Valid).OrderBy(s=>s.Time).DistinctBy(s=>s.Time));}catch{}
        Prune(DateTimeOffset.Now);
    }
    // A damaged JSON array can contain null entries; retain its remaining valid samples.
    private static bool Valid(SensorSample? s)=>s is not null&&double.IsFinite(s.TemperatureC)&&s.TemperatureC is >=-40 and <=100&&double.IsFinite(s.Humidity)&&s.Humidity is >=0 and <=100;
    public bool Add(SensorSample sample)
    {
        if(!Valid(sample)||samples.Count>0&&sample.Time<=samples[^1].Time)return false;
        samples.Add(sample);Prune(sample.Time);return true;
    }
    private void Prune(DateTimeOffset now)
    {samples.RemoveAll(s=>s.Time<now.AddHours(-24)||s.Time>now.AddMinutes(1));if(samples.Count>6000)samples.RemoveRange(0,samples.Count-6000);}
    public List<SensorSample> Range(DateTimeOffset now,int hours)=>samples.Where(s=>s.Time>=now.AddHours(-hours)&&s.Time<=now).ToList();
    public void Save()
    {
        if(file==null)return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",JsonSerializer.Serialize(samples));File.Move(file+".tmp",file,true);
    }
}
