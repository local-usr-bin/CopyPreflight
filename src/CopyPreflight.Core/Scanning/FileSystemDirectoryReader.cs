using System.IO;

namespace CopyPreflight.Core;

/// <summary>
/// The physical implementation of the scanner's directory-listing seam.
/// </summary>
/// <remarks>
/// <para>
/// Children come from a single directory listing. Attributes and lengths are therefore taken from
/// the data the listing already returned, and reading them costs no additional file-system calls.
/// </para>
/// <para>
/// The listing is enumerated lazily, and it never follows a reparse point: a link is reported as
/// the link itself, with the reparse point attribute set, not as whatever it points at.
/// </para>
/// </remarks>
internal sealed class FileSystemDirectoryReader : ISourceDirectoryReader
{
    /// <inheritdoc />
    public IEnumerable<ObservedDirectoryChild> ReadChildren(string absoluteDirectoryPath)
    {
        foreach (var info in new DirectoryInfo(absoluteDirectoryPath).EnumerateFileSystemInfos())
        {
            // The name comes straight from the listing and needs no further file-system access.
            var name = info.Name;
            FileAttributes? attributes = null;
            long? lengthInBytes = null;
            Exception? metadataFailure = null;

            try
            {
                attributes = info.Attributes;

                // A directory has no file length, and none is invented for it.
                if ((attributes.Value & FileAttributes.Directory) == 0 && info is FileInfo fileInfo)
                {
                    lengthInBytes = fileInfo.Length;
                }
            }
            catch (Exception exception) when (SourceScanFailurePolicy.IsRecoverableFileSystemFailure(exception))
            {
                attributes = null;
                lengthInBytes = null;
                metadataFailure = exception;
            }

            yield return new ObservedDirectoryChild(name, attributes, lengthInBytes, metadataFailure);
        }
    }
}
