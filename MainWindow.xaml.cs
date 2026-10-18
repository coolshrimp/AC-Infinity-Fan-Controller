using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ACInfinityDesktop;
public partial class MainWindow : Window
{
    private readonly AppearanceWindow appearanceWindow=new();
    private readonly ColorPickerWindow colorPicker=new();
    private bool syncingAppearance,editingAccent;
    private readonly BluetoothWindow bluetoothWindow=new();
    private readonly ProgramsWindow programsWindow=new();
    private readonly HelpWindow helpWindow=new();
    private readonly HistoryWindow historyWindow=new();
    private readonly SensorHistory history=new();
    private DateTimeOffset historySaved;
    private readonly SpeedPolling speedPolling=new();
    private bool editingSpeed, submittingSpeed;
    private double speedAtStart;
    private bool loadingDisplayName;
    private readonly List<int> smokeSpeedCommands=new();
    private readonly TaskCompletionSource smokeReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task SmokeReady=>smokeReady.Task;
    private string historyAddress="";
    private Window[] Panels=>new Window[]{appearanceWindow,bluetoothWindow,programsWindow,helpWindow,colorPicker,historyWindow};
    private TextBox AddressBox=>bluetoothWindow.AddressBox;
    private ListBox DevicesList=>bluetoothWindow.DevicesList;
    private Button ScanButton=>bluetoothWindow.ScanButton;
    private Button ConnectButton=>bluetoothWindow.ConnectButton;
    private Button DisconnectButton=>bluetoothWindow.DisconnectButton;
    private CheckBox AutoConnectBox=>appearanceWindow.AutoConnectBox;
    private Slider OpacitySlider=>appearanceWindow.OpacitySlider;
    private TextBlock ProgramsText=>programsWindow.ProgramsText;
    private readonly BluetoothController controller=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(10)};
    private Forms.NotifyIcon? tray;
    private bool busy, refreshing, quitting, fahrenheit, initialized;
    private readonly System.Threading.SemaphoreSlim operationGate=new(1,1);
    private int animatedFanSpeed=-1;
    private readonly bool smoke=Environment.GetCommandLineArgs().Contains("--smoke");
    private readonly bool captureLive=Environment.GetCommandLineArgs().Contains("--capture-live");
    private Preferences prefs=new();
    private readonly ReconnectPolicy reconnect=new();
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ACInfinityDesktop");
    private string preferences;
    private readonly bool additional;
    private string connectingAddress="";
    private bool restorePending;
    internal bool IsQuitting=>quitting;
    internal bool RestorePending=>restorePending;
    internal string ControllerAddress=>connectingAddress.Length>0?connectingAddress:prefs.address;
    public MainWindow() : this(null,false) { }
    internal MainWindow(string? settingsPath,bool additional)
    {
        this.additional=additional;
        preferences=settingsPath??Path.Combine(DataDirectory,"preferences.json");
        InitializeComponent();
        foreach(var panel in Panels){panel.Resources=Resources;panel.SizeChanged+=(_,_)=>UpdateDockedPanels();}
        LocationChanged+=(_,_)=>UpdateDockedPanels();SizeChanged+=(_,_)=>UpdateDockedPanels();
        SpeedSlider.ValueChanged+=Speed_Changed;
        SpeedSlider.PreviewMouseLeftButtonDown+=(_,_)=>BeginSpeedEdit();
        SpeedSlider.AddHandler(Mouse.PreviewMouseUpEvent,new MouseButtonEventHandler(async(_,e)=>{if(e.ChangedButton==MouseButton.Left)await ReleaseSpeedAsync();}),true);
        SpeedSlider.LostMouseCapture+=async(_,_)=>{if(Mouse.LeftButton==MouseButtonState.Released)await ReleaseSpeedAsync();else {editingSpeed=false;UpdateView();}};
        SpeedSlider.PreviewKeyDown+=(_,e)=>{if(IsSpeedKey(e.Key))BeginSpeedEdit();};
        SpeedSlider.KeyUp+=async(_,e)=>{if(IsSpeedKey(e.Key))await ReleaseSpeedAsync();};
        SpeedSlider.LostKeyboardFocus+=async(_,_)=>await ReleaseSpeedAsync();
        ScanButton.Click+=Scan_Click;ConnectButton.Click+=Connect_Click;DisconnectButton.Click+=Disconnect_Click;
        bluetoothWindow.AddControllerButton.Click+=(_,_)=>{if(!captureLive)((App)Application.Current).OpenAdditionalController();};
        DevicesList.SelectionChanged+=Device_Selected;bluetoothWindow.WindowsBluetoothButton.Click+=WindowsBluetooth_Click;
        AutoConnectBox.Click+=Appearance_Changed;OpacitySlider.ValueChanged+=Appearance_Changed;
        appearanceWindow.PanelColorButton.Click+=(_,_)=>PickColor(false);
        appearanceWindow.AccentColorButton.Click+=(_,_)=>PickColor(true);
        appearanceWindow.DefaultsButton.Click+=RestoreAppearance;
        bluetoothWindow.DisplayNameBox.ItemsSource=ColorChoices.FanNames;
        bluetoothWindow.DisplayNameBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler(DisplayName_Changed));
        appearanceWindow.PanelPresetBox.ItemsSource=ColorChoices.Panels;appearanceWindow.AccentPresetBox.ItemsSource=ColorChoices.Accents;
        appearanceWindow.PanelPresetBox.SelectionChanged+=(_,_)=>PresetSelected(false);
        appearanceWindow.AccentPresetBox.SelectionChanged+=(_,_)=>PresetSelected(true);
        appearanceWindow.FahrenheitBox.Click+=(_,_)=>{fahrenheit=appearanceWindow.FahrenheitBox.IsChecked==true;SavePreferences();UpdateView();};
        foreach(var range in new[]{historyWindow.HourButton,historyWindow.SixHourButton,historyWindow.DayButton})range.Click+=Range_Click;
        colorPicker.Finished+=color=>{if(color.HasValue){string hex=$"#{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";if(editingAccent)prefs.Accent=hex;else prefs.PanelColor=hex;SavePreferences();}ApplyAppearance();TogglePanel(appearanceWindow);};
        bluetoothWindow.ConnectionStatusText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=StatusText});
        prefs=smoke||additional&&settingsPath==null?new():Preferences.Read(preferences);
        // Every controller opens on Status, including settings from older layouts.
        prefs.WidgetView="Status";
        AddressBox.Text="";fahrenheit=prefs.unit=="F";
        AutoConnectBox.IsChecked=prefs.AutoConnect;
        OpacitySlider.Value=Math.Clamp(prefs.Transparency,45,100);Topmost=prefs.Pinned;
        if(prefs.Left is double x && prefs.Top is double y && double.IsFinite(x)&&double.IsFinite(y))
        {Left=Math.Clamp(x,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));Top=Math.Clamp(y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Height));}
        historyAddress=prefs.address;history.SelectDevice(DataDirectory,prefs.address,smoke);
        initialized=true;UpdateDeviceIdentity();ApplyAppearance();ApplyWidgetView();
        IsVisibleChanged+=(_,_)=>UpdateFanAnimation(controller.Connected?controller.State.Output:null);
        timer.Tick+=async(_,_)=>{if(busy||quitting)return;if(controller.Connected)await RunAsync(controller.RefreshAsync,"",true,true);else {UpdateView();await AutoConnectAsync();}};
        UpdateView();
    }
    private async void Window_Loaded(object sender,RoutedEventArgs e)
    {
        foreach(var panel in Panels)panel.Owner=this;
        if(smoke){
            if(prefs.WidgetView!="Status"||ModeText.Visibility!=Visibility.Visible||ChartPanel.Visibility!=Visibility.Collapsed)
                throw new InvalidOperationException("Widget must open on Status with the controller mode visible.");
            ChartTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(ChartPanel.Visibility!=Visibility.Visible||ModeText.Visibility!=Visibility.Visible)
                throw new InvalidOperationException("Chart must retain the controller status and controls.");
            StatusTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(ChartPanel.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Status did not hide the inline chart.");
            // Exercise release-to-set without a radio, including telemetry during a drag.
            SpeedSlider.IsEnabled=true;SpeedSlider.Value=3;
            if(smokeSpeedCommands.Count!=0)throw new InvalidOperationException("Telemetry issued a speed command");
            BeginSpeedEdit();SpeedSlider.Value=8;UpdateView();
            if(SpeedSlider.Value!=8||smokeSpeedCommands.Count!=0)throw new InvalidOperationException("Refresh interrupted slider editing");
            await ReleaseSpeedAsync();await ReleaseSpeedAsync();
            if(smokeSpeedCommands.Count!=1||smokeSpeedCommands[0]!=8)throw new InvalidOperationException("Release must submit exactly once");
            SpeedSlider.IsEnabled=true;BeginSpeedEdit();await ReleaseSpeedAsync();
            if(smokeSpeedCommands.Count!=1)throw new InvalidOperationException("Unchanged release issued a command");
            SpeedSlider.IsEnabled=true;SpeedSlider.Value=5;BeginSpeedEdit();SpeedSlider.Value=0;await ReleaseSpeedAsync();
            if(smokeSpeedCommands.Count!=2||smokeSpeedCommands[1]!=0)throw new InvalidOperationException("Off release failed");
            UpdateView();
            Pin_Click(this,new RoutedEventArgs());if(!Topmost)throw new InvalidOperationException("Pin failed");
            Pin_Click(this,new RoutedEventArgs());if(Topmost)throw new InvalidOperationException("Unpin failed");
            Connection_Click(this,new RoutedEventArgs());if(!bluetoothWindow.IsVisible)throw new InvalidOperationException("Connection panel failed");
            prefs.address="AA:BB:CC:DD:EE:FF";UpdateDeviceIdentity();bluetoothWindow.DisplayNameBox.SelectedIndex=1;
            if(prefs.DisplayName()!="CLOUDLINE T6")throw new InvalidOperationException("Suggested name failed");
            bluetoothWindow.DisplayNameBox.Text="Office fan";
            if(prefs.DisplayName()!="Office fan")throw new InvalidOperationException("Custom name failed");
            prefs.address="";prefs.DisplayNames.Clear();UpdateDeviceIdentity();
            double oldLeft=Left,oldPanelLeft=bluetoothWindow.Left;Left+=20;
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var dockingArea=MonitorWorkArea();
            bool clampedHorizontally=Math.Abs(bluetoothWindow.Left-dockingArea.Left)<1||Math.Abs(bluetoothWindow.Left+bluetoothWindow.ActualWidth-dockingArea.Right)<1;
            // Small desktops can correctly keep the panel against the screen edge.
            if(Math.Abs(bluetoothWindow.Left-oldPanelLeft)<.1&&!clampedHorizontally)throw new InvalidOperationException("Docked window did not follow");
            if(bluetoothWindow.Left<dockingArea.Left-1||bluetoothWindow.Left+bluetoothWindow.ActualWidth>dockingArea.Right+1)
                throw new InvalidOperationException("Docked window escaped the monitor work area.");
            Left=oldLeft;
            Connection_Click(this,new RoutedEventArgs());
            prefs.PanelColor="#164E63";prefs.Accent="#F0ABFC";OpacitySlider.Value=65;
            if(((SolidColorBrush)Shell.Background).Color.A!=(byte)(255*65/100)||prefs.PanelColor!="#164E63"||prefs.Accent!="#F0ABFC"||prefs.Transparency!=65)throw new InvalidOperationException("Appearance failed");
            RestoreAppearance(this,new RoutedEventArgs());
            appearanceWindow.PanelPresetBox.SelectedIndex=2;
            if(prefs.PanelColor!="#1C302C")throw new InvalidOperationException("Panel preset failed");
            appearanceWindow.AccentPresetBox.SelectedIndex=1;
            if(prefs.Accent!="#A855F7")throw new InvalidOperationException("Accent preset failed");
            appearanceWindow.FahrenheitBox.IsChecked=true;appearanceWindow.FahrenheitBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(!fahrenheit||UnitButton.Content.ToString()!="°F")throw new InvalidOperationException("Settings temperature switch failed");
            Unit_Click(this,new RoutedEventArgs());if(appearanceWindow.FahrenheitBox.IsChecked==true)throw new InvalidOperationException("Temperature controls are out of sync");
            PickColor(true);colorPicker.HexBox.Text="invalid";
            if(colorPicker.UseButton.IsEnabled)throw new InvalidOperationException("Invalid hex accepted");
            colorPicker.HexBox.Text="#12AB34";colorPicker.UseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(prefs.Accent!="#12AB34"||!appearanceWindow.IsVisible)throw new InvalidOperationException("Custom color failed");
            RestoreAppearance(this,new RoutedEventArgs());ClosePopups();
            History_Click(this,new RoutedEventArgs());
            if(!historyWindow.IsVisible||ActualWidth!=450)throw new InvalidOperationException("Separate history panel changed the main widget.");
            historyWindow.DayButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(prefs.HistoryHours!=24)throw new InvalidOperationException("History panel range did not update.");
            ClosePopups();
            Tray_Click(this,new RoutedEventArgs());if(IsVisible)throw new InvalidOperationException("Tray hide failed");ShowWindow();
            if(Environment.GetCommandLineArgs().Contains("--picker"))PickColor(true);
            if(Environment.GetCommandLineArgs().Contains("--settings"))Settings_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--bluetooth"))
            {
                DevicesList.ItemsSource=new[]{new DiscoveredController(0xAABBCCDDEEFF,"Example Controller 67",-65,1){Verified=true}};
                Connection_Click(this,new RoutedEventArgs());
            }
            if(Environment.GetCommandLineArgs().Contains("--programs"))Programs_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--help-panel"))Help_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--history"))History_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--transparent"))OpacitySlider.Value=45;
            if(Environment.GetCommandLineArgs().Contains("--light")){prefs.PanelColor="#F0E9D6";prefs.Accent="#CC3377";ApplyAppearance();Settings_Click(this,new RoutedEventArgs());}
            if(Environment.GetCommandLineArgs().Any(arg=>arg is "--status" or "--controls" or "--readings")){prefs.WidgetView="Status";ApplyWidgetView();}
            if(Environment.GetCommandLineArgs().Contains("--chart")){prefs.WidgetView="Chart";ApplyWidgetView();}
            if(Environment.GetCommandLineArgs().Contains("--demo"))
            {
                var now=DateTimeOffset.Now;for(int i=0;i<1440;i++)history.Add(new SensorSample(now.AddHours(-6).AddSeconds(i*15),23+Math.Sin(i/150d),48+Math.Cos(i/170d)*3));
                DeviceNameText.Text="Example fan";TemperatureText.Text="23.1";HumidityText.Text="48.0";VpdText.Text="1.47";OutputText.Text="6";ModeText.Text="ON · MANUAL";OnOffToggle.IsEnabled=true;OnOffToggle.IsChecked=true;OnOffToggle.Content="On";SpeedSlider.IsEnabled=true;SpeedSlider.Value=6;SpeedSlider.Tag=6d;StatusText.Text="Preview · simulated readings";UpdateCharts();
            }
            if(AddressBox.Text.Length!=0)throw new InvalidOperationException("Address field must start blank");
            // SizeToContent settles after Loaded and the requested view change.
            // Await that layout cycle instead of asserting against the previous height.
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            UpdateLayout();
            var content=(FrameworkElement)Shell.Child;
            File.WriteAllText(App.GetOutput(Environment.GetCommandLineArgs())+".layout.json",JsonSerializer.Serialize(new
            {
                windowHeight=ActualHeight,shellHeight=Shell.ActualHeight,contentHeight=content.ActualHeight,
                desiredHeight=content.DesiredSize.Height,workArea=SystemParameters.WorkArea.ToString(),
                dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip
            },new JsonSerializerOptions{WriteIndented=true}));
            if(Shell.ActualHeight+1<content.ActualHeight)throw new InvalidOperationException($"Widget content overflow: shell {Shell.ActualHeight}, content {content.ActualHeight}, window {ActualHeight}.");
            smokeReady.TrySetResult();return;
        }
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/ui/icon.ico")).Stream;
        using var source=new System.Drawing.Icon(stream);
        tray=new Forms.NotifyIcon{Icon=(System.Drawing.Icon)source.Clone(),Text="AC Infinity Desktop · .NET",Visible=true};
        UpdateWidgetCaption();
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Show controller",null,(_,_)=>Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Quit",null,async(_,_)=>await Dispatcher.InvokeAsync(QuitAsync).Task.Unwrap());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowWindow);
        timer.Start();await AutoConnectAsync();
    }
    internal void ShowWindow(){Show();WindowState=WindowState.Normal;Activate();}
    internal void RestoreWhenConnected()
    {
        // A remembered controller retries quietly: no empty window or tray icon.
        restorePending=true;timer.Start();_ = AutoConnectAsync();
    }
    internal async Task CheckMultipleWidgetsAsync()
    {
        if(!smoke)throw new InvalidOperationException("Multiple-widget checks require smoke mode.");
        var app=(App)Application.Current;
        prefs.address="AA:BB:CC:DD:EE:FF";
        app.OpenAdditionalController();
        await app.Windows.OfType<MainWindow>().Single(w=>w!=this).SmokeReady.WaitAsync(TimeSpan.FromSeconds(30));
        var child=app.Windows.OfType<MainWindow>().Single(w=>w!=this);
        app.OpenAdditionalController();
        if(app.Windows.OfType<MainWindow>().Count()!=2)throw new InvalidOperationException("Empty controller chooser was duplicated.");
        child.prefs.address="11:22:33:44:55:66";
        child.prefs.Rename("Second controller");
        child.fahrenheit=!fahrenheit;
        if(!app.IsControllerOpen(prefs.address,child)||!app.IsControllerOpen(child.prefs.address,this)||
            prefs.DisplayName()=="Second controller"||ReferenceEquals(history,child.history)||ReferenceEquals(controller,child.controller)||child.fahrenheit==fahrenheit)
            throw new InvalidOperationException("Controller widgets must own independent connections and settings.");
        await child.QuitAsync();
        if(app.Windows.OfType<MainWindow>().Count(w=>!w.IsQuitting)!=1||!IsVisible)
            throw new InvalidOperationException("Closing one controller closed the other widget.");
        var pending=new MainWindow(null,true);pending.prefs.address="11:22:33:44:55:66";
        pending.RestoreWhenConnected();
        if(pending.IsVisible||!pending.RestorePending||pending.tray!=null)
            throw new InvalidOperationException("An offline remembered controller created a visible widget.");
        await pending.QuitAsync();
        prefs.address="";UpdateDeviceIdentity();
    }
    private async void Window_Closing(object? sender,CancelEventArgs e){if(!quitting){e.Cancel=true;await QuitAsync();}}
    public async Task QuitAsync()
    {
        if(quitting)return;ClosePopups();SavePreferences();quitting=true;timer.Stop();
        appearanceWindow.AllowClose=bluetoothWindow.AllowClose=programsWindow.AllowClose=helpWindow.AllowClose=colorPicker.AllowClose=historyWindow.AllowClose=true;
        SaveHistory();
        foreach(var panel in Panels)panel.Close();
        await controller.DisconnectAsync();
        if(tray!=null){tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Icon?.Dispose();tray.Dispose();}
        Close();
        // Each controller owns its lifetime; quitting one must not stop the others.
        var remaining=Application.Current.Windows.OfType<MainWindow>().Where(w=>!w.quitting).ToArray();
        if(remaining.All(w=>w.restorePending))
        {
            // Quiet retries must not keep an otherwise closed application alive.
            foreach(var pending in remaining)await pending.QuitAsync();
            Application.Current.Shutdown();
        }
    }
    private void SavePreferences()
    {
        if(smoke||captureLive||!initialized||additional&&string.IsNullOrEmpty(prefs.address))return;
        prefs.unit=fahrenheit?"F":"C";prefs.Pinned=Topmost;prefs.Left=Left;prefs.Top=Top;
        try{prefs.Save(preferences);}catch(Exception ex){StatusText.Text="Could not save preferences: "+ex.Message;}
    }
    private async Task AutoConnectAsync()
    {
        if(smoke||busy||quitting||controller.Connected||!reconnect.Due(prefs.AutoConnect,DateTime.UtcNow))return;
        await RunAsync(async()=>
        {
            if(captureLive)
            {
                // ConnectAsync already validates the service and live-data schema.
                // Avoid opening a separate probe connection just for documentation.
                var candidates=await BluetoothController.ScanAsync(prefs.address);
                var compatible=candidates.Where(d=>d.Type is null or 1).ToList();
                if(compatible.Count!=1)throw new IOException("Live capture needs exactly one compatible controller; select it in normal mode first.");
                await ConnectTargetAsync(compatible[0]);
                return;
            }
            var found=await DiscoverVerifiedAsync(prefs.address);
            if(quitting)return;
            DevicesList.ItemsSource=found;
            var supported=found.Where(d=>d.SupportsControls).ToList();
            if(supported.Count==1)await ConnectTargetAsync(supported[0]);
            else {reconnect.Failed(DateTime.UtcNow);StatusText.Text=supported.Count>1?"Several verified controllers found. Choose one in Bluetooth settings.":found.Count>0?"AC Infinity devices found, but their models do not support control in this version.":"No controller verified yet. Retrying automatically; close other controller apps if needed.";}
        },"Looking for your controller…",true);
    }
    private async Task<List<DiscoveredController>> DiscoverVerifiedAsync(string address)
    {
        var candidates=await BluetoothController.ScanAsync(address);
        var verified=new List<DiscoveredController>();int failed=0;
        DevicesList.ItemsSource=verified.ToArray();DevicesList.Visibility=Visibility.Visible;
        foreach(var candidate in candidates)
        {
            if(quitting)break;
            // Never probe an address already reserved by another widget: probing
            // could compete with that widget's active GATT session.
            if(((App)Application.Current).IsControllerOpen(candidate.Mac,this))continue;
            StatusText.Text=$"Checking nearby controller {verified.Count+failed+1} of {candidates.Count}...";
            try
            {
                var result=await BluetoothController.VerifyAsync(candidate);
                if(quitting)break;
                verified.Add(result);DevicesList.ItemsSource=verified.ToArray();
            }
            catch(Exception ex) when(ex is IOException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {failed++;}
        }
        ConnectButton.Visibility=Visibility.Visible;
        StatusText.Text=$"{verified.Count} verified controller(s)."+(failed>0?$" {failed} could not be verified; move closer or close other controller apps and rescan.":"");
        return verified;
    }
    private void UpdateDeviceIdentity()
    {
        loadingDisplayName=true;
        bool selected=!string.IsNullOrEmpty(prefs.address);
        bluetoothWindow.DisplayNameBox.IsEnabled=selected;
        bluetoothWindow.DisplayNameBox.Text=prefs.DisplayName()=="AC Infinity"?"":prefs.DisplayName();
        bluetoothWindow.BluetoothNameText.Text=selected?"Bluetooth name: "+prefs.DeviceName:"Bluetooth name: no controller selected";
        bluetoothWindow.ControllerModelText.Text=!selected?"Controller: not identified":prefs.ControllerType==1?"Controller: Controller 67 (CTR67A)":"Controller 67-compatible connection; exact model unconfirmed";
        DeviceNameText.Text=prefs.DisplayName();UpdateWidgetCaption();loadingDisplayName=false;
    }
    private void UpdateWidgetCaption()
    {
        Title="AC Infinity Desktop · "+prefs.DisplayName();
        if(tray!=null){var label="AC Infinity · "+prefs.DisplayName();tray.Text=label[..Math.Min(63,label.Length)];}
    }
    private void DisplayName_Changed(object sender,TextChangedEventArgs e)
    {
        if(loadingDisplayName||!initialized)return;
        prefs.Rename(e.OriginalSource is TextBox entry?entry.Text:bluetoothWindow.DisplayNameBox.Text);DeviceNameText.Text=prefs.DisplayName();DeviceNameText.ToolTip=DeviceNameText.Text;UpdateWidgetCaption();SavePreferences();
    }
    private async Task ConnectTargetAsync(DiscoveredController target)
    {
        var app=(App)Application.Current;
        if(app.IsControllerOpen(target.Mac,this))throw new InvalidOperationException("This controller already has an open widget.");
        // Reserve before the first await so two windows cannot race to connect.
        connectingAddress=target.Mac;
        try {await controller.ConnectAsync(target);}
        finally {connectingAddress="";}
        if(quitting){await controller.DisconnectAsync();return;}
        if(historyAddress!=target.Mac){SaveHistory();history.SelectDevice(DataDirectory,target.Mac,smoke);historyAddress=target.Mac;}
        if(additional)preferences=Path.Combine(DataDirectory,"controllers",target.Mac.Replace(":","")+".json");
        prefs.DeviceName=target.Name;prefs.ControllerType=target.Type;prefs.address=target.Mac;UpdateDeviceIdentity();AddressBox.Text=target.Mac;reconnect.Reset();SavePreferences();
        DevicesList.Visibility=Visibility.Collapsed;ConnectButton.Visibility=Visibility.Collapsed;
        Title="AC Infinity Desktop · "+prefs.DisplayName();
        SpeedSlider.Value=controller.State.Output??0;
        StatusText.Text="Connected. Readings refresh every 10 seconds, or 2 seconds while changing.";
        if(restorePending){restorePending=false;Show();}
    }
    private async Task RunAsync(Func<Task> action,string message,bool background=false,bool isRefresh=false)
    {
        // A user command may wait for a status read; duplicate refreshes are skipped.
        if(busy && (!refreshing || isRefresh))return;
        await operationGate.WaitAsync();
        if(quitting){operationGate.Release();return;}
        busy=true;refreshing=isRefresh;UpdateButtons();
        if(message.Length>0){StatusText.Text=message;StatusText.Foreground=(Brush)Resources["MutedBrush"];}
        try {await action();UpdateView();if(!background){StatusText.Text=controller.Connected?"Controller responded. Readings refresh every 10 seconds, or 2 seconds while changing.":"Ready.";StatusText.Foreground=(Brush)Resources["MutedBrush"];}}
        catch(Exception ex){if(!controller.Connected)reconnect.Failed(DateTime.UtcNow);UpdateView();StatusText.Text=ex is OperationCanceledException?"Connection timed out. Move closer and close other AC Infinity apps.":ex.Message;StatusText.Foreground=Brushes.LightSalmon;}
        finally {busy=false;refreshing=false;UpdateButtons();operationGate.Release();}
    }
    private async void Scan_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        await RunAsync(async()=>
        {
            reconnect.Reset();var address=AddressBox.Text.Trim();var devices=await DiscoverVerifiedAsync(address);
            if(quitting)return;
            var compatible=devices;var supported=devices.Where(d=>d.SupportsControls).ToList();
            DevicesList.ItemsSource=compatible;DevicesList.Visibility=Visibility.Visible;ConnectButton.Visibility=Visibility.Visible;
            if(supported.Count==1 && prefs.AutoConnect)await ConnectTargetAsync(supported[0]);
            else StatusText.Text=supported.Count>0?"Choose a verified controller below.":compatible.Count>0?"Detected models are not supported for control yet.":"No controller could be verified. Move closer, close other controller apps, and rescan.";
        },"Scanning nearby Bluetooth devices…",true);
    }
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(DevicesList.SelectedItem is not DiscoveredController target || !target.SupportsControls)return;
        reconnect.Reset();await RunAsync(()=>ConnectTargetAsync(target),"Connecting and validating Controller 67 settings…");
    }
    private void Device_Selected(object sender,SelectionChangedEventArgs e)=>UpdateButtons();
    private async void Disconnect_Click(object sender,RoutedEventArgs e){reconnect.Paused=true;await RunAsync(controller.DisconnectAsync,"Releasing Bluetooth...");if(!controller.Connected)AddressBox.Clear();StatusText.Text="Disconnected. Automatic retries paused until you scan, connect, or restart.";}
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await RunAsync(controller.RefreshAsync,"",true,true);
    private static bool IsSpeedKey(Key key)=>key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    private void BeginSpeedEdit()
    {
        if(editingSpeed||!SpeedSlider.IsEnabled)return;
        editingSpeed=true;speedAtStart=SpeedSlider.Value;
    }
    private async Task ReleaseSpeedAsync()
    {
        if(!editingSpeed)return;
        editingSpeed=false;int level=(int)SpeedSlider.Value;
        if(captureLive){UpdateView();return;}
        if(level==(int)speedAtStart){UpdateView();return;}
        if(smoke){smokeSpeedCommands.Add(level);return;}
        if(!controller.Connected||submittingSpeed){UpdateView();return;}
        submittingSpeed=true;
        try
        {
            await RunAsync(async()=>{
                if(level==0)await controller.SetModeAsync(false);else await controller.SetSpeedAsync(level);
                FollowRamp(level==0?controller.State.Off:level);
            },level==0?"Selecting saved Off mode...":$"Setting speed to {level}...");
        }
        finally{submittingSpeed=false;UpdateView();}
    }
    private async void OnOff_Click(object sender,RoutedEventArgs e)
    {
        if(captureLive){UpdateView();return;}
        bool turnOn=OnOffToggle.IsChecked==true;
        OnOffToggle.IsChecked=controller.Connected&&controller.State.Mode==2;
        await RunAsync(async()=>{await controller.SetModeAsync(turnOn);FollowRamp(turnOn?controller.State.On:controller.State.Off);},"Updating fan mode...");
    }
    private void Unit_Click(object sender,RoutedEventArgs e){fahrenheit=!fahrenheit;SavePreferences();UpdateView();}
    private void Pin_Click(object sender,RoutedEventArgs e){Topmost=!Topmost;foreach(var panel in Panels)panel.Topmost=Topmost;ApplyAppearance();SavePreferences();}
    private void Speed_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {if(editingSpeed)PresetText.Text=(int)e.NewValue==0?"Release to select saved Off mode (minimum speed may be above zero).":$"Release to set speed {(int)e.NewValue}.";}
    private void UpdateButtons()
    {
        bool live=controller.Connected;
        bool blockControls=busy&&!refreshing;
        foreach(var b in new[]{RefreshButton})b.IsEnabled=live&&!blockControls;
        OnOffToggle.IsEnabled=live&&!blockControls;SpeedSlider.IsEnabled=live&&!blockControls;ScanButton.IsEnabled=!blockControls&&!live;AddressBox.IsEnabled=!blockControls&&!live;
        ScanButton.Visibility=live?Visibility.Collapsed:Visibility.Visible;
        DisconnectButton.Visibility=live?Visibility.Visible:Visibility.Collapsed;DisconnectButton.IsEnabled=!blockControls;
        DevicesList.IsEnabled=!blockControls;ConnectButton.IsEnabled=!blockControls&&!live && DevicesList.SelectedItem is DiscoveredController selected && selected.SupportsControls;
    }
    private void UpdateView()
    {
        bool live=controller.Connected;var s=controller.State;
        DeviceNameText.Text=prefs.DisplayName();
        DeviceNameText.ToolTip=DeviceNameText.Text;
        if(live&&s.TemperatureC is double ht&&s.Humidity is double hr && history.Add(new SensorSample(s.Updated,ht,hr)) && DateTimeOffset.Now-historySaved>TimeSpan.FromMinutes(1))SaveHistory();
        UpdateCharts();
        speedPolling.Observe(s.Output,s.Updated,DateTimeOffset.Now,live);timer.Interval=speedPolling.Interval;
        ConnectionBadge.Text=live?"● Connected":"● Offline";ConnectionBadge.Foreground=live?Brushes.LightGreen:Brushes.LightSlateGray;
        UnitButton.Content=fahrenheit?"°F":"°C";appearanceWindow.FahrenheitBox.IsChecked=fahrenheit;appearanceWindow.FahrenheitBox.Content=fahrenheit?"Fahrenheit (°F)":"Celsius (°C)";
        TemperatureText.Text=live&&s.TemperatureC is double t?(fahrenheit?t*1.8+32:t).ToString("0.0"):"—";
        HumidityText.Text=live&&s.Humidity is double h?h.ToString("0.0"):"—";
        VpdText.Text=live&&s.TemperatureC is double c&&s.Humidity is double rh?(0.6108*Math.Exp(17.27*c/(c+237.3))*(1-rh/100)).ToString("0.00"):"—";
        OnOffToggle.IsChecked=live&&s.Mode==2;OnOffToggle.Content=!live?"On / Off":s.Mode==2?"On":s.Mode==1?"Off":"Auto";
        UpdateFanAnimation(live?s.Output:null);
        OutputText.Text=live?s.Output?.ToString()??"—":"—";ModeText.Text=live?s.ModeName+(speedPolling.Target.HasValue?$" · {s.Output} → {speedPolling.Target}":""):"Not connected";
        UpdatedText.Text=live?$"Last updated {s.Updated:HH:mm:ss}":"No live readings";
        SpeedSlider.Tag=(double)(live?s.Output??0:0);
        if(!editingSpeed&&!submittingSpeed)SpeedSlider.Value=live?s.Output??0:0;
        if(!editingSpeed)PresetText.Text=!live?"Release slider to set speed. Zero selects saved Off mode.":
            speedPolling.Target.HasValue?$"Current {s.Output} → target {speedPolling.Target} · checking every 2 seconds":
            speedPolling.Fast?"Speed changing · checking every 2 seconds":$"Release to set speed · Off uses saved minimum {s.Off}.";
        ProgramsText.Text=live?DescribePrograms(s.Settings):"Connect to read stored timer, cycle, Auto, and schedule settings.";
        UpdateButtons();
    }
    private void FollowRamp(int target){speedPolling.Request(target,DateTimeOffset.Now);timer.Interval=speedPolling.Interval;}
    private void View_Click(object sender,RoutedEventArgs e)
    {prefs.WidgetView=(string)((Button)sender).Tag;ApplyWidgetView();SavePreferences();}
    private void Range_Click(object sender,RoutedEventArgs e)
    {prefs.HistoryHours=int.Parse((string)((Button)sender).Tag);UpdateCharts();SavePreferences();}
    private void History_Click(object sender,RoutedEventArgs e){UpdateCharts();TogglePanel(historyWindow);}
    private void ApplyWidgetView()
    {
        if(prefs.WidgetView!="Chart")prefs.WidgetView="Status";
        DeviceStatusPanel.Visibility=Visibility.Visible;
        ChartPanel.Visibility=prefs.WidgetView=="Chart"?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in new[]{StatusTab,ChartTab})
        {button.Background=(string)button.Tag==prefs.WidgetView?(Brush)Resources["HeaderBrush"]:Brushes.Transparent;button.BorderBrush=(string)button.Tag==prefs.WidgetView?(Brush)Resources["AccentBrush"]:Brushes.Transparent;}
        UpdateCharts();
    }
    private void UpdateCharts()
    {
        if(prefs.HistoryHours is not (1 or 6 or 24))prefs.HistoryHours=6;
        var now=DateTimeOffset.Now;var data=history.Range(now,prefs.HistoryHours);
        HistoryChart.Update(data,prefs.HistoryHours,fahrenheit,now);
        historyWindow.HistoryChart.Update(data,prefs.HistoryHours,fahrenheit,now);
        historyWindow.DeviceText.Text=prefs.DisplayName();
        historyWindow.HistorySummary.Text=data.Count==0?"No saved readings in this time range.":$"{data.Count:N0} samples in this range · "+(controller.Connected?"Recording live":"Saved history · disconnected")+$"\nFirst: {data[0].Time.ToLocalTime():MMM d, HH:mm:ss} · Latest: {data[^1].Time.ToLocalTime():MMM d, HH:mm:ss}";
        foreach(var button in new[]{HourButton,SixHourButton,DayButton,historyWindow.HourButton,historyWindow.SixHourButton,historyWindow.DayButton})button.BorderBrush=int.Parse((string)button.Tag)==prefs.HistoryHours?(Brush)Resources["AccentBrush"]:Brushes.Transparent;
    }
    private void SaveHistory()
    {if(smoke||captureLive)return;try{history.Save();historySaved=DateTimeOffset.Now;}catch(Exception ex){StatusText.Text="Could not save local history: "+ex.Message;}}
    internal async Task CaptureDocumentationAsync(string directory)
    {
        if(!captureLive)throw new InvalidOperationException("Live capture requires --capture-live.");
        var deadline=DateTime.UtcNow.AddSeconds(180);
        while(!controller.Connected&&DateTime.UtcNow<deadline)await Task.Delay(250);
        if(!controller.Connected)throw new IOException("No live controller connected within three minutes. Close other controller apps and try again.");
        // Wait for another real sample so the chart contains a measured interval.
        await Task.Delay(11000);
        if(!controller.Connected)throw new IOException("Controller disconnected before capture.");
        Directory.CreateDirectory(directory);
        OpacitySlider.Value=100;prefs.HistoryHours=1;ApplyAppearance();ClosePopups();
        foreach(var view in new[]{("Status","status"),("Chart","chart")})
        {
            prefs.WidgetView=view.Item1;ApplyWidgetView();UpdateView();UpdateLayout();
            await Task.Delay(150);
            App.CaptureSurface(this,Path.Combine(directory,view.Item2+".png"));
        }
        TogglePanel(historyWindow);UpdateCharts();historyWindow.UpdateLayout();
        await Task.Delay(150);
        App.CaptureSurface(historyWindow,Path.Combine(directory,"history.png"));
        TogglePanel(appearanceWindow);appearanceWindow.UpdateLayout();
        await Task.Delay(150);
        App.CaptureSurface(appearanceWindow,Path.Combine(directory,"appearance.png"));
    }
    private void UpdateFanAnimation(int? output)
    {
        int speed=IsVisible&&output is >=0 and <=10?output.Value:0;
        if(speed==animatedFanSpeed)return;
        animatedFanSpeed=speed;
        // Preserve the current blade position when changing speed or stopping.
        double angle=FanRotation.Angle%360;
        FanRotation.BeginAnimation(RotateTransform.AngleProperty,null);
        FanRotation.Angle=angle;
        if(speed==0)return;
        FanRotation.BeginAnimation(RotateTransform.AngleProperty,new System.Windows.Media.Animation.DoubleAnimation
        {
            From=angle,To=angle+360,Duration=TimeSpan.FromSeconds(5d/speed),
            RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever
        });
    }
    private void Connection_Click(object sender,RoutedEventArgs e)
    {
        if(DevicesList.Items.Count>0){DevicesList.Visibility=Visibility.Visible;ConnectButton.Visibility=Visibility.Visible;}
        TogglePanel(bluetoothWindow);
    }
    private void Credit_Click(object sender,RoutedEventArgs e)=>Branding.OpenWebsite(this);
    private void Settings_Click(object sender,RoutedEventArgs e)=>TogglePanel(appearanceWindow);
    private void Programs_Click(object sender,RoutedEventArgs e)=>TogglePanel(programsWindow);
    private void Help_Click(object sender,RoutedEventArgs e)=>TogglePanel(helpWindow);
    private void ClosePopups(){foreach(var panel in Panels)panel.Hide();}
    private void TogglePanel(Window panel)
    {
        bool open=!panel.IsVisible;ClosePopups();if(!open)return;
        panel.Topmost=Topmost;panel.Show();panel.UpdateLayout();UpdateDockedPanels();panel.Activate();
    }
    private void UpdateDockedPanels()
    {
        if(!IsLoaded||!IsVisible)return;
        foreach(var panel in Panels.Where(p=>p.IsVisible))PositionDockedPanel(panel);
    }
    private Rect MonitorWorkArea()
    {
        var screen=Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var source=PresentationSource.FromVisual(this);var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        var start=transform.Transform(new Point(screen.Left,screen.Top));var end=transform.Transform(new Point(screen.Right,screen.Bottom));
        return new Rect(start,end);
    }
    private void PositionDockedPanel(Window panel)
    {
        // Prefer the space beside the widget; clamp within this monitor's work area.
        var position=PanelDocking.Place(new Rect(Left,Top,ActualWidth,ActualHeight),new Size(panel.ActualWidth,panel.ActualHeight),MonitorWorkArea());
        if(Math.Abs(panel.Left-position.X)>.1)panel.Left=position.X;
        if(Math.Abs(panel.Top-position.Y)>.1)panel.Top=position.Y;
    }
    internal FrameworkElement SmokeSurface=>Panels.FirstOrDefault(p=>p.IsVisible)??(FrameworkElement)this;
    private void Tray_Click(object sender,RoutedEventArgs e){ClosePopups();SavePreferences();Hide();}
    private async void Close_Click(object sender,RoutedEventArgs e)=>await QuitAsync();
    private void WindowsBluetooth_Click(object sender,RoutedEventArgs e)
    {try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true});}catch(Exception ex){StatusText.Text=ex.Message;}}
    private void Panel_Drag(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        DependencyObject? node=e.OriginalSource as DependencyObject;
        while(node!=null && node!=Shell)
        {
            if(node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.Thumb or Slider or TextBox or ComboBox or ListBox or System.Windows.Controls.Primitives.ScrollBar)return;
            node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
        }
        if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed){try{DragMove();SavePreferences();}catch(InvalidOperationException){}}
    }
    private void Appearance_Changed(object sender,RoutedEventArgs e)
    {
        if(!initialized)return;
        bool was=prefs.AutoConnect;prefs.AutoConnect=AutoConnectBox.IsChecked==true;
        prefs.Transparency=OpacitySlider.Value;
        if(!was&&prefs.AutoConnect)reconnect.Reset();
        ApplyAppearance();SavePreferences();
    }
    private static Color ParseColor(string? value,Color fallback)
    {try{return (Color)ColorConverter.ConvertFromString(value??"");}catch{return fallback;}}
    private Color PanelColor()=>ParseColor(prefs.PanelColor,prefs.Theme=="Slate"?Color.FromRgb(48,61,80):prefs.Theme=="Forest"?Color.FromRgb(28,48,44):Color.FromRgb(23,26,32));
    private void PresetSelected(bool accent)
    {
        if(syncingAppearance||!initialized)return;
        var choice=(accent?appearanceWindow.AccentPresetBox:appearanceWindow.PanelPresetBox).SelectedItem as ColorChoice;
        if(choice==null)return;
        if(choice.Name=="Custom"){PickColor(accent);return;}
        if(accent)prefs.Accent=choice.Hex;else prefs.PanelColor=choice.Hex;ApplyAppearance();SavePreferences();
    }
    private void PickColor(bool accent)
    {
        editingAccent=accent;
        colorPicker.Configure(accent?ParseColor(prefs.Accent,Color.FromRgb(145,199,242)):PanelColor(),accent?"Custom accent color":"Custom panel color");
        TogglePanel(colorPicker);
    }
    private void RestoreAppearance(object sender,RoutedEventArgs e)
    {
        prefs.PanelColor=null;prefs.Theme="Midnight";prefs.Accent="#91C7F2";prefs.Transparency=94;
        OpacitySlider.Value=94;ApplyAppearance();SavePreferences();
    }
    private void ApplyAppearance()
    {
        var accent=ParseColor(prefs.Accent,Color.FromRgb(145,199,242));
        var panel=PanelColor();bool light=(panel.R*.299+panel.G*.587+panel.B*.114)>155;
        Color Mix(Color value,double factor)=>Color.FromRgb((byte)Math.Clamp(value.R*factor,0,255),(byte)Math.Clamp(value.G*factor,0,255),(byte)Math.Clamp(value.B*factor,0,255));
        SolidColorBrush Glass(Color color){color.A=(byte)(255*Math.Clamp(prefs.Transparency,45,100)/100);return new(color);}
        Resources["AccentBrush"]=new SolidColorBrush(accent);
        Resources["WindowBrush"]=Glass(panel);Resources["CardBrush"]=Glass(Mix(panel,.86));Resources["HeaderBrush"]=Glass(Mix(panel,light?.92:1.18));
        Resources["TextBrush"]=new SolidColorBrush(light?Color.FromRgb(20,30,40):Color.FromRgb(238,243,248));
        Resources["MutedBrush"]=new SolidColorBrush(light?Color.FromRgb(50,65,80):Color.FromRgb(175,193,215));
        appearanceWindow.PanelSwatch.Background=new SolidColorBrush(panel);appearanceWindow.AccentSwatch.Background=new SolidColorBrush(accent);
        appearanceWindow.PanelColorText.Text=$"Custom color  #{panel.R:X2}{panel.G:X2}{panel.B:X2}";
        appearanceWindow.AccentColorText.Text=$"Custom color  #{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        syncingAppearance=true;
        appearanceWindow.PanelPresetBox.SelectedItem=ColorChoices.Panels.FirstOrDefault(c=>c.Hex.Equals($"#{panel.R:X2}{panel.G:X2}{panel.B:X2}",StringComparison.OrdinalIgnoreCase))??ColorChoices.Panels.Last();
        appearanceWindow.AccentPresetBox.SelectedItem=ColorChoices.Accents.FirstOrDefault(c=>c.Hex.Equals($"#{accent.R:X2}{accent.G:X2}{accent.B:X2}",StringComparison.OrdinalIgnoreCase))??ColorChoices.Accents.Last();
        syncingAppearance=false;
        PinTilt.Angle=Topmost?0:45;PinButton.ToolTip=Topmost?"Unpin from top":"Pin on top";
        PinButton.Foreground=Topmost?(Brush)Resources["AccentBrush"]:Brushes.LightSteelBlue;
        if(initialized)ApplyWidgetView();
    }
    private static string DescribePrograms(Dictionary<byte,byte[]> f)
    {
        var lines=new List<string>{"Stored presets; the active mode is shown above."};
        if(f.TryGetValue(19,out var a)&&a.Length==7)lines.Add($"Auto: low {a[4]}°C / {a[6]}% RH; high {a[2]}°C / {a[5]}% RH.");
        if(f.TryGetValue(20,out var on)&&on.Length==4)lines.Add($"Timer to On: {BinaryPrimitives.ReadUInt32BigEndian(on)/60d:0.#} minutes");
        if(f.TryGetValue(21,out var off)&&off.Length==4)lines.Add($"Timer to Off: {BinaryPrimitives.ReadUInt32BigEndian(off)/60d:0.#} minutes");
        if(f.TryGetValue(22,out var cycle)&&cycle.Length==8)lines.Add($"Cycle: {BinaryPrimitives.ReadUInt32BigEndian(cycle)/60d:0.#} min On / {BinaryPrimitives.ReadUInt32BigEndian(cycle.AsSpan(4))/60d:0.#} min Off");
        if(f.TryGetValue(23,out var sched)&&sched.Length==4)lines.Add($"Daily schedule: {sched[0]:00}:{sched[1]:00}–{sched[2]:00}:{sched[3]:00} (controller clock)");
        return string.Join(Environment.NewLine,lines);
    }
}
