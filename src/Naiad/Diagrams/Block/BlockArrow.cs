namespace Naiad.Diagrams.Block;

/// <summary>
/// The outline of a block arrow, after Mermaid's: a bar with a head at each end it points to, or for two
/// directions at right angles, a wedge. Points are relative to the bottom-left corner of a box
/// <c>width</c> by <c>height</c>, with y growing upwards as negative numbers. <c>padding</c> is how far the
/// heads stand out from the bar.
/// </summary>
static class BlockArrow
{
    public static (double X, double Y)[] Points(BlockArrowDirection directions, double width, double height, double padding)
    {
        var right = directions.HasFlag(BlockArrowDirection.Right);
        var left = directions.HasFlag(BlockArrowDirection.Left);
        var up = directions.HasFlag(BlockArrowDirection.Up);
        var down = directions.HasFlag(BlockArrowDirection.Down);
        var midpoint = height / 2;

        if (right && left && up && down)
        {
            return
            [
                // Bottom
                (0, 0),
                (midpoint, 0),
                (width / 2, 2 * padding),
                (width - midpoint, 0),
                (width, 0),

                // Right
                (width, -height / 3),
                (width + 2 * padding, -height / 2),
                (width, -2 * height / 3),
                (width, -height),

                // Top
                (width - midpoint, -height),
                (width / 2, -height - 2 * padding),
                (midpoint, -height),

                // Left
                (0, -height),
                (0, -2 * height / 3),
                (-2 * padding, -height / 2),
                (0, -height / 3)
            ];
        }

        if (right && left && up)
        {
            return
            [
                (midpoint, 0),
                (width - midpoint, 0),
                (width, -height / 2),
                (width - midpoint, -height),
                (midpoint, -height),
                (0, -height / 2)
            ];
        }

        if (right && left && down)
        {
            return
            [
                (0, 0),
                (midpoint, -height),
                (width - midpoint, -height),
                (width, 0)
            ];
        }

        if (right && up && down)
        {
            return
            [
                (0, 0),
                (width, -midpoint),
                (width, -height + midpoint),
                (0, -height)
            ];
        }

        if (left && up && down)
        {
            return
            [
                (width, 0),
                (0, -midpoint),
                (0, -height + midpoint),
                (width, -height)
            ];
        }

        if (right && left)
        {
            return
            [
                (midpoint, 0),
                (midpoint, -padding),
                (width - midpoint, -padding),
                (width - midpoint, 0),
                (width, -height / 2),
                (width - midpoint, -height),
                (width - midpoint, -height + padding),
                (midpoint, -height + padding),
                (midpoint, -height),
                (0, -height / 2)
            ];
        }

        if (up && down)
        {
            return
            [
                (width / 2, 0),
                (0, -padding),
                (midpoint, -padding),
                (midpoint, -height + padding),
                (0, -height + padding),
                (width / 2, -height),
                (width, -height + padding),
                (width - midpoint, -height + padding),
                (width - midpoint, -padding),
                (width, -padding)
            ];
        }

        if (right && up)
        {
            return [(0, 0), (width, -midpoint), (0, -height)];
        }

        if (right && down)
        {
            return [(0, 0), (width, 0), (0, -height)];
        }

        if (left && up)
        {
            return [(width, 0), (0, -midpoint), (width, -height)];
        }

        if (left && down)
        {
            return [(width, 0), (0, 0), (width, -height)];
        }

        if (right)
        {
            return
            [
                (midpoint, -padding),
                (width - midpoint, -padding),
                (width - midpoint, 0),
                (width, -height / 2),
                (width - midpoint, -height),
                (width - midpoint, -height + padding),
                (midpoint, -height + padding)
            ];
        }

        if (left)
        {
            return
            [
                (midpoint, 0),
                (midpoint, -padding),
                (width - midpoint, -padding),
                (width - midpoint, -height + padding),
                (midpoint, -height + padding),
                (midpoint, -height),
                (0, -height / 2)
            ];
        }

        if (up)
        {
            return
            [
                (midpoint, -padding),
                (midpoint, -height + padding),
                (0, -height + padding),
                (width / 2, -height),
                (width, -height + padding),
                (width - midpoint, -height + padding),
                (width - midpoint, -padding)
            ];
        }

        if (down)
        {
            return
            [
                (width / 2, 0),
                (0, -padding),
                (midpoint, -padding),
                (midpoint, -height + padding),
                (width - midpoint, -height + padding),
                (width - midpoint, -padding),
                (width, -padding)
            ];
        }

        // No direction: a plain box.
        return [(0, 0), (width, 0), (width, -height), (0, -height)];
    }
}
