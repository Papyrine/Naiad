namespace Naiad.Diagrams.TreeView;

public class TreeViewNode
{
    /// <summary>The label as written, without the trailing <c>/</c> that marks a directory.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the label ended in <c>/</c>. Directories are drawn in bold.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>The class given with <c>:::name</c>. The built-in <c>highlight</c> class draws a band behind the row.</summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// The icon given with <c>icon(...)</c>: an icon pack reference (<c>pack:name</c>), one of the built-in
    /// <c>file</c> and <c>folder</c> icons, or <c>none</c> (also what an empty <c>icon()</c> gives) for no
    /// icon. Null when the node names no icon, which draws none.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>The text after <c>##</c>, drawn in italics in a column to the right of the labels.</summary>
    public string? Description { get; set; }

    public List<TreeViewNode> Children { get; } = [];

    public bool IsHighlighted =>
        CssClass is not null &&
        CssClass.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("highlight");
}
