using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LdapCloudSync.App.Views;

public partial class AssetCheckInWindow : Window
{
    private TextBox? _activeTextBox;
    private bool _isKeyboardVisible;
    private bool _isSymbolKeyboardVisible;

    public AssetCheckInWindow()
    {
        InitializeComponent();

        if (DataContext is ViewModels.AssetCheckInViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            vm.CheckInCompleted += OnCheckInCompleted;
        }

        AddHandler(UIElement.GotKeyboardFocusEvent, new RoutedEventHandler(OnInputGotKeyboardFocus), true);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        Topmost = false;
    }

    private void OnInputGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        var textBox = TryGetOwningTextBox(e.OriginalSource);
        if (textBox is not null)
            _activeTextBox = textBox;
    }

    private static TextBox? TryGetOwningTextBox(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is TextBox textBox)
                return textBox;

            current = current is FrameworkContentElement fce
                ? fce.Parent
                : VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ToggleOnScreenKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _isKeyboardVisible = !_isKeyboardVisible;

        if (FindName("OnScreenKeyboardPanel") is FrameworkElement panel)
            panel.Visibility = _isKeyboardVisible ? Visibility.Visible : Visibility.Collapsed;

        if (_isKeyboardVisible)
            SetSymbolKeyboardVisible(false);

        if (FindName("KeyboardToggleButton") is Button toggle)
            toggle.Content = _isKeyboardVisible ? "Hide On-Screen Keyboard" : "On-Screen Keyboard";
    }

    private void ToggleSymbolKeyboard_Click(object sender, RoutedEventArgs e)
    {
        SetSymbolKeyboardVisible(!_isSymbolKeyboardVisible);

        if (_activeTextBox is not null && _activeTextBox.IsEnabled)
            _activeTextBox.Focus();
    }

    private void SetSymbolKeyboardVisible(bool visible)
    {
        _isSymbolKeyboardVisible = visible;

        if (FindName("AlphaKeyboardLayout") is FrameworkElement alpha)
            alpha.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;

        if (FindName("SymbolKeyboardLayout") is FrameworkElement symbols)
            symbols.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KeyboardKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var key = button.Tag?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(key))
            return;

        if (_activeTextBox is null || !_activeTextBox.IsEnabled)
            return;

        var targetTextBox = _activeTextBox;
        targetTextBox.Focus();

        if (string.Equals(key, "BACKSPACE", StringComparison.Ordinal))
        {
            if (targetTextBox.SelectionLength > 0)
            {
                var start = targetTextBox.SelectionStart;
                targetTextBox.Text = targetTextBox.Text.Remove(start, targetTextBox.SelectionLength);
                targetTextBox.SelectionStart = start;
            }
            else if (targetTextBox.SelectionStart > 0)
            {
                var removeIndex = targetTextBox.SelectionStart - 1;
                targetTextBox.Text = targetTextBox.Text.Remove(removeIndex, 1);
                targetTextBox.SelectionStart = removeIndex;
            }

            return;
        }

        var insertText = string.Equals(key, "SPACE", StringComparison.Ordinal) ? " " : key;
        var selectionStart = targetTextBox.SelectionStart;
        var selectionLength = targetTextBox.SelectionLength;

        if (selectionLength > 0)
            targetTextBox.Text = targetTextBox.Text.Remove(selectionStart, selectionLength);

        targetTextBox.Text = targetTextBox.Text.Insert(selectionStart, insertText);
        targetTextBox.SelectionStart = selectionStart + insertText.Length;
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
