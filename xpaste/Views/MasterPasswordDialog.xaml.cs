using System.Windows;
using xpaste.Services;

namespace xpaste.Views;

/// <summary>
/// Modal dialog that prompts the user for the master password.
/// On first launch it initialises a new encrypted store; on subsequent launches
/// it attempts to unlock the existing store and reports failure if the password is wrong.
/// </summary>
public partial class MasterPasswordDialog : Window
{
    private readonly SnippetStore _store;
    private readonly bool _isFirstLaunch;

    /// <summary>
    /// Initialises the dialog.
    /// </summary>
    /// <param name="store">The snippet store to initialise or unlock.</param>
    /// <param name="isFirstLaunch">
    /// <c>true</c> when no store file exists yet and a new master password should be set;
    /// <c>false</c> when unlocking an existing store.
    /// </param>
    public MasterPasswordDialog(SnippetStore store, bool isFirstLaunch)
    {
        InitializeComponent();
        _store = store;
        _isFirstLaunch = isFirstLaunch;
        if (isFirstLaunch)
        {
            SubtitleText.Text = "Set a master password to protect your snippets";
            Title = "xpaste — Set Master Password";
        }
    }

    /// <summary>Handles the Unlock button click. Validates the password and sets <see cref="System.Windows.Window.DialogResult"/>.</summary>
    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        var pwd = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(pwd))
        {
            ErrorText.Text = "Password cannot be empty.";
            return;
        }

        if (_isFirstLaunch)
        {
            _store.Initialize(pwd);
            DialogResult = true;
        }
        else if (_store.Unlock(pwd))
        {
            DialogResult = true;
        }
        else
        {
            ErrorText.Text = "Incorrect password. Try again.";
        }
    }

    /// <summary>Allows the user to confirm the password by pressing Enter.</summary>
    private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            OnUnlock(sender, e);
    }
}
