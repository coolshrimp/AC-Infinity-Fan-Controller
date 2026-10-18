using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
namespace ACInfinityDesktop;
public sealed class Preferences
{
    public string address {get;set;}="";
    public string DeviceName {get;set;}="Choose device";
    public int? ControllerType {get;set;}
    public Dictionary<string,string> DisplayNames {get;set;}=new();
    public string DisplayName()
    {
        if(!string.IsNullOrEmpty(address)&&DisplayNames!=null&&DisplayNames.TryGetValue(address.ToUpperInvariant(),out var name)&&!string.IsNullOrWhiteSpace(name))return name.Trim();
        return "AC Infinity";
    }
    public void Rename(string name)
    {
        if(string.IsNullOrEmpty(address))return;
        DisplayNames??=new();var key=address.ToUpperInvariant();name=name.Trim();
        if(name.Length==0)DisplayNames.Remove(key);else DisplayNames[key]=name[..Math.Min(name.Length,40)];
    }
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
    public static Preferences Read(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new();
            // Older or hand-edited settings may contain explicit JSON nulls.
            value.address ??= "";
            value.DeviceName ??= "Choose device";
            value.DisplayNames ??= new();
            value.WidgetView = value.WidgetView is "Readings" or "Chart" ? value.WidgetView : "Status";
            value.HistoryHours = value.HistoryHours is 1 or 6 or 24 ? value.HistoryHours : 6;
            value.unit = value.unit == "F" ? "F" : "C";
            value.Transparency = double.IsFinite(value.Transparency) ? Math.Clamp(value.Transparency,45,100) : 94;
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }
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
