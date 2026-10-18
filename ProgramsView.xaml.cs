using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ACInfinityDesktop;

public partial class ProgramsView : UserControl
{
    private bool modeSelectorReady,cancelModeCommit;
    private int modeWhenOpened,lastRequestedMode;
    internal event Action<int>? ControllerModeCommitted;

    public ProgramsView()
    {
        InitializeComponent();
        ApplyProgramKind();
        lastRequestedMode=SelectedMode;
        modeSelectorReady=true;
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

    private int SelectedMode => (ProgramKindBox.SelectedItem as ComboBoxItem)?.Tag is string tag&&int.TryParse(tag,out int mode)?mode:0;
    private void Section_Click(object sender,RoutedEventArgs e)=>SelectSection(((Button)sender).Tag?.ToString()=="Controller");
    private void ProgramKind_Changed(object sender,SelectionChangedEventArgs e)
    {
        ApplyProgramKind();
        // An open popup's keyboard highlight is only a preview until it closes.
        if(modeSelectorReady&&!cancelModeCommit&&!ProgramKindBox.IsDropDownOpen)CommitControllerMode();
    }
    private void ProgramKind_DropDownOpened(object sender,EventArgs e)
    {
        modeWhenOpened=SelectedMode;cancelModeCommit=false;
    }
    private void ProgramKind_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape&&ProgramKindBox.IsDropDownOpen)cancelModeCommit=true;
    }
    private void ProgramKind_DropDownClosed(object sender,EventArgs e)
    {
        if(cancelModeCommit)
        {
            cancelModeCommit=false;
            bool wasReady=modeSelectorReady;modeSelectorReady=false;
            try
            {
                if(modeWhenOpened is >=1 and <=7)ProgramKindBox.SelectedIndex=modeWhenOpened-1;
                lastRequestedMode=SelectedMode;
            }
            finally {modeSelectorReady=wasReady;}
            return;
        }
        CommitControllerMode();
    }
    private void CommitControllerMode()
    {
        int mode=SelectedMode;
        if(!modeSelectorReady||mode is <1 or >7||mode==lastRequestedMode)return;
        // Updating before notifying also suppresses duplicate SelectionChanged/close events.
        lastRequestedMode=mode;
        ControllerModeCommitted?.Invoke(mode);
    }

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
