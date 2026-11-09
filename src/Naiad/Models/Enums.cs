namespace Naiad;

public enum Direction
{
    TopToBottom,
    BottomToTop,
    LeftToRight,
    RightToLeft
}

public enum NodeShape
{
    Rectangle,
    RoundedRectangle,
    Stadium,
    Subroutine,
    Cylinder,
    Circle,
    DoubleCircle,
    Asymmetric,
    Diamond,
    Hexagon,
    Parallelogram,
    ParallelogramAlt,
    Trapezoid,
    TrapezoidAlt,
    Lean,
    LeanAlt,
    Odd,
    Card,
    LinedDocument,
    LinedCylinder,
    Document,
    TriangleUp,
    TriangleDown,
    Hourglass,
    Lightning,
    TaggedRect,
    TaggedDocument,
    MultiRect,
    MultiDocument,
    MultiProcess,
    NotchedRectangle,
    CurlyBraces,
    CurlyBracket,
    BowTie,
    CrossedCircle,
    HalfRoundedRectangle,
    TiltedCylinder,
    DividedRectangle,
    WindowPane,
    Text,

    /// <summary>Mermaid's <c>lin-rect</c>: a process box with a narrow band down its left side.</summary>
    LinedRectangle,

    /// <summary>Mermaid's <c>sm-circ</c> (start): a small unlabelled circle.</summary>
    SmallCircle,

    /// <summary>Mermaid's <c>fr-circ</c> (stop): a small unlabelled circle with a dot inside.</summary>
    FramedCircle,

    /// <summary>Mermaid's <c>fork</c> / <c>join</c>: an unlabelled bar across the flow.</summary>
    ForkJoin,

    /// <summary>Mermaid's <c>brace-r</c>: a comment with a curly brace on its right.</summary>
    CurlyBracketRight,

    /// <summary>Mermaid's <c>curv-trap</c> (display).</summary>
    CurvedTrapezoid,

    /// <summary>Mermaid's <c>f-circ</c> (junction): a small filled circle.</summary>
    FilledCircle,

    /// <summary>Mermaid's <c>notch-pent</c> (loop limit).</summary>
    NotchedPentagon,

    /// <summary>Mermaid's <c>sl-rect</c> (manual input).</summary>
    SlopedRectangle,

    /// <summary>Mermaid's <c>flag</c> (paper tape).</summary>
    Flag,

    /// <summary>Mermaid's <c>datastore</c>: a data flow diagram store, ruled above and below.</summary>
    Datastore,

    /// <summary>Mermaid's <c>folder</c>.</summary>
    Folder,

    /// <summary>Mermaid's <c>bucket</c>.</summary>
    Bucket,

    /// <summary>Mermaid's <c>console</c>: a terminal window.</summary>
    Console,

    /// <summary>Mermaid's <c>browser</c>: a browser window.</summary>
    Browser,

    /// <summary>Mermaid's <c>person</c>: a head above a rounded body.</summary>
    Person,

    /// <summary>Mermaid's <c>bang</c>.</summary>
    Bang,

    /// <summary>Mermaid's <c>cloud</c>.</summary>
    Cloud
}

public enum EdgeType
{
    Arrow,
    Open,
    DottedArrow,
    Dotted,
    ThickArrow,
    Thick,
    CircleEnd,
    CrossEnd,
    BiDirectional,
    BiDirectionalCircle,
    BiDirectionalCross,

    // `~~~`: takes part in layout like any other edge, but is not drawn.
    Invisible
}

public enum EdgeStyle
{
    Solid,
    Dotted,
    Thick
}
