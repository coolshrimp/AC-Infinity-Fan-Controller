using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
namespace ACInfinityDesktop;
public partial class HelpWindow : Window
{
    internal bool AllowClose {get;set;}
    public HelpWindow(){InitializeComponent();}
    private void Credit_Click(object sender,RoutedEventArgs e)=>Branding.OpenWebsite(this);
    private void Close_Click(object sender,RoutedEventArgs e)=>Hide();
    private void Window_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Hide();e.Handled=true;}}
    private void Window_Closing(object? sender,CancelEventArgs e){if(!AllowClose){e.Cancel=true;Hide();}}
}
