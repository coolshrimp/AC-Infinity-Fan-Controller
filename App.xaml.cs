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
        if (!smoke)
        {
            instance=new SingleInstance();
            if(!instance.IsPrimary){Shutdown();return;}
        }
        var window = new MainWindow(); MainWindow=window; window.Show();
        instance?.Listen(()=>Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!Dispatcher.HasShutdownStarted)window.ShowWindow();
        })));
        if(smoke)
        {
            await Task.Delay(800);
            window.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(GetOutput(e.Args))) encoder.Save(output);
            await window.QuitAsync();
        }
    }
    private static string GetOutput(string[] args)
    {
        int i=Array.IndexOf(args,"--output");
        return i>=0 && i+1<args.Length ? args[i+1] : Path.Combine(AppContext.BaseDirectory,"diagnostic.json");
    }
    protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
}
