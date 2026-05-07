using System.Windows;
using xpaste.Services;

namespace xpaste.Views;

/// <summary>
/// Dialog that allows the user to change the master password while the store is unlocked.
/// Verifies the current password, validates that the new password is confirmed, then
/// re-encrypts all snippets under the new key via <see cref="SnippetStore.ChangePassword"/>.
/// </summary>
public partial class ChangePasswordDialog : Window
{
    private readonly SnippetStore _store;

    /// <summary>
    /// Initialises the dialog with the store whose master password will be changed.
    /// </summary>
    /// <param name="store">The currently unlocked snippet store.</param>
    public ChangePasswordDialog(SnippetStore store)
    {
        InitializeComponent();
        _store = store;
    }

    /// <summary>Handles the Change Password button click.</summary>
    private void OnChange(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var current = CurrentPasswordBox.Password;
        var newPwd = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(current))
        {
            ErrorText.Text = "Current password cannot be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(newPwd))
        {
            ErrorText.Text = "New password cannot be empty.";
            return;
        }

        if (newPwd != confirm)
        {
            ErrorText.Text = "New passwords do not match.";
            return;
        }

        if (!_store.ChangePassword(current, newPwd))
        {
            ErrorText.Text = "Current password is incorrect.";
            return;
        }

        DialogResult = true;
    }

    /// <summary>Allows confirming the form by pressing Enter in any password box.</summary>
    private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            OnChange(sender, e);
    }
}
