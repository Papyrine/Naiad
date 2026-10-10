namespace Naiad.Diagrams.Sequence;

public class SequenceRenderer : IDiagramRenderer<SequenceModel>
{
    const double participantWidth = 100;
    const double participantHeight = 40;
    const double participantSpacing = 150;
    const double messageSpacing = 50;
    const double activationWidth = 10;
    const double noteMinWidth = 120;
    const double singleLineNoteHeight = 40;
    const double notePadding = 10;
    const double noteGap = 10;
    const double selfMessageLoopWidth = 40;
    const double actorHeadRadius = 9;
    const double actorArmSpread = 10;
    const double actorLegSpread = 8;

    // An actor's name is drawn under the figure rather than inside a box, so diagrams containing one
    // need a taller header band to keep the label clear of the lifelines.
    const double actorLabelHeight = 24;

    public SvgDocument Render(SequenceModel model, RenderOptions options)
    {
        var (participantPositions, width) = CalculateLayout(model, options);
        var (height, elementYPositions) = CalculateHeight(model, options);
        var headerHeight = HeaderHeight(model, options);

        var builder = new SvgBuilder();
        builder.Size(width, height);
        builder.AddArrowMarker();
        builder.AddArrowMarker("arrowhead-dotted");
        builder.AddCrossMarker();

        // Add title if present
        var titleOffset = 0.0;
        if (!string.IsNullOrEmpty(model.Title))
        {
            titleOffset = 30;
            builder.AddText(
                width / 2,
                20,
                model.Title,
                anchor: "middle",
                fontSize: 16,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var startY = options.Padding + titleOffset;

        // Draw participants (top)
        DrawParticipants(builder, model, participantPositions, startY, options);

        // Draw lifelines
        var lifelineStartY = startY + headerHeight;
        var lifelineEndY = height - options.Padding - headerHeight;
        DrawLifelines(builder, model, participantPositions, lifelineStartY, lifelineEndY);

        // Activation bars are backdrop for the conversation: they cover the lifeline but must sit under
        // the message arrows, labels and notes that cross them.
        var activations = CalculateActivations(model, elementYPositions);
        DrawActivations(builder, activations, participantPositions);

        // Draw elements (messages, notes)
        DrawElements(builder, model, participantPositions, elementYPositions, options);

        // Draw participants (bottom) - optional, mimics Mermaid behavior
        DrawParticipants(builder, model, participantPositions, lifelineEndY, options);

        return builder.Build();
    }

    // The header band is as tall as its tallest participant: a box grows with the lines of its name, and
    // an actor's name hangs below the figure.
    static double HeaderHeight(SequenceModel model, RenderOptions options)
    {
        var height = BoxHeight(model, options);
        foreach (var participant in model.Participants)
        {
            if (participant.Type == ParticipantType.Actor)
            {
                height = Math.Max(
                    height,
                    participantHeight + actorLabelHeight + ExtraLines(participant.DisplayName, options));
            }
        }

        return height;
    }

    // Every participant box shares one height, so a name broken over several lines grows all of them.
    static double BoxHeight(SequenceModel model, RenderOptions options)
    {
        var height = participantHeight;
        foreach (var participant in model.Participants)
        {
            if (participant.Type != ParticipantType.Actor)
            {
                height = Math.Max(height, participantHeight + ExtraLines(participant.DisplayName, options));
            }
        }

        return height;
    }

    // The height a label's `<br/>` breaks add beyond its first line.
    static double ExtraLines(string? text, RenderOptions options) =>
        (LabelLines.Count(text) - 1) * options.FontSize * SvgBuilder.LineHeightFactor;

    /// <summary>
    /// Places the participants and sizes the canvas around everything that hangs off them — notes beside
    /// the outer lifelines and self-message loops. A note to the left of the first participant shifts the
    /// whole diagram right instead of being clipped at the canvas edge.
    /// </summary>
    static (Dictionary<string, double> positions, double width) CalculateLayout(
        SequenceModel model, RenderOptions options)
    {
        var positions = new Dictionary<string, double>();
        var x = options.Padding + participantWidth / 2;

        foreach (var participant in model.Participants)
        {
            positions[participant.Id] = x;
            x += participantSpacing;
        }

        var minX = options.Padding;
        var maxX = options.Padding + participantWidth;
        if (model.Participants.Count > 0)
        {
            maxX = positions[model.Participants[^1].Id] + participantWidth / 2;
        }

        foreach (var element in model.Elements)
        {
            switch (element)
            {
                case Note note when positions.ContainsKey(note.ParticipantId):
                    var (noteX, noteWidth) = NoteGeometry(note, positions, options);
                    minX = Math.Min(minX, noteX);
                    maxX = Math.Max(maxX, noteX + noteWidth);
                    break;

                case Message {Text: not null} msg
                    when msg.FromId == msg.ToId && positions.TryGetValue(msg.FromId, out var selfX):
                    maxX = Math.Max(
                        maxX,
                        selfX + selfMessageLoopWidth + 5 + MeasureText(msg.Text, options.FontSize));
                    break;
            }
        }

        var shift = Math.Max(0, options.Padding - minX);
        if (shift > 0)
        {
            foreach (var id in positions.Keys.ToList())
            {
                positions[id] += shift;
            }
        }

        return (positions, maxX + shift + options.Padding);
    }

    static (double height, Dictionary<int, double> elementYPositions) CalculateHeight(
        SequenceModel model, RenderOptions options)
    {
        var elementYPositions = new Dictionary<int, double>();
        var headerHeight = HeaderHeight(model, options);
        var y = options.Padding + headerHeight + messageSpacing;
        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : 30;

        for (var i = 0; i < model.Elements.Count; i++)
        {
            var element = model.Elements[i];
            y += LeadIn(element, options);
            elementYPositions[i] = y + titleOffset;
            y += GetElementHeight(element, options);
        }

        var totalHeight = y + headerHeight + options.Padding + titleOffset;
        return (totalHeight, elementYPositions);
    }

    // Room needed above an element's own position. A message's label sits above its arrow and grows
    // upwards with each extra line; beside a self-message's loop it is centred, so half its growth is above.
    static double LeadIn(SequenceElement element, RenderOptions options)
    {
        if (element is not Message message)
        {
            return 0;
        }

        var extra = ExtraLines(message.Text, options);
        if (message.FromId == message.ToId)
        {
            return extra / 2;
        }

        return extra;
    }

    static double NoteHeight(Note note, RenderOptions options) =>
        singleLineNoteHeight + ExtraLines(note.Text, options);

    static double GetElementHeight(SequenceElement element, RenderOptions options) =>
        element switch
        {
            Message message when message.FromId == message.ToId =>
                messageSpacing + ExtraLines(message.Text, options) / 2,
            Message => messageSpacing,
            Note note => NoteHeight(note, options) + 10,
            Activation => 0, // Activations don't add height
            _ => messageSpacing
        };

    static void DrawParticipants(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions, double y, RenderOptions options)
    {
        var boxHeight = BoxHeight(model, options);
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];

            if (participant.Type == ParticipantType.Actor)
            {
                DrawActor(builder, x, y, participant.DisplayName, options);
            }
            else
            {
                DrawParticipantBox(builder, x, y, boxHeight, participant.DisplayName, options);
            }
        }
    }

