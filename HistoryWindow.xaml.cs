using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace ACInfinityDesktop;

// The owning widget supplies its own controller's samples; this panel never opens Bluetooth.
public partial class HistoryWindow : Window
{
    internal bool AllowClose {get;set;}
    public HistoryWindow(){InitializeComponent();HistoryChart.SetPlotHeight(84);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Hide();
    private void Window_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Hide();e.Handled=true;}}
    private void Window_Closing(object? sender,CancelEventArgs e){if(!AllowClose){e.Cancel=true;Hide();}}
}
