namespace Naiad.Diagrams.Packet;

public class PacketRenderer : IDiagramRenderer<PacketModel>
{
    // Mermaid's bit width: wide enough for a three-letter flag name in a one-bit field.
    const double bitWidth = 32;
    const double labelPadding = 2;
    const double minLabelFontSize = 7;
    const double rowHeight = 40;
    const double bitNumberHeight = 20;
    const double titleHeight = 40;

    static readonly string[] fieldColors =
    [
        "#E3F2FD", "#E8F5E9", "#FFF3E0", "#F3E5F5",
        "#FCE4EC", "#E0F7FA", "#FFF8E1", "#F1F8E9"
    ];

    public SvgDocument Render(PacketModel model, RenderOptions options)
    {
        if (model.Fields.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty packet",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // `packet.bitsPerRow` sets the width of a row; `packet.bitOrder: descending` mirrors every row, so
        // it reads from its highest bit down to its lowest as registers are drawn; `packet.showBits: false`
        // leaves out the bit numbers.
        var bitsPerRow = model.BitsPerRow;
        if (model.Config.GetNumber("packet", "bitsPerRow") is double configured and >= 1)
        {
            bitsPerRow = (int) configured;
        }

        var descending = model.Config.GetString("packet", "bitOrder") == "descending";
        var showBits = model.Config.GetBool("packet", "showBits") ?? true;

        // The column a run of bits starting at a column of the row is drawn from.
        int Column(int firstColumn, int bits)
        {
            if (descending)
            {
                return bitsPerRow - firstColumn - bits;
            }

            return firstColumn;
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var numbersHeight = 0.0;
        if (showBits)
        {
            numbersHeight = bitNumberHeight;
        }

        // Calculate total rows needed
        var maxBit = model.Fields.Max(f => f.EndBit);
        var totalRows = maxBit / bitsPerRow + 1;

        var width = bitsPerRow * bitWidth + options.Padding * 2;
        var height = totalRows * rowHeight + numbersHeight + options.Padding * 2 + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                width / 2,
                options.Padding + titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 4,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var baseY = options.Padding + titleOffset;

        // Draw bit numbers
        for (var i = 0; showBits && i < bitsPerRow; i++)
        {
            var x = options.Padding + Column(i, 1) * bitWidth + bitWidth / 2;
            builder.AddText(
                x,
                baseY + numbersHeight / 2,
                i.ToString(CultureInfo.InvariantCulture),
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 4,
                fontFamily: options.FontFamily,
                fill: "#666");
        }

        // Draw fields
        var colorIndex = 0;
        foreach (var field in model.Fields)
        {
            var startRow = field.StartBit / bitsPerRow;
            var endRow = field.EndBit / bitsPerRow;
            var color = fieldColors[colorIndex % fieldColors.Length];

            if (startRow == endRow)
            {
                // Field fits in one row
                var startCol = field.StartBit % bitsPerRow;
                var fieldWidth = field.Width;

                var x = options.Padding + Column(startCol, fieldWidth) * bitWidth;
                var y = baseY + numbersHeight + startRow * rowHeight;

                builder.AddRect(
                    x,
                    y,
                    fieldWidth * bitWidth,
                    rowHeight,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);

                AddLabel(builder, field.Label, x, y, fieldWidth, options);
            }
            else
            {
                // Field spans multiple rows
                for (var row = startRow; row <= endRow; row++)
                {
                    int colStart, colEnd;

                    if (row == startRow)
                    {
                        colStart = field.StartBit % bitsPerRow;
                        colEnd = bitsPerRow - 1;
                    }
                    else if (row == endRow)
                    {
                        colStart = 0;
                        colEnd = field.EndBit % bitsPerRow;
                    }
                    else
                    {
                        colStart = 0;
                        colEnd = bitsPerRow - 1;
                    }

                    var fieldWidth = colEnd - colStart + 1;
                    var x = options.Padding + Column(colStart, fieldWidth) * bitWidth;
                    var y = baseY + numbersHeight + row * rowHeight;

                    builder.AddRect(
                        x,
                        y,
                        fieldWidth * bitWidth,
                        rowHeight,
                        fill: color,
                        stroke: "#333",
                        strokeWidth: 1);

                    // Only show label in first row
                    if (row == startRow)
                    {
                        AddLabel(builder, field.Label, x, y, fieldWidth, options);
                    }
                }
            }

            colorIndex++;
        }

        return builder.Build();
    }

    // A label is shrunk to fit its field, so the names of adjacent one-bit flags do not run into each other.
    static void AddLabel(SvgBuilder builder, string label, double x, double y, int bits, RenderOptions options)
    {
        var available = bits * bitWidth - labelPadding * 2;
        var fontSize = (double) (options.FontSize - 2);
        var width = MeasureText(label, fontSize);
        if (width > available)
        {
            fontSize = Math.Max(minLabelFontSize, fontSize * available / width);
        }

        builder.AddText(
            x + bits * bitWidth / 2,
            y + rowHeight / 2,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: "#333");
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;
}