    static void DrawParticipantBox(SvgBuilder builder, double cx, double y, double height,
        string text, RenderOptions options)
    {
        builder.AddRect(
            cx - participantWidth / 2,
            y,
            participantWidth,
            height,
            rx: 3,
            fill: "#ECECFF",
            stroke: "#9370DB",
            strokeWidth: 1);

        builder.AddTextLines(
            cx,
            y + height / 2,
            text,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily);
    }

    static void DrawActor(SvgBuilder builder, double cx, double y,
        string text, RenderOptions options)
    {
        // The whole figure is scaled to the participant band, so the legs reach the bottom of the band
        // and the name sits clear beneath it.
        var headY = y + actorHeadRadius;
        var bodyTop = headY + actorHeadRadius;
        var bodyBottom = y + participantHeight * 0.775;
        var armY = bodyTop + 4;
        var legBottom = y + participantHeight;

        // Head
        builder.AddCircle(
            cx,
            headY,
            actorHeadRadius,
            fill: "#ECECFF",
            stroke: "#9370DB",
            strokeWidth: 1);

        // Body
        builder.AddLine(
            cx,
            bodyTop,
            cx,
            bodyBottom,
            stroke: "#9370DB",
            strokeWidth: 1);

        // Arms
        builder.AddLine(
            cx - actorArmSpread,
            armY,
            cx + actorArmSpread,
            armY,
            stroke: "#9370DB",
            strokeWidth: 1);

        // Legs, splaying down and out from the base of the body
        builder.AddLine(
            cx,
            bodyBottom,
            cx - actorLegSpread,
            legBottom,
            stroke: "#9370DB",
            strokeWidth: 1);
        builder.AddLine(
            cx,
            bodyBottom,
            cx + actorLegSpread,
            legBottom,
            stroke: "#9370DB",
            strokeWidth: 1);

        // Label below
        builder.AddTextLines(
            cx,
            legBottom + 4,
            text,
            anchor: "middle",
            baseline: "top",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily);
    }

