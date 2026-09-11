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
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ACInfinityDesktop;
public partial class MainWindow : Window
{
    private readonly AppearanceWindow appearanceWindow=new();
    private readonly BluetoothWindow bluetoothWindow=new();
    private readonly ProgramsWindow programsWindow=new();
    private readonly HelpWindow helpWindow=new();
    private Window[] Panels=>new Window[]{appearanceWindow,bluetoothWindow,programsWindow,helpWindow};
    private TextBox AddressBox=>bluetoothWindow.AddressBox;
    private ListBox DevicesList=>bluetoothWindow.DevicesList;
    private Button ScanButton=>bluetoothWindow.ScanButton;
    private Button ConnectButton=>bluetoothWindow.ConnectButton;
    private Button DisconnectButton=>bluetoothWindow.DisconnectButton;
    private CheckBox AutoConnectBox=>appearanceWindow.AutoConnectBox;
    private Slider OpacitySlider=>appearanceWindow.OpacitySlider;
    private TextBlock ProgramsText=>programsWindow.ProgramsText;
    private readonly BluetoothController controller=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private Forms.NotifyIcon? tray;
    private bool busy, refreshing, quitting, fahrenheit, initialized;
    private readonly System.Threading.SemaphoreSlim operationGate=new(1,1);
    private int animatedFanSpeed=-1;
    private readonly bool smoke=Environment.GetCommandLineArgs().Contains("--smoke");
    private Preferences prefs=new();
    private readonly ReconnectPolicy reconnect=new();
    private readonly string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ACInfinityDesktop","preferences.json");
    public MainWindow()
    {
        InitializeComponent();
        foreach(var panel in Panels)panel.Resources=Resources;
        ScanButton.Click+=Scan_Click;ConnectButton.Click+=Connect_Click;DisconnectButton.Click+=Disconnect_Click;
        DevicesList.SelectionChanged+=Device_Selected;bluetoothWindow.WindowsBluetoothButton.Click+=WindowsBluetooth_Click;
        AutoConnectBox.Click+=Appearance_Changed;OpacitySlider.ValueChanged+=Appearance_Changed;
        appearanceWindow.PanelColorButton.Click+=(_,_)=>PickColor(false);
        appearanceWindow.AccentColorButton.Click+=(_,_)=>PickColor(true);
        appearanceWindow.DefaultsButton.Click+=RestoreAppearance;
        bluetoothWindow.ConnectionStatusText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=StatusText});
        prefs=smoke?new():Preferences.Read(preferences);
        AddressBox.Text="";fahrenheit=prefs.unit=="F";
        AutoConnectBox.IsChecked=prefs.AutoConnect;
        OpacitySlider.Value=Math.Clamp(prefs.Transparency,45,100);Topmost=prefs.Pinned;
        if(prefs.Left is double x && prefs.Top is double y && double.IsFinite(x)&&double.IsFinite(y))
        {Left=Math.Clamp(x,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));Top=Math.Clamp(y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-Height));}
        initialized=true;ApplyAppearance();
        IsVisibleChanged+=(_,_)=>UpdateFanAnimation(controller.Connected?controller.State.Output:null);
        timer.Tick+=async(_,_)=>{if(busy||quitting)return;if(controller.Connected)await RunAsync(controller.RefreshAsync,"",true,true);else {UpdateView();await AutoConnectAsync();}};
        UpdateView();
    }
    private async void Window_Loaded(object sender,RoutedEventArgs e)
    {
        foreach(var panel in Panels)panel.Owner=this;
        if(smoke){
            Pin_Click(this,new RoutedEventArgs());if(!Topmost)throw new InvalidOperationException("Pin failed");
            Pin_Click(this,new RoutedEventArgs());if(Topmost)throw new InvalidOperationException("Unpin failed");
            Connection_Click(this,new RoutedEventArgs());if(!bluetoothWindow.IsVisible)throw new InvalidOperationException("Connection panel failed");
            Connection_Click(this,new RoutedEventArgs());
            prefs.PanelColor="#164E63";prefs.Accent="#F0ABFC";OpacitySlider.Value=65;
            if(((SolidColorBrush)Shell.Background).Color.A!=(byte)(255*65/100)||prefs.PanelColor!="#164E63"||prefs.Accent!="#F0ABFC"||prefs.Transparency!=65)throw new InvalidOperationException("Appearance failed");
            RestoreAppearance(this,new RoutedEventArgs());
            Tray_Click(this,new RoutedEventArgs());if(IsVisible)throw new InvalidOperationException("Tray hide failed");ShowWindow();
            if(Environment.GetCommandLineArgs().Contains("--settings"))Settings_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--bluetooth"))
            {
                DevicesList.ItemsSource=new[]{new DiscoveredController(0xAABBCCDDEEFF,"Example Controller 67",-65,1){Verified=true}};
                Connection_Click(this,new RoutedEventArgs());
            }
            if(Environment.GetCommandLineArgs().Contains("--programs"))Programs_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--help-panel"))Help_Click(this,new RoutedEventArgs());
            if(Environment.GetCommandLineArgs().Contains("--transparent"))OpacitySlider.Value=45;
            if(Environment.GetCommandLineArgs().Contains("--light")){prefs.PanelColor="#F0E9D6";prefs.Accent="#CC3377";ApplyAppearance();Settings_Click(this,new RoutedEventArgs());}
            if(AddressBox.Text.Length!=0)throw new InvalidOperationException("Address field must start blank");
            UpdateLayout();if(Shell.ActualHeight+1<((FrameworkElement)Shell.Child).ActualHeight)throw new InvalidOperationException("Widget content overflow");return;
        }
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/ui/icon.ico")).Stream;
        using var source=new System.Drawing.Icon(stream);
        tray=new Forms.NotifyIcon{Icon=(System.Drawing.Icon)source.Clone(),Text="AC Infinity Desktop · .NET",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Show controller",null,(_,_)=>Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Quit",null,async(_,_)=>await Dispatcher.InvokeAsync(QuitAsync).Task.Unwrap());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowWindow);
        timer.Start();await AutoConnectAsync();
    }
    internal void ShowWindow(){Show();WindowState=WindowState.Normal;Activate();}
    private async void Window_Closing(object? sender,CancelEventArgs e){if(!quitting){e.Cancel=true;await QuitAsync();}}
    public async Task QuitAsync()
    {
        if(quitting)return;ClosePopups();SavePreferences();quitting=true;timer.Stop();
        appearanceWindow.AllowClose=bluetoothWindow.AllowClose=programsWindow.AllowClose=helpWindow.AllowClose=true;
        foreach(var panel in Panels)panel.Close();
        await controller.DisconnectAsync();
        if(tray!=null){tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Icon?.Dispose();tray.Dispose();}
        Application.Current.Shutdown();
    }
    private void SavePreferences()
    {
        if(smoke||!initialized)return;
        prefs.unit=fahrenheit?"F":"C";prefs.Pinned=Topmost;prefs.Left=Left;prefs.Top=Top;
        try{prefs.Save(preferences);}catch(Exception ex){StatusText.Text="Could not save preferences: "+ex.Message;}
    }
    private async Task AutoConnectAsync()
    {
        if(smoke||busy||quitting||controller.Connected||!reconnect.Due(prefs.AutoConnect,DateTime.UtcNow))return;
        await RunAsync(async()=>
        {
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
            StatusText.Text=$"Checking {candidate.Name} ({verified.Count+failed+1}/{candidates.Count})?";
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
    private async Task ConnectTargetAsync(DiscoveredController target)
    {
        await controller.ConnectAsync(target);
        if(quitting){await controller.DisconnectAsync();return;}
        prefs.address=target.Mac;AddressBox.Text=target.Mac;reconnect.Reset();SavePreferences();
        DevicesList.Visibility=Visibility.Collapsed;ConnectButton.Visibility=Visibility.Collapsed;
        SpeedSlider.Value=controller.State.On is >=1 and <=10?controller.State.On:5;
        StatusText.Text="Connected. Live readings refresh every 15 seconds.";
    }
    private async Task RunAsync(Func<Task> action,string message,bool background=false,bool isRefresh=false)
    {
        // A user command may wait for a status read; duplicate refreshes are skipped.
        if(busy && (!refreshing || isRefresh))return;
        await operationGate.WaitAsync();
        if(quitting){operationGate.Release();return;}
        busy=true;refreshing=isRefresh;UpdateButtons();
        if(message.Length>0){StatusText.Text=message;StatusText.Foreground=(Brush)Resources["MutedBrush"];}
        try {await action();UpdateView();if(!background){StatusText.Text=controller.Connected?"Controller responded. Live readings refresh every 15 seconds.":"Ready.";StatusText.Foreground=(Brush)Resources["MutedBrush"];}}
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
    private async void Apply_Click(object sender,RoutedEventArgs e){int level=(int)SpeedSlider.Value;await RunAsync(()=>controller.SetSpeedAsync(level),"Applying manual speed…");}
    private async void On_Click(object sender,RoutedEventArgs e)=>await RunAsync(()=>controller.SetModeAsync(true),"Selecting On mode…");
    private async void Off_Click(object sender,RoutedEventArgs e)=>await RunAsync(()=>controller.SetModeAsync(false),"Selecting Off mode…");
    private void Unit_Click(object sender,RoutedEventArgs e){fahrenheit=!fahrenheit;SavePreferences();UpdateView();}
    private void Pin_Click(object sender,RoutedEventArgs e){Topmost=!Topmost;foreach(var panel in Panels)panel.Topmost=Topmost;ApplyAppearance();SavePreferences();}
    private void Speed_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(SpeedValue!=null)SpeedValue.Text=((int)e.NewValue).ToString();}
    private void UpdateButtons()
    {
        bool live=controller.Connected;
        bool blockControls=busy&&!refreshing;
        foreach(var b in new[]{OnButton,OffButton,ApplyButton,RefreshButton})b.IsEnabled=live&&!blockControls;
        SpeedSlider.IsEnabled=live&&!blockControls;ScanButton.IsEnabled=!blockControls&&!live;AddressBox.IsEnabled=!blockControls&&!live;
        ScanButton.Visibility=live?Visibility.Collapsed:Visibility.Visible;
        DisconnectButton.Visibility=live?Visibility.Visible:Visibility.Collapsed;DisconnectButton.IsEnabled=!blockControls;
        DevicesList.IsEnabled=!blockControls;ConnectButton.IsEnabled=!blockControls&&!live && DevicesList.SelectedItem is DiscoveredController selected && selected.SupportsControls;
    }
    private void UpdateView()
    {
        bool live=controller.Connected;var s=controller.State;
        ConnectionBadge.Text=live?"● Connected":"● Offline";ConnectionBadge.Foreground=live?Brushes.LightGreen:Brushes.LightSlateGray;
        UnitButton.Content=fahrenheit?"°F":"°C";
        TemperatureText.Text=live&&s.TemperatureC is double t?(fahrenheit?t*1.8+32:t).ToString("0.0"):"—";
        HumidityText.Text=live&&s.Humidity is double h?h.ToString("0.0"):"—";
        VpdText.Text=live&&s.TemperatureC is double c&&s.Humidity is double rh?(0.6108*Math.Exp(17.27*c/(c+237.3))*(1-rh/100)).ToString("0.00"):"—";
        UpdateFanAnimation(live?s.Output:null);
        OutputText.Text=live?s.Output?.ToString()??"—":"—";ModeText.Text=live?s.ModeName:"Not connected";
        UpdatedText.Text=live?$"Live read {s.Updated:HH:mm:ss}":"No live readings";
        PresetText.Text=live?$"Saved On: {s.On} · Off: {s.Off}. Manual controls replace the active mode. Off mode uses that saved level.":"Manual controls replace the active mode. Off mode uses the stored minimum speed.";
        ProgramsText.Text=live?DescribePrograms(s.Settings):"Connect to read stored timer, cycle, Auto, and schedule settings.";
        UpdateButtons();
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
        panel.Topmost=Topmost;panel.Show();panel.UpdateLayout();
        // Prefer the space beside the widget; clamp within this monitor's work area.
        var screen=Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var source=PresentationSource.FromVisual(this);var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        var start=transform.Transform(new Point(screen.Left,screen.Top));var end=transform.Transform(new Point(screen.Right,screen.Bottom));
        double x=Left+ActualWidth+12;if(x+panel.ActualWidth>end.X)x=Left-panel.ActualWidth-12;
        panel.Left=Math.Clamp(x,start.X,Math.Max(start.X,end.X-panel.ActualWidth));
        panel.Top=Math.Clamp(Top,start.Y,Math.Max(start.Y,end.Y-panel.ActualHeight));panel.Activate();
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
    private sealed class ColorDialogOwner : Forms.IWin32Window
    {
        public IntPtr Handle {get;}
        public ColorDialogOwner(Window window)=>Handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
    }
    private void PickColor(bool accent)
    {
        var current=accent?ParseColor(prefs.Accent,Color.FromRgb(145,199,242)):PanelColor();
        using var picker=new Forms.ColorDialog{FullOpen=true,AnyColor=true,Color=System.Drawing.Color.FromArgb(current.R,current.G,current.B)};
        if(picker.ShowDialog(new ColorDialogOwner(appearanceWindow))!=Forms.DialogResult.OK)return;
        string hex=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        if(accent)prefs.Accent=hex;else prefs.PanelColor=hex;
        ApplyAppearance();SavePreferences();
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
        ApplyButton.Foreground=(accent.R*.299+accent.G*.587+accent.B*.114)>155?Brushes.Black:Brushes.White;
        Resources["WindowBrush"]=Glass(panel);Resources["CardBrush"]=Glass(Mix(panel,.86));Resources["HeaderBrush"]=Glass(Mix(panel,light?.92:1.18));
        Resources["TextBrush"]=new SolidColorBrush(light?Color.FromRgb(20,30,40):Color.FromRgb(238,243,248));
        Resources["MutedBrush"]=new SolidColorBrush(light?Color.FromRgb(50,65,80):Color.FromRgb(175,193,215));
        appearanceWindow.PanelSwatch.Background=new SolidColorBrush(panel);appearanceWindow.AccentSwatch.Background=new SolidColorBrush(accent);
        appearanceWindow.PanelColorText.Text=$"Panel color  #{panel.R:X2}{panel.G:X2}{panel.B:X2}";
        appearanceWindow.AccentColorText.Text=$"Accent color  #{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        PinTilt.Angle=Topmost?0:45;PinButton.ToolTip=Topmost?"Unpin from top":"Pin on top";
        PinButton.Foreground=Topmost?(Brush)Resources["AccentBrush"]:Brushes.LightSteelBlue;
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
