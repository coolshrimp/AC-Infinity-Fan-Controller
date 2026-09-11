using System;
using System.Windows.Media;
namespace ACInfinityDesktop;
public sealed record ColorChoice(string Name,string Hex);
internal static class ColorChoices
{
    public static readonly ColorChoice[] Panels={new("Midnight","#171A20"),new("Slate","#303D50"),new("Forest","#1C302C"),new("Ocean","#16364A"),new("Plum","#35243D"),new("Warm cream","#F0E9D6"),new("Custom","#00000000")};
    public static readonly ColorChoice[] Accents={new("Sky blue","#91C7F2"),new("Violet","#A855F7"),new("Mint","#6EE7B7"),new("Coral","#FB7185"),new("Amber","#FBBF24"),new("White","#FFFFFF"),new("Custom","#00000000")};
    public static readonly string[] FanNames={"CLOUDLINE T4","CLOUDLINE T6","CLOUDLINE T8","CLOUDLINE T10","CLOUDLINE T12","Intake fan","Exhaust fan","Grow tent","Workshop fan"};
    public static Color Hsv(double hue,double saturation,double value)
    {
        hue=((hue%360)+360)%360;saturation=Math.Clamp(saturation,0,1);value=Math.Clamp(value,0,1);
        double c=value*saturation,x=c*(1-Math.Abs(hue/60%2-1)),m=value-c;
        var rgb=hue switch {<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        return Color.FromRgb((byte)Math.Round((rgb.Item1+m)*255),(byte)Math.Round((rgb.Item2+m)*255),(byte)Math.Round((rgb.Item3+m)*255));
    }
    public static (double H,double S,double V) ToHsv(Color color)
    {
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
        double h=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);
        return ((h+360)%360,max==0?0:d/max,max);
    }
    public static bool TryHex(string text,out Color color)
    {
        color=Colors.Black;text=text.Trim().TrimStart('#');
        if(text.Length!=6||!uint.TryParse(text,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var rgb))return false;
        color=Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);return true;
    }
}
