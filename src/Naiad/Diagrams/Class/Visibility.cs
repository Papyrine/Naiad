namespace Naiad.Diagrams.Class;

public enum Visibility
{
    Public,      // +
    Private,     // -
    Protected,   // #
    PackagePrivate, // ~

    // No marker written, as on an enumeration value. Mermaid shows such a member without a prefix.
    None
}
