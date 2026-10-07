using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LdapCloudSync.App.Views;

public partial class AssetCheckInWindow : Window
{
    private static readonly TimeSpan TouchLaunchWindow = TimeSpan.FromMilliseconds(1500);
    private DateTime _lastOskLaunchAttemptUtc = DateTime.MinValue;
    private DateTime _lastTouchInputUtc = DateTime.MinValue;

    public AssetCheckInWindow()
    {
        InitializeComponent();

        if (DataContext is ViewModels.AssetCheckInViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            vm.CheckInCompleted += OnCheckInCompleted;
        }

        AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnInputPreviewTouchDown), true);
        AddHandler(UIElement.PreviewStylusDownEvent, new StylusDownEventHandler(OnInputPreviewStylusDown), true);
        AddHandler(UIElement.GotKeyboardFocusEvent, new RoutedEventHandler(OnInputGotKeyboardFocus), true);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        Topmost = false;
    }

    private void OnInputPreviewTouchDown(object sender, TouchEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            _lastTouchInputUtc = DateTime.UtcNow;
    }

    private void OnInputPreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            _lastTouchInputUtc = DateTime.UtcNow;
    }

    private void OnInputGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TextBox)
            return;

        var sinceTouch = DateTime.UtcNow - _lastTouchInputUtc;
        if (sinceTouch > TouchLaunchWindow)
            return;

        var elapsed = DateTime.UtcNow - _lastOskLaunchAttemptUtc;
        if (elapsed < TimeSpan.FromSeconds(1))
            return;

        _lastOskLaunchAttemptUtc = DateTime.UtcNow;
        TryLaunchOnScreenKeyboard();
    }

    private static void TryLaunchOnScreenKeyboard()
    {
        try
        {
            if (Process.GetProcessesByName("osk").Length > 0)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "osk.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            // Best-effort launch in kiosk mode.
        }
    }

    private void OnCloseRequested()
    {
        Dispatcher.Invoke(() =>
        {
            Close();
            App.ShowOrActivateMainWindow();
        });
    }

    private void OnCheckInCompleted(string message)
    {
        Dispatcher.Invoke(() => MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(message) ? "Check-in complete." : message,
            "Asset Check-In",
            MessageBoxButton.OK,
            MessageBoxImage.Information));
    }

    private void OpenConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is not null)
            Owner = null;

        if (App.TryLaunchElevatedConfigProcess())
            App.Current.Shutdown();
        else
            MessageBox.Show(this, "Unable to launch the elevated configuration window.", "Asset Check-In", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CloseRefresh_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
