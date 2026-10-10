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
    const double selfMessageLoopHeight = 30;

    // Block frames (loop, alt, opt, par, critical, break). The lead-in is how far above a message's arrow
    // the space for its label begins, so an edge placed there clears the label of what follows.
    const double frameLeadIn = 28;
    const double frameGap = 8;
    const double frameHeaderHeight = 30;
    const double frameDividerHeight = 26;
    const double frameTabHeight = 20;
    const double frameTabMinWidth = 50;
    const double frameNestingStep = 10;
    const string frameStroke = "#9370DB";

    // `box` groups: the gap between a box and the participants inside it, and the band its title sits in.
    const double boxMargin = 10;
    const double boxTitleBand = 26;
    const double actorHeadRadius = 9;
    const double actorArmSpread = 10;
    const double actorLegSpread = 8;

    // An actor's name is drawn under the figure rather than inside a box, so diagrams containing one
    // need a taller header band to keep the label clear of the lifelines.
    const double actorLabelHeight = 24;

    public SvgDocument Render(SequenceModel model, RenderOptions options)
    {
        var plan = PlanRows(model, options);
        var elements = plan.Elements;
        var elementYPositions = plan.Ys;
        var height = plan.Height;
        var (participantPositions, width) = CalculateLayout(model, plan, options);
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

        // Participant boxes run the height of the diagram behind everything, with their titles in a band
        // above the participants.
        var boxTop = options.Padding + titleOffset;
        var startY = boxTop + BoxBand(model);
        DrawBoxes(builder, model, participantPositions, boxTop, height - options.Padding / 2, options);

        // A created participant is drawn where its first message arrives, and a destroyed one stops at its
        // last, so neither gets the usual box at that end of the diagram.
        var createdAt = LifeEvents(model, elements, elementYPositions, _ => _.IsCreated, first: true);
        var destroyedAt = LifeEvents(model, elements, elementYPositions, _ => _.IsDestroyed, first: false);

        // `rect` backgrounds go down first, under the lifelines and everything else.
        DrawRectBackgrounds(builder, plan.Frames);

        // Draw participants (top)
        DrawParticipants(builder, model, participantPositions, startY, options, createdAt);

        // Draw lifelines
        var lifelineStartY = startY + headerHeight;
        var lifelineEndY = height - options.Padding - headerHeight;
        DrawLifelines(builder, model, participantPositions, lifelineStartY, lifelineEndY, createdAt, destroyedAt, options);

        // Activation bars are backdrop for the conversation: they cover the lifeline but must sit under
        // the message arrows, labels and notes that cross them.
        var activations = CalculateActivations(elements, elementYPositions);
        DrawActivations(builder, activations, participantPositions);

        // Block frames sit over the lifelines and under the messages and notes they enclose.
        DrawFrames(builder, plan.Frames, options);

        // Draw elements (messages, notes)
        DrawElements(builder, elements, model, participantPositions, elementYPositions, createdAt, options);
        DrawLifeEvents(builder, model, participantPositions, createdAt, destroyedAt, options);

        // Draw participants (bottom) - optional, mimics Mermaid behavior
        DrawParticipants(builder, model, participantPositions, lifelineEndY, options, destroyedAt);

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
        SequenceModel model, Plan plan, RenderOptions options)
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

        // A `box` reaches a little beyond the participants it holds.
        if (model.Boxes.Count > 0)
        {
            minX -= boxMargin;
            maxX += boxMargin;
        }

        foreach (var element in plan.Elements)
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

        foreach (var frame in plan.Frames)
        {
            SpanFrame(frame, plan.Elements, model, positions, options);
            minX = Math.Min(minX, frame.Left);
            maxX = Math.Max(maxX, frame.Right);
        }

        var shift = Math.Max(0, options.Padding - minX);
        if (shift > 0)
        {
            foreach (var id in positions.Keys.ToList())
            {
                positions[id] += shift;
            }

            foreach (var frame in plan.Frames)
            {
                frame.Left += shift;
                frame.Right += shift;
            }
        }

        return (positions, maxX + shift + options.Padding);
    }

    // The vertical plan of the diagram: the messages, notes and activations in drawing order with the
    // y each is drawn at, and the frames of the blocks around them.
    sealed class Plan
    {
        public List<SequenceElement> Elements { get; } = [];
        public Dictionary<int, double> Ys { get; } = [];
        public List<Frame> Frames { get; } = [];
        public double Height { get; set; }
    }

    // A block drawn around part of the conversation. Kind is the word shown in its corner tab (`loop`,
    // `alt`, ...); a `rect` has none and is just a filled background.
    sealed class Frame
    {
        public string? Kind { get; init; }
        public string? Title { get; init; }
        public string? Fill { get; init; }
        public List<(double Y, string? Label)> Dividers { get; } = [];
        public int FirstElement { get; init; }
        public int EndElement { get; set; }

        // How many levels of block are nested inside this one; each makes it a little wider.
        public int InnerDepth { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
    }

    // The sections of a block element: its body, then one per `else` / `and` / `option`. Null for
    // anything that is not a block.
    static (string? Kind, string? Fill, List<(string? Label, List<SequenceElement> Elements)> Sections)? AsBlock(
        SequenceElement element)
    {
        List<(string? Label, List<SequenceElement> Elements)> sections;
        switch (element)
        {
            case Loop loop:
                return ("loop", null, [(loop.Label, loop.Elements)]);
            case Opt opt:
                return ("opt", null, [(opt.Condition, opt.Elements)]);
            case Break breakBlock:
                return ("break", null, [(breakBlock.Label, breakBlock.Elements)]);
            case Rect rect:
                return (null, rect.Color ?? "#EDF2AE", [(null, rect.Elements)]);
            case Alt alt:
                sections = [(alt.Condition, alt.Elements)];
                sections.AddRange(alt.ElseBranches.Select(_ => (_.Condition, _.Elements)));
                return ("alt", null, sections);
            case Par par:
                sections = [(par.Label, par.Elements)];
                sections.AddRange(par.AndBranches.Select(_ => (_.Label, _.Elements)));
                return ("par", null, sections);
            case Critical critical:
                sections = [(critical.Label, critical.Elements)];
                sections.AddRange(critical.OptionBranches.Select(_ => (_.Label, _.Elements)));
                return ("critical", null, sections);
            default:
                return null;
        }
    }

    /// <summary>
    /// Walks the conversation top to bottom, giving every message, note and activation its y and every
    /// block a frame. A frame opens just above its first element with room for its header, gets a divider
    /// where each <c>else</c> / <c>and</c> / <c>option</c> begins, and closes under the lowest thing inside it.
    /// </summary>
    static Plan PlanRows(SequenceModel model, RenderOptions options)
    {
        var plan = new Plan();
        var headerHeight = HeaderHeight(model, options);
        var titleOffset = (string.IsNullOrEmpty(model.Title) ? 0 : 30) + BoxBand(model);
        var y = options.Padding + headerHeight + messageSpacing + titleOffset;

        // The bottom edge of the lowest thing placed so far, which a frame edge must stay clear of.
        var lastBottom = double.NegativeInfinity;

        Walk(model.Elements);
        plan.Height = y + headerHeight + options.Padding;
        return plan;

        // Returns how many levels of block the elements hold.
        int Walk(List<SequenceElement> elements)
        {
            var depth = 0;
            foreach (var element in elements)
            {
                if (AsBlock(element) is not (var kind, var fill, var sections))
                {
                    Place(element);
                    continue;
                }

                var frame = new Frame
                {
                    Kind = kind,
                    Fill = fill,
                    Title = sections[0].Label,
                    FirstElement = plan.Elements.Count,
                    Top = Math.Max(y - frameLeadIn, lastBottom + frameGap)
                };
                plan.Frames.Add(frame);

                // A `rect` has no header to make room for.
                var header = kind is null ? frameGap : frameHeaderHeight;
                y = frame.Top + frameLeadIn + header;
                lastBottom = frame.Top + header - frameGap;

                var inner = 0;
                for (var i = 0; i < sections.Count; i++)
                {
                    if (i > 0)
                    {
                        var dividerY = Math.Max(y - frameLeadIn, lastBottom + frameGap);
                        frame.Dividers.Add((dividerY, sections[i].Label));
                        y = dividerY + frameLeadIn + frameDividerHeight;
                        lastBottom = dividerY + frameDividerHeight - frameGap;
                    }

                    inner = Math.Max(inner, Walk(sections[i].Elements));
                }

                frame.EndElement = plan.Elements.Count;
                frame.InnerDepth = inner;
                frame.Bottom = lastBottom + frameGap;
                lastBottom = frame.Bottom;
                y = Math.Max(y, frame.Bottom + frameLeadIn);
                depth = Math.Max(depth, inner + 1);
            }

            return depth;
        }

        void Place(SequenceElement element)
        {
            y += LeadIn(element, options);

            // A message's label sits above its arrow. After a note - which is taller than the step a
            // message takes - the arrow has to drop far enough for the label to clear the note.
            if (element is Message labelled &&
                labelled.FromId != labelled.ToId &&
                !string.IsNullOrEmpty(labelled.Text))
            {
                y = Math.Max(y, lastBottom + frameLeadIn + ExtraLines(labelled.Text, options));
            }

            plan.Ys[plan.Elements.Count] = y;
            plan.Elements.Add(element);

            switch (element)
            {
                case Message message when message.FromId == message.ToId:
                    lastBottom = Math.Max(lastBottom, y + selfMessageLoopHeight + ExtraLines(message.Text, options) / 2 + 4);
                    break;
                case Message:
                    lastBottom = Math.Max(lastBottom, y + 12);
                    break;
                case Note note:
                    lastBottom = Math.Max(lastBottom, y + NoteHeight(note, options));
                    break;
            }

            y += GetElementHeight(element, options);
        }
    }

    // A frame reaches from the leftmost to the rightmost thing it encloses, a step wider for each level of
    // block nested inside it, and at least wide enough for its own header.
    static void SpanFrame(Frame frame, List<SequenceElement> elements, SequenceModel model,
        Dictionary<string, double> positions, RenderOptions options)
    {
        var left = double.PositiveInfinity;
        var right = double.NegativeInfinity;

        void Include(double from, double to)
        {
            left = Math.Min(left, from);
            right = Math.Max(right, to);
        }

        for (var i = frame.FirstElement; i < frame.EndElement; i++)
        {
            switch (elements[i])
            {
                case Message message
                    when positions.TryGetValue(message.FromId, out var fromX) &&
                         positions.TryGetValue(message.ToId, out var toX):
                    Include(Math.Min(fromX, toX) - participantWidth / 2, Math.Max(fromX, toX) + participantWidth / 2);
                    if (message.FromId == message.ToId &&
                        message.Text is not null)
                    {
                        Include(fromX, fromX + selfMessageLoopWidth + 5 + MeasureText(message.Text, options.FontSize));
                    }

                    break;

                case Note note when positions.ContainsKey(note.ParticipantId):
                    var (noteX, noteWidth) = NoteGeometry(note, positions, options);
                    Include(noteX, noteX + noteWidth);
                    break;
            }
        }

        // A block with nothing in it spans the whole conversation.
        if (double.IsInfinity(left))
        {
            foreach (var participant in model.Participants)
            {
                var x = positions[participant.Id];
                Include(x - participantWidth / 2, x + participantWidth / 2);
            }
        }

        if (double.IsInfinity(left))
        {
            Include(options.Padding, options.Padding + participantWidth);
        }

        var margin = frameNestingStep * (frame.InnerDepth + 1);
        frame.Left = left - margin;
        frame.Right = Math.Max(right + margin, frame.Left + FrameHeaderWidth(frame, options));
    }

    static double FrameTabWidth(Frame frame, RenderOptions options) =>
        Math.Max(frameTabMinWidth, MeasureText(frame.Kind ?? "", options.FontSize) + 20);

    static string? FrameLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return $"[{LabelLines.Flatten(label)}]";
    }

    static double FrameHeaderWidth(Frame frame, RenderOptions options)
    {
        if (frame.Kind is null)
        {
            return 0;
        }

        var width = FrameTabWidth(frame, options) + 20;
        if (FrameLabel(frame.Title) is { } title)
        {
            width += MeasureText(title, options.FontSize);
        }

        foreach (var (_, label) in frame.Dividers)
        {
            if (FrameLabel(label) is { } dividerLabel)
            {
                width = Math.Max(width, MeasureText(dividerLabel, options.FontSize) + 20);
            }
        }

        return width;
    }

    static void DrawRectBackgrounds(SvgBuilder builder, List<Frame> frames)
    {
        foreach (var frame in frames)
        {
            if (frame.Kind is null)
            {
                builder.AddRect(
                    frame.Left,
                    frame.Top,
                    frame.Right - frame.Left,
                    frame.Bottom - frame.Top,
                    fill: frame.Fill,
                    stroke: "none");
            }
        }
    }

    static void DrawFrames(SvgBuilder builder, List<Frame> frames, RenderOptions options)
    {
        foreach (var frame in frames)
        {
            if (frame.Kind is null)
            {
                continue;
            }

            builder.AddRect(
                frame.Left,
                frame.Top,
                frame.Right - frame.Left,
                frame.Bottom - frame.Top,
                fill: "none",
                stroke: frameStroke,
                strokeWidth: 1);

            // Corner tab naming the block, its lower right corner cut off.
            var tabWidth = FrameTabWidth(frame, options);
            var tab = string.Create(
                CultureInfo.InvariantCulture,
                $"M{frame.Left:0.##},{frame.Top:0.##} H{frame.Left + tabWidth:0.##} V{frame.Top + frameTabHeight - 7:0.##} L{frame.Left + tabWidth - 7:0.##},{frame.Top + frameTabHeight:0.##} H{frame.Left:0.##} Z");
            builder.AddPath(tab, fill: "#ECECFF", stroke: frameStroke, strokeWidth: 1);
            builder.AddText(
                frame.Left + (tabWidth - 7) / 2,
                frame.Top + frameTabHeight / 2,
                frame.Kind,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold");

            if (FrameLabel(frame.Title) is { } title)
            {
                builder.AddText(
                    (frame.Left + tabWidth + frame.Right) / 2,
                    frame.Top + frameTabHeight / 2,
                    title,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
            }

            foreach (var (dividerY, label) in frame.Dividers)
            {
                builder.AddLine(
                    frame.Left,
                    dividerY,
                    frame.Right,
                    dividerY,
                    stroke: frameStroke,
                    strokeWidth: 1,
                    strokeDasharray: "3,3");

                if (FrameLabel(label) is { } dividerLabel)
                {
                    builder.AddText(
                        (frame.Left + frame.Right) / 2,
                        dividerY + frameDividerHeight / 2,
                        dividerLabel,
                        anchor: "middle",
                        baseline: "middle",
                        fontSize: options.FontSize,
                        fontFamily: options.FontFamily);
                }
            }
        }
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

    // Room above the participants for the titles of `box` groups.
    static double BoxBand(SequenceModel model)
    {
        if (model.Boxes.Any(_ => !string.IsNullOrEmpty(_.Title)))
        {
            return boxTitleBand;
        }

        if (model.Boxes.Count > 0)
        {
            return boxMargin;
        }

        return 0;
    }

    static void DrawBoxes(SvgBuilder builder, SequenceModel model, Dictionary<string, double> positions,
        double top, double bottom, RenderOptions options)
    {
        foreach (var box in model.Boxes)
        {
            var xs = box.ParticipantIds.Where(positions.ContainsKey).Select(_ => positions[_]).ToList();
            if (xs.Count == 0)
            {
                continue;
            }

            var left = xs.Min() - participantWidth / 2 - boxMargin;
            var right = xs.Max() + participantWidth / 2 + boxMargin;
            builder.AddRect(
                left,
                top,
                right - left,
                bottom - top,
                fill: box.Color ?? "none",
                stroke: "#999",
                strokeWidth: 1);

            if (!string.IsNullOrEmpty(box.Title))
            {
                builder.AddText(
                    (left + right) / 2,
                    top + boxTitleBand / 2,
                    LabelLines.Flatten(box.Title),
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
            }
        }
    }

    // The y of the first (or last) message involving each participant that matches, which is where a
    // created participant appears and a destroyed one ends.
    static Dictionary<string, double> LifeEvents(SequenceModel model, List<SequenceElement> elements,
        Dictionary<int, double> yPositions, Func<Participant, bool> match, bool first)
    {
        var events = new Dictionary<string, double>();
        foreach (var participant in model.Participants)
        {
            if (!match(participant))
            {
                continue;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i] is not Message message ||
                    (message.FromId != participant.Id && message.ToId != participant.Id))
                {
                    continue;
                }

                events[participant.Id] = yPositions[i];
                if (first)
                {
                    break;
                }
            }
        }

        return events;
    }

    static void DrawLifeEvents(SvgBuilder builder, SequenceModel model, Dictionary<string, double> positions,
        Dictionary<string, double> createdAt, Dictionary<string, double> destroyedAt, RenderOptions options)
    {
        var boxHeight = BoxHeight(model, options);
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];
            if (createdAt.TryGetValue(participant.Id, out var createdY))
            {
                if (participant.Type == ParticipantType.Actor)
                {
                    DrawActor(builder, x, createdY - participantHeight / 2, participant.DisplayName, options);
                }
                else
                {
                    DrawParticipantBox(builder, x, createdY - boxHeight / 2, boxHeight, participant.DisplayName, options);
                }
            }

            if (destroyedAt.TryGetValue(participant.Id, out var destroyedY))
            {
                const double arm = 8;
                builder.AddLine(x - arm, destroyedY - arm, x + arm, destroyedY + arm, stroke: frameStroke, strokeWidth: 2);
                builder.AddLine(x - arm, destroyedY + arm, x + arm, destroyedY - arm, stroke: frameStroke, strokeWidth: 2);
            }
        }
    }

    static void DrawParticipants(SvgBuilder builder, SequenceModel model,
        Dictionary<string, double> positions, double y, RenderOptions options, Dictionary<string, double> skip)
    {
        var boxHeight = BoxHeight(model, options);
        foreach (var participant in model.Participants)
        {
            if (skip.ContainsKey(participant.Id))
            {
                continue;
            }

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
        Dictionary<string, double> positions, double startY, double endY,
        Dictionary<string, double> createdAt, Dictionary<string, double> destroyedAt, RenderOptions options)
    {
        var defaultStart = startY;
        var defaultEnd = endY;
        foreach (var participant in model.Participants)
        {
            var x = positions[participant.Id];

            // A created participant's lifeline hangs from the box drawn where it was created.
            startY = defaultStart;
            if (createdAt.TryGetValue(participant.Id, out var createdY))
            {
                startY = createdY + CreatedHeight(model, participant, options) / 2;
            }

            endY = destroyedAt.GetValueOrDefault(participant.Id, defaultEnd);
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
        List<SequenceElement> elements, Dictionary<int, double> yPositions)
    {
        var activations = new Dictionary<string, List<(double startY, double endY)>>();
        var activeLifelines = new Dictionary<string, double>();
        double? lastMessageY = null;

        for (var i = 0; i < elements.Count; i++)
        {
            var y = yPositions[i];

            switch (elements[i])
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

    // How tall a participant is where it is drawn mid-diagram: its box, or the actor figure and its name.
    static double CreatedHeight(SequenceModel model, Participant participant, RenderOptions options)
    {
        if (participant.Type == ParticipantType.Actor)
        {
            return participantHeight + (actorLabelHeight + ExtraLines(participant.DisplayName, options)) * 2;
        }

        return BoxHeight(model, options);
    }

    static void DrawElements(SvgBuilder builder, List<SequenceElement> elements, SequenceModel model,
        Dictionary<string, double> positions,
        Dictionary<int, double> yPositions,
        Dictionary<string, double> createdAt,
        RenderOptions options)
    {
        var messageNumber = 0;

        for (var i = 0; i < elements.Count; i++)
        {
            var y = yPositions[i];

            switch (elements[i])
            {
                case Message msg:
                    messageNumber++;
                    // The message that creates a participant stops at the edge of its box.
                    var inset = 0.0;
                    if (createdAt.TryGetValue(msg.ToId, out var createdY) &&
                        createdY == y &&
                        msg.FromId != msg.ToId)
                    {
                        inset = participantWidth / 2;
                    }

                    DrawMessage(
                        builder,
                        msg,
                        positions,
                        y,
                        options,
                        model.AutoNumber ? model.AutoNumberStart + (messageNumber - 1) * model.AutoNumberStep : null,
                        inset);
                    break;

                case Note note:
                    DrawNote(builder, note, positions, y, options);
                    break;
            }
        }
    }

    static void DrawMessage(SvgBuilder builder, Message msg,
        Dictionary<string, double> positions, double y,
        RenderOptions options, int? number, double targetInset = 0)
    {
        var fromX = positions[msg.FromId];
        var toX = positions[msg.ToId];
        var isSelfMessage = msg.FromId == msg.ToId;
        toX -= Math.Sign(toX - fromX) * targetInset;

        var isDotted = msg.Type is MessageType.Dotted or MessageType.DottedArrow
            or MessageType.DottedOpen or MessageType.DottedCross or MessageType.DottedAsync
            or MessageType.DottedBiDirectional;

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
            if (msg.Type is MessageType.BiDirectional or MessageType.DottedBiDirectional)
            {
                DrawArrowhead(builder, toX, fromX, y, msg.Type);
            }

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
            case MessageType.BiDirectional:
            case MessageType.DottedBiDirectional:
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
