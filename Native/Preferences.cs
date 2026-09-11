using System;
using System.IO;
using System.Text.Json;
namespace ACInfinityDesktop;
public sealed class Preferences
{
    public string address {get;set;}="";
    public string DeviceName {get;set;}="Choose device";
    public string WidgetView {get;set;}="Status";
    public int HistoryHours {get;set;}=6;
    public string unit {get;set;}="C";
    public bool AutoConnect {get;set;}=true;
    public bool Pinned {get;set;}
    public string Accent {get;set;}="#91C7F2";
    public string Theme {get;set;}="Midnight";
    public string? PanelColor {get;set;}
    public double Transparency {get;set;}=94;
    public double? Left {get;set;}
    public double? Top {get;set;}
    public static Preferences Read(string path){try{return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))??new();}catch{return new();}}
    public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);}
}
internal sealed class ReconnectPolicy
{
    public bool Paused {get;set;}
    public int Failures {get;private set;}
    public DateTime Next {get;private set;}
    public bool Due(bool enabled,DateTime now)=>enabled&&!Paused&&now>=Next;
    public void Failed(DateTime now){Failures++;Next=now.AddSeconds(Math.Min(120,15*Math.Pow(2,Math.Min(Failures,3))));}
    public void Reset(){Failures=0;Next=DateTime.MinValue;Paused=false;}
}
