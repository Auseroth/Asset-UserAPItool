using System.Windows;
using System.Windows.Controls;
using LdapCloudSync.App.ViewModels;

namespace LdapCloudSync.App.Views;

public partial class AdSettingsView : UserControl
{
    public AdSettingsView()
    {
        InitializeComponent();
    }

    // PasswordBox doesn't support binding — handle manually
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdSettingsViewModel vm && sender is PasswordBox pb)
        {
            vm.Password = pb.Password;
        }
    }
}