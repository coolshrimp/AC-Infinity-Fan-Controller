using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace ACInfinityDesktop;

public partial class MainWindow
{
    private readonly DispatcherTimer scheduleTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool scheduleTickRunning,scheduleStopping;
    private AppScheduleSession scheduleSession=null!;
    private Forms.ToolStripMenuItem? trayScheduleStop;
    private bool HasAppSchedule => prefs.AppSchedule.Enabled||prefs.ScheduleRestore!=null;
    private void InitializeScheduling()
    {
        scheduleSession=new AppScheduleSession(prefs,controller,SaveScheduleState);
        LoadAppScheduleEditors();
        programsWindow.ReadControllerButton.Click+=async(_,_)=>await ProgramActionAsync(async()=>{await controller.ReadProgramsAsync();LoadControllerEditors();},"Reading controller settings…");
        programsWindow.ApplyControllerButton.Click+=async(_,_)=>await ProgramActionAsync(async()=>
        {
            var program=ReadControllerEditors();
            await StopAppScheduleCoreAsync(true);
            await controller.ApplyProgramAsync(program);LoadControllerEditors();
            programsWindow.ProgramActionText.Text="Saved and verified on the controller. This mode runs without this PC.";
        },"Saving controller program…");
        programsWindow.StartAppButton.Click+=async(_,_)=>await ProgramActionAsync(async()=>
        {
            var schedule=ReadAppScheduleEditors();
            await scheduleSession.StartAsync(schedule);
            FollowRamp(prefs.AppSchedule.Evaluate(DateTimeOffset.Now).Speed);
            programsWindow.ProgramActionText.Text="PC schedule started. Minimize to the tray to keep it running in the background.";
        },"Starting PC schedule…");
        programsWindow.StopAppButton.Click+=async(_,_)=>await StopAppScheduleAsync();
        scheduleTimer.Tick+=async(_,_)=>await ScheduleTickAsync();
        if(!smoke&&!captureLive)scheduleTimer.Start();
    }
    private static int ReadMinutes(TextBox box,string label)
    {
        if(!TimeOnly.TryParseExact(box.Text.Trim(),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
            throw new ArgumentException(label+" must use HH:mm, for example 08:00.");
        return time.Hour*60+time.Minute;
    }
    private static int ReadInteger(TextBox box,string label)
    {
        if(!int.TryParse(box.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out int value))throw new ArgumentException(label+" must be a whole number.");
        return value;
    }
    private static string ClockText(int minutes)=>$"{minutes/60:00}:{minutes%60:00}";
    private ControllerProgram ReadControllerEditors()
    {
        var window=programsWindow;
        int mode=int.Parse((string)((ComboBoxItem)window.ProgramKindBox.SelectedItem).Tag,CultureInfo.InvariantCulture);
        var program=new ControllerProgram{Mode=mode,RunSpeed=ReadInteger(window.ControllerRunSpeedBox,"Run speed"),IdleSpeed=ReadInteger(window.ControllerIdleSpeedBox,"Idle speed")};
        if(mode==7){program.StartMinute=ReadMinutes(window.ControllerStartBox,"Start time");program.EndMinute=ReadMinutes(window.ControllerEndBox,"End time");}
        else {program.OnMinutes=ReadInteger(window.ControllerOnMinutesBox,"Run duration");program.OffMinutes=ReadInteger(window.ControllerOffMinutesBox,"Idle duration");}
        string error=program.Validate();if(error.Length>0)throw new ArgumentException(error);return program;
    }
    private AppFanSchedule ReadAppScheduleEditors()
    {
        var window=programsWindow;
        var schedule=new AppFanSchedule{Enabled=true,StartMinute=ReadMinutes(window.AppStartBox,"Start time"),EndMinute=ReadMinutes(window.AppEndBox,"End time"),
            OnMinutes=ReadInteger(window.AppOnMinutesBox,"Run duration"),OffMinutes=ReadInteger(window.AppOffMinutesBox,"Idle duration"),
            RunSpeed=ReadInteger(window.AppRunSpeedBox,"Run speed"),IdleSpeed=ReadInteger(window.AppIdleSpeedBox,"Idle speed")};
        string error=schedule.Validate();if(error.Length>0)throw new ArgumentException(error);return schedule;
    }
    private void LoadAppScheduleEditors()
    {
        var s=prefs.AppSchedule;var w=programsWindow;
        w.AppStartBox.Text=ClockText(s.StartMinute);w.AppEndBox.Text=ClockText(s.EndMinute);
        w.AppOnMinutesBox.Text=s.OnMinutes.ToString(CultureInfo.InvariantCulture);w.AppOffMinutesBox.Text=s.OffMinutes.ToString(CultureInfo.InvariantCulture);
        w.AppRunSpeedBox.Text=s.RunSpeed.ToString(CultureInfo.InvariantCulture);w.AppIdleSpeedBox.Text=s.IdleSpeed.ToString(CultureInfo.InvariantCulture);
    }
    private void LoadControllerEditors()
    {
        var s=controller.State;var w=programsWindow;
        if(s.Mode is 6 or 7)w.ProgramKindBox.SelectedIndex=s.Mode==7?0:1;
        w.ControllerRunSpeedBox.Text=s.On.ToString(CultureInfo.InvariantCulture);w.ControllerIdleSpeedBox.Text=s.Off.ToString(CultureInfo.InvariantCulture);
        if(s.Settings.TryGetValue(23,out var daily)&&daily.Length==4)
        {w.ControllerStartBox.Text=$"{daily[0]:00}:{daily[1]:00}";w.ControllerEndBox.Text=$"{daily[2]:00}:{daily[3]:00}";}
        if(s.Settings.TryGetValue(22,out var cycle)&&cycle.Length==8)
        {
            // Display fractions if existing settings include seconds. Saving accepts whole minutes.
            w.ControllerOnMinutesBox.Text=(BinaryPrimitives.ReadUInt32BigEndian(cycle)/60d).ToString("0.##",CultureInfo.InvariantCulture);
            w.ControllerOffMinutesBox.Text=(BinaryPrimitives.ReadUInt32BigEndian(cycle.AsSpan(4))/60d).ToString("0.##",CultureInfo.InvariantCulture);
        }
    }
    private void SaveScheduleState()
    {
        if(smoke||captureLive)return;
        prefs.Save(preferences);
    }
    private async Task ProgramActionAsync(Func<Task> action,string message)
    {
        if(captureLive)return;
        await RunAsync(async()=>
        {
            try{programsWindow.ProgramActionText.Text="";await action();}
            catch(Exception ex){programsWindow.ProgramActionText.Text=ex.Message;throw;}
        },message,true);
        UpdateScheduleView();
    }
    private async Task ApplyAppScheduleCoreAsync()
    {
        if(smoke||captureLive||scheduleStopping)return;
        int oldMode=controller.State.Mode,oldOn=controller.State.On,oldOff=controller.State.Off;
        await scheduleSession.TickAsync(DateTimeOffset.Now);
        if(prefs.AppSchedule.Enabled&&(oldMode!=controller.State.Mode||oldOn!=controller.State.On||oldOff!=controller.State.Off))
            FollowRamp(prefs.AppSchedule.Evaluate(DateTimeOffset.Now).Speed);
    }
    private async Task ScheduleTickAsync()
    {
        if(smoke||captureLive||scheduleTickRunning||scheduleStopping||quitting||busy||!HasAppSchedule)return;
        scheduleTickRunning=true;
        try
        {
            if(controller.Connected)await RunAsync(ApplyAppScheduleCoreAsync,"",true);
            UpdateScheduleView();
        }
        finally{scheduleTickRunning=false;}
    }
    private Task StopAppScheduleCoreAsync(bool disable)=>scheduleSession.StopAsync(disable);
    private async Task StopAppScheduleAsync()=>await ProgramActionAsync(async()=>
    {
        await StopAppScheduleCoreAsync(true);
        programsWindow.ProgramActionText.Text=prefs.ScheduleRestore==null?"PC schedule stopped. Previous controller mode and presets restored.":"PC schedule stopped. Reconnect this controller to restore its previous mode and presets.";
    },"Stopping PC schedule…");
    private async Task RestoreScheduleBeforeQuitAsync()
    {
        scheduleStopping=true;scheduleTimer.Stop();
        if(smoke||captureLive)return;
        await operationGate.WaitAsync();
        try
        {
            // An in-flight start may still be capturing its baseline. Inspect it only
            // after that operation finishes, then restore before releasing Bluetooth.
            if(prefs.ScheduleRestore==null)return;
            // Enabled remains saved: relaunch resumes only after this controller reconnects.
            await StopAppScheduleCoreAsync(false);
        }
        catch(Exception ex){StatusText.Text="Previous controller settings will be restored on reconnect: "+ex.Message;}
        finally{operationGate.Release();}
    }
    private void CheckScheduleInputs()
    {
        if(!smoke)throw new InvalidOperationException("Scheduling UI checks require smoke mode.");
        var w=programsWindow;var original=prefs.AppSchedule;
        if(w.ApplyControllerButton.IsEnabled||w.StartAppButton.IsEnabled||w.StopAppButton.IsEnabled)
            throw new InvalidOperationException("Disconnected scheduling controls accepted a write.");
        if(ReadAppScheduleEditors().Validate().Length>0)throw new InvalidOperationException("Default app schedule is invalid.");
        w.AppStartBox.Text="25:00";
        try {ReadAppScheduleEditors();throw new InvalidOperationException("Invalid schedule time was accepted.");}
        catch(ArgumentException){}
        w.AppStartBox.Text="08:00";w.AppRunSpeedBox.Text="0";
        try {ReadAppScheduleEditors();throw new InvalidOperationException("Invalid running speed was accepted.");}
        catch(ArgumentException){}
        w.AppRunSpeedBox.Text="5";w.ProgramKindBox.SelectedIndex=1;
        if(w.ControllerCyclePanel.Visibility!=Visibility.Visible||w.ControllerDailyPanel.Visibility!=Visibility.Collapsed||ReadControllerEditors().Mode!=6)
            throw new InvalidOperationException("Controller cycle fields do not match the selected mode.");
        w.ProgramKindBox.SelectedIndex=0;
        if(ReadControllerEditors().Mode!=7)throw new InvalidOperationException("Controller daily fields do not match the selected mode.");
        prefs.AppSchedule=ReadAppScheduleEditors();UpdateScheduleView();
        Tray_Click(this,new RoutedEventArgs());
        if(!prefs.AppSchedule.Enabled||IsVisible)throw new InvalidOperationException("Minimizing disabled the app schedule.");
        ShowWindow();prefs.AppSchedule=original;UpdateScheduleView();
    }
    private void UpdateScheduleView()
    {
        if(!initialized)return;
        bool available=!captureLive&&controller.Connected&&(!busy||refreshing)&&!scheduleStopping;
        var w=programsWindow;
        w.ReadControllerButton.IsEnabled=available;w.ApplyControllerButton.IsEnabled=available;w.StartAppButton.IsEnabled=available;
        w.StopAppButton.IsEnabled=!captureLive&&HasAppSchedule&&(!busy||refreshing)&&!scheduleStopping;
        if(trayScheduleStop!=null)trayScheduleStop.Enabled=HasAppSchedule;
        string text="No app schedule running.";
        if(prefs.AppSchedule.Enabled)
        {
            var d=prefs.AppSchedule.Evaluate(DateTimeOffset.Now);
            text=controller.Connected?$"{d.Description} · speed {d.Speed}/10 · next {d.NextChange.ToLocalTime():ddd HH:mm}. Running in the tray when minimized.":
                "Waiting for Bluetooth. The controller keeps its last mode while disconnected; the current scheduled phase resumes on reconnect.";
        }
        else if(prefs.ScheduleRestore!=null)text="Stopped. Previous mode and presets will be restored when this controller reconnects.";
        w.AppStatusText.Text=text;
        ScheduleStatusText.Text=prefs.AppSchedule.Enabled?"This PC schedule · "+(controller.Connected?prefs.AppSchedule.Evaluate(DateTimeOffset.Now).Description:"waiting for Bluetooth"):
            prefs.ScheduleRestore!=null?"Schedule stopped · restoration pending":"";
        ScheduleStatusText.Visibility=HasAppSchedule?Visibility.Visible:Visibility.Collapsed;
        if(tray!=null)
        {
            var caption="AC Infinity · "+prefs.DisplayName()+(prefs.AppSchedule.Enabled?" · PC schedule":"");
            tray.Text=caption[..Math.Min(63,caption.Length)];
        }
    }
}
