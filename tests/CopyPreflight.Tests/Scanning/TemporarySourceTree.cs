using System.Diagnostics;
using System.IO;

namespace CopyPreflight.Tests;

/// <summary>
/// A throwaway source tree under the process temp directory, for scanner tests.
/// </summary>
/// <remarks>
/// <para>
/// Disposal deletes the tree. Reparse points are unlinked as themselves, and before the directories
/// that contain them: deleting a directory tree that still contains a junction fails on Windows, and
/// following one would delete what it points at.
/// </para>
/// <para>
/// Nothing here changes security configuration, requires elevation or needs Developer Mode.
/// </para>
/// </remarks>
internal sealed class TemporarySourceTree : IDisposable
{
    private readonly List<string> _externalDirectories = [];

    internal TemporarySourceTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "CopyPreflight.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
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

    /// <summary>Creates a directory outside the scanned tree and returns its absolute path.</summary>
    internal string CreateExternalDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CopyPreflight.Tests-external-" + Guid.NewGuid().ToString("N"));
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
