using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace ACInfinityDesktop;
public partial class SensorChartView : UserControl
{
    private List<SensorSample> samples=new();
    private int hours=6;
    private bool fahrenheit;
    private DateTimeOffset now;
    public SensorChartView(){InitializeComponent();SizeChanged+=(_,_)=>Draw();}
    public void Update(List<SensorSample> data,int range,bool useFahrenheit,DateTimeOffset end)
    {samples=data;hours=range;fahrenheit=useFahrenheit;now=end;Draw();}
    private void Draw()
    {
        if(Plots==null)return;
        Plots.Visibility=samples.Count>=2?Visibility.Visible:Visibility.Collapsed;
        EmptyText.Visibility=samples.Count>=2?Visibility.Collapsed:Visibility.Visible;
        StartLabel.Text=now.AddHours(-hours).ToLocalTime().ToString("HH:mm");
        if(samples.Count<2)return;
        Plot(TempCanvas,s=>fahrenheit?s.TemperatureC*1.8+32:s.TemperatureC,Colors.Coral,TempRange,fahrenheit?"°F":"°C");
        Plot(HumidityCanvas,s=>s.Humidity,Colors.SkyBlue,HumidityRange,"%");
        Plot(VpdCanvas,s=>s.Vpd,Colors.MediumSeaGreen,VpdRange,"kPa");
    }
    private void Plot(Canvas canvas,Func<SensorSample,double> value,Color color,TextBlock label,string unit)
    {
        canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.Height;
        if(w<=0)return;
        double min=samples.Min(value),max=samples.Max(value),pad=Math.Max((max-min)*.1,.05);
        label.Text=$"{min:0.00}–{max:0.00} {unit}";
        min-=pad;max+=pad;
        foreach(double y in new[]{0d,h/2,h})canvas.Children.Add(new Line{X2=w,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromArgb(40,140,155,170)),StrokeThickness=1});
        Polyline? line=null;SensorSample? previous=null;
        foreach(var sample in samples)
        {
            if(line==null||previous!=null&&(sample.Time-previous.Time).TotalSeconds>60)
            {line=new Polyline{Stroke=new SolidColorBrush(color),StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round};canvas.Children.Add(line);}
            double x=w*(sample.Time-now.AddHours(-hours)).TotalHours/hours;
            line.Points.Add(new Point(x,h-2-(h-4)*(value(sample)-min)/(max-min)));previous=sample;
        }
    }
}
