using System;
using System.Windows;
namespace ACInfinityDesktop;
internal static class PanelDocking
{
    public static Point Place(Rect main,Size panel,Rect work)
    {
        const double gap=10;
        var candidates=new[]{new Point(main.Right+gap,main.Top),new Point(main.Left-panel.Width-gap,main.Top),new Point(main.Left,main.Bottom+gap),new Point(main.Left,main.Top-panel.Height-gap)};
        foreach(var p in candidates)if(work.Contains(new Rect(p,panel)))return p;
        // When vertical clamping is needed, preserve a side attachment if it fits.
        foreach(var p in candidates[..2])if(p.X>=work.Left&&p.X+panel.Width<=work.Right)
            return new Point(p.X,Math.Clamp(p.Y,work.Top,Math.Max(work.Top,work.Bottom-panel.Height)));
        // Very small work areas cannot contain both windows side-by-side.
        return new Point(Math.Clamp(candidates[0].X,work.Left,Math.Max(work.Left,work.Right-panel.Width)),Math.Clamp(main.Top,work.Top,Math.Max(work.Top,work.Bottom-panel.Height)));
    }
}
