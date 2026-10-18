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
    private byte[]? editorAutoRaw;
    private bool editorFahrenheit,loadingControllerEditors,modeActivationPending;
    private AppScheduleSession scheduleSession=null!;
    private Forms.ToolStripMenuItem? trayScheduleStop;
    private bool HasAppSchedule => prefs.AppSchedule.Enabled||prefs.ScheduleRestore!=null;
    private void InitializeScheduling()
    {
        scheduleSession=new AppScheduleSession(prefs,controller,SaveScheduleState);
        LoadAppScheduleEditors();
        programsWindow.ControllerModeCommitted+=async mode=>await ActivateControllerModeAsync(mode);
        programsWindow.ReadControllerButton.Click+=async(_,_)=>await ProgramActionAsync(async()=>{await controller.ReadProgramsAsync();LoadControllerEditors();},"Reading controller settings…");
        programsWindow.ApplyControllerButton.Click+=async(_,_)=>await SaveControllerEditorAsync(true);
        programsWindow.SaveControllerButton.Click+=async(_,_)=>await SaveControllerEditorAsync(false);
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
    private static double ReadDuration(TextBox box,string label)
    {
        var parts=box.Text.Trim().Split(':');
        if(parts.Length!=3||!int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out int h)||
           !int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out int m)||
           !int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out int sec)||
           h<0||h>24||m<0||m>59||sec<0||sec>59||h*3600+m*60+sec is <1 or >86400)
            throw new ArgumentException(label+" must use HH:mm:ss, from 00:00:01 to 24:00:00.");
        return (h*3600+m*60+sec)/60d;
    }
    private static string DurationText(uint seconds)=>$"{seconds/3600:00}:{seconds/60%60:00}:{seconds%60:00}";
    private ControllerProgram ReadControllerEditors()
    {
        var w=programsWindow;
        int mode=int.Parse((string)((ComboBoxItem)w.ProgramKindBox.SelectedItem).Tag,CultureInfo.InvariantCulture);
        var p=new ControllerProgram{Mode=mode,RunSpeed=ReadInteger(w.ControllerRunSpeedBox,"Run speed"),IdleSpeed=ReadInteger(w.ControllerIdleSpeedBox,"Idle speed")};
        switch(mode)
        {
            case 3:
                if(editorAutoRaw==null)throw new ArgumentException("Read the controller's Auto settings before editing them.");
                var auto=ControllerAutoSettings.FromRaw(editorAutoRaw,editorFahrenheit);
                auto.HighTemperatureEnabled=w.HighTemperatureEnabledBox.IsChecked==true;
                auto.LowTemperatureEnabled=w.LowTemperatureEnabledBox.IsChecked==true;
                auto.HighHumidityEnabled=w.HighHumidityEnabledBox.IsChecked==true;
                auto.LowHumidityEnabled=w.LowHumidityEnabledBox.IsChecked==true;
                auto.HighTemperature=ReadInteger(w.HighTemperatureBox,"High temperature");
                auto.LowTemperature=ReadInteger(w.LowTemperatureBox,"Low temperature");
                auto.HighHumidity=ReadInteger(w.HighHumidityBox,"High humidity");
                auto.LowHumidity=ReadInteger(w.LowHumidityBox,"Low humidity");p.Auto=auto;break;
            case 4:p.TimerOnMinutes=ReadDuration(w.TimerOnBox,"Timer to On");break;
            case 5:p.TimerOffMinutes=ReadDuration(w.TimerOffBox,"Timer to Off");break;
            case 6:p.OnMinutes=ReadDuration(w.ControllerOnMinutesBox,"Run duration");p.OffMinutes=ReadDuration(w.ControllerOffMinutesBox,"Idle duration");break;
            case 7:p.StartMinute=ReadMinutes(w.ControllerStartBox,"Start time");p.EndMinute=ReadMinutes(w.ControllerEndBox,"End time");break;
        }
        string error=p.Validate();if(error.Length>0)throw new ArgumentException(error);return p;
    }
    private async Task SaveControllerEditorAsync(bool activate)=>await ProgramActionAsync(async()=>
    {
        var program=ReadControllerEditors();
        await StopAppScheduleCoreAsync(true);
        await controller.ReadProgramsAsync();
        // Another controller app may have edited Auto since this editor was loaded.
        // Require a fresh read instead of overwriting its reserved bits or F/C pairs.
        if(program.Mode==3&&(!controller.State.Settings.TryGetValue(19,out var current)||
            editorAutoRaw==null||!System.Linq.Enumerable.SequenceEqual(current,editorAutoRaw)))
            throw new InvalidOperationException("Auto settings changed since they were loaded. Read settings, then make your edits again.");
        if(activate&&(program.Mode is 4 or 5)&&controller.State.Mode==program.Mode)
        {
            // Save & activate deliberately restarts this countdown with the edited duration.
            // Pause in its pre-expiry idle/run state so configuration verifies before restart.
            await controller.SelectModeAsync(program.Mode==4?1:2);
        }
        await controller.ApplyProgramAsync(program,activate);
        LoadControllerEditors();
        programsWindow.ProgramActionText.Text=activate?"Saved and verified. The selected mode now runs on the controller without this PC.":
            "Saved and verified. The controller's active mode was kept; shared On/Off presets take effect immediately.";
    },activate?"Saving and activating controller mode…":"Saving controller settings…");
    private void ModeMenu_Click(object sender,RoutedEventArgs e)
    {
        if(captureLive)return;
        foreach(var item in ModeMenu.Items)
            if(item is MenuItem m&&m.Tag is string tag){m.IsCheckable=true;m.IsChecked=tag==controller.State.Mode.ToString(CultureInfo.InvariantCulture);}
        // ContextMenu is a separate popup tree; share this widget's live theme resources.
        ModeMenu.Resources=Resources;ModeMenu.PlacementTarget=(Button)sender;ModeMenu.IsOpen=true;
    }
    private async void ControllerMode_Click(object sender,RoutedEventArgs e)
    {
        int mode=int.Parse((string)((MenuItem)sender).Tag,CultureInfo.InvariantCulture);
        await ActivateControllerModeAsync(mode);
    }
    private async Task ActivateControllerModeAsync(int mode)
    {
        if(loadingControllerEditors||captureLive||smoke||mode is <1 or >7)return;
        if(modeActivationPending||busy&&!refreshing||scheduleStopping)
        {
            SetControllerModeSelection(controller.State.Mode);
            return;
        }
        // Disconnected selection remains useful for browsing; it never arms a later write.
        if(!controller.Connected)return;
        if(mode==controller.State.Mode&&!HasAppSchedule)return;
        modeActivationPending=true;UpdateScheduleView();
        try
        {
        await ProgramActionAsync(async()=>
        {
            try
            {
                await StopAppScheduleCoreAsync(true);
                await controller.SelectModeAsync(mode);
                // Mode-only selection does not refresh Auto/timers/cycle/daily values.
                // Read every editor field afresh instead of displaying a previous snapshot.
                await controller.ReadProgramsAsync();
                LoadControllerEditors();
                programsWindow.ProgramActionText.Text="Active mode: "+controller.State.ModeName+". Stored settings loaded; edit the values below, then save changes.";
            }
            catch
            {
                SetControllerModeSelection(controller.State.Mode);
                throw;
            }
        },"Selecting controller mode…");
        }
        finally {modeActivationPending=false;UpdateScheduleView();}
    }
    private void SetControllerModeSelection(int mode)
    {
        if(mode is <1 or >7)return;
        bool wasLoading=loadingControllerEditors;loadingControllerEditors=true;
        try {programsWindow.ProgramKindBox.SelectedIndex=mode-1;}
        finally {loadingControllerEditors=wasLoading;}
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
    private void LoadControllerEditors(int? selectedMode=null)
    {
        bool wasLoading=loadingControllerEditors;loadingControllerEditors=true;
        try
        {
            var s=controller.State;var w=programsWindow;
            if((selectedMode??s.Mode) is >=1 and <=7)w.ProgramKindBox.SelectedIndex=(selectedMode??s.Mode)-1;
            w.ControllerRunSpeedBox.Text=s.On.ToString(CultureInfo.InvariantCulture);w.ControllerIdleSpeedBox.Text=s.Off.ToString(CultureInfo.InvariantCulture);
            if(s.Settings.TryGetValue(23,out var daily)&&daily.Length==4)
            {w.ControllerStartBox.Text=$"{daily[0]:00}:{daily[1]:00}";w.ControllerEndBox.Text=$"{daily[2]:00}:{daily[3]:00}";}
            if(s.Settings.TryGetValue(22,out var cycle)&&cycle.Length==8)
            {w.ControllerOnMinutesBox.Text=DurationText(BinaryPrimitives.ReadUInt32BigEndian(cycle));w.ControllerOffMinutesBox.Text=DurationText(BinaryPrimitives.ReadUInt32BigEndian(cycle.AsSpan(4)));}
            if(s.Settings.TryGetValue(20,out var on)&&on.Length==4)w.TimerOnBox.Text=DurationText(BinaryPrimitives.ReadUInt32BigEndian(on));
            if(s.Settings.TryGetValue(21,out var off)&&off.Length==4)w.TimerOffBox.Text=DurationText(BinaryPrimitives.ReadUInt32BigEndian(off));
            editorAutoRaw=null;
            if(s.Settings.TryGetValue(19,out var raw)&&raw.Length==7)
            {
                editorAutoRaw=(byte[])raw.Clone();editorFahrenheit=fahrenheit;
                var a=ControllerAutoSettings.FromRaw(raw,editorFahrenheit);w.AutoUnitText.Text=editorFahrenheit?"°F":"°C";
                w.HighTemperatureEnabledBox.IsChecked=a.HighTemperatureEnabled;w.LowTemperatureEnabledBox.IsChecked=a.LowTemperatureEnabled;
                w.HighHumidityEnabledBox.IsChecked=a.HighHumidityEnabled;w.LowHumidityEnabledBox.IsChecked=a.LowHumidityEnabled;
                w.HighTemperatureBox.Text=a.HighTemperature.ToString(CultureInfo.InvariantCulture);w.LowTemperatureBox.Text=a.LowTemperature.ToString(CultureInfo.InvariantCulture);
                w.HighHumidityBox.Text=a.HighHumidity.ToString(CultureInfo.InvariantCulture);w.LowHumidityBox.Text=a.LowHumidity.ToString(CultureInfo.InvariantCulture);
            }
        }
        finally {loadingControllerEditors=wasLoading;}
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
        int nativeModeBefore=controller.State.Mode;
        if(w.SaveControllerButton.IsEnabled||w.ApplyControllerButton.IsEnabled||w.StartAppButton.IsEnabled||w.StopAppButton.IsEnabled)
            throw new InvalidOperationException("Disconnected scheduling controls accepted a write.");
        if(ReadAppScheduleEditors().Validate().Length>0)throw new InvalidOperationException("Default app schedule is invalid.");
        w.AppStartBox.Text="25:00";
        try {ReadAppScheduleEditors();throw new InvalidOperationException("Invalid schedule time was accepted.");}
        catch(ArgumentException){}
        w.AppStartBox.Text="08:00";w.AppRunSpeedBox.Text="0";
        try {ReadAppScheduleEditors();throw new InvalidOperationException("Invalid running speed was accepted.");}
        catch(ArgumentException){}
        w.AppRunSpeedBox.Text="5";w.ProgramKindBox.SelectedIndex=5;
        if(w.ControllerCyclePanel.Visibility!=Visibility.Visible||w.ControllerDailyPanel.Visibility!=Visibility.Collapsed||ReadControllerEditors().Mode!=6)
            throw new InvalidOperationException("Controller cycle fields do not match the selected mode.");
        w.ProgramKindBox.SelectedIndex=6;
        if(ReadControllerEditors().Mode!=7)throw new InvalidOperationException("Controller daily fields do not match the selected mode.");
        w.ProgramKindBox.SelectedIndex=3;w.TimerOnBox.Text="24:00:00";
        if(ReadControllerEditors().TimerOnMinutes!=1440)throw new InvalidOperationException("24-hour timer was truncated.");
        w.TimerOnBox.Text="00:00:01";
        if(ReadControllerEditors().TimerOnMinutes!=1d/60)throw new InvalidOperationException("Timer seconds were truncated.");
        w.TimerOnBox.Text="24:00:01";
        try{ReadControllerEditors();throw new InvalidOperationException("Timer above 24 hours accepted.");}catch(ArgumentException){}
        w.TimerOnBox.Text="00:30:00";
        editorAutoRaw=new byte[]{0xaf,88,31,60,16,80,50};editorFahrenheit=false;
        w.ProgramKindBox.SelectedIndex=2;w.HighTemperatureBox.Text="31";w.LowTemperatureBox.Text="16";
        w.HighHumidityBox.Text="80";w.LowHumidityBox.Text="50";
        w.HighTemperatureEnabledBox.IsChecked=true;w.LowTemperatureEnabledBox.IsChecked=true;
        w.HighHumidityEnabledBox.IsChecked=true;w.LowHumidityEnabledBox.IsChecked=false;
        var encoded=ReadControllerEditors().Auto!.Encode();
        if(encoded[0]!=0xae||encoded[1]!=88||encoded[3]!=60)throw new InvalidOperationException("Auto editor lost reserved bits or unchanged F/C pairs.");
        editorAutoRaw=null;w.ProgramKindBox.SelectedIndex=6;
        LoadControllerEditors();
        if(controller.State.Mode!=nativeModeBefore)throw new InvalidOperationException("Programmatic editor selection or reload changed controller mode.");
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
        // Keep the active selector in step with manual controls, timer completion, and
        // PC schedule phases without replacing draft values during regular polling.
        if(!smoke&&!captureLive&&controller.Connected&&!busy&&!loadingControllerEditors&&!w.ProgramKindBox.IsDropDownOpen&&
            w.ProgramKindBox.SelectedIndex+1!=controller.State.Mode)
            SetControllerModeSelection(controller.State.Mode);
        int selectedMode=w.ProgramKindBox.SelectedIndex+1;
        bool activeTimer=controller.Connected&&(selectedMode is 4 or 5)&&selectedMode==controller.State.Mode;
        w.ProgramKindBox.IsEnabled=!captureLive&&(!busy||refreshing)&&!scheduleStopping&&!modeActivationPending;
        ModeButton.IsEnabled=FanOptionsButton.IsEnabled=available&&!modeActivationPending;
        w.SaveControllerButton.IsEnabled=available&&!activeTimer;
        w.SaveControllerButton.ToolTip=activeTimer?"Use Save & activate to restart this countdown with your edited duration.":"Save edited values while keeping the active mode; shared On/Off presets take effect immediately.";
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
