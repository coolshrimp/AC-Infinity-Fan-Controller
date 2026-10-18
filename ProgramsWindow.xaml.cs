using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ACInfinityDesktop;

public partial class ProgramsWindow : Window
{
    internal bool AllowClose {get;set;}

    public ProgramsWindow()
    {
        InitializeComponent();
        ApplyProgramKind();
    }

    // Switching views changes presentation only; unsaved entries stay intact.
    private void Section_Click(object sender,RoutedEventArgs e)
    {
        bool controller=((Button)sender).Tag?.ToString()=="Controller";
        ControllerSection.Visibility=controller?Visibility.Visible:Visibility.Collapsed;
        AppSection.Visibility=controller?Visibility.Collapsed:Visibility.Visible;
        ControllerTab.SetResourceReference(BackgroundProperty,controller?"CardBrush":"HeaderBrush");
        ControllerTab.SetResourceReference(BorderBrushProperty,controller?"AccentBrush":"MutedBrush");
        ThisPcTab.SetResourceReference(BackgroundProperty,controller?"HeaderBrush":"CardBrush");
        ThisPcTab.SetResourceReference(BorderBrushProperty,controller?"MutedBrush":"AccentBrush");
    }

    private void ProgramKind_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProgramKind();

    private void ApplyProgramKind()
    {
        // SelectedIndex can fire while InitializeComponent is still creating fields.
        if(ControllerDailyPanel==null||ControllerCyclePanel==null)return;
        bool cycle=(ProgramKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()=="6";
        ControllerDailyPanel.Visibility=cycle?Visibility.Collapsed:Visibility.Visible;
        ControllerCyclePanel.Visibility=cycle?Visibility.Visible:Visibility.Collapsed;
    }

    private void Header_Drag(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left||e.ButtonState!=MouseButtonState.Pressed)return;
        try{DragMove();}catch(InvalidOperationException){}
    }

    private void Close_Click(object sender,RoutedEventArgs e)=>Hide();
    private void Window_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Hide();e.Handled=true;}}
    private void Window_Closing(object? sender,CancelEventArgs e){if(!AllowClose){e.Cancel=true;Hide();}}
}