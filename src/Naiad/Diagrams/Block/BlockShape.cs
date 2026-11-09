namespace Naiad.Diagrams.Block;

public enum BlockShape
{
    Rectangle,          // ["label"], or no brackets at all
    Rounded,            // ("label")
    Stadium,            // (["label"])
    Circle,             // (("label"))
    Diamond,            // {"label"}
    Hexagon,            // {{"label"}}
    Subroutine,         // [["label"]]
    Cylinder,           // [("label")]
    DoubleCircle,       // ((("label")))
    Asymmetric,         // >"label"]
    LeanRight,          // [/"label"/]
    LeanLeft,           // [\"label"\]
    Trapezoid,          // [/"label"\]
    InvertedTrapezoid,  // [\"label"/]
    BlockArrow          // <["label"]>(right)
}
