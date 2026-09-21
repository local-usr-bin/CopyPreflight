using System.IO;

namespace CopyPreflight.Core;

/// <summary>
/// The minimal file-system reads the scanner needs from the operating system.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately tiny internal seam, not a virtual file system. It exists so the two
/// behaviours that cannot be produced with ordinary file-system means — an enumeration that fails
/// on an accessible-looking directory, and a reparse point shaped like a file — can be exercised
/// deterministically by tests. Producing a real failing enumeration would require changing
/// directory ACLs, and this repository does not change security configuration to run tests.
/// </para>
/// <para>
/// It is <b>not</b> part of the product API, and it is not a stepping stone towards a pluggable
/// file-system abstraction. It covers exactly one operation: listing the children of one
/// directory.
/// </para>
/// </remarks>
internal interface ISourceDirectoryReader
{
    /// <summary>Lists the immediate children of one directory.</summary>
    /// <param name="absoluteDirectoryPath">Absolute path of an existing directory to list.</param>
    /// <returns>
    /// One observation per child. The sequence is enumerated lazily, so a failure of the underlying
    /// listing surfaces while the sequence is being iterated rather than when it is requested.
    /// </returns>
    IEnumerable<ObservedDirectoryChild> ReadChildren(string absoluteDirectoryPath);
}

/// <summary>
/// What the directory listing reported about one child object.
/// </summary>
/// <param name="Name">The child's name, exactly as the file system reported it.</param>
/// <param name="Attributes">
/// The child's attributes, or <see langword="null"/> when they could not be read. A null value
/// means the object exists but could not be classified, so no entry can be produced for it.
/// </param>
/// <param name="LengthInBytes">
/// The logical length the file system reports for the object, when it is shaped like a file. It is
/// the raw reported value: deciding whether it may be attributed to the object is the scanner's
/// business, not this seam's.
/// </param>
/// <param name="MetadataFailure">
/// The failure that prevented the attributes from being read, when there was one. It is passed
/// along so the scanner can describe it, and it is not retained beyond that.
/// </param>
internal readonly record struct ObservedDirectoryChild(
    string Name,
    FileAttributes? Attributes,
    long? LengthInBytes,
    Exception? MetadataFailure);
