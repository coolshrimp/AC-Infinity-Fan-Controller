using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace ACInfinityDesktop;
public partial class ColorPickerWindow : Window
{
    internal bool AllowClose {get;set;}
    public event Action<Color?>? Finished;
    private bool updating;
    private double hue,saturation,value;
    private Color selected;
    public ColorPickerWindow(){InitializeComponent();}
    public void Configure(Color color,string title){PickerTitle.Text=title;selected=color;(hue,saturation,value)=ColorChoices.ToHsv(color);UpdateColor();}
    private void UpdateColor()
    {
        updating=true;selected=ColorChoices.Hsv(hue,saturation,value);HueSlider.Value=hue;
        ColorArea.Background=new SolidColorBrush(ColorChoices.Hsv(hue,1,1));PreviewSwatch.Background=new SolidColorBrush(selected);
        HexBox.Text=$"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";UseButton.IsEnabled=true;ValidationText.Text="";PlaceMarker();updating=false;
    }
    private void PlaceMarker(){Canvas.SetLeft(SelectionDot,Math.Clamp(saturation*ColorArea.ActualWidth-6,0,Math.Max(0,ColorArea.ActualWidth-12)));Canvas.SetTop(SelectionDot,Math.Clamp((1-value)*ColorArea.ActualHeight-6,0,Math.Max(0,ColorArea.ActualHeight-12)));}
    private void PickAt(MouseEventArgs e){var p=e.GetPosition(ColorArea);saturation=Math.Clamp(p.X/Math.Max(1,ColorArea.ActualWidth),0,1);value=1-Math.Clamp(p.Y/Math.Max(1,ColorArea.ActualHeight),0,1);UpdateColor();}
    private void Area_Down(object sender,MouseButtonEventArgs e){ColorArea.Focus();ColorArea.CaptureMouse();PickAt(e);e.Handled=true;}
    private void Area_Move(object sender,MouseEventArgs e){if(ColorArea.IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed)PickAt(e);}
    private void Area_Up(object sender,MouseButtonEventArgs e){if(ColorArea.IsMouseCaptured){PickAt(e);ColorArea.ReleaseMouseCapture();}e.Handled=true;}
    private void Area_Size(object sender,SizeChangedEventArgs e){if(SelectionDot!=null)PlaceMarker();}
    private void Area_Key(object sender,KeyEventArgs e){switch(e.Key){case Key.Left:saturation-=.01;break;case Key.Right:saturation+=.01;break;case Key.Up:value+=.01;break;case Key.Down:value-=.01;break;default:return;}saturation=Math.Clamp(saturation,0,1);value=Math.Clamp(value,0,1);UpdateColor();e.Handled=true;}
    private void Hue_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||HexBox==null)return;hue=e.NewValue;UpdateColor();}
    private void Hex_Changed(object sender,TextChangedEventArgs e)
    {
        if(updating||UseButton==null)return;
        bool valid=ColorChoices.TryHex(HexBox.Text,out var color);UseButton.IsEnabled=valid;ValidationText.Text=valid?"":"Enter a valid six-digit hex color.";
        if(!valid)return;
        selected=color;(hue,saturation,value)=ColorChoices.ToHsv(color);updating=true;HueSlider.Value=hue;ColorArea.Background=new SolidColorBrush(ColorChoices.Hsv(hue,1,1));PreviewSwatch.Background=new SolidColorBrush(color);PlaceMarker();updating=false;
    }
    private void Use_Click(object sender,RoutedEventArgs e){if(UseButton.IsEnabled)Finished?.Invoke(selected);}
    private void Cancel_Click(object sender,RoutedEventArgs e)=>Finished?.Invoke(null);
    private void Window_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Finished?.Invoke(null);e.Handled=true;}}
    private void Window_Closing(object? sender,CancelEventArgs e){if(!AllowClose){e.Cancel=true;Finished?.Invoke(null);}}
}
