using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace CopyPreflight.App;

/// <summary>
/// The check screen: it collects the two folder paths, starts and cancels a check, and shows what came
/// back.
/// </summary>
/// <remarks>
/// <para>
/// The code behind does three things and no more: it owns the composition root, it forwards button
/// presses, and it decides whether an attempt to close the window may proceed. Every piece of state,
/// every piece of text and every decision about what may be concluded lives in
/// <see cref="CheckViewModel"/>, which is why the behaviour can be tested without a window.
/// </para>
/// <para>
/// Nothing in the check path touches the interface from another thread. The workflow is handed a
/// progress sink that posts to the synchronization context captured when the run starts, so snapshots
/// arrive on this thread already, and the interface is never updated from the thread the scan runs on.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly CheckViewModel _viewModel;

    /// <summary>Creates the window over the real workflow.</summary>
    public MainWindow()
        : this(new CheckViewModel())
    {
    }

    /// <summary>
    /// Creates the window over a supplied view model. For tests only.
    /// </summary>
    /// <param name="viewModel">The state machine the window binds to and forwards its events to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Exists so that a test can put the window in front of the states it binds to — an input refusal, a
    /// run in progress, a finished assessment with a finding selected — and check the bindings against
    /// the real markup. It is not a way to replace the workflow in production: the window is still built
    /// over the real view model by the constructor above.
    /// </remarks>
    internal MainWindow(CheckViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // Wired here rather than in the markup, so that all of this window's wiring is in one place.
        Closing += OnClosing;
    }

    /// <summary>
    /// Refuses an ordinary close while a check is still running, asking it to stop instead.
    /// </summary>
    /// <remarks>
    /// The window is not hidden and the check is not abandoned: the close is cancelled, cancellation is
    /// requested, and the window stays responsive until the run has genuinely returned, at which point
    /// closing it again works. Nothing is waited for synchronously here, so the interface cannot lock up
    /// on a run that is slow to stop. A forced process termination, a sign-out or a shutdown is not
    /// intercepted.
    /// </remarks>
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_viewModel.TryBlockClose())
        {
            args.Cancel = true;
        }
    }

    /// <summary>
    /// Asks for the source folder. The interface is only touched after the dialog has been accepted, so
    /// cancelling the dialog leaves whatever was typed exactly as it was.
    /// </summary>
    private void BrowseSource_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the source folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SourcePath = dialog.FolderName;
        }
    }

    /// <summary>Asks for the target folder; cancelling the dialog changes nothing.</summary>
    private void BrowseTarget_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the target folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.TargetPath = dialog.FolderName;
        }
    }

    /// <summary>
    /// Starts a check. This is the only place an <c>async void</c> is used: it is the event boundary
    /// where the framework requires one, and everything below it returns a task.
    /// </summary>
    private async void StartCheck_Click(object sender, RoutedEventArgs args)
        => await _viewModel.StartCheckAsync();

    /// <summary>Asks the running check to stop.</summary>
    private void CancelCheck_Click(object sender, RoutedEventArgs args)
        => _viewModel.CancelCheck();
}
