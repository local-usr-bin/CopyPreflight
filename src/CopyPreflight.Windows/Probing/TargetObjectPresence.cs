namespace CopyPreflight.Windows;

/// <summary>
/// What kind of object was found at one mapped destination location.
/// </summary>
/// <remarks>
/// <para>
/// The three values are mutually exclusive and cover everything this milestone observes. There is no
/// "unknown" member: an object whose kind could not be established is not described by this type at
/// all, because the observation that would carry it says the check did not answer instead.
/// </para>
/// <para>
/// <see cref="ReparsePoint"/> is deliberately a member of this scale rather than a separate flag.
/// Windows reports the reparse attribute together with the directory attribute for a junction or a
/// mount point, and Microsoft's attribute documentation gives the reparse attribute its own meaning
/// that is judged first. Modelling it as a third presence keeps that precedence in the type instead
/// of leaving every consumer to remember the order of two checks — and it means a reparse point can
/// never be mistaken for an ordinary directory.
/// </para>
/// <para>
/// It says nothing about <i>which</i> reparse point it is. Classifying it as a symbolic link, a
/// junction, a mount point or something else, and resolving what it points at, are deliberately out
/// of scope.
/// </para>
/// </remarks>
public enum TargetObjectPresence
{
    /// <summary>A regular file: Windows reported neither the directory nor the reparse attribute.</summary>
    File = 0,

    /// <summary>A regular directory: Windows reported the directory attribute, without the reparse attribute.</summary>
    Directory = 1,

    /// <summary>
    /// An object carrying the reparse point attribute, whether or not it also reports the directory
    /// attribute. It is not classified beyond that.
    /// </summary>
    ReparsePoint = 2,
}
