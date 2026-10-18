using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;

namespace ACInfinityDesktop;
public partial class App : Application
{
    private SingleInstance? instance;
    internal static void CaptureSurface(FrameworkElement surface,string path)
    {
        surface.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
    internal bool IsControllerOpen(string address,MainWindow except) => Windows.OfType<MainWindow>()
        .Any(w=>w!=except&&!w.IsQuitting&&string.Equals(w.ControllerAddress,address,StringComparison.OrdinalIgnoreCase));

    internal void OpenAdditionalController()
    {
        // Reuse an empty chooser instead of accumulating unconnected widgets.
        var chooser=Windows.OfType<MainWindow>().FirstOrDefault(w=>!w.IsQuitting&&string.IsNullOrEmpty(w.ControllerAddress));
        if(chooser!=null){chooser.ShowWindow();return;}
        var widget=new MainWindow(null,true);
        var anchor=Windows.OfType<MainWindow>().FirstOrDefault(w=>w!=widget&&!w.IsQuitting&&w.IsVisible);
        if(anchor!=null){widget.Left=anchor.Left+40;widget.Top=anchor.Top+40;}
        widget.Show();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length==2 && e.Args[0]=="--instance-client" && Guid.TryParse(e.Args[1],out var testId))
        {
            using var client=new SingleInstance("ACInfinityDesktop.Test."+testId);
            Shutdown(client.IsPrimary?1:0);return;
        }
        if (e.Args.Contains("--self-test"))
        {
            try { File.WriteAllText(GetOutput(e.Args), JsonSerializer.Serialize(ProtocolChecks.Run(), new JsonSerializerOptions {WriteIndented=true})); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(GetOutput(e.Args), ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--probe"))
        {
            var controller = new BluetoothController();
            try
            {
                int addressIndex=Array.IndexOf(e.Args,"--address");
                if(addressIndex<0||addressIndex+1>=e.Args.Length)throw new ArgumentException("Use --address with the controller Bluetooth address.");
                var devices = await BluetoothController.ScanAsync(e.Args[addressIndex+1]);
                var target = devices.FirstOrDefault() ?? throw new IOException("Controller not advertising.");
                await controller.ConnectAsync(target);
                var samples = new System.Collections.Generic.List<object>();
                for(int i=0;i<3;i++) {if(i>0){await Task.Delay(2000);await controller.RefreshAsync();} samples.Add(new {controller.Connected,controller.State.TemperatureC,controller.State.Humidity,controller.State.Output,controller.State.ModeName,controller.State.On,controller.State.Off,controller.State.Updated});}
                File.WriteAllText(GetOutput(e.Args),JsonSerializer.Serialize(samples,new JsonSerializerOptions {WriteIndented=true}));
                await controller.DisconnectAsync(); Shutdown(0);
            }
            catch(Exception ex){ File.WriteAllText(GetOutput(e.Args),ex.ToString()); await controller.DisconnectAsync();Shutdown(1); }
            return;
        }
        bool smoke=e.Args.Contains("--smoke");
        if(smoke)DispatcherUnhandledException+=(_,failure)=>
        {
            try{File.WriteAllText(GetOutput(e.Args)+".error.txt",failure.Exception.ToString());}
            catch(Exception loggingError){Console.Error.WriteLine(loggingError.Message);}
            failure.Handled=true;Shutdown(1);
        };
        if (!smoke)
        {
            instance=new SingleInstance();
            if(!instance.IsPrimary){Shutdown();return;}
        }
        var window = new MainWindow(); MainWindow=window; window.Show();
        if(!smoke&&!e.Args.Contains("--capture-live"))
        {
            var directory=Path.Combine(ACInfinityDesktop.MainWindow.DataDirectory,"controllers");
            if(Directory.Exists(directory))
                foreach(var path in Directory.EnumerateFiles(directory,"*.json"))
                {
                    var saved=Preferences.Read(path);
                    if(!saved.AutoConnect||string.IsNullOrEmpty(saved.address)||Windows.OfType<MainWindow>().Any(w=>string.Equals(w.ControllerAddress,saved.address,StringComparison.OrdinalIgnoreCase)))continue;
                    new MainWindow(path,true).RestoreWhenConnected();
                }
        }
        instance?.Listen(()=>Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!Dispatcher.HasShutdownStarted)
                foreach(var widget in Windows.OfType<MainWindow>().Where(w=>!w.IsQuitting&&!w.RestorePending).ToArray())widget.ShowWindow();
        })));
        if(smoke)
        {
            await window.SmokeReady.WaitAsync(TimeSpan.FromSeconds(30));
            if(e.Args.Contains("--multi-controller"))await window.CheckMultipleWidgetsAsync();
            window.UpdateLayout();CaptureSurface(window.SmokeSurface,GetOutput(e.Args));
            await window.QuitAsync();
        }
        else if(e.Args.Contains("--capture-live"))
        {
            int exitCode=0;
            try {await window.CaptureDocumentationAsync(GetOutput(e.Args));}
            catch(Exception ex){Console.Error.WriteLine(ex.Message);exitCode=1;}
            await window.QuitAsync();Environment.ExitCode=exitCode;
        }
    }
    internal static string GetOutput(string[] args)
    {
        int i=Array.IndexOf(args,"--output");
        return i>=0 && i+1<args.Length ? args[i+1] : Path.Combine(AppContext.BaseDirectory,"diagnostic.json");
    }
    protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
}
