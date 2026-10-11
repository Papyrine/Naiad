namespace Naiad.Diagrams.Kanban;

public class KanbanRenderer : IDiagramRenderer<KanbanModel>
{
    const double columnWidth = 180;
    const double columnPadding = 15;
    const double taskHeight = 40;
    const double taskPadding = 8;
    const double taskWidth = columnWidth - taskPadding * 2;
    const double taskTextInset = 12;
    const double taskLineHeight = 16;
    const double headerHeight = 40;
    const double titleHeight = 40;

    static string[] columnColors =
    [
        "#E3F2FD", "#E8F5E9", "#FFF3E0", "#F3E5F5",
        "#FCE4EC", "#E0F7FA", "#FFF8E1", "#F1F8E9"
    ];

    static string[] taskColors =
    [
        "#BBDEFB", "#C8E6C9", "#FFE0B2", "#E1BEE7",
        "#F8BBD0", "#B2EBF2", "#FFECB3", "#DCEDC8"
    ];

    public SvgDocument Render(KanbanModel model, RenderOptions options)
    {
        if (model.Columns.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty board",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        // A card is as tall as its wrapped name and details need, so every column is laid out first and
        // the board takes the height of the tallest.
        var cards = model.Columns
            .Select(column => column.Tasks.Select(task => BuildCard(task, options)).ToList())
            .ToList();
        var headers = model.Columns
            .Select(column => WrapText(column.Name, MaxChars(columnWidth - taskPadding * 2, options.FontSize, bold: true)))
            .ToList();
        var headerBand = Math.Max(headerHeight, headers.Max(_ => _.Count) * (taskLineHeight + 2) + 16);
        var tallest = cards.Max(column => column.Sum(card => card.Height + taskPadding));
        var contentHeight = headerBand + tallest + columnPadding * 2;

        var width = model.Columns.Count * (columnWidth + columnPadding) + options.Padding * 2;
        var height = contentHeight + options.Padding * 2 + titleOffset;

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

        // Draw columns
        for (var i = 0; i < model.Columns.Count; i++)
        {
            var x = options.Padding + i * (columnWidth + columnPadding);
            var y = options.Padding + titleOffset;

            var columnColor = columnColors[i % columnColors.Length];
            var taskColor = taskColors[i % taskColors.Length];

            // Column background
            builder.AddRect(
                x,
                y,
                columnWidth,
                contentHeight,
                rx: 8,
                fill: columnColor,
                stroke: "#ccc",
                strokeWidth: 1);

            // Column header
            builder.AddRect(
                x,
                y,
                columnWidth,
                headerBand,
                rx: 8,
                fill: columnColor,
                stroke: "none");
            builder.AddRect(
                x,
                y + headerBand - 8,
                columnWidth,
                8,
                fill: columnColor,
                stroke: "none");

            DrawLines(
                builder,
                headers[i],
                x + columnWidth / 2,
                y + headerBand / 2,
                taskLineHeight + 2,
                "middle",
                options.FontSize,
                "bold",
                "#333",
                options);

            // Tasks
            var taskX = x + taskPadding;
            var taskY = y + headerBand + columnPadding;
            foreach (var card in cards[i])
            {
                DrawCard(builder, card, taskX, taskY, taskColor, options);
                taskY += card.Height + taskPadding;
            }
        }

        return builder.Build();
    }

    static void DrawCard(SvgBuilder builder, Card card, double x, double y, string columnTaskColor, RenderOptions options)
    {
        // Task card
        builder.AddRect(
            x,
            y,
            taskWidth,
            card.Height,
            rx: 4,
            fill: "#fff",
            stroke: "#ddd",
            strokeWidth: 1);

        // Color bar on left: the task's priority when it has one, otherwise its column's colour.
        builder.AddRect(
            x,
            y,
            4,
            card.Height,
            rx: 2,
            fill: PriorityColor(card.Task.Priority) ?? columnTaskColor,
            stroke: "none");

        // Task text, centred in the room above the details line.
        var detailsHeight = card.Details is null ? 0 : taskLineHeight;
        DrawLines(
            builder,
            card.Lines,
            x + taskTextInset,
            y + (card.Height - detailsHeight) / 2,
            taskLineHeight,
            "start",
            options.FontSize - 2,
            null,
            "#333",
            options);

        if (card.Details is not null)
        {
            builder.AddText(
                x + taskTextInset,
                y + card.Height - taskLineHeight / 2 - 4,
                card.Details,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize - 4,
                fontFamily: options.FontFamily,
                fill: "#777");
        }
    }

    // Draws lines of text centred vertically on centerY.
    static void DrawLines(
        SvgBuilder builder,
        List<string> lines,
        double x,
        double centerY,
        double lineHeight,
        string anchor,
        double fontSize,
        string? fontWeight,
        string fill,
        RenderOptions options)
    {
        var lineY = centerY - (lines.Count - 1) * lineHeight / 2;
        foreach (var line in lines)
        {
            builder.AddText(
                x,
                lineY,
                line,
                anchor: anchor,
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fontWeight: fontWeight,
                fill: fill);
            lineY += lineHeight;
        }
    }

    static Card BuildCard(KanbanTask task, RenderOptions options)
    {
        var lines = WrapText(task.Name, MaxChars(taskWidth - taskTextInset - 6, options.FontSize - 2, bold: false));

        // The ticket and the assignee share one line under the name.
        string? details = null;
        if (task.Ticket is not null || task.Assigned is not null)
        {
            details = string.Join("  ", new[] { task.Ticket, task.Assigned }.Where(_ => _ is not null));
        }

        var height = lines.Count * taskLineHeight + 16;
        if (details is not null)
        {
            height += taskLineHeight;
        }

        return new(task, lines, details, Math.Max(taskHeight, height));
    }

    // Mermaid's priorities, most urgent first. Anything else leaves the column's colour on the card.
    static string? PriorityColor(string? priority) =>
        priority?.Trim().ToLowerInvariant() switch
        {
            "very high" => "#D32F2F",
            "high" => "#F57C00",
            "low" => "#1976D2",
            "very low" => "#90CAF9",
            _ => null
        };

    static int MaxChars(double width, double fontSize, bool bold)
    {
        var factor = bold ? 0.62 : 0.55;
        return Math.Max(4, (int) (width / (fontSize * factor)));
    }

    // Breaks text into lines of at most maxChars, at spaces where it can and inside a word where it must.
    static List<string> WrapText(string text, int maxChars)
    {
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = word;
            while (piece.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                lines.Add(piece[..maxChars]);
                piece = piece[maxChars..];
            }

            if (current.Length > 0 &&
                current.Length + 1 + piece.Length > maxChars)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        if (lines.Count == 0)
        {
            lines.Add("");
        }

        return lines;
    }

    sealed record Card(KanbanTask Task, List<string> Lines, string? Details, double Height);
}
