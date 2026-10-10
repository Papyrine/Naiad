// ReSharper disable MemberCanBeMadeStatic.Local
namespace Naiad.Diagrams.State;

[SuppressMessage("Performance", "CA1822:Mark members as static")]
public class StateRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<StateModel>
{
    ILayoutEngine layoutEngine = layoutEngine ?? new DagreEngine();

    // Track placed label bounds to avoid label-to-label overlaps
    record LabelBounds(double Left, double Top, double Width, double Height);
    List<LabelBounds> placedLabels = [];

    // Composite id -> every state id inside it, transitively. Lets the self-checks tell containment from
    // collision: a child sitting inside its container, or an edge crossing its own container's border, is
    // the drawing working as intended, not an overlap.
    Dictionary<string, HashSet<string>> compositeContents = new(StringComparer.Ordinal);

    void CollectCompositeContents(List<State> states)
    {
        foreach (var state in states)
        {
            if (!state.IsComposite)
            {
                continue;
            }

            var contents = new HashSet<string>(StringComparer.Ordinal);
            AddDescendants(state, contents);
            compositeContents[state.Id] = contents;
            CollectCompositeContents(state.NestedStates);
        }
    }

    static void AddDescendants(State composite, HashSet<string> contents)
    {
        foreach (var nested in composite.NestedStates)
        {
            contents.Add(nested.Id);
            if (nested.IsComposite)
            {
                AddDescendants(nested, contents);
            }
        }
    }

    // Layout self-checks (label/line/node overlaps, out-of-bounds). Opt-in via ValidateLayout so the
    // tracking and O(n^2) checks stay off the production render path; the test suite turns it on to guard
    // against regressions. When false, the Track* calls below short-circuit and the checks never run.
    internal bool ValidateLayout { get; set; }
    List<TextBounds> textBounds = [];
    List<LineBounds> lineBounds = [];
    List<NodeBounds> nodeBounds = [];
    double svgWidth;
    double svgHeight;

    record TextBounds(double X, double Y, double Width, double Height, string Label);
    record LineBounds(double X1, double Y1, double X2, double Y2, string Label, string? FromId, string? ToId);
    record NodeBounds(double X, double Y, double Width, double Height, string Label, string? Id);

    const double stateMinWidth = 40;
    const double stateHeight = 40;
    const double statePadding = 30;
    const double stateRadius = 5;
    const double specialStateSize = 20;

    // A composite's box: the band its title sits in, and the clearance around its laid-out contents.
    const double compositeTitleHeight = 30;
    const double compositePadding = 20;

    // Kept between a routed edge's exit and a state it has to clear, and how far apart the exits tried are.
    const double exitClearance = 5;
    const double exitStep = 6;
    const double noteMinWidth = 60;
    const double singleLineNoteHeight = 40;
    const double notePadding = 20;
    const double noteHorizontalOffset = 60;

    // Clearance kept between a routed-edge corridor and a note pushed out past it.
    const double noteCorridorGap = 20;
    const double noteVerticalOffset = 50;

    public SvgDocument Render(StateModel model, RenderOptions options)
    {
        // Nothing to lay out: the measuring below takes the extremes of the states and has none to take.
        if (model.States.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(0, 0);
            emptyBuilder.Padding(options.Padding);
            return emptyBuilder.Build();
        }

        placedLabels.Clear();
        textBounds.Clear();
        lineBounds.Clear();
        nodeBounds.Clear();
        compositeContents.Clear();
        if (ValidateLayout)
        {
            CollectCompositeContents(model.States);
        }

        // Size every composite from its own contents before the outer layout runs, so it takes part as a
        // node of the right size rather than one sized from its label.
        LayoutCompositeInteriors(model.States, model.Direction, options);

        // Convert to graph model for layout
        var graphModel = ConvertToGraphModel(model, options);

        // Run layout
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            // More horizontal space
            NodeSeparation = 120,
            RankSeparation = 80
        };
        var layoutResult = layoutEngine.BuildLayout(graphModel, layoutOptions);

        // Copy positions back to state model
        CopyPositionsToModel(model, graphModel);

        // The interior layout left nested states positioned relative to their own container; now that the
        // containers have their final places, move the contents into them.
        PlaceCompositeChildren(model.States);

        // Align start/end nodes and their single children
        AlignSingleChildNodes(model, model.Direction);

        // Resize fork/join bars to span their connected states
        AdjustForkJoinWidths(model);

        // Calculate extra space needed for notes
        var stateMap = BuildStateMap(model.States);
        var (noteExtraWidth, noteExtraHeight, noteExtraLeft) = CalculateNoteExtraSpace(model, stateMap, options);

        // Calculate extra space needed for bidirectional forward edges (curve left)
        var curveExtraLeft = CalculateCurveExtraLeft(model, stateMap);
        var totalExtraLeft = Math.Max(noteExtraLeft, curveExtraLeft);

        // Calculate extra space needed for back-edges (curve right)
        var curveExtraRight = CalculateCurveExtraRight(model, stateMap);

        // Calculate extra height for end node if it was repositioned
        var endExtraHeight = CalculateEndNodeExtraHeight(model, layoutResult.Height);

        // Calculate extra height for routed transitions that go around obstacles
        var routedExtraHeight = CalculateRoutedTransitionExtraHeight(model, stateMap, layoutResult.Height);

        // Shift all positions right if notes or curves extend past left edge
        if (totalExtraLeft > 0)
        {
            foreach (var state in model.States)
            {
                state.Position = state.Position with
                {
                    X = state.Position.X + totalExtraLeft
                };
            }
        }

        // Ensure end nodes don't overlap with other states (run after position shift)
        AdjustEndNodePosition(model);

        // Build SVG
        var svgWidth = layoutResult.Width + noteExtraWidth + totalExtraLeft + curveExtraRight;
        var svgHeight = layoutResult.Height + noteExtraHeight + endExtraHeight + routedExtraHeight;
        this.svgWidth = svgWidth;
        this.svgHeight = svgHeight;
        var builder = new SvgBuilder();
        builder.Size(svgWidth, svgHeight);
        builder.Padding(options.Padding);
        builder.AddArrowMarker();

        // Composite boxes are filled, so they go down before the transitions that run inside them -
        // drawing a container after its own contents painted over them.
        RenderCompositeBoxes(builder, model.States, options);

        // Render transitions first (behind states)
        RenderTransitions(builder, model, options);

        // Render states
        RenderStates(builder, model.States, options);

        // Render notes
        RenderNotes(builder, model, options);

        if (ValidateLayout)
        {
            CheckForTextOverlaps();
            CheckForLinesUnderNodes();
            CheckForNodeOverlaps();
            CheckForElementsOutsideBounds();
        }

