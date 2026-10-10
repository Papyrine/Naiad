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
    Text
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
