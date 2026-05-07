using System.ComponentModel;
using System.Windows;
using xpaste.ViewModels;

namespace xpaste;

/// <summary>
/// The main snippet-management window. It never truly closes — pressing the title-bar X
/// hides it back to the system tray unless <see cref="AllowClose"/> is set to <c>true</c>
/// (which the application does only during a deliberate exit).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>
    /// Initialises the window and binds it to <paramref name="vm"/>.
    /// </summary>
    /// <param name="vm">The view-model that drives all snippet CRUD operations.</param>
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>
    /// When <c>false</c> (the default), closing the window hides it to the tray instead.
    /// Set to <c>true</c> immediately before calling <see cref="Window.Close"/> on exit.
    /// </summary>
    public bool AllowClose { get; set; }

    /// <summary>Intercepts the close event and hides the window unless <see cref="AllowClose"/> is set.</summary>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
