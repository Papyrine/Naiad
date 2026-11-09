namespace Naiad.Diagrams.Block;

/// <summary>
/// The ways a block arrow (<c>id&lt;["label"]&gt;(right)</c>) points. <c>x</c> is both left and right, <c>y</c>
/// both up and down, and several may be listed: <c>(x, down)</c>.
/// </summary>
[Flags]
public enum BlockArrowDirection
{
    None = 0,
    Right = 1,
    Left = 2,
    Up = 4,
    Down = 8
}
