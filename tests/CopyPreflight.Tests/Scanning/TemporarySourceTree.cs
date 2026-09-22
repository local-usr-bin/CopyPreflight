using System.Diagnostics;
using System.IO;

namespace CopyPreflight.Tests;

/// <summary>
/// A throwaway directory tree inside the repository, for tests that need real directories and files
/// on disk.
/// </summary>
/// <remarks>
/// <para>
/// Introduced for the scanner tests and reused by the target-probe tests, which need the same kind of
/// fixture: real paths the operating system actually answers questions about.
/// </para>
/// <para>
/// <b>The tree is created inside the checkout the tests belong to, not in the machine's shared temp
/// directory.</b> The repository is the only tree these tests are entitled to write in, and the
/// fixture must not depend on where an individual machine happens to put its temp files. The root
/// below the repository is found by walking up from the test binary to the directory that holds the
/// solution file, so no developer's user name and no absolute path is written into the source. When
/// that directory cannot be found the constructor fails loudly instead of quietly falling back to a
/// system temp path.
/// </para>
/// <para>
/// Disposal deletes the trees this instance created, and nothing else: each tree is removed in full,
/// and the shared parent directory the trees are created under is left alone because other test
/// trees may still be using it. Reparse points are unlinked as themselves, and before the
/// directories that contain them: deleting a directory tree that still contains a junction fails on
/// Windows, and following one would delete what it points at — so removing a junction never removes
/// what it points at.
/// </para>
/// <para>
/// Nothing here changes security configuration, requires elevation or needs Developer Mode.
/// </para>
/// </remarks>
internal sealed class TemporarySourceTree : IDisposable
{
    /// <summary>
    /// The file whose directory is taken to be the repository root. Any file agreed on by the whole
    /// checkout would do; the solution file is the one every build of this repository starts from.
    /// </summary>
    private const string RepositoryMarkerFileName = "CopyPreflight.sln";

    /// <summary>
    /// The ignored directory below the repository root that holds every fixture tree.
    /// </summary>
    /// <remarks>
    /// It is already covered by the repository's <c>artifacts/</c> ignore rule, so a fixture can
    /// never show up as an untracked file. Resolved once and reused, so the walk happens at most once
    /// per test run and a failure is reported directly rather than wrapped.
    /// </remarks>
    private static readonly Lazy<string> FixtureRootDirectory = new(ResolveFixtureRootDirectory);

    private readonly List<string> _externalDirectories = [];

    internal TemporarySourceTree()
    {
        Root = Path.Combine(
            FixtureRootDirectory.Value,
            "CopyPreflight.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// The directory fixtures are created under: an ignored folder at the repository root.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No directory at or above the test binary holds <see cref="RepositoryMarkerFileName"/>, so there
    /// is no repository-owned place to create the fixture in.
    /// </exception>
    private static string ResolveFixtureRootDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarkerFileName)))
            {
                return Path.Combine(directory.FullName, "artifacts", "test-fixtures");
            }
        }

        throw new InvalidOperationException(
            $"Could not determine the fixture root: no directory containing '{RepositoryMarkerFileName}' "
            + $"was found at or above the test binary's location '{AppContext.BaseDirectory}'. These tests "
            + "create their fixture trees inside the repository they belong to and must not fall back to "
            + "the system temp directory.");
    }

    /// <summary>Absolute path of the root directory of this tree.</summary>
    internal string Root { get; }

    /// <summary>Absolute path of one entry below the root.</summary>
    internal string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    /// <summary>Creates a directory below the root and returns its absolute path.</summary>
    internal string CreateDirectory(string relativePath)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a file of exactly the given size below the root and returns its absolute path.</summary>
    internal string CreateFile(string relativePath, int lengthInBytes = 0)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[lengthInBytes]);
        return path;
    }

    /// <summary>
    /// Creates a directory outside the scanned tree — but inside the same repository-owned fixture root
    /// — and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// It is a sibling of the scanned tree rather than a child of it, so a junction into it is a
    /// genuine reparse point that does not also place the target inside the tree being scanned.
    /// </remarks>
    internal string CreateExternalDirectory()
    {
        var path = Path.Combine(
            FixtureRootDirectory.Value,
            "CopyPreflight.Tests-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _externalDirectories.Add(path);
        return path;
    }

    /// <summary>Writes a file of the given size into an externally created directory.</summary>
    internal string CreateExternalFile(string externalDirectory, string fileName, int lengthInBytes)
    {
        var path = Path.Combine(externalDirectory, fileName);
        File.WriteAllBytes(path, new byte[lengthInBytes]);
        return path;
    }

    /// <summary>
    /// Creates a real directory junction below the root and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// Junctions are used because any Windows user can create one, while symbolic links need an
    /// elevated token or Developer Mode. Tests therefore require no environment configuration and
    /// still work against a genuine reparse point on disk.
    /// </remarks>
    internal string CreateDirectoryJunction(string relativePath, string targetDirectory)
    {
        var path = PathOf(relativePath);
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{targetDirectory}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Creating a directory junction at '{path}' failed with exit code {process.ExitCode}: " +
                $"{standardOutput.Trim()} {standardError.Trim()}");
        }

        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DeleteTree(Root);

        foreach (var directory in _externalDirectories)
        {
            DeleteTree(directory);
        }
    }

    private static void DeleteTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var attributes = File.GetAttributes(entry);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Unlink the link itself. Recursing would delete the link target instead.
                if (isDirectory)
                {
                    Directory.Delete(entry, recursive: false);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            else if (isDirectory)
            {
                DeleteTree(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(directory, recursive: false);
    }
}
