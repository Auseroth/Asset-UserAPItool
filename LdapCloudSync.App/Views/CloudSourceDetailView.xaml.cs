using System.Windows.Controls;

namespace LdapCloudSync.App.Views;

public partial class CloudSourceDetailView : UserControl
{
    public CloudSourceDetailView()
    {
        InitializeComponent();
    }

    private void AdPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.CloudSourceViewModel vm)
            vm.AdPassword = ((PasswordBox)sender).Password;
    }
}