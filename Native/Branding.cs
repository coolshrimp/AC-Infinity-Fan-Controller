using System;
using System.Diagnostics;
using System.Windows;
namespace ACInfinityDesktop;
internal static class Branding
{
    public const string Website="https://coolshrimpmodz.com/";
    public static void OpenWebsite(Window owner)
    {
        try{Process.Start(new ProcessStartInfo(Website){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(owner,"Could not open the website: "+ex.Message,"Coolshrimp Mods");}
    }
}
