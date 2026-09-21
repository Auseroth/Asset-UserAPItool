using System.Windows;

namespace LdapCloudSync.App.Views;

public partial class AssetCheckInWindow : Window
{
    public AssetCheckInWindow()
    {
        InitializeComponent();

        if (DataContext is ViewModels.AssetCheckInViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            vm.CheckInCompleted += OnCheckInCompleted;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        Topmost = false;
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
