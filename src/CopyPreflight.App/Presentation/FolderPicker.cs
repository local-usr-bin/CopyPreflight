using System.IO;

namespace CopyPreflight.App;

/// <summary>
/// The fixed configuration of one folder-picking dialog: which dialog it is, what it is called, and
/// the folder it may start in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The source box and the target box are two separate purposes, so each gets its own dialog
/// identity.</b> Windows keys a dialog's remembered state — its size and its last used folder — on
/// the <see cref="DialogId"/>, so two identities mean two independent histories and neither box can
/// drag the other to a folder it was never used for. Without that separation the two dialogs share
/// one history, which is how browsing for a source used to decide where browsing for a target
/// started.
/// </para>
/// <para>
/// <b>The identities are constants compiled into the application.</b> An identity generated per
/// process would carry no history at all, and an identity shared by both dialogs is exactly the
/// coupling this replaces. They are never regenerated: changing one silently abandons the location
/// its users had grown used to, so a value here is permanent once shipped.
/// </para>
/// <para>
/// <b>The starting folder is derived from the path entered for this dialog and from nothing else.</b>
/// Each factory takes one path — its own — so there is no way to express "start the target where the
/// source was", and no fallback reads the other box. A path that is not fully qualified is not
/// offered at all.
/// </para>
/// <para>
/// <b>An offered folder is a suggestion, never a selection.</b> The dialogs write nothing back on
/// their own: the entered path is replaced only when the user accepts the dialog's answer, and
/// cancelling leaves everything exactly as it was.
/// </para>
/// <para>
/// This type performs no file-system call of its own. A fully qualified path is offered as a starting
/// folder without asking whether it exists, whether it is reachable or whether it is a directory:
/// those are observations, and they belong to the scanner and the probes rather than to the
/// interface thread. A path the platform cannot use is simply not applied by the dialog, which then
/// keeps the location it remembered for this identity.
/// </para>
/// </remarks>
internal sealed record FolderPicker
{
    /// <summary>Identity of the source dialog, for Windows' per-dialog remembered state.</summary>
    /// <remarks>
    /// Permanent: it exists to keep the source history apart from the target history, and replacing it
    /// would both throw that history away and re-couple the two dialogs.
    /// </remarks>
    private static readonly Guid SourceDialogId = new("d4e2b1a7-3c56-4f18-9a2d-6b0c5e7f1a34");

    /// <summary>Identity of the target dialog, for Windows' per-dialog remembered state.</summary>
    /// <remarks>Permanent, and deliberately different from <see cref="SourceDialogId"/>.</remarks>
    private static readonly Guid TargetDialogId = new("8f3c7d21-5a94-4b6e-8c17-2d9e4a6b3f58");

    private FolderPicker(string title, Guid dialogId, string? initialDirectory)
    {
        Title = title;
        DialogId = dialogId;
        InitialDirectory = initialDirectory;
    }

    /// <summary>The dialog's caption.</summary>
    internal string Title { get; }

    /// <summary>
    /// Which dialog this is. Windows remembers a dialog's size and last used folder separately for each
    /// identity, so this is what keeps the two boxes' histories apart.
    /// </summary>
    internal Guid DialogId { get; }

    /// <summary>
    /// The folder to start in, or <see langword="null"/> to leave the choice to the dialog's own
    /// remembered location and, failing that, to the platform's default.
    /// </summary>
    internal string? InitialDirectory { get; }

    /// <summary>Configures the dialog used for the source folder.</summary>
    /// <param name="enteredPath">
    /// What the source box currently holds. It is offered as the starting folder when it is a
    /// fully qualified path; the target box is never consulted.
    /// </param>
    internal static FolderPicker ForSource(string? enteredPath)
        => new("Select the source folder", SourceDialogId, OfferStartingFolder(enteredPath));

    /// <summary>Configures the dialog used for the target folder.</summary>
    /// <param name="enteredPath">
    /// What the target box currently holds, considered exactly as the source path is above and with no
    /// relation to it.
    /// </param>
    internal static FolderPicker ForTarget(string? enteredPath)
        => new("Select the target folder", TargetDialogId, OfferStartingFolder(enteredPath));

    /// <summary>
    /// Whether the path entered for one box may be used to start that box's dialog in.
    /// </summary>
    /// <remarks>
    /// The only thing asked of the value is whether it is a complete path, which is the same input
    /// rule the request itself applies. Whether the folder is there is deliberately not asked here:
    /// it is a file-system question, the interface thread does not answer those, and the dialog
    /// handles a folder it cannot use by keeping its remembered location.
    /// </remarks>
    private static string? OfferStartingFolder(string? enteredPath)
        => !string.IsNullOrWhiteSpace(enteredPath) && Path.IsPathFullyQualified(enteredPath)
            ? enteredPath
            : null;
}
