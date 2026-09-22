using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using CopyPreflight.App;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The real window, over the real markup, driven into the states it has to show.
/// </summary>
/// <remarks>
/// <para>
/// The state machine is asserted without a window in <see cref="CheckViewModelTests"/>, and that is
/// where its rules belong. What can only be checked here is the wiring: that the markup's own bindings
/// resolve against this view model, that the notifications the view model raises actually reach the
/// elements bound to them, that the buttons' <c>Click</c> attributes really reach the code behind, and
/// that the window's <c>Closing</c> hook really is the view model's refusal.
/// </para>
/// <para>
/// Creating a window needs a single-threaded apartment with a dispatcher of its own, so each of these
/// tests runs its body on one and shuts it down afterwards. Nothing is pumped except while waiting for
/// a signal the test itself defines — the run reaching a terminal state — so no assertion depends on
/// how long anything took.
/// </para>
/// <para>
/// What is deliberately <b>not</b> covered here: pressing <c>Browse…</c>, which opens a modal folder
/// dialog and cannot be driven without a person at the machine. That the attribute is wired is visible
/// in the markup, and it is listed as not manually verified rather than claimed.
/// </para>
/// </remarks>
public class MainWindowBindingTests
{
    [Fact]
    public void TheCheckScreen_ShowsTheStatesItIsDrivenInto()
        => OnDispatchedThread(() =>
        {
            using var source = new TemporarySourceTree();
            using var target = new TemporarySourceTree();
            using var other = new TemporarySourceTree();

            // A real conflict on the real file system: the same name on both sides is a decided warning.
            source.CreateFile("a.txt", 3);
            target.CreateFile("a.txt", 7);

            var viewModel = new CheckViewModel(WorkflowInputs.Over());
            viewModel.SourcePath = source.Root;
            viewModel.TargetPath = target.Root;

            WithShownWindow(viewModel, window =>
            {
                var start = FindButton(window, "Start check");
                var cancel = FindButton(window, "Cancel");
                var progress = Find<ProgressBar>(window);
                var grid = Find<DataGrid>(window);
                var sourceBox = FindBoundTo<TextBox>(window, TextBox.TextProperty, "SourcePath");
                var message = FindBoundTo<TextBlock>(window, TextBlock.TextProperty, "InputMessage");
                var conclusion = FindBoundTo<TextBlock>(window, TextBlock.TextProperty, "ConclusionText");
                var statement = FindBoundTo<TextBlock>(window, TextBlock.TextProperty, "BlockingStatementText");
                var emptyFindings = FindBoundTo<TextBlock>(window, TextBlock.TextProperty, "FindingsEmptyText");
                var detail = FindBoundTo<ScrollViewer>(window, UIElement.VisibilityProperty, "HasSelection");
                var detailPath = FindBoundTo<TextBox>(detail, TextBox.TextProperty, "SourcePathText");

                // An initial screen offers a check, offers no cancellation, and shows no zeroes.
                Assert.True(start.IsEnabled);
                Assert.False(cancel.IsEnabled);
                Assert.True(sourceBox.IsEnabled);
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
                Assert.Equal(Visibility.Collapsed, grid.Visibility);
                Assert.Equal(Visibility.Visible, emptyFindings.Visibility);
                Assert.Equal(Visibility.Collapsed, message.Visibility);
                Assert.Equal(Visibility.Collapsed, statement.Visibility);
                Assert.Equal(Visibility.Collapsed, detail.Visibility);
                Assert.Equal("Nothing has been checked yet.", conclusion.Text);

                // An input the check refuses is reported where the markup binds the message — which is
                // the notification, not the property: nothing re-reads the binding here.
                viewModel.SourcePath = "relative-folder";

                var refused = viewModel.StartCheckAsync();
                Assert.True(refused.IsCompleted, "An input the check refuses must not start a run.");

                Assert.Equal(Visibility.Visible, message.Visibility);
                Assert.Contains("complete path", message.Text);

                // Correcting the input clears the message again.
                viewModel.SourcePath = source.Root;
                Assert.Equal(Visibility.Collapsed, message.Visibility);

                var settled = WhenSettled(viewModel);

                // The markup's Click attribute is what starts the check, so it is started the way the
                // window starts it — not by reaching past the button into the view model.
                start.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, start));

                // A run in progress is visible at once: the controls react before the work returns.
                Assert.Equal(CheckState.Running, viewModel.State);
                Assert.False(start.IsEnabled);
                Assert.True(cancel.IsEnabled);
                Assert.False(sourceBox.IsEnabled);
                Assert.Equal(Visibility.Visible, progress.Visibility);
                Assert.Equal("Checking…", viewModel.StateText);

                PumpUntil(settled);
                Settle(window);

                // A finished assessment puts its findings in the list and stops saying the list is empty.
                Assert.Equal(CheckState.Completed, viewModel.State);
                Assert.NotEmpty(viewModel.Findings);
                Assert.Equal(Visibility.Visible, grid.Visibility);
                Assert.Equal(viewModel.Findings.Count, grid.Items.Count);
                Assert.Equal(Visibility.Collapsed, emptyFindings.Visibility);
                Assert.Equal(Visibility.Collapsed, message.Visibility);
                Assert.Equal("Assessment finished.", viewModel.StateText);
                Assert.True(viewModel.HasBlockingStatement);
                Assert.Equal(Visibility.Visible, statement.Visibility);
                Assert.Equal("No known blocking issue was detected.", statement.Text);

                // Selecting a row fills the detail pane with that row's own text.
                var index = IndexOfCollision(viewModel);
                grid.SelectedIndex = index;
                Settle(window);

                Assert.Same(viewModel.Findings[index], viewModel.SelectedFinding);
                Assert.Equal(Visibility.Visible, detail.Visibility);
                Assert.Equal("a.txt", detailPath.Text);

                // Editing a path after the check discards the whole result: the summary, the conclusion,
                // the list and the selection all go, not just the result behind them.
                viewModel.TargetPath = other.Root;
                Settle(window);

                Assert.Equal(CheckState.Idle, viewModel.State);
                Assert.Equal(Visibility.Collapsed, grid.Visibility);
                Assert.Equal(Visibility.Visible, emptyFindings.Visibility);
                Assert.Equal(Visibility.Collapsed, statement.Visibility);
                Assert.Equal(Visibility.Collapsed, detail.Visibility);
                Assert.Null(viewModel.SelectedFinding);
                Assert.Equal("Nothing has been checked yet.", conclusion.Text);
                Assert.False(cancel.IsEnabled);
                Assert.True(start.IsEnabled);
            });
        });

    [Fact]
    public void TheWindow_RefusesAnOrdinaryCloseWhileACheckIsRunning()
        => OnDispatchedThread(() =>
        {
            using var source = new TemporarySourceTree();
            using var target = new TemporarySourceTree();

            // A check held inside the source observation, so the window can be closed while it is
            // genuinely running without depending on how fast anything is.
            var reader = new ControllableDirectoryReader();
            var viewModel = new CheckViewModel(WorkflowInputs.Over(new SourceScanner(reader)));
            viewModel.SourcePath = source.Root;
            viewModel.TargetPath = target.Root;

            var closed = false;

            WithShownWindow(viewModel, window =>
            {
                window.Closed += (_, _) => closed = true;

                var start = FindButton(window, "Start check");
                var settled = WhenSettled(viewModel);

                start.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, start));
                Assert.True(reader.WaitUntilListingStarted(), "The check should have begun observing the source.");
                Assert.Equal(CheckState.Running, viewModel.State);

                window.Close();

                // The close was refused and the run was asked to stop, without the interface waiting for
                // it: the screen is still busy, and it is the markup's Closing hook that did it.
                Assert.False(closed, "An ordinary close must be refused while a check is still running.");
                Assert.Equal(CheckState.Cancelling, viewModel.State);
                Assert.True(viewModel.IsBusy);

                reader.Release();
                PumpUntil(settled);

                // Once the run has genuinely stopped, the same close is accepted.
                Assert.Equal(CheckState.Cancelled, viewModel.State);
                window.Close();
                Assert.True(closed, "A close must be accepted once the run has ended.");
            });
        });

    // ------------------------------------------------------------------ the dispatcher thread

    /// <summary>Runs a body on a fresh STA thread whose dispatcher it installs as the context.</summary>
    /// <remarks>
    /// The context is installed before anything is created on the thread, because the view model's
    /// progress channel captures whatever context is current when a run starts — the same thing a
    /// window's own dispatcher context does in production.
    /// </remarks>
    private static void OnDispatchedThread(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "The window thread did not finish.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Creates the window over the view model, shows it, and closes it on the way out.</summary>
    private static void WithShownWindow(CheckViewModel viewModel, Action<MainWindow> body)
    {
        var window = new MainWindow(viewModel);

        try
        {
            window.Show();
            Settle(window);
            body(window);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    /// <summary>
    /// Lets the dispatcher finish the work a state change queued, so the markup has caught up with it.
    /// </summary>
    /// <remarks>
    /// A binding can only be read once it has been applied, and a change that makes a collapsed part of
    /// the window visible also has to be laid out before the bindings inside it carry a value. Work at
    /// this priority runs after everything the change itself queued, so a read after it is a read of a
    /// window that has finished reacting rather than one that is mid-reaction. It is never called while
    /// a run is meant to be observed as running: settling could return the run's own continuation.
    /// </remarks>
    private static void Settle(Window window)
        => window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

    /// <summary>A task that completes when the screen has reached a state it will stay in.</summary>
    private static Task WhenSettled(CheckViewModel viewModel)
    {
        var settled = new TaskCompletionSource();

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CheckViewModel.State) && !viewModel.IsBusy)
            {
                settled.TrySetResult();
            }
        };

        return settled.Task;
    }

    /// <summary>Runs queued dispatcher work until the task completes.</summary>
    /// <remarks>
    /// The frame is stopped by the task itself, not by a timer or a delay, so nothing here depends on
    /// how long a run takes.
    /// </remarks>
    private static void PumpUntil(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();

        task.ContinueWith(
            _ => frame.Continue = false,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.PushFrame(frame);
    }

    // ------------------------------------------------------------------ reading the markup

    /// <summary>The first button whose content is the given label.</summary>
    private static Button FindButton(DependencyObject root, string content)
    {
        var match = Descendants(root)
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));

        Assert.True(match is not null, $"No button says '{content}'.");

        return match!;
    }

    private static T Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        var match = Descendants(root).OfType<T>().FirstOrDefault();

        Assert.True(match is not null, $"The window holds no {typeof(T).Name}.");

        return match!;
    }

    /// <summary>
    /// The element whose binding, on one property, follows the given path.
    /// </summary>
    /// <remarks>
    /// Finding elements by what they are bound to rather than by name is what makes this a check of the
    /// markup: an element that stopped being bound to this path would not be found at all.
    /// </remarks>
    private static T FindBoundTo<T>(DependencyObject root, DependencyProperty property, string path)
        where T : DependencyObject
    {
        var match = Descendants(root)
            .OfType<T>()
            .FirstOrDefault(element => BindingOperations.GetBinding(element, property)?.Path?.Path == path);

        Assert.True(match is not null, $"No {typeof(T).Name} is bound to '{path}'.");

        return match!;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
            {
                continue;
            }

            yield return node;

            foreach (var descendant in Descendants(node))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Where the row for the collided entry sits in the displayed order.</summary>
    private static int IndexOfCollision(CheckViewModel viewModel)
    {
        for (var index = 0; index < viewModel.Findings.Count; index++)
        {
            if (viewModel.Findings[index].SourceRelativePath == "a.txt")
            {
                return index;
            }
        }

        throw new InvalidOperationException("The run produced no finding for the collided entry.");
    }
}