    static void DrawLifelines(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions, double startY, double endY)
    {
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];
            builder.AddLine(
                x,
                startY,
                x,
                endY,
                stroke: "#999",
                strokeWidth: 1,
                strokeDasharray: "5,5");
        }
    }

    /// <summary>
    /// Works out the span of every activation bar. Mermaid's <c>+</c> activates the message's target and
    /// its <c>-</c> deactivates the message's <em>sender</em>, so <c>Bob--&gt;&gt;-Alice</c> closes Bob's bar.
    /// </summary>
    static Dictionary<string, List<(double startY, double endY)>> CalculateActivations(
        SequenceModel model, Dictionary<int, double> yPositions)
    {
        var activations = new Dictionary<string, List<(double startY, double endY)>>();
        var activeLifelines = new Dictionary<string, double>();
        double? lastMessageY = null;

        for (var i = 0; i < model.Elements.Count; i++)
        {
            var y = yPositions[i];

            switch (model.Elements[i])
            {
                case Message msg:
                    lastMessageY = y;

                    if (msg.Activate)
                    {
                        activeLifelines[msg.ToId] = y;
                    }

                    if (msg.Deactivate)
                    {
                        Close(msg.FromId, y);
                    }

                    break;

                case Activation activation:
                    // A standalone `activate`/`deactivate` line takes no vertical space of its own, so it
                    // would otherwise inherit the *next* message's slot. It refers to the message above it.
                    var activationY = lastMessageY ?? y;

                    if (activation.IsActivate)
                    {
                        activeLifelines[activation.ParticipantId] = activationY;
                    }
                    else
                    {
                        Close(activation.ParticipantId, activationY);
                    }

                    break;
            }
        }

        // Close any remaining activations
        // ReSharper disable once UseIndexFromEndExpression
        var lastY = yPositions.Count > 0 ? yPositions[yPositions.Count - 1] + messageSpacing : 0;
        foreach (var participantId in activeLifelines.Keys.ToList())
        {
            Close(participantId, lastY);
        }

        return activations;

        void Close(string participantId, double endY)
        {
            if (!activeLifelines.TryGetValue(participantId, out var startY))
            {
                return;
            }

            if (!activations.TryGetValue(participantId, out var ranges))
            {
                ranges = [];
                activations[participantId] = ranges;
            }

            ranges.Add((startY, endY));
            activeLifelines.Remove(participantId);
        }
    }

    static void DrawElements(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions,
        Dictionary<int, double> yPositions,
        RenderOptions options)
    {
        var messageNumber = 0;

        for (var i = 0; i < model.Elements.Count; i++)
        {
            var y = yPositions[i];

            switch (model.Elements[i])
            {
                case Message msg:
                    messageNumber++;
                    DrawMessage(
                        builder,
                        msg,
                        positions,
                        y,
                        options,
                        model.AutoNumber ? messageNumber : null);
                    break;

                case Note note:
                    DrawNote(builder, note, positions, y, options);
                    break;
            }
        }
    }

    static void DrawMessage(SvgBuilder builder, Message msg,
        Dictionary<string, double> positions, double y,
        RenderOptions options, int? number)
    {
        var fromX = positions[msg.FromId];
        var toX = positions[msg.ToId];
        var isSelfMessage = msg.FromId == msg.ToId;

        var isDotted = msg.Type is MessageType.Dotted or MessageType.DottedArrow
            or MessageType.DottedOpen or MessageType.DottedCross or MessageType.DottedAsync;

        var markerEnd = msg.Type switch
        {
            MessageType.SolidCross or MessageType.DottedCross => "url(#cross)",
            MessageType.SolidOpen or MessageType.DottedOpen => null,
            _ => "url(#arrowhead)"
        };

        var dashArray = isDotted ? "5,5" : null;

        if (isSelfMessage)
        {
            // Self-referencing message - draw as a loop
            const int loopHeight = 30;
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M{fromX:0.##},{y:0.##} L{fromX + selfMessageLoopWidth:0.##},{y:0.##} L{fromX + selfMessageLoopWidth:0.##},{y + loopHeight:0.##} L{fromX:0.##},{y + loopHeight:0.##}");
            builder.AddPath(
                path,
                fill: "none",
                stroke: "#333",
                strokeWidth: 1,
                strokeDasharray: dashArray,
                markerEnd: markerEnd);

            // Text above
            if (!string.IsNullOrEmpty(msg.Text))
            {
                var labelText = number.HasValue ? $"{number}. {msg.Text}" : msg.Text;
                builder.AddTextLines(
                    fromX + selfMessageLoopWidth + 5,
                    y + loopHeight / 2,
                    labelText,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
            }
        }
        else
        {
            builder.AddLine(
                fromX,
                y,
                toX,
                y,
                stroke: "#333",
                strokeWidth: 1,
                strokeDasharray: dashArray);

            // Draw arrowhead manually since line doesn't support marker
            DrawArrowhead(builder, fromX, toX, y, msg.Type);

            // Text above the line
            if (!string.IsNullOrEmpty(msg.Text) || number.HasValue)
            {
                var labelText = number.HasValue && !string.IsNullOrEmpty(msg.Text)
                    ? $"{number}. {msg.Text}"
                    : number.HasValue
                        ? $"{number}."
                        : msg.Text!;

                var midX = (fromX + toX) / 2;
                builder.AddTextLines(
                    midX,
                    y - 8,
                    labelText,
                    anchor: "middle",
                    baseline: "bottom",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
            }
        }
    }

    static void DrawArrowhead(SvgBuilder builder, double fromX, double toX, double y, MessageType type)
    {
        var direction = Math.Sign(toX - fromX);
        const int arrowSize = 8;

        switch (type)
        {
            case MessageType.SolidArrow:
            case MessageType.DottedArrow:
            case MessageType.Solid:
            case MessageType.Dotted:
            case MessageType.SolidAsync:
            case MessageType.DottedAsync:
                // Filled arrowhead
                var backX = toX - direction * arrowSize;
                builder.AddPolygon([
                    new(toX, y),
                    new(backX, y - arrowSize / 2),
                    new(backX, y + arrowSize / 2)
                ], fill: "#333");
                break;

            case MessageType.SolidOpen:
            case MessageType.DottedOpen:
                // Open arrowhead (just lines)
                builder.AddLine(
                    toX - direction * arrowSize,
                    y - arrowSize / 2,
                    toX,
                    y,
                    stroke: "#333",
                    strokeWidth: 1);
                builder.AddLine(
                    toX - direction * arrowSize,
                    y + arrowSize / 2,
                    toX,
                    y,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case MessageType.SolidCross:
            case MessageType.DottedCross:
                // X mark
                builder.AddLine(
                    toX - arrowSize / 2,
                    y - arrowSize / 2,
                    toX + arrowSize / 2,
                    y + arrowSize / 2,
                    stroke: "#333",
                    strokeWidth: 2);
                builder.AddLine(
                    toX - arrowSize / 2,
                    y + arrowSize / 2,
                    toX + arrowSize / 2,
                    y - arrowSize / 2,
                    stroke: "#333",
                    strokeWidth: 2);
                break;
        }
    }

    /// <summary>
    /// Where a note sits and how wide it is. Notes grow to fit their text, and a note "over" two
    /// participants spans from one to the other rather than floating between them.
    /// </summary>
    static (double x, double width) NoteGeometry(Note note,
        Dictionary<string, double> positions, RenderOptions options)
    {
        var participantX = positions[note.ParticipantId];
        var textWidth = MeasureText(note.Text, options.FontSize) + notePadding * 2;

        switch (note.Position)
        {
            case NotePosition.RightOf:
                return (participantX + participantWidth / 2 + noteGap, Math.Max(noteMinWidth, textWidth));

            case NotePosition.LeftOf:
                var leftWidth = Math.Max(noteMinWidth, textWidth);
                return (participantX - participantWidth / 2 - noteGap - leftWidth, leftWidth);

            case NotePosition.Over:
            default:
                if (!string.IsNullOrEmpty(note.OverParticipantId2) &&
                    positions.TryGetValue(note.OverParticipantId2, out var participant2X))
                {
                    var left = Math.Min(participantX, participant2X) - participantWidth / 2;
                    var right = Math.Max(participantX, participant2X) + participantWidth / 2;
                    var span = Math.Max(right - left, textWidth);
                    return ((left + right) / 2 - span / 2, span);
                }

                var overWidth = Math.Max(noteMinWidth, textWidth);
                return (participantX - overWidth / 2, overWidth);
        }
    }

    static void DrawNote(SvgBuilder builder, Note note,
        Dictionary<string, double> positions, double y, RenderOptions options)
    {
        var (noteX, noteWidth) = NoteGeometry(note, positions, options);
        var noteHeight = NoteHeight(note, options);

        // Note box (folded corner style)
        const int foldSize = 8;
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"M{noteX:0.##},{y:0.##} L{noteX + noteWidth - foldSize:0.##},{y:0.##} L{noteX + noteWidth:0.##},{y + foldSize:0.##} L{noteX + noteWidth:0.##},{y + noteHeight:0.##} L{noteX:0.##},{y + noteHeight:0.##} Z");

        builder.AddPath(path, fill: "#FFFFCC", stroke: "#AAAA33", strokeWidth: 1);

        // Fold line
        builder.AddLine(
            noteX + noteWidth - foldSize,
            y,
            noteX + noteWidth - foldSize,
            y + foldSize,
            stroke: "#AAAA33",
            strokeWidth: 1);
        builder.AddLine(
            noteX + noteWidth - foldSize,
            y + foldSize,
            noteX + noteWidth,
            y + foldSize,
            stroke: "#AAAA33",
            strokeWidth: 1);

        // Note text
        builder.AddTextLines(
            noteX + noteWidth / 2,
            y + noteHeight / 2,
            note.Text,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily);
    }

    static void DrawActivations(
        SvgBuilder builder,
        Dictionary<string, List<(double startY, double endY)>> activations,
        Dictionary<string, double> positions)
    {
        foreach (var (participantId, ranges) in activations)
        {
            var x = positions[participantId];
            foreach (var (startY, endY) in ranges)
            {
                builder.AddRect(
                    x - activationWidth / 2,
                    startY,
                    activationWidth,
                    endY - startY,
                    fill: "#F4F4F4",
                    stroke: "#666",
                    strokeWidth: 1);
            }
        }
    }

    // A label with `<br/>` breaks is as wide as its longest line.
    static double MeasureText(string text, double fontSize) =>
        LabelLines.WidestLength(text) * fontSize * 0.55;
}
