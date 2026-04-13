using System.Windows;

namespace LdapCloudSync.App.Views;

public partial class TestSyncPreviewWindow : Window
{
    public TestSyncPreviewWindow()
    {
        InitializeComponent();
    }

    /// <summary>True if the user clicked "Proceed with Sync".</summary>
    public bool Confirmed { get; private set; }

    private void Proceed_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}