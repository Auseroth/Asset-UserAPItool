using System.Windows;

namespace LdapCloudSync.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenUpdateWindow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UpdateWindow
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}
