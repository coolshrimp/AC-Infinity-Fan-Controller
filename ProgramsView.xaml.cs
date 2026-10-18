using System.Windows;
using System.Windows.Controls;

namespace ACInfinityDesktop;

public partial class ProgramsView : UserControl
{
    public ProgramsView()
    {
        InitializeComponent();
        ApplyProgramKind();
    }

    // Section changes leave the editor values intact.
    internal void SelectSection(bool controller)
    {
        ControllerSection.Visibility=controller?Visibility.Visible:Visibility.Collapsed;
        AppSection.Visibility=controller?Visibility.Collapsed:Visibility.Visible;
        ControllerActions.Visibility=controller?Visibility.Visible:Visibility.Collapsed;
        AppActions.Visibility=controller?Visibility.Collapsed:Visibility.Visible;
        ControllerTab.SetResourceReference(Control.BackgroundProperty,controller?"CardBrush":"HeaderBrush");
        ControllerTab.SetResourceReference(Control.BorderBrushProperty,controller?"AccentBrush":"MutedBrush");
        ThisPcTab.SetResourceReference(Control.BackgroundProperty,controller?"HeaderBrush":"CardBrush");
        ThisPcTab.SetResourceReference(Control.BorderBrushProperty,controller?"MutedBrush":"AccentBrush");
    }

    private void Section_Click(object sender,RoutedEventArgs e)=>SelectSection(((Button)sender).Tag?.ToString()=="Controller");
    private void ProgramKind_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProgramKind();

    private void ApplyProgramKind()
    {
        // SelectedIndex may change before InitializeComponent has created the panels.
        if(ControllerDailyPanel==null||ControllerCyclePanel==null||ControllerAutoPanel==null||
            ControllerTimerOnPanel==null||ControllerTimerOffPanel==null)return;
        string? mode=(ProgramKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        ControllerAutoPanel.Visibility=mode=="3"?Visibility.Visible:Visibility.Collapsed;
        ControllerTimerOnPanel.Visibility=mode=="4"?Visibility.Visible:Visibility.Collapsed;
        ControllerTimerOffPanel.Visibility=mode=="5"?Visibility.Visible:Visibility.Collapsed;
        ControllerCyclePanel.Visibility=mode=="6"?Visibility.Visible:Visibility.Collapsed;
        ControllerDailyPanel.Visibility=mode=="7"?Visibility.Visible:Visibility.Collapsed;
    }
}