        return builder.Build();
    }

    void TrackText(double x, double y, string text, string anchor, double fontSize, bool bold = false)
    {
        if (!ValidateLayout)
        {
            return;
        }

        var width = MeasureText(text, fontSize, bold);
        var height = fontSize * 1.2 * LabelLines.Count(text); // Approximate line height

        // Adjust x based on anchor
        var left = anchor switch
        {
            "middle" => x - width / 2,
            "end" => x - width,
            // "start" or default
            _ => x
        };

        // Adjust y (text is typically centered vertically with dominant-baseline="middle")
        var top = y - height / 2;

        textBounds.Add(new(left, top, width, height, text));
    }

    /// <summary>
    /// Records a label that is drawn inside a sized background chip. Re-measuring the glyphs instead
    /// understates what was painted - the chip is wider and taller than the text it holds - so two chips
    /// could touch while the check saw a gap between them.
    /// </summary>
    void TrackTextBox(double centerX, double centerY, double width, double height, string text)
    {
        if (!ValidateLayout)
        {
            return;
        }

        textBounds.Add(new(centerX - width / 2, centerY - height / 2, width, height, text));
    }

    void CheckForTextOverlaps()
    {
        for (var i = 0; i < textBounds.Count; i++)
        {
            var a = textBounds[i];
            for (var j = i + 1; j < textBounds.Count; j++)
            {
                var b = textBounds[j];

                // Check for rectangle overlap
                var overlapsX = a.X < b.X + b.Width && a.X + a.Width > b.X;
                var overlapsY = a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

                if (overlapsX && overlapsY)
                {
                    throw new InvalidOperationException(
                        $"Text overlap detected: \"{a.Label}\" at ({a.X:F1},{a.Y:F1},{a.Width:F1}x{a.Height:F1}) overlaps with \"{b.Label}\" at ({b.X:F1},{b.Y:F1},{b.Width:F1}x{b.Height:F1})");
                }
            }
        }
    }

    /// <summary>
    /// Records a drawn segment for the layout self-checks. <paramref name="fromId"/> and
    /// <paramref name="toId"/> are the states the segment actually joins, so the crossing check can tell an
    /// attachment from an overlap by identity instead of guessing from proximity.
    /// </summary>
    void TrackLine(double x1, double y1, double x2, double y2, string label, string? fromId = null, string? toId = null)
    {
        if (!ValidateLayout)
        {
            return;
        }

        lineBounds.Add(new(x1, y1, x2, y2, label, fromId, toId));
    }

    /// <summary>
    /// Records a drawn cubic as a chain of short segments. Tracking a flare by the straight chord between
    /// its ends describes a path that was never drawn: the chord stops where the curve is only starting to
    /// bend away, so everything the curve sweeps past is invisible to the self-checks.
    /// </summary>
    void TrackCubic(
        double x0,
        double y0,
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3,
        string label,
        string? fromId,
        string? toId)
    {
        if (!ValidateLayout)
        {
            return;
        }

        const int segments = 12;
        var previousX = x0;
        var previousY = y0;

        for (var i = 1; i <= segments; i++)
        {
            var t = (double) i / segments;
            var u = 1 - t;
            var x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            var y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;

            TrackLine(previousX, previousY, x, y, label, fromId, toId);
            previousX = x;
            previousY = y;
        }
    }

    /// <summary>Records a drawn quadratic, by way of its equivalent cubic.</summary>
    void TrackQuadratic(
        double x0,
        double y0,
        double controlX,
        double controlY,
        double x1,
        double y1,
        string label,
        string? fromId,
        string? toId) =>
        TrackCubic(
            x0, y0,
            x0 + 2 * (controlX - x0) / 3, y0 + 2 * (controlY - y0) / 3,
            x1 + 2 * (controlX - x1) / 3, y1 + 2 * (controlY - y1) / 3,
            x1, y1,
            label, fromId, toId);

    void TrackNode(double x, double y, double width, double height, string label, string? id = null)
    {
        if (!ValidateLayout)
        {
            return;
        }

        nodeBounds.Add(new(x - width / 2, y - height / 2, width, height, label, id));
    }

    void CheckForLinesUnderNodes()
    {
        foreach (var line in lineBounds)
        {
            foreach (var node in nodeBounds)
            {
                // Notes are checked alongside state nodes. Note placement keeps them off the routed
                // back-edge / forward-edge corridors, so a line under a note is a real defect.

                // A segment is exempt from this node only when it genuinely attaches to it. This used to be
                // inferred from proximity - any endpoint within 10 units of the box counted as attached -
                // which exempted the very overlaps worth catching: the `reset` edge in TransitionLabels left
                // Inactive at a point sitting exactly on the final-state marker's border, so its run straight
                // through that marker was read as an attachment and never reported. A note is never an edge
                // endpoint, so it carries no id and is always checked.
                if (node.Id != null && (node.Id == line.FromId || node.Id == line.ToId))
                {
                    continue;
                }

                // Any line with an endpoint inside a composite must cross that composite's border, so the
                // container is exempt for it. Other composites stay checked: a nested transition escaping
                // sideways through an unrelated box is still a defect.
                if (node.Id != null &&
                    compositeContents.TryGetValue(node.Id, out var containedStates) &&
                    ((line.FromId != null && containedStates.Contains(line.FromId)) ||
                     (line.ToId != null && containedStates.Contains(line.ToId))))
                {
                    continue;
                }

                // Check if line segment passes through node's bounding box
                if (LineIntersectsRect(line.X1, line.Y1, line.X2, line.Y2,
                    node.X, node.Y, node.Width, node.Height))
                {
                    throw new InvalidOperationException(
                        $"Line passes under node: \"{line.Label}\" from ({line.X1:F1},{line.Y1:F1}) to ({line.X2:F1},{line.Y2:F1}) " +
                        $"passes under \"{node.Label}\" at ({node.X:F1},{node.Y:F1},{node.Width:F1}x{node.Height:F1})");
                }
            }
        }
    }

    void CheckForNodeOverlaps()
    {
        for (var i = 0; i < nodeBounds.Count; i++)
        {
            var a = nodeBounds[i];
            for (var j = i + 1; j < nodeBounds.Count; j++)
            {
                var b = nodeBounds[j];

                // Check for rectangle overlap with margin
                const double margin = 2.0;

                var overlapsX = a.X < b.X + b.Width - margin &&
                                a.X + a.Width > b.X + margin;
                if (!overlapsX)
                {
                    continue;
                }

                var overlapsY = a.Y < b.Y + b.Height - margin &&
                                a.Y + a.Height > b.Y + margin;
                if (!overlapsY)
                {
                    continue;
                }

                // A composite and one of its own descendants are supposed to intersect - but then the child
                // must sit entirely inside the container's content region, below the title band. A partial
                // overlap here is a child escaping its box, which is exactly what the unimplemented-
                // containment bug looked like.
                if (IsInsideDeclaredContainer(a, b) || IsInsideDeclaredContainer(b, a))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Node overlap detected: \"{a.Label}\" at ({a.X:F1},{a.Y:F1},{a.Width:F1}x{a.Height:F1}) overlaps with \"{b.Label}\" at ({b.X:F1},{b.Y:F1},{b.Width:F1}x{b.Height:F1})");
            }
        }
    }

    bool IsInsideDeclaredContainer(NodeBounds container, NodeBounds child)
    {
        if (container.Id == null ||
            child.Id == null ||
            !compositeContents.TryGetValue(container.Id, out var contents) ||
            !contents.Contains(child.Id))
        {
            return false;
        }

        const double tolerance = 1;
        var contentTop = container.Y + compositeTitleHeight;
        var fullyInside = child.X >= container.X - tolerance &&
                          child.Y >= contentTop - tolerance &&
                          child.X + child.Width <= container.X + container.Width + tolerance &&
                          child.Y + child.Height <= container.Y + container.Height + tolerance;

        if (!fullyInside)
        {
            throw new InvalidOperationException(
                $"Node escapes its composite: \"{child.Label}\" at ({child.X:F1},{child.Y:F1},{child.Width:F1}x{child.Height:F1}) is not fully inside \"{container.Label}\"'s content region ({container.X:F1},{contentTop:F1},{container.Width:F1}x{container.Height - compositeTitleHeight:F1})");
        }

        return true;
    }

    void CheckForElementsOutsideBounds()
    {
        // Check nodes
        foreach (var node in nodeBounds)
        {
            if (node.X < 0 || node.Y < 0 ||
                node.X + node.Width > svgWidth ||
                node.Y + node.Height > svgHeight)
            {
                throw new InvalidOperationException(
                    $"Node outside bounds: \"{node.Label}\" at ({node.X:F1},{node.Y:F1},{node.Width:F1}x{node.Height:F1}) is outside SVG bounds (0,0,{svgWidth:F1}x{svgHeight:F1})");
            }
        }

        // Check text
        foreach (var text in textBounds)
        {
            if (text.X < 0 || text.Y < 0 ||
                text.X + text.Width > svgWidth ||
                text.Y + text.Height > svgHeight)
            {
                throw new InvalidOperationException(
                    $"Text outside bounds: \"{text.Label}\" at ({text.X:F1},{text.Y:F1},{text.Width:F1}x{text.Height:F1}) is outside SVG bounds (0,0,{svgWidth:F1}x{svgHeight:F1})");
            }
        }

        // Check lines
        foreach (var line in lineBounds)
        {
            if (line.X1 < 0 || line.Y1 < 0 || line.X2 < 0 || line.Y2 < 0 ||
                line.X1 > svgWidth || line.Y1 > svgHeight ||
                line.X2 > svgWidth || line.Y2 > svgHeight)
            {
                throw new InvalidOperationException(
                    $"Line outside bounds: \"{line.Label}\" from ({line.X1:F1},{line.Y1:F1}) to ({line.X2:F1},{line.Y2:F1}) is outside SVG bounds (0,0,{svgWidth:F1}x{svgHeight:F1})");
            }
        }
    }

    static bool LineIntersectsRect(double x1, double y1, double x2, double y2,
        double rx, double ry, double rw, double rh)
    {
        // Check if line segment intersects rectangle interior (not just edges)
        // Use parametric line equation and check for intersection with rectangle

        var left = rx;
        var right = rx + rw;
        var top = ry;
        var bottom = ry + rh;

        // Shrink the rect slightly to avoid edge cases at connection points
        const double margin = 2.0;
        left += margin;
        right -= margin;
        top += margin;
        bottom -= margin;

        if (right <= left ||
            bottom <= top)
        {
            return false;
        }

        // Check if either endpoint is inside the rectangle (shouldn't happen for valid lines)
        // Skip endpoints since they might be at connection points

        // Use Cohen-Sutherland style clipping to find if line passes through interior
        // Sample points along the line and check if any are inside
        const int steps = 20;
        for (var i = 1; i < steps; i++) // Skip endpoints (i=0 and i=steps)
        {
            var t = i / (double)steps;
            var px = x1 + t * (x2 - x1);
            var py = y1 + t * (y2 - y1);

            if (px > left &&
                px < right &&
                py > top &&
                py < bottom)
            {
                return true;
            }
        }

        return false;
    }

    static double CalculateCurveExtraLeft(StateModel model, Dictionary<string, State> stateMap)
    {
        // Check if any bidirectional forward edges will curve left
        var bidirectionalPairs = FindBidirectionalPairs(model.Transitions);
        if (bidirectionalPairs.Count == 0)
        {
            return 0;
        }

        var leftEdge = model.States.Min(_ => _.Position.X - _.Width / 2);
        double maxExtraNeeded = 0;

        foreach (var transition in model.Transitions)
        {
            var pairKey = GetPairKey(transition.FromId, transition.ToId);
            if (!bidirectionalPairs.Contains(pairKey))
            {
                continue;
            }

            // Check if this is a forward edge (not back edge)
            if (IsBackEdge(transition, stateMap))
            {
                continue;
            }

            // Forward edge of bidirectional pair - calculate how far left it extends
            // The curve goes to baseLeftEdge - 50
            var baseLeftEdge = leftEdge - 50;
            var curveExtraNeeded = -baseLeftEdge; // How much past x=0 it goes

            // Also account for label width if present (label is centered on vertical line)
            var labelExtraNeeded = 0.0;
            if (!string.IsNullOrEmpty(transition.Label))
            {
                var labelWidth = MeasureText(transition.Label, 12); // FontSize - 2
                var labelLeft = baseLeftEdge - labelWidth / 2;
                labelExtraNeeded = -labelLeft;
            }

            maxExtraNeeded = Math.Max(maxExtraNeeded, Math.Max(curveExtraNeeded, labelExtraNeeded));
        }

        if (maxExtraNeeded > 0)
        {
            return maxExtraNeeded + 10;
        }

        return 0; // Add margin
    }

    static double CalculateCurveExtraRight(StateModel model, Dictionary<string, State> stateMap)
    {
        var rightEdge = model.States.Max(_ => _.Position.X + _.Width / 2);

        // Get all back-edges with their indices for position calculation
        var backEdges = model.Transitions
            .Where(_ => IsBackEdge(_, stateMap))
            .OrderBy(_ =>
            {
                if (stateMap.TryGetValue(_.FromId, out var s))
                {
                    return s.Position.X;
                }

                return 0;
            })
            .ToList();

        if (backEdges.Count == 0)
        {
            return 0;
        }

        double maxExtraNeeded = 0;
        var baseRightEdge = rightEdge + 50;
        const int lineSpacing = 50;

        for (var i = 0; i < backEdges.Count; i++)
        {
            var transition = backEdges[i];
            var edgeX = baseRightEdge + i * lineSpacing;

            // Calculate space needed for the curve itself
            var curveExtraNeeded = edgeX - rightEdge;

            // Also account for label width if present (label is centered on vertical line)
            var labelExtraNeeded = 0.0;
            if (!string.IsNullOrEmpty(transition.Label))
            {
                var labelWidth = MeasureText(transition.Label, 12); // FontSize - 2
                var labelRight = edgeX + labelWidth / 2;
                labelExtraNeeded = labelRight - rightEdge;
            }

            maxExtraNeeded = Math.Max(maxExtraNeeded, Math.Max(curveExtraNeeded, labelExtraNeeded));
        }

        if (maxExtraNeeded > 0)
        {
            // Add margin
            return maxExtraNeeded + 20;
        }

        return 0;
    }

    static double CalculateEndNodeExtraHeight(StateModel model, double layoutHeight)
    {
        var endNode = model.States.FirstOrDefault(_ => _.Type == StateType.End);
        if (endNode == null)
        {
            return 0;
        }

        var endBottom = endNode.Position.Y + specialStateSize / 2;
        var extraNeeded = endBottom - layoutHeight;
        if (extraNeeded > 0)
        {
            // Add margin
            return extraNeeded + 10;
        }

        return 0;
    }

    static double CalculateRoutedTransitionExtraHeight(StateModel model, Dictionary<string, State> stateMap, double layoutHeight)
    {
        double maxExtraNeeded = 0;

        foreach (var transition in model.Transitions)
        {
            if (!stateMap.TryGetValue(transition.FromId, out var fromState) ||
                !stateMap.TryGetValue(transition.ToId, out var toState))
            {
                continue;
            }

            var (startX, startY) = GetConnectionPoint(fromState, toState);
            var (endX, endY) = GetConnectionPoint(toState, fromState);

            var obstacle = FindObstacleState(startX, startY, endX, endY, transition, stateMap);
            if (obstacle == null)
            {
                continue;
            }

            // Calculate how far down the routed path goes
            var obstacleBottom = obstacle.Position.Y + obstacle.Height / 2;
            var targetBottom = toState.Type == StateType.End
                ? toState.Position.Y + specialStateSize / 2
                : toState.Position.Y + toState.Height / 2;
            const double margin = 30.0;
            var horizontalY = Math.Max(obstacleBottom, targetBottom) + margin;

            var extraNeeded = horizontalY - layoutHeight;
            maxExtraNeeded = Math.Max(maxExtraNeeded, extraNeeded);
        }

        if (maxExtraNeeded > 0)
        {
            return maxExtraNeeded + 10;
        }

        return 0;
    }

    // Where a note's box starts. The side is the one the diagram asked for - `note right of X` /
    // `note left of X` - and a note on a side carrying a routed-edge corridor (back-edges curve right,
    // bidirectional forward edges curve left) is pushed out past that corridor rather than flipped to the
    // other side, so the declared side survives without a corridor line running under the note. Shared by
    // CalculateNoteExtraSpace (space reservation) and RenderNotes (placement) so the two agree on where
    // each note lands.
    static double NoteX(StateModel model, StateNote note, State state, double noteWidth, Dictionary<string, State> stateMap)
    {
        if (note.Position == NotePosition.RightOf)
        {
            var x = state.Position.X + state.Width / 2 + noteHorizontalOffset - noteWidth / 2;
            var corridor = CalculateCurveExtraRight(model, stateMap);
            if (corridor > 0)
            {
                var statesRight = model.States.Max(_ => _.Position.X + _.Width / 2);
                x = Math.Max(x, statesRight + corridor + noteCorridorGap);
            }

            return x;
        }

        var leftX = state.Position.X - state.Width / 2 - noteHorizontalOffset - noteWidth / 2;
        var leftCorridor = CalculateCurveExtraLeft(model, stateMap);
        if (leftCorridor > 0)
        {
            var statesLeft = model.States.Min(_ => _.Position.X - _.Width / 2);
            leftX = Math.Min(leftX, statesLeft - leftCorridor - noteCorridorGap - noteWidth);
        }

        return leftX;
    }

    static (double extraWidth, double extraHeight, double extraLeft) CalculateNoteExtraSpace(StateModel model, Dictionary<string, State> stateMap, RenderOptions options)
    {
        double maxExtraWidth = 0;
        double maxExtraHeight = 0;
        double maxExtraLeft = 0;

        foreach (var note in model.Notes)
        {
            if (!stateMap.TryGetValue(note.StateId, out var state))
            {
                continue;
            }

            var noteWidth = Math.Max(noteMinWidth, MeasureText(note.Text, options.FontSize - 2) + notePadding);
            var noteHeight = NoteHeight(note, options);

            // Check horizontal space needed - notes go outside the diagram, clear of any edge corridor
            var noteX = NoteX(model, note, state, noteWidth, stateMap);

            // Check if note extends past right edge
            var noteRightEdge = noteX + noteWidth;
            var stateRightEdge = model.States.Max(_ => _.Position.X + _.Width / 2);
            var extraWidthNeeded = noteRightEdge - stateRightEdge;

            // Check if note extends past left edge
            var stateLeftEdge = model.States.Min(_ => _.Position.X - _.Width / 2);
            var extraLeftNeeded = stateLeftEdge - noteX;
            maxExtraWidth = Math.Max(maxExtraWidth, extraWidthNeeded);
            maxExtraLeft = Math.Max(maxExtraLeft, extraLeftNeeded);

            // Check if note extends below
            var spaceAbove = state.Position.Y;
            var maxY = model.States.Max(_ => _.Position.Y + _.Height / 2);
            var spaceBelow = maxY - state.Position.Y;
            var placeBelow = spaceBelow >= spaceAbove;

            if (placeBelow)
            {
                var noteBottomEdge = state.Position.Y + state.Height / 2 + noteVerticalOffset + noteHeight;
                var extraHeightNeeded = noteBottomEdge - maxY;
                maxExtraHeight = Math.Max(maxExtraHeight, extraHeightNeeded);
            }
        }

        return (
            maxExtraWidth > 0 ? maxExtraWidth + 20 : 0,
            maxExtraHeight > 0 ? maxExtraHeight + 20 : 0,
            maxExtraLeft > 0 ? maxExtraLeft + 20 : 0
        );
    }

    /// <summary>
    /// Lays out each composite's contents in a graph of their own, depth first, and sizes the composite to
    /// the result. Nested states were previously added to the outer layout as flat siblings, so a composite
    /// rendered as an empty box with its own contents beside it. Laying the interior out separately also
    /// keeps a transition that names the composite pointing at a real node, which a cluster is not: aiming
    /// such an edge at a state inside the cluster instead drags the outside endpoint into the box.
    /// </summary>
    void LayoutCompositeInteriors(List<State> states, Direction direction, RenderOptions options)
    {
        foreach (var state in states)
        {
            if (!state.IsComposite)
            {
                continue;
            }

            // Depth first, so a nested composite is already sized when its parent lays out.
            LayoutCompositeInteriors(state.NestedStates, direction, options);

            var interior = new StateLayoutGraph
            {
                Direction = direction
            };

            foreach (var nested in state.NestedStates)
            {
                var (width, height) = nested.IsComposite
                    ? (nested.Width, nested.Height)
                    : CalculateStateSize(nested, options);

                interior.AddNode(new()
                {
                    Id = nested.Id,
                    Label = nested.Description ?? nested.Id,
                    Width = width,
                    Height = height
                });
            }

            foreach (var transition in state.NestedTransitions)
            {
                interior.AddEdge(new()
                {
                    SourceId = transition.FromId,
                    TargetId = transition.ToId,
                    Label = transition.Label
                });
            }

            var result = layoutEngine.BuildLayout(interior, new()
            {
                Direction = direction,
                NodeSeparation = 60,
                RankSeparation = 50
            });

            // Held relative to the interior's own origin until PlaceCompositeChildren moves them. Sizes
            // come from here too: a nested state takes no part in the outer layout, so nothing else sets
            // them and its box would be drawn zero-sized.
            foreach (var nested in state.NestedStates)
            {
                var node = interior.GetNode(nested.Id);
                if (node != null)
                {
                    nested.Position = node.Position;
                    nested.Width = node.Width;
                    nested.Height = node.Height;
                }
            }

            state.Width = result.Width + compositePadding * 2;
            state.Height = result.Height + compositePadding * 2 + compositeTitleHeight;
        }
    }

    /// <summary>
    /// Translates each composite's contents from the interior layout's own coordinates into the box's final
    /// position. Runs parent-first, so a nested composite is absolute before its own children are moved.
    /// </summary>
    static void PlaceCompositeChildren(List<State> states)
    {
        foreach (var state in states)
        {
            if (!state.IsComposite)
            {
                continue;
            }

            var originX = state.Position.X - state.Width / 2 + compositePadding;
            var originY = state.Position.Y - state.Height / 2 + compositeTitleHeight + compositePadding;

            foreach (var nested in state.NestedStates)
            {
                nested.Position = new(originX + nested.Position.X, originY + nested.Position.Y);
            }

            PlaceCompositeChildren(state.NestedStates);
        }
    }

    static GraphDiagramBase ConvertToGraphModel(StateModel model, RenderOptions options)
    {
        var graph = new StateLayoutGraph
        {
            Direction = model.Direction
        };

        AddStatesToGraph(graph, model.States, options);

        foreach (var transition in model.Transitions)
        {
            graph.AddEdge(new()
            {
                SourceId = transition.FromId,
                TargetId = transition.ToId,
                Label = transition.Label
            });
        }

        return graph;
    }

    /// <summary>
    /// Only the states at this level become layout nodes. A composite enters as a single node already sized
    /// to its contents by <see cref="LayoutCompositeInteriors"/>; its children are placed inside it
    /// afterwards and take no part in the outer layout.
    /// </summary>
    static void AddStatesToGraph(StateLayoutGraph graph, List<State> states, RenderOptions options)
    {
        foreach (var state in states)
        {
            var (width, height) = state.IsComposite
                ? (state.Width, state.Height)
                : CalculateStateSize(state, options);

            graph.AddNode(new()
            {
                Id = state.Id,
                Label = state.Description ?? state.Id,
                Width = width,
                Height = height
            });
        }
    }

    static (double width, double height) CalculateStateSize(State state, RenderOptions options)
    {
        if (state.Type is StateType.Start or StateType.End)
        {
            return (specialStateSize, specialStateSize);
        }

        if (state.Type is StateType.Fork or StateType.Join)
        {
            // Fixed compact width for fork/join bars
            return (100, 8);
        }

        if (state.Type == StateType.Choice)
        {
            return (specialStateSize * 2, specialStateSize * 2);
        }

        // Size based on content
        var label = state.Description ?? state.Id;
        var textWidth = MeasureText(label, options.FontSize);
        var width = Math.Max(stateMinWidth, textWidth + statePadding);

        return (width, stateHeight + ExtraLines(label, options.FontSize));
    }

    static void CopyPositionsToModel(StateModel model, GraphDiagramBase graph) =>
        CopyPositionsToStates(model.States, graph);

    static void CopyPositionsToStates(List<State> states, GraphDiagramBase graph)
    {
        foreach (var state in states)
        {
            var node = graph.GetNode(state.Id);
            if (node != null)
            {
                state.Position = node.Position;
                state.Width = node.Width;
                state.Height = node.Height;
            }

            // Nested states keep their interior-relative positions here; PlaceCompositeChildren moves
            // them once every container has its final place.
        }
    }

    /// <summary>
    /// Lines the start marker up with its only child, and the end marker up with its only parent, so a
    /// terminal marker sits squarely on the run it belongs to instead of wherever the ranking left it.
    /// The alignment is along the <em>cross</em> axis - the one ranks do not advance along - which is X for
    /// a top-down diagram and Y for a left-to-right one. Doing it on X unconditionally moved a start's child
    /// onto its own neighbour under <c>direction LR</c>, where X is what separates the ranks.
    /// </summary>
    static void AlignSingleChildNodes(StateModel model, Direction direction)
    {
        var contentStates = model.States
            .Where(_ => _.Type != StateType.Start && _.Type != StateType.End)
            .ToList();
        if (contentStates.Count == 0)
        {
            return;
        }

        var horizontalRanks = direction is Direction.LeftToRight or Direction.RightToLeft;

        var center = horizontalRanks
            ? (contentStates.Min(_ => _.Position.Y) + contentStates.Max(_ => _.Position.Y)) / 2
            : (contentStates.Min(_ => _.Position.X) + contentStates.Max(_ => _.Position.X)) / 2;

        var startNode = model.States.FirstOrDefault(_ => _.Type == StateType.Start);
        if (startNode != null)
        {
            startNode.Position = WithCrossAxis(startNode.Position, center, horizontalRanks);

            // If start has only one child, align that child with start
            var startChildren = model.Transitions.Where(_ => _.FromId == startNode.Id).ToList();
            if (startChildren.Count == 1)
            {
                var childState = model.States.FirstOrDefault(_ => _.Id == startChildren[0].ToId);
                if (childState != null &&
                    childState.Type != StateType.Fork)
                {
                    childState.Position = WithCrossAxis(childState.Position, center, horizontalRanks);
                }
            }
        }

        // Center end node with its parent if it has only one
        var endNode = model.States.FirstOrDefault(_ => _.Type == StateType.End);
        if (endNode != null)
        {
            var endParents = model.Transitions.Where(_ => _.ToId == endNode.Id).ToList();
            if (endParents.Count == 1)
            {
                var parentState = model.States.FirstOrDefault(_ => _.Id == endParents[0].FromId);
                if (parentState != null)
                {
                    var parentCross = horizontalRanks ? parentState.Position.Y : parentState.Position.X;
                    endNode.Position = WithCrossAxis(endNode.Position, parentCross, horizontalRanks);
                }
            }
        }
    }

    static Position WithCrossAxis(Position position, double value, bool horizontalRanks)
    {
        if (horizontalRanks)
        {
            return position with {Y = value};
        }

        return position with {X = value};
    }

    static void AdjustEndNodePosition(StateModel model)
    {
        var endNode = model.States.FirstOrDefault(_ => _.Type == StateType.End);
        if (endNode == null)
        {
            return;
        }

        const double margin = 30;
        const double endHalfSize = specialStateSize / 2;

        // Find siblings at similar Y level (within 100 pixels) and move end node to the right
        foreach (var state in model.States)
        {
            if (state.Type is
                StateType.End or
                StateType.Start or
                StateType.Fork or
                StateType.Join or
                StateType.Choice)
            {
                continue;
            }

            // Check if this state is at a similar vertical level as the end node
            var yDistance = Math.Abs(state.Position.Y - endNode.Position.Y);
            if (yDistance > 100)
            {
                continue;
            }

            // Check if they're horizontally close (would overlap in a straight line from parent)
            var xDistance = Math.Abs(state.Position.X - endNode.Position.X);
            if (xDistance > state.Width)
            {
                continue;
            }

            // Move end node to the right of this state, at the same Y level
            var stateRight = state.Position.X + state.Width / 2;
            var newX = stateRight + margin + endHalfSize;
            endNode.Position = state.Position with {X = newX};
        }
    }

    static void AdjustForkJoinWidths(StateModel model)
    {
        var stateMap = BuildStateMap(model.States);

        foreach (var state in model.States)
        {
            if (state.Type is not (StateType.Fork or StateType.Join))
            {
                continue;
            }

            // Find all connected states
            var connectedStates = new List<State>();

            foreach (var transition in model.Transitions)
            {
                // Fork: outgoing transitions (fork --> target)
                if (state.Type == StateType.Fork &&
                    transition.FromId == state.Id)
                {
                    if (stateMap.TryGetValue(transition.ToId, out var target))
                    {
                        connectedStates.Add(target);
                    }
                }
                // Join: incoming transitions (source --> join)
                if (state.Type == StateType.Join &&
                    transition.ToId == state.Id)
                {
                    if (stateMap.TryGetValue(transition.FromId, out var source))
                    {
                        connectedStates.Add(source);
                    }
                }
            }

            if (connectedStates.Count >= 2)
            {
                // Calculate width based on number of connected states
                // Keep bars compact - roughly 40px per connected state
                var barWidth = Math.Max(80, connectedStates.Count * 50);
                state.Width = barWidth;
                // Center between leftmost and rightmost connected states
                var leftState = connectedStates.MinBy(_ => _.Position.X);
                var rightState = connectedStates.MaxBy(_ => _.Position.X);
                state.Position = state.Position with
                {
                    X = (leftState!.Position.X + rightState!.Position.X) / 2
                };
            }
        }
    }

    void RenderStates(SvgBuilder builder, List<State> states, RenderOptions options)
    {
        foreach (var state in states)
        {
            RenderState(builder, state, options);
        }
    }

    /// <summary>
    /// Draws every composite's container - box, title and separator - ahead of the transitions, so a filled
    /// container cannot paint over the contents it holds. The states inside are drawn afterwards, with the
    /// rest.
    /// </summary>
    void RenderCompositeBoxes(SvgBuilder builder, List<State> states, RenderOptions options)
    {
        foreach (var state in states)
        {
            if (!state.IsComposite)
            {
                continue;
            }

            RenderCompositeState(builder, state, options);
            TrackNode(state.Position.X, state.Position.Y, state.Width, state.Height, state.Id, state.Id);
            RenderCompositeBoxes(builder, state.NestedStates, options);
        }
    }

    void RenderState(SvgBuilder builder, State state, RenderOptions options)
    {
        var x = state.Position.X;
        var y = state.Position.Y;

        switch (state.Type)
        {
            case StateType.Start:
                // Filled circle
                builder.AddCircle(
                    x,
                    y,
                    specialStateSize / 2,
                    fill: "#333",
                    stroke: "#333",
                    strokeWidth: 1);
                TrackNode(x, y, specialStateSize, specialStateSize, state.Id, state.Id);
                break;

            case StateType.End:
                // Double circle
                builder.AddCircle(
                    x,
                    y,
                    specialStateSize / 2,
                    fill: "none",
                    stroke: "#333",
                    strokeWidth: 2);
                builder.AddCircle(
                    x,
                    y,
                    specialStateSize / 4,
                    fill: "#333",
                    stroke: "#333",
                    strokeWidth: 1);
                TrackNode(x, y, specialStateSize, specialStateSize, state.Id, state.Id);
                break;

            case StateType.Fork:
            case StateType.Join:
                // Horizontal bar
                builder.AddRect(
                    x - state.Width / 2,
                    y - state.Height / 2,
                    state.Width,
                    state.Height,
                    fill: "#333",
                    stroke: "#333");
                TrackNode(x, y, state.Width, state.Height, state.Id, state.Id);
                break;

            case StateType.Choice:
                // Diamond
                var halfW = state.Width / 2;
                var halfH = state.Height / 2;
                var diamondPath = string.Create(
                    CultureInfo.InvariantCulture,
                    $"M{x:0.##},{y - halfH:0.##} L{x + halfW:0.##},{y:0.##} L{x:0.##},{y + halfH:0.##} L{x - halfW:0.##},{y:0.##} Z");
                builder.AddPath(
                    diamondPath,
                    fill: "#fff",
                    stroke: "#333",
                    strokeWidth: 1);
                TrackNode(x, y, state.Width, state.Height, state.Id, state.Id);
                break;

            default:
                if (state.IsComposite)
                {
                    // The container went down with the other composite boxes, before the transitions.
                    RenderStates(builder, state.NestedStates, options);
                }
                else
                {
                    // Normal state - rounded rectangle
                    RenderNormalState(builder, state, options);
                }
                break;
        }
    }

    void RenderNormalState(SvgBuilder builder, State state, RenderOptions options)
    {
        var x = state.Position.X - state.Width / 2;
        var y = state.Position.Y - state.Height / 2;

        builder.AddRect(
            x,
            y,
            state.Width,
            state.Height,
            rx: stateRadius,
            fill: "#ECECFF",
            stroke: "#9370DB",
            strokeWidth: 1);

        TrackNode(state.Position.X, state.Position.Y, state.Width, state.Height, state.Id, state.Id);

        var label = state.Description ?? state.Id;
        if (state.Type == StateType.Normal)
        {
            builder.AddTextLines(
                state.Position.X,
                state.Position.Y,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            TrackText(state.Position.X, state.Position.Y, label, "middle", options.FontSize);
        }
    }

    void RenderCompositeState(SvgBuilder builder, State state, RenderOptions options)
    {
        // For now, render as a larger box with nested states inside
        // In a full implementation, we'd calculate the bounding box of nested states
        var x = state.Position.X - state.Width / 2;
        var y = state.Position.Y - state.Height / 2;

        builder.AddRect(
            x,
            y,
            state.Width,
            state.Height,
            rx: stateRadius,
            fill: "#F4F4F4",
            stroke: "#666",
            strokeWidth: 2);

        // Title
        builder.AddText(
            state.Position.X,
            y + 15,
            state.Id,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold");
        TrackText(state.Position.X, y + 15, state.Id, "middle", options.FontSize, bold: true);

        // Separator line
        builder.AddLine(
            x,
            y + 30,
            x + state.Width,
            y + 30,
            stroke: "#666",
            strokeWidth: 1);
    }

    void RenderTransitions(SvgBuilder builder, StateModel model, RenderOptions options)
    {
        var stateMap = BuildStateMap(model.States);

        // Build set of bidirectional pairs (where A->B and B->A both exist)
        var bidirectionalPairs = FindBidirectionalPairs(model.Transitions);

        // Collect all back-edges to assign unique offsets
        var backEdges = model.Transitions
            .Where(_ => IsBackEdge(_, stateMap) &&
                        !bidirectionalPairs.Contains(GetPairKey(_.FromId, _.ToId)))
            .OrderBy(_ =>
            {
                if (stateMap.TryGetValue(_.FromId, out var s))
                {
                    return s.Position.X;
                }

                return 0;
            })
            .ToList();

        // Index back-edges once so the per-transition loop is O(E), not O(E²) via IndexOf.
        var backEdgeIndices = new Dictionary<StateTransition, int>();
        for (var i = 0; i < backEdges.Count; i++)
        {
            backEdgeIndices[backEdges[i]] = i;
        }

        foreach (var transition in model.Transitions)
        {
            var pairKey = GetPairKey(transition.FromId, transition.ToId);
            if (bidirectionalPairs.Contains(pairKey))
            {
                // Bidirectional pair - use curves (forward curves left, back curves right)
                var isBackEdge = IsBackEdge(transition, stateMap);
                RenderCurvedTransition(builder, transition, stateMap, isBackEdge, model, 0, options);
            }
            else if (IsBackEdge(transition, stateMap))
            {
                // Single back-edge (no forward counterpart) - curve to the right with offset
                var backEdgeIndex = backEdgeIndices.GetValueOrDefault(transition, -1);
                RenderCurvedTransition(builder, transition, stateMap, isBackEdge: true, model, backEdgeIndex, options);
            }
            else
            {
                // Regular forward transition with no back-edge - straight line
                RenderTransition(builder, transition, stateMap, options);
            }
        }

        // Render nested transitions
        RenderNestedTransitions(builder, model.States, stateMap, model, options);
    }

    /// <summary>
    /// Draws the transitions declared inside each composite, descending into nested composites. Only the
    /// top level was walked before, so a composite inside a composite had its own transitions dropped.
    /// </summary>
    void RenderNestedTransitions(
        SvgBuilder builder,
        List<State> states,
        Dictionary<string, State> outerMap,
        StateModel model,
        RenderOptions options)
    {
        foreach (var state in states)
        {
            if (!state.IsComposite)
            {
                continue;
            }

            var nestedMap = BuildStateMap(state.NestedStates);
            foreach (var map in outerMap)
            {
                nestedMap.TryAdd(map.Key, map.Value);
            }

            var nestedBidirectional = FindBidirectionalPairs(state.NestedTransitions);

            var nestedBackEdges = state.NestedTransitions
                .Where(_ => IsBackEdge(_, nestedMap) && !nestedBidirectional.Contains(GetPairKey(_.FromId, _.ToId)))
                .OrderBy(_ =>
                {
                    if (nestedMap.TryGetValue(_.FromId, out var from))
                    {
                        return from.Position.X;
                    }

                    return 0;
                })
                .ToList();

            foreach (var transition in state.NestedTransitions)
            {
                var pairKey = GetPairKey(transition.FromId, transition.ToId);
                if (nestedBidirectional.Contains(pairKey))
                {
                    var isBackEdge = IsBackEdge(transition, nestedMap);
                    RenderCurvedTransition(builder, transition, nestedMap, isBackEdge, model, 0, options);
                }
                else if (IsBackEdge(transition, nestedMap))
                {
                    var backEdgeIndex = nestedBackEdges.IndexOf(transition);
                    RenderCurvedTransition(builder, transition, nestedMap, isBackEdge: true, model, backEdgeIndex, options);
                }
                else
                {
                    RenderTransition(builder, transition, nestedMap, options);
                }
            }

            RenderNestedTransitions(builder, state.NestedStates, nestedMap, model, options);
        }
    }

    static HashSet<string> FindBidirectionalPairs(List<StateTransition> transitions)
    {
        var pairs = new HashSet<string>();
        var edgeSet = new HashSet<string>();

        foreach (var t in transitions)
        {
            var forward = $"{t.FromId}->{t.ToId}";
            var reverse = $"{t.ToId}->{t.FromId}";

            if (edgeSet.Contains(reverse))
            {
                // Found bidirectional pair
                pairs.Add(GetPairKey(t.FromId, t.ToId));
            }
            edgeSet.Add(forward);
        }

        return pairs;
    }

    static string GetPairKey(string a, string b)
    {
        if (string.Compare(a, b, StringComparison.Ordinal) < 0)
        {
            return $"{a}|{b}";
        }

        return $"{b}|{a}";
    }

    static bool IsBackEdge(StateTransition transition, Dictionary<string, State> stateMap)
    {
        if (!stateMap.TryGetValue(transition.FromId, out var fromState) ||
            !stateMap.TryGetValue(transition.ToId, out var toState))
        {
            return false;
        }

        // Back-edge: source is below target (going upward in the diagram)
        return fromState.Position.Y > toState.Position.Y + 20;
    }

    void RenderCurvedTransition(SvgBuilder builder, StateTransition transition,
        Dictionary<string, State> stateMap, bool isBackEdge, StateModel model, int backEdgeIndex, RenderOptions options)
    {
        if (!stateMap.TryGetValue(transition.FromId, out var fromState) ||
            !stateMap.TryGetValue(transition.ToId, out var toState))
        {
            return;
        }

        if (isBackEdge)
        {
            // Route back-edges around the right side of the diagram
            // Space lines apart enough for labels to be centered on each line without overlap
            // Exclude special states (Start/End) from edge calculation since they may be repositioned
            var normalStates = model.States.Where(_ => _.Type == StateType.Normal).ToList();
            var baseRightEdge = (normalStates.Count > 0 ? normalStates.Max(_ => _.Position.X + _.Width / 2) : 100) + 50;

            // Use spacing of 50px between lines - enough for typical labels
            const int lineSpacing = 50;
            var rightEdge = baseRightEdge + backEdgeIndex * lineSpacing;

            // Back-edges use smooth curves: angle out, go vertical, angle back in
            var startX = fromState.Position.X + fromState.Width / 2;
            // Enter right side of target state - offset each line so they don't overlap
            // Outer lines (higher index, further right) enter higher to avoid crossing
            var endX = toState.Position.X + toState.Width / 2;
            const double entrySpacing = 15.0;
            var endY = toState.Position.Y - backEdgeIndex * entrySpacing;
            // Exit from the right side of the source, clear of anything parked beside it
            var startY = ClearExitY(model, fromState, toState, startX, rightEdge, endY);

            // Radius for the quarter-circle curves at corners
            var curveRadius = CurveRadius(rightEdge - startX, startY - endY);

            // Path: smooth curve out, vertical line, smooth curve in
            // Curves gradually transition - tangent horizontal at state, tangent vertical at line
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M {startX:0.##} {startY:0.##} C {startX + curveRadius:0.##} {startY:0.##}, {rightEdge:0.##} {startY - curveRadius:0.##}, {rightEdge:0.##} {startY - curveRadius * 2:0.##} L {rightEdge:0.##} {endY + curveRadius * 2:0.##} C {rightEdge:0.##} {endY + curveRadius:0.##}, {endX + curveRadius:0.##} {endY:0.##}, {endX:0.##} {endY:0.##}");

            builder.AddPath(path, fill: "none", stroke: "#333", strokeWidth: 1);

            var lineLabel = transition.Label ?? $"{transition.FromId}->{transition.ToId}";
            // Track segments for collision detection (symmetric at both ends)
            // Exit: only track initial horizontal portion before curve rises
            TrackCubic(
                startX, startY,
                startX + curveRadius, startY,
                rightEdge, startY - curveRadius,
                rightEdge, startY - curveRadius * 2,
                lineLabel, transition.FromId, transition.ToId);
            // Vertical segment
            TrackLine(rightEdge, startY - curveRadius * 2, rightEdge, endY + curveRadius * 2, lineLabel, transition.FromId, transition.ToId);
            // Entry: only track final horizontal portion after curve flattens
            TrackCubic(
                rightEdge, endY + curveRadius * 2,
                rightEdge, endY + curveRadius,
                endX + curveRadius, endY,
                endX, endY,
                lineLabel, transition.FromId, transition.ToId);

            // Arrowhead comes in horizontally from the right
            DrawArrowhead(builder, endX + curveRadius, endY, endX, endY);

            // Draw label centered on this back-edge's vertical line
            if (!string.IsNullOrEmpty(transition.Label))
            {
                var labelWidth = MeasureText(transition.Label, options.FontSize - 2) + 8;
                var labelHeight = TransitionLabelHeight(transition.Label, options);

                // Position label centered on the vertical line segment
                // Position at midpoint of the vertical segment
                var labelY = (fromState.Position.Y + toState.Position.Y) / 2;

                // Register this label's position to prevent future overlaps
                placedLabels.Add(new(rightEdge - labelWidth / 2, labelY - labelHeight / 2, labelWidth, labelHeight));

                builder.AddEdgeLabel(
                    rightEdge,
                    labelY,
                    labelWidth,
                    labelHeight,
                    transition.Label,
                    options.FontSize - 2,
                    options.FontFamily,
                    fill: "#666");
                TrackTextBox(rightEdge, labelY, labelWidth, labelHeight, transition.Label);
            }
        }
        else
        {
            // Forward edge (mirror of back-edge) - curves to the LEFT
            // Route around the left side of the diagram
            // Exclude special states (Start/End) from edge calculation
            var normalStates = model.States.Where(_ => _.Type == StateType.Normal).ToList();
            var baseLeftEdge = (normalStates.Count > 0 ? normalStates.Min(_ => _.Position.X - _.Width / 2) : 0) - 50;

            // Use same spacing as back-edges
            const int lineSpacing = 50;
            var leftEdge = baseLeftEdge - backEdgeIndex * lineSpacing;

            var startX = fromState.Position.X - fromState.Width / 2;
            // Enter left side of target state
            var endX = toState.Position.X - toState.Width / 2;
            const double entrySpacing = 15.0;
            var endY = toState.Position.Y + backEdgeIndex * entrySpacing;
            // Exit from the left side of the source, clear of anything parked beside it
            var startY = ClearExitY(model, fromState, toState, startX, leftEdge, endY);

            // Radius for the quarter-circle curves at corners (mirror of back-edge)
            var curveRadius = CurveRadius(startX - leftEdge, endY - startY);

            // Path: smooth curve out to left, vertical line down, smooth curve in
            // Mirror of back-edge algorithm
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M {startX:0.##} {startY:0.##} C {startX - curveRadius:0.##} {startY:0.##}, {leftEdge:0.##} {startY + curveRadius:0.##}, {leftEdge:0.##} {startY + curveRadius * 2:0.##} L {leftEdge:0.##} {endY - curveRadius * 2:0.##} C {leftEdge:0.##} {endY - curveRadius:0.##}, {endX - curveRadius:0.##} {endY:0.##}, {endX:0.##} {endY:0.##}");

            builder.AddPath(path, fill: "none", stroke: "#333", strokeWidth: 1);

            var lineLabel = transition.Label ?? $"{transition.FromId}->{transition.ToId}";
            // Track segments for collision detection (mirror of back-edge)
            TrackCubic(
                startX, startY,
                startX - curveRadius, startY,
                leftEdge, startY + curveRadius,
                leftEdge, startY + curveRadius * 2,
                lineLabel, transition.FromId, transition.ToId);
            TrackLine(leftEdge, startY + curveRadius * 2, leftEdge, endY - curveRadius * 2, lineLabel, transition.FromId, transition.ToId);
            TrackCubic(
                leftEdge, endY - curveRadius * 2,
                leftEdge, endY - curveRadius,
                endX - curveRadius, endY,
                endX, endY,
                lineLabel, transition.FromId, transition.ToId);

            // Arrowhead comes in horizontally from the left
            DrawArrowhead(builder, endX - curveRadius, endY, endX, endY);

            if (!string.IsNullOrEmpty(transition.Label))
            {
                var labelWidth = MeasureText(transition.Label, options.FontSize - 2) + 8;
                var labelHeight = TransitionLabelHeight(transition.Label, options);

                // Position label centered on this edge's vertical line
                var labelY = (fromState.Position.Y + toState.Position.Y) / 2;

                // Register this label's position to prevent future overlaps
                placedLabels.Add(new(leftEdge - labelWidth / 2, labelY - labelHeight / 2, labelWidth, labelHeight));

                builder.AddEdgeLabel(
                    leftEdge,
                    labelY,
                    labelWidth,
                    labelHeight,
                    transition.Label,
                    options.FontSize - 2,
                    options.FontFamily);
                TrackTextBox(leftEdge, labelY, labelWidth, labelHeight, transition.Label);
            }
        }
    }

    /// <summary>
    /// Y at which a routed edge leaves its source. The exit sweeps horizontally out to the corridor at the
    /// source's centre height, so a state parked beside the source is cut through - the final-state marker
    /// sits directly right of Inactive in TransitionLabels, and the reset edge ran through its ring. Slides
    /// the exit along the source's border, nearest the centre first, until the curve it produces is clear.
    /// Keeps the centre when nothing clears, rather than pushing the exit off the border.
    /// </summary>
    static double ClearExitY(StateModel model, State from, State to, double startX, double corridorX, double endY)
    {
        var left = Math.Min(startX, corridorX);
        var right = Math.Max(startX, corridorX);

        var blockers = new List<(double Left, double Top, double Right, double Bottom)>();
        foreach (var state in model.States)
        {
            if (state.Id == from.Id || state.Id == to.Id)
            {
                continue;
            }

            var halfWidth = state.Width / 2;
            if (state.Position.X + halfWidth < left || state.Position.X - halfWidth > right)
            {
                continue;
            }

            var halfHeight = state.Height / 2;
            blockers.Add((
                state.Position.X - halfWidth - exitClearance,
                state.Position.Y - halfHeight - exitClearance,
                state.Position.X + halfWidth + exitClearance,
                state.Position.Y + halfHeight + exitClearance));
        }

        if (blockers.Count == 0)
        {
            return from.Position.Y;
        }

        // Stay far enough inside the source's border that the exit lands on its straight part, not a corner.
        var reach = from.Height / 2 - exitClearance;
        foreach (var candidate in ExitCandidates(from.Position.Y, reach))
        {
            if (ExitCurveClear(startX, candidate, corridorX, endY, blockers))
            {
                return candidate;
            }
        }

        return from.Position.Y;
    }

    static IEnumerable<double> ExitCandidates(double centerY, double reach)
    {
        yield return centerY;

        for (var offset = exitStep; offset <= reach; offset += exitStep)
        {
            yield return centerY - offset;
            yield return centerY + offset;
        }
    }

    /// <summary>
    /// Whether the exit flare leaving <paramref name="startY"/> stays out of every blocker. Samples the same
    /// cubic the path is built from, so the test sees the curve that will actually be drawn rather than a
    /// straight-line approximation of it - the curve climbs away from the source quickly, and treating it as
    /// a horizontal stub rejects exits that are in fact clear.
    /// </summary>
    static bool ExitCurveClear(
        double startX,
        double startY,
        double corridorX,
        double endY,
        List<(double Left, double Top, double Right, double Bottom)> blockers)
    {
        var radius = CurveRadius(Math.Abs(corridorX - startX), endY - startY);
        var towardCorridor = Math.Sign(corridorX - startX);
        var towardTarget = endY < startY ? -1 : 1;

        double x0 = startX, y0 = startY;
        double x1 = startX + towardCorridor * radius, y1 = startY;
        double x2 = corridorX, y2 = startY + towardTarget * radius;
        double x3 = corridorX, y3 = startY + towardTarget * radius * 2;

        const int samples = 24;
        for (var i = 0; i <= samples; i++)
        {
            var t = (double) i / samples;
            var u = 1 - t;
            var x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            var y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;

            foreach (var blocker in blockers)
            {
                if (x > blocker.Left && x < blocker.Right && y > blocker.Top && y < blocker.Bottom)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Corner radius for a routed edge's two quarter-circle flares. Each flare consumes
    /// <c>2 * radius</c> of the vertical run, and the straight segment joins where they end, so a radius
    /// past a quarter of that run puts the second flare's start *above* the first flare's end and the
    /// straight segment doubles back over the label.
    /// </summary>
    static double CurveRadius(double horizontalRun, double verticalRun) =>
        Math.Min(Math.Min(80, horizontalRun / 2), Math.Abs(verticalRun) / 4);

    static Dictionary<string, State> BuildStateMap(List<State> states)
    {
        var map = new Dictionary<string, State>();
        foreach (var state in states)
        {
            map[state.Id] = state;
            if (state.IsComposite)
            {
                foreach (var nested in BuildStateMap(state.NestedStates))
                {
                    map.TryAdd(nested.Key, nested.Value);
                }
            }
        }
        return map;
    }

    void RenderTransition(
        SvgBuilder builder,
        StateTransition transition,
        Dictionary<string, State> stateMap,
        RenderOptions options)
    {
        if (!stateMap.TryGetValue(transition.FromId, out var fromState) ||
            !stateMap.TryGetValue(transition.ToId, out var toState))
        {
            return;
        }

        var (startX, startY) = GetConnectionPoint(fromState, toState);
        var (endX, endY) = GetConnectionPoint(toState, fromState);

        // Check if line would pass through any other state
        var obstacleState = FindObstacleState(startX, startY, endX, endY, transition, stateMap);

        if (obstacleState == null)
        {
            // Draw straight arrow line
            builder.AddLine(startX, startY, endX, endY, stroke: "#333", strokeWidth: 1);

            var lineLabel = transition.Label ?? $"{transition.FromId}->{transition.ToId}";
            TrackLine(startX, startY, endX, endY, lineLabel, transition.FromId, transition.ToId);

            // Draw arrowhead
            DrawArrowhead(builder, startX, startY, endX, endY);

            // Draw label if present
            if (!string.IsNullOrEmpty(transition.Label))
            {
                // Find position that doesn't overlap with states or other labels
                var (labelX, labelY) = FindNonOverlappingLabelPosition(
                    startX, startY, endX, endY, transition.Label, stateMap, options, toState.Type == StateType.End);

                var labelWidth = MeasureText(transition.Label, options.FontSize - 2) + 8;
                var labelHeight = TransitionLabelHeight(transition.Label, options);

                // Register this label's position to prevent future overlaps
                placedLabels.Add(new(labelX - labelWidth / 2, labelY - labelHeight / 2, labelWidth, labelHeight));

                builder.AddEdgeLabel(
                    labelX,
                    labelY,
                    labelWidth,
                    labelHeight,
                    transition.Label,
                    options.FontSize - 2,
                    options.FontFamily);
                TrackTextBox(labelX, labelY, labelWidth, labelHeight, transition.Label);
            }
        }
        else
        {
            // Route around the obstacle
            RenderRoutedTransition(builder, transition, fromState, toState, obstacleState, stateMap, options);
        }
    }

    (double x, double y) FindNonOverlappingLabelPosition(
        double startX, double startY, double endX, double endY,
        string label, Dictionary<string, State> stateMap, RenderOptions options, bool isToEnd)
    {
        var labelWidth = MeasureText(label, options.FontSize - 2) + 8;
        var labelHeight = TransitionLabelHeight(label, options);

        // Estimate maximum bounds from states
        var maxStateX = stateMap.Values.Max(_ => _.Position.X + _.Width / 2);
        var maxStateY = stateMap.Values.Max(_ => _.Position.Y + _.Height / 2);

        // Try different positions along the line and with different offsets
        double[] tValues = isToEnd ? [0.85, 0.7, 0.6, 0.5, 0.4, 0.3] : [0.5, 0.4, 0.6, 0.3, 0.7, 0.25, 0.75];
        double[] yOffsets = [-10, -25, 10, -40, 25, 40, -55, 55];

        foreach (var t in tValues)
        {
            var baseX = startX + t * (endX - startX);
            var baseY = startY + t * (endY - startY);

            foreach (var yOffset in yOffsets)
            {
                var labelX = baseX;
                var labelY = baseY + yOffset;

                // Calculate label bounds with generous margin
                var labelLeft = labelX - labelWidth / 2;
                var labelRight = labelX + labelWidth / 2;
                var labelTop = labelY - labelHeight / 2;
                var labelBottom = labelY + labelHeight / 2;

                // Check overlap with all states - use large margin to account for state labels
                var overlaps = false;
                foreach (var kvp in stateMap)
                {
                    var state = kvp.Value;
                    // Use larger margin (20px) to account for state label text which may extend beyond box
                    const double margin = 20.0;
                    var stateLeft = state.Position.X - state.Width / 2 - margin;
                    var stateRight = state.Position.X + state.Width / 2 + margin;
                    var stateTop = state.Position.Y - state.Height / 2 - margin;
                    var stateBottom = state.Position.Y + state.Height / 2 + margin;

                    if (labelLeft < stateRight && labelRight > stateLeft &&
                        labelTop < stateBottom && labelBottom > stateTop)
                    {
                        overlaps = true;
                        break;
                    }
                }

                // Check overlap with previously placed labels
                if (!overlaps)
                {
                    foreach (var placed in placedLabels)
                    {
                        var placedRight = placed.Left + placed.Width;
                        var placedBottom = placed.Top + placed.Height;

                        if (labelLeft < placedRight && labelRight > placed.Left &&
                            labelTop < placedBottom && labelBottom > placed.Top)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                // Check if label would be outside SVG bounds (estimate bounds from states)
                if (labelLeft < 0 ||
                    labelTop < 0 ||
                    labelRight > maxStateX + 150 ||
                    labelBottom > maxStateY + 100)
                {
                    overlaps = true;
                }

                if (!overlaps)
                {
                    return (labelX, labelY);
                }
            }
        }

        // Fallback: use original position but ensure it's within bounds
        var fallbackT = isToEnd ? 0.85 : 0.5;
        var fallbackX = startX + fallbackT * (endX - startX);
        var fallbackY = startY + fallbackT * (endY - startY) - 10;

        // Ensure fallback doesn't go outside bounds
        fallbackX = Math.Max(labelWidth / 2, Math.Min(maxStateX + 100, fallbackX));
        fallbackY = Math.Max(labelHeight / 2, Math.Min(maxStateY + 50, fallbackY));

        return (fallbackX, fallbackY);
    }

    static State? FindObstacleState(
        double x1,
        double y1,
        double x2,
        double y2,
        StateTransition transition,
        Dictionary<string, State> stateMap)
    {
        // Don't route transitions to end nodes - their position is adjusted to avoid overlap
        if (stateMap.TryGetValue(transition.ToId, out var toState) &&
            toState.Type == StateType.End)
        {
            return null;
        }

        foreach (var kvp in stateMap)
        {
            var state = kvp.Value;
            // Skip source and target states
            if (state.Id == transition.FromId || state.Id == transition.ToId)
            {
                continue;
            }

            // Skip special states (start/end circles are small)
            if (state.Type is StateType.Start or StateType.End)
            {
                continue;
            }

            // Check if line passes through this state
            var left = state.Position.X - state.Width / 2 - 5;
            var right = state.Position.X + state.Width / 2 + 5;
            var top = state.Position.Y - state.Height / 2 - 5;
            var bottom = state.Position.Y + state.Height / 2 + 5;

            // A box holding both ends is the container the transition runs inside, not something in its
            // way. Without this every transition inside a composite treated the composite as an obstacle
            // and was routed out around it.
            if (x1 > left && x1 < right && y1 > top && y1 < bottom &&
                x2 > left && x2 < right && y2 > top && y2 < bottom)
            {
                continue;
            }

            // Sample points along the line
            for (var i = 1; i < 20; i++)
            {
                var t = i / 20.0;
                var px = x1 + t * (x2 - x1);
                var py = y1 + t * (y2 - y1);

                if (px > left && px < right && py > top && py < bottom)
                {
                    return state;
                }
            }
        }

        return null;
    }

    void RenderRoutedTransition(
        SvgBuilder builder,
        StateTransition transition,
        State fromState,
        State toState,
        State obstacle,
        Dictionary<string, State> stateMap,
        RenderOptions options)
    {
        // Connection points
        var startX = fromState.Position.X;
        var startY = fromState.Position.Y + fromState.Height / 2;
        var endX = toState.Position.X;
        // Since we're routing around and approaching from below, connect to BOTTOM of target
        var endY = toState.Type == StateType.End
            ? toState.Position.Y + specialStateSize / 2
            : toState.Position.Y + toState.Height / 2;

        const double margin = 30.0;

        // Find all states that are in the vertical path region (between startX/endX and obstacle)
        // and calculate routeX that avoids them all
        var obstacleLeft = obstacle.Position.X - obstacle.Width / 2;
        var obstacleRight = obstacle.Position.X + obstacle.Width / 2;

        // Determine initial side preference based on closest side of primary obstacle
        var fromX = fromState.Position.X;
        var preferLeft = Math.Abs(fromX - obstacleLeft) < Math.Abs(fromX - obstacleRight);

        // Find leftmost and rightmost extent of all states that might be in the routing path
        var minLeft = obstacleLeft;
        var maxRight = obstacleRight;

        foreach (var kvp in stateMap)
        {
            var state = kvp.Value;
            // Skip source state, but INCLUDE target state in bounds (we need to route around it)
            if (state.Id == transition.FromId)
            {
                continue;
            }

            if (state.Type is StateType.Start or StateType.End)
            {
                continue;
            }

            // Check if this state is in the Y range where we might route
            var stateTop = state.Position.Y - state.Height / 2;
            var routeYRange = Math.Max(startY, endY) + margin * 2;

            if (stateTop < routeYRange)
            {
                // This state might be in our routing path - expand the bounds
                minLeft = Math.Min(minLeft, state.Position.X - state.Width / 2);
                maxRight = Math.Max(maxRight, state.Position.X + state.Width / 2);
            }
        }

        // Route around all states
        var routeX = preferLeft
            ? minLeft - margin
            : maxRight + margin;

        // Create path: down from start, horizontal to route position, down past obstacle and target, then to end
        var obstacleTop = obstacle.Position.Y - obstacle.Height / 2;
        var obstacleBottom = obstacle.Position.Y + obstacle.Height / 2;

        // The horizontal return segment should be below both the obstacle AND the target
        var targetBottom = toState.Type == StateType.End
            ? toState.Position.Y + specialStateSize / 2
            : toState.Position.Y + toState.Height / 2;
        var horizontalY = Math.Max(obstacleBottom, targetBottom) + margin;

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"M {startX:0.##} {startY:0.##} L {startX:0.##} {obstacleTop - margin:0.##} L {routeX:0.##} {obstacleTop - margin:0.##} L {routeX:0.##} {horizontalY:0.##} L {endX:0.##} {horizontalY:0.##} L {endX:0.##} {endY:0.##}");

        builder.AddPath(path, fill: "none", stroke: "#333", strokeWidth: 1);

        var lineLabel = transition.Label ?? $"{transition.FromId}->{transition.ToId}";
        // Track the segments
        TrackLine(startX, startY, startX, obstacleTop - margin, lineLabel, transition.FromId, transition.ToId);
        TrackLine(startX, obstacleTop - margin, routeX, obstacleTop - margin, lineLabel, transition.FromId, transition.ToId);
        TrackLine(routeX, obstacleTop - margin, routeX, horizontalY, lineLabel, transition.FromId, transition.ToId);
        TrackLine(routeX, horizontalY, endX, horizontalY, lineLabel, transition.FromId, transition.ToId);
        TrackLine(endX, horizontalY, endX, endY, lineLabel, transition.FromId, transition.ToId);

        // Draw arrowhead (pointing up since we approach from below)
        DrawArrowhead(builder, endX, horizontalY, endX, endY);

        // Draw label if present
        if (!string.IsNullOrEmpty(transition.Label))
        {
            // Find position that doesn't overlap with states or other labels
            var defaultY = obstacle.Position.Y;
            var (labelX, labelY) = FindNonOverlappingLabelPositionForRouted(
                routeX, defaultY, routeX, obstacleTop - margin, horizontalY, transition.Label, stateMap, options);

            var labelWidth = MeasureText(transition.Label, options.FontSize - 2) + 8;
            var labelHeight = TransitionLabelHeight(transition.Label, options);

            // Register this label's position to prevent future overlaps
            placedLabels.Add(new(labelX - labelWidth / 2, labelY - labelHeight / 2, labelWidth, labelHeight));

            builder.AddEdgeLabel(
                labelX,
                labelY,
                labelWidth,
                labelHeight,
                transition.Label,
                options.FontSize - 2,
                options.FontFamily);
            TrackTextBox(labelX, labelY, labelWidth, labelHeight, transition.Label);
        }
    }

    (double x, double y) FindNonOverlappingLabelPositionForRouted(
        double defaultX, double defaultY, double routeX, double topY, double bottomY,
        string label, Dictionary<string, State> stateMap, RenderOptions options)
    {
        var labelWidth = MeasureText(label, options.FontSize - 2) + 8;
        var labelHeight = TransitionLabelHeight(label, options);

        // Try positions along the vertical route segment
        double[] yPositions = [defaultY, (topY + bottomY) / 2, topY + 30, bottomY - 30, topY + 60, bottomY - 60];
        double[] xOffsets = [0, -50, 50, -100, 100, -150, 150];

        foreach (var yPos in yPositions)
        {
            foreach (var xOffset in xOffsets)
            {
                var labelX = routeX + xOffset;
                var labelY = yPos;

                var labelLeft = labelX - labelWidth / 2;
                var labelRight = labelX + labelWidth / 2;
                var labelTop = labelY - labelHeight / 2;
                var labelBottom = labelY + labelHeight / 2;

                var overlaps = false;
                foreach (var kvp in stateMap)
                {
                    var state = kvp.Value;
                    const double margin = 20.0;
                    var stateLeft = state.Position.X - state.Width / 2 - margin;
                    var stateRight = state.Position.X + state.Width / 2 + margin;
                    var stateTop = state.Position.Y - state.Height / 2 - margin;
                    var stateBottom = state.Position.Y + state.Height / 2 + margin;

                    if (labelLeft < stateRight && labelRight > stateLeft &&
                        labelTop < stateBottom && labelBottom > stateTop)
                    {
                        overlaps = true;
                        break;
                    }
                }

                // Check overlap with previously placed labels
                if (!overlaps)
                {
                    foreach (var placed in placedLabels)
                    {
                        var placedRight = placed.Left + placed.Width;
                        var placedBottom = placed.Top + placed.Height;

                        if (labelLeft < placedRight && labelRight > placed.Left &&
                            labelTop < placedBottom && labelBottom > placed.Top)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (labelLeft < 0 || labelTop < 0)
                {
                    overlaps = true;
                }

                if (!overlaps)
                {
                    return (labelX, labelY);
                }
            }
        }

        return (Math.Max(labelWidth / 2, defaultX), Math.Max(labelHeight / 2, defaultY));
    }

    // Calculate where a line from center to target intersects the node's edge
    static (double x, double y) GetEdgeIntersection(State state, double targetX, double targetY)
    {
        var cx = state.Position.X;
        var cy = state.Position.Y;
        var dx = targetX - cx;
        var dy = targetY - cy;

        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return (cx, cy);
        }

        // For circular nodes (start/end)
        if (state.Type is StateType.Start or StateType.End)
        {
            var angle = Math.Atan2(dy, dx);
            const double radius = specialStateSize / 2;
            return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }

        // For diamond (choice) - edge equation: |x| + |y| = size
        if (state.Type == StateType.Choice)
        {
            // For a diamond, intersection at parameter t where |t*dx| + |t*dy| = size
            var t = specialStateSize / (Math.Abs(dx) + Math.Abs(dy));
            return (cx + dx * t, cy + dy * t);
        }

        // For fork/join (horizontal bar)
        if (state.Type is StateType.Fork or StateType.Join)
        {
            // Always connect from top or bottom of the bar
            var y = dy > 0 ? cy + state.Height / 2 : cy - state.Height / 2;
            // X position along the bar based on target direction
            var x = Math.Clamp(cx + dx * 0.1, cx - state.Width / 2 + 5, cx + state.Width / 2 - 5);
            return (x, y);
        }

        // For rectangular nodes - find edge intersection
        var halfW = state.Width / 2;
        var halfH = state.Height / 2;

        // Calculate intersection with rectangle edges
        var tX = Math.Abs(dx) > 0.001 ? halfW / Math.Abs(dx) : double.MaxValue;
        var tY = Math.Abs(dy) > 0.001 ? halfH / Math.Abs(dy) : double.MaxValue;
        var t2 = Math.Min(tX, tY);

        return (cx + dx * t2, cy + dy * t2);
    }

    // Line targets center of destination, clips at edge of source
    static (double x, double y) GetConnectionPoint(State from, State to) =>
        GetEdgeIntersection(from, to.Position.X, to.Position.Y);

    static void DrawArrowhead(SvgBuilder builder, double fromX, double fromY, double toX, double toY)
    {
        var angle = Math.Atan2(toY - fromY, toX - fromX);
        const int arrowSize = 8;

        var backAngle1 = angle + Math.PI - Math.PI / 6;
        var backAngle2 = angle + Math.PI + Math.PI / 6;

        builder.AddPolygon(
            [
                new(toX, toY),
                new(toX + arrowSize * Math.Cos(backAngle1), toY + arrowSize * Math.Sin(backAngle1)),
                new(toX + arrowSize * Math.Cos(backAngle2), toY + arrowSize * Math.Sin(backAngle2))
            ],
            fill: "#333");
    }

    void RenderNotes(SvgBuilder builder, StateModel model, RenderOptions options)
    {
        var stateMap = BuildStateMap(model.States);

        foreach (var note in model.Notes)
        {
            if (!stateMap.TryGetValue(note.StateId, out var state))
            {
                continue;
            }

            // Calculate note dimensions based on text content
            var noteWidth = Math.Max(noteMinWidth, MeasureText(note.Text, options.FontSize - 2) + notePadding);
            var noteHeight = NoteHeight(note, options);

            // Determine vertical placement based on available space
            var spaceAbove = state.Position.Y;
            var maxY = model.States.Max(_ => _.Position.Y + _.Height / 2);
            var spaceBelow = maxY - state.Position.Y;
            var placeBelow = spaceBelow >= spaceAbove;

            // Position note outside the diagram on its declared side, clear of any edge corridor
            var noteX = NoteX(model, note, state, noteWidth, stateMap);

            var noteY = placeBelow
                ? state.Position.Y + state.Height / 2 + noteVerticalOffset
                : state.Position.Y - state.Height / 2 - noteVerticalOffset - noteHeight;

            // Check for overlaps with other states and adjust position
            const double minGap = 15;
            foreach (var otherState in model.States)
            {
                if (otherState.Id == state.Id) continue;

                var otherTop = otherState.Position.Y - otherState.Height / 2;
                var otherBottom = otherState.Position.Y + otherState.Height / 2;
                var otherLeft = otherState.Position.X - otherState.Width / 2;
                var otherRight = otherState.Position.X + otherState.Width / 2;

                var noteBottom = noteY + noteHeight;
                var noteRight = noteX + noteWidth;

                // Check horizontal overlap
                var horizontalOverlap = noteX < otherRight + minGap && noteRight > otherLeft - minGap;

                if (horizontalOverlap)
                {
                    // If note bottom overlaps with other state top, move note up
                    if (noteBottom > otherTop - minGap && noteY < otherTop)
                    {
                        noteY = otherTop - noteHeight - minGap;
                    }
                    // If note top overlaps with other state bottom, move note down
                    else if (noteY < otherBottom + minGap && noteBottom > otherBottom)
                    {
                        noteY = otherBottom + minGap;
                    }
                }
            }

            // A transition label may already occupy the slot. The note box is opaque, so it would cover the
            // label; step it past the label, further from its state, as long as that keeps it on the canvas.
            foreach (var placed in placedLabels)
            {
                if (noteX >= placed.Left + placed.Width + minGap ||
                    noteX + noteWidth <= placed.Left - minGap ||
                    noteY >= placed.Top + placed.Height + minGap ||
                    noteY + noteHeight <= placed.Top - minGap)
                {
                    continue;
                }

                var clearY = placeBelow
                    ? placed.Top + placed.Height + minGap
                    : placed.Top - noteHeight - minGap;
                if (clearY >= 0 &&
                    clearY + noteHeight <= svgHeight)
                {
                    noteY = clearY;
                }
            }

            // Note box with folded corner
            const int foldSize = 8;
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M{noteX:0.##},{noteY:0.##} L{noteX + noteWidth - foldSize:0.##},{noteY:0.##} L{noteX + noteWidth:0.##},{noteY + foldSize:0.##} L{noteX + noteWidth:0.##},{noteY + noteHeight:0.##} L{noteX:0.##},{noteY + noteHeight:0.##} Z");

            builder.AddPath(path, fill: "#FFFFCC", stroke: "#AAAA33", strokeWidth: 1);

            // Track note as a node for line-under-node detection. The id is what lets the connector below
            // be exempted from its own note while still being checked against everything else.
            var noteId = $"note:{note.StateId}:{note.Text}";
            TrackNode(noteX + noteWidth / 2, noteY + noteHeight / 2, noteWidth, noteHeight, $"Note: {note.Text}", noteId);

            // Fold corner
            builder.AddLine(
                noteX + noteWidth - foldSize,
                noteY,
                noteX + noteWidth - foldSize,
                noteY + foldSize,
                stroke: "#AAAA33",
                strokeWidth: 1);
            builder.AddLine(
                noteX + noteWidth - foldSize,
                noteY + foldSize,
                noteX + noteWidth,
                noteY + foldSize,
                stroke: "#AAAA33",
                strokeWidth: 1);

            // Note text
            builder.AddTextLines(
                noteX + noteWidth / 2,
                noteY + noteHeight / 2,
                note.Text,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily);
            TrackText(noteX + noteWidth / 2, noteY + noteHeight / 2, note.Text, "middle", options.FontSize - 2);

            // Curved dashed line connecting note to state using center-targeting algorithm
            var noteCenterX = noteX + noteWidth / 2;
            var noteCenterY = noteY + noteHeight / 2;

            // State connection point - target note center, clip at state edge
            var (stateConnectX, stateConnectY) = GetEdgeIntersection(state, noteCenterX, noteCenterY);

            // Note connection point - target state center, clip at note edge (rectangle)
            var dx = state.Position.X - noteCenterX;
            var dy = state.Position.Y - noteCenterY;
            var noteHalfW = noteWidth / 2;
            var noteHalfH = noteHeight / 2;
            var tX = Math.Abs(dx) > 0.001 ? noteHalfW / Math.Abs(dx) : double.MaxValue;
            var tY = Math.Abs(dy) > 0.001 ? noteHalfH / Math.Abs(dy) : double.MaxValue;
            var t = Math.Min(tX, tY);
            var noteConnectX = noteCenterX + dx * t;
            var noteConnectY = noteCenterY + dy * t;

            // Draw curved dashed line
            var midY = (stateConnectY + noteConnectY) / 2;
            var curvePath = string.Create(
                CultureInfo.InvariantCulture,
                $"M {stateConnectX:0.##} {stateConnectY:0.##} Q {stateConnectX:0.##} {midY:0.##}, {noteConnectX:0.##} {noteConnectY:0.##}");

            builder.AddPath(curvePath, fill: "none", stroke: "#333", strokeWidth: 1, strokeDasharray: "5,5");

            // The connector is a drawn line like any other. It went untracked, so it was free to run under
            // any state on its way to the note without the self-checks noticing.
            TrackQuadratic(
                stateConnectX, stateConnectY,
                stateConnectX, midY,
                noteConnectX, noteConnectY,
                $"note connector for {state.Id}",
                state.Id,
                noteId);
        }
    }

    // A label with `<br/>` breaks is as wide as its longest line.
    static double MeasureText(string text, double fontSize, bool bold = false) =>
        LabelLines.WidestLength(text) * fontSize * (bold ? 0.7 : 0.6);

    // The height a label's `<br/>` breaks add beyond its first line.
    static double ExtraLines(string? text, double fontSize) =>
        (LabelLines.Count(text) - 1) * fontSize * SvgBuilder.LineHeightFactor;

    static double NoteHeight(StateNote note, RenderOptions options) =>
        singleLineNoteHeight + ExtraLines(note.Text, options.FontSize - 2);

    // The chip behind a transition label: one line is 16 tall, and each further line adds a line pitch.
    static double TransitionLabelHeight(string label, RenderOptions options) =>
        16 + ExtraLines(label, options.FontSize - 2);
}
