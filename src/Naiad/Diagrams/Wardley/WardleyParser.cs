class WardleyParser : IDiagramParser<WardleyModel>
{
    static Parser<WardleyModel> parser;

    static WardleyParser()
    {
        var whitespace = CommonParsers.InlineWhitespace;
        var requiredWhitespace = Literals.AnyOf(" \t");
        var restOfLine = Literals.NoneOf("\r\n", minSize: 0).AsTrimmedString();

        // A statement may be followed by a %% comment on the same line, as Mermaid's own examples are.
        var lineEnd =
            whitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.LineEnd));

        // An unquoted name is words of letters, digits and _()&, each starting with a letter or an open
        // paren, separated by spaces or tabs. A hyphen joins two runs of word characters ("real-time"),
        // so it never starts an arrow such as -> or -.-> as part of the name before it.
        var wordCharacters = Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '(' or ')' or '&');
        var word =
            Capture(wordCharacters.And(ZeroOrMany(Literals.Char('-').And(wordCharacters))))
                .When(static (_, span) => char.IsLetter(span.Span[0]) || span.Span[0] == '(');
        var bareName =
            Capture(word.And(ZeroOrMany(requiredWhitespace.And(word)))).AsString();
        var name = CommonParsers.QuotedString.Or(bareName);

        var digits = Literals.AnyOf("0123456789");
        var unsignedNumber =
            Capture(digits.And(Literals.Char('.').And(digits).Optional()))
                .Then(static _ => double.Parse(_.Span, NumberStyles.Float, CultureInfo.InvariantCulture));

        // Mermaid takes a position either as a fraction (up to 1) or as a percentage (above 1, up to 100).
        var coordinate =
            unsignedNumber
                .Assert(static _ => _ <= 100, static _ => $"A coordinate must be between 0-1 (decimal) or 0-100 (percentage). Received: {_.ToString(CultureInfo.InvariantCulture)}")
                .Then(static _ => _ <= 1 ? _ : _ / 100);

        Parser<(T, T)> Pair<T>(Parser<T> value) =>
            Literals.Char('[')
                .SkipAnd(whitespace)
                .SkipAnd(value)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(','))
                .AndSkip(whitespace)
                .And(value)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(']'));

        // [visibility, evolution]: the visibility comes first, the reverse of an (x, y) pair.
        var position = Pair(coordinate);

        Parser<string> Keyword(string keyword) =>
            whitespace.SkipAnd(Literals.Text(keyword)).AndSkip(requiredWhitespace);

        var title =
            whitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IContent?>(static _ => new TitleItem(_));

        // The accessible title and description have nowhere to go in the rendered map, so they are read and
        // dropped.
        var accessibility =
            OneOf(
                    whitespace
                        .SkipAnd(TextIgnoreCase("accTitle"))
                        .SkipAnd(whitespace)
                        .SkipAnd(Literals.Char(':'))
                        .SkipAnd(restOfLine)
                        .SkipAnd(CommonParsers.LineEnd),
                    whitespace
                        .SkipAnd(TextIgnoreCase("accDescr"))
                        .SkipAnd(whitespace)
                        .SkipAnd(Literals.Char(':'))
                        .SkipAnd(restOfLine)
                        .SkipAnd(CommonParsers.LineEnd),
                    whitespace
                        .SkipAnd(TextIgnoreCase("accDescr"))
                        .SkipAnd(whitespace)
                        .SkipAnd(Literals.Char('{'))
                        .SkipAnd(Literals.NoneOf("}", minSize: 0))
                        .SkipAnd(Literals.Char('}'))
                        .SkipAnd(lineEnd))
                .Then<IContent?>(static _ => null);

        var size =
            Keyword("size")
                .SkipAnd(whitespace)
                .SkipAnd(Pair(unsignedNumber))
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new SizeItem(_.Item1, _.Item2));

        // A stage is a name, optionally where it ends (Genesis@0.25), optionally a second name below it
        // (Genesis / Concept). Mermaid wants the boundary before the second name; after it is accepted too.
        var boundary =
            whitespace.SkipAnd(Literals.Char('@')).SkipAnd(whitespace).SkipAnd(coordinate);
        var stage =
            name
                .And(boundary.Optional())
                .AndSkip(whitespace)
                .And(Literals.Char('/').SkipAnd(whitespace).SkipAnd(name).Optional())
                .And(boundary.Optional())
                .Then(static _ => new Stage(
                    _.Item3.HasValue ? $"{_.Item1.Trim()} / {_.Item3.Value.Trim()}" : _.Item1.Trim(),
                    _.Item2.HasValue ? _.Item2.Value : _.Item4.ToNullable()));

        var evolution =
            Keyword("evolution")
                .SkipAnd(whitespace)
                .SkipAnd(Separated(whitespace.And(Literals.Text("->")).And(whitespace), stage))
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new EvolutionItem(_));

        var anchor =
            Keyword("anchor")
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .And(position)
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new NodeItem(WardleyNodeKind.Anchor, _.Item1, _.Item2.Item1, _.Item2.Item2, []));

        // label [x, y] moves a label from its default place, in pixels.
        var labelOffset =
            Literals.Text("label")
                .SkipAnd(whitespace)
                .SkipAnd(Pair(CommonParsers.SignedDecimal))
                .Then(static _ => new Decoration(_.Item1, _.Item2, null, false));

        var strategy =
            OneOf(
                Literals.Text("build").Then(WardleySourceStrategy.Build),
                Literals.Text("buy").Then(WardleySourceStrategy.Buy),
                Literals.Text("outsource").Then(WardleySourceStrategy.Outsource),
                Literals.Text("market").Then(WardleySourceStrategy.Market));

        var inertia = Literals.Text("inertia").Then(new Decoration(null, null, null, true));

        var parenthesised =
            Literals.Char('(')
                .SkipAnd(whitespace)
                .SkipAnd(
                    strategy.Then(static _ => new Decoration(null, null, _, false))
                        .Or(inertia))
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(')'));

        // Mermaid wants these in the order label, strategy, inertia; any order is accepted here.
        var decoration =
            whitespace.SkipAnd(OneOf(labelOffset, parenthesised, inertia));

        var component =
            Keyword("component")
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .And(position)
                .And(ZeroOrMany(decoration))
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new NodeItem(WardleyNodeKind.Component, _.Item1, _.Item2.Item1, _.Item2.Item2, _.Item3));

        var skipLine =
            whitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Inside a pipeline, a component gives only its evolution: it takes its visibility from the pipeline.
        var pipelineComponent =
            Keyword("component")
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char('['))
                .AndSkip(whitespace)
                .And(coordinate)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(']'))
                .And(whitespace.SkipAnd(labelOffset).Optional())
                .AndSkip(lineEnd)
                .Then<PipelineComponent?>(static _ => new PipelineComponent(
                    _.Item1,
                    _.Item2,
                    _.Item3.HasValue ? _.Item3.Value.OffsetX : null,
                    _.Item3.HasValue ? _.Item3.Value.OffsetY : null));

        var pipeline =
            Keyword("pipeline")
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(lineEnd)
                .And(pipelineComponent.Or(skipLine.Then<PipelineComponent?>(static _ => null)).Many())
                .AndSkip(whitespace)
                .AndSkip(Literals.Char('}'))
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new PipelineItem(
                    _.Item1,
                    _.Item2.Where(static _ => _.HasValue).Select(static _ => _!.Value).ToList()));

        var note =
            Keyword("note")
                .SkipAnd(whitespace)
                .SkipAnd(CommonParsers.QuotedString)
                .AndSkip(whitespace)
                .And(position)
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new NoteItem(_.Item1, _.Item2.Item1, _.Item2.Item2));

        // annotations has to be tried before annotation, which it starts with.
        var annotations =
            whitespace
                .SkipAnd(Literals.Text("annotations"))
                .SkipAnd(whitespace)
                .SkipAnd(position)
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new AnnotationsItem(_.Item1, _.Item2));

        var annotation =
            Keyword("annotation")
                .SkipAnd(whitespace)
                .SkipAnd(CommonParsers.UnsignedInt)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(','))
                .AndSkip(whitespace)
                .And(position)
                .AndSkip(whitespace)
                .And(CommonParsers.QuotedString.Optional())
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new AnnotationItem(_.Item1, _.Item2.Item1, _.Item2.Item2, _.Item3.OrNull()));

        Parser<IContent?> Force(string keyword, bool decelerating) =>
            Keyword(keyword)
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .And(position)
                .AndSkip(lineEnd)
                .Then<IContent?>(_ => new AcceleratorItem(_.Item1, _.Item2.Item1, _.Item2.Item2, decelerating));

        var evolve =
            Keyword("evolve")
                .SkipAnd(whitespace)
                .SkipAnd(name)
                .AndSkip(whitespace)
                .And(coordinate)
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ => new EvolveItem(_.Item1, _.Item2));

        // A link's arrow says whether it is dashed and which ends get arrowheads: +> forward, +< backward,
        // +<> both, and +'label'> (or < or <>) the same with a label on the link.
        var flowDirection =
            OneOf(
                Literals.Text("<>").Then(WardleyFlow.Bidirectional),
                Literals.Text("<").Then(WardleyFlow.Backward),
                Literals.Text(">").Then(WardleyFlow.Forward));

        var labelledFlow =
            Literals.Text("+'")
                .SkipAnd(Literals.NoneOf("'\r\n", minSize: 0).AsString())
                .AndSkip(Literals.Char('\''))
                .And(flowDirection)
                .Then(static _ => new Arrow(false, _.Item2, _.Item1));

        var arrow =
            OneOf(
                labelledFlow,
                Literals.Char('+').SkipAnd(flowDirection).Then(static _ => new Arrow(false, _, null)),
                Literals.Text("-.->").Then(new Arrow(true, WardleyFlow.None, null)),
                Literals.Text("-->").Then(new Arrow(false, WardleyFlow.None, null)),
                Literals.Text("->").Then(new Arrow(false, WardleyFlow.None, null)),
                Literals.Text(">").Then(new Arrow(false, WardleyFlow.None, null)));

        // A flow port may also follow the target (A -> B +>).
        var targetPort =
            whitespace.SkipAnd(Literals.Char('+')).SkipAnd(flowDirection);

        var linkLabel =
            whitespace.SkipAnd(Literals.Char(';')).SkipAnd(restOfLine);

        var link =
            whitespace
                .SkipAnd(name)
                .AndSkip(whitespace)
                .And(arrow)
                .AndSkip(whitespace)
                .And(name)
                .And(targetPort.Optional())
                .And(linkLabel.Optional())
                .AndSkip(lineEnd)
                .Then<IContent?>(static _ =>
                {
                    var (source, linkArrow, target, port, annotation) = _;
                    var flow = linkArrow.Flow == WardleyFlow.None && port.HasValue ? port.Value : linkArrow.Flow;
                    var text = linkArrow.Label ?? (annotation.HasValue && annotation.Value.Length > 0 ? annotation.Value : null);
                    return new LinkItem(source.Trim(), target.Trim(), linkArrow.Dashed, flow, text);
                });

        var content =
            OneOf(
                skipLine.Then<IContent?>(static _ => null),
                title,
                accessibility,
                size,
                evolution,
                anchor,
                component,
                pipeline,
                note,
                annotations,
                annotation,
                Force("accelerator", false),
                Force("deaccelerator", true),
                evolve,
                link);

        parser =
            whitespace
                .SkipAnd(TextIgnoreCase("wardley-beta"))
                .SkipAnd(lineEnd)
                .SkipAnd(content.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.Where(static _ => _ is not null).Select(static _ => _!).ToList()));
    }

    /// <summary>
    /// Builds the map in the order Mermaid does, whatever order the statements came in: anchors before
    /// components (which is the order they are drawn in), and pipelines, links and evolves only once every
    /// node they may refer to is known.
    /// </summary>
    static WardleyModel BuildModel(List<IContent> content)
    {
        var model = new WardleyModel();
        var nodes = new Dictionary<string, WardleyNode>();

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem titleItem:
                    model.Title = titleItem.Text;
                    break;
                case SizeItem sizeItem:
                    model.Width = sizeItem.Width;
                    model.Height = sizeItem.Height;
                    break;
                case EvolutionItem evolutionItem:
                    model.Stages.Clear();
                    model.Stages.AddRange(evolutionItem.Stages.Select(static _ => _.Name));
                    model.StageBoundaries.Clear();
                    model.StageBoundaries.AddRange(evolutionItem.Stages.Where(static _ => _.Boundary.HasValue).Select(static _ => _.Boundary!.Value));
                    break;
            }
        }

        foreach (var nodeItem in content.OfType<NodeItem>().OrderBy(static _ => _.Kind == WardleyNodeKind.Anchor ? 0 : 1))
        {
            var label = nodeItem.Name.Trim();
            var offsetX = nodeItem.Decorations.LastOrDefault(static _ => _.OffsetX.HasValue).OffsetX;
            var offsetY = nodeItem.Decorations.LastOrDefault(static _ => _.OffsetY.HasValue).OffsetY;
            var strategy = nodeItem.Decorations.LastOrDefault(static _ => _.Strategy.HasValue).Strategy;
            var inertia = nodeItem.Decorations.Any(static _ => _.Inertia);

            // Declaring a node again moves it, keeping the label offset it had if the new one sets none.
            var node = AddNode(label, label, nodeItem.Kind, nodeItem.Visibility, nodeItem.Evolution, offsetX, offsetY);
            node.Inertia = inertia;
            node.SourceStrategy = strategy;
        }

        foreach (var noteItem in content.OfType<NoteItem>())
        {
            model.Notes.Add(new()
            {
                Text = noteItem.Text,
                Visibility = noteItem.Visibility,
                Evolution = noteItem.Evolution
            });
        }

        // Mermaid refuses a pipeline whose parent is not a node; here the pipeline is left out instead.
        foreach (var pipelineItem in content.OfType<PipelineItem>())
        {
            var parentId = pipelineItem.Parent.Trim();
            if (!nodes.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            parent.IsPipelineParent = true;
            var pipeline = new WardleyPipeline
            {
                ParentId = parentId
            };
            model.Pipelines.RemoveAll(_ => _.ParentId == parentId);
            model.Pipelines.Add(pipeline);

            foreach (var pipelineComponent in pipelineItem.Components)
            {
                var label = pipelineComponent.Name.Trim();
                var id = $"{parentId}_{label}";
                AddNode(id, label, WardleyNodeKind.PipelineComponent, parent.Visibility, pipelineComponent.Evolution, pipelineComponent.OffsetX, pipelineComponent.OffsetY);
                pipeline.ComponentIds.Add(id);
            }
        }

        foreach (var linkItem in content.OfType<LinkItem>())
        {
            model.Links.Add(new()
            {
                Source = ResolveNodeId(linkItem.Source),
                Target = ResolveNodeId(linkItem.Target),
                Dashed = linkItem.Dashed,
                Flow = linkItem.Flow,
                Label = linkItem.Label
            });
        }

        // An evolve of a node that does not exist draws nothing, as in Mermaid; evolving a node again
        // replaces its earlier trend.
        foreach (var evolveItem in content.OfType<EvolveItem>())
        {
            // By id or label, as a link names its node, so a pipeline component can be evolved too.
            var nodeId = ResolveNodeId(evolveItem.Name.Trim());
            if (!nodes.ContainsKey(nodeId))
            {
                continue;
            }

            var trend = new WardleyTrend
            {
                NodeId = nodeId,
                TargetEvolution = evolveItem.Target
            };
            var index = model.Trends.FindIndex(_ => _.NodeId == nodeId);
            if (index >= 0)
            {
                model.Trends[index] = trend;
            }
            else
            {
                model.Trends.Add(trend);
            }
        }

        if (content.OfType<AnnotationsItem>().FirstOrDefault() is { } annotationsItem)
        {
            model.AnnotationsBox = (annotationsItem.Visibility, annotationsItem.Evolution);
        }

        foreach (var annotationItem in content.OfType<AnnotationItem>())
        {
            model.Annotations.Add(new()
            {
                Number = annotationItem.Number,
                Visibility = annotationItem.Visibility,
                Evolution = annotationItem.Evolution,
                Text = annotationItem.Text
            });
        }

        foreach (var acceleratorItem in content.OfType<AcceleratorItem>())
        {
            var accelerator = new WardleyAccelerator
            {
                Name = acceleratorItem.Name.Trim(),
                Visibility = acceleratorItem.Visibility,
                Evolution = acceleratorItem.Evolution
            };
            (acceleratorItem.Decelerating ? model.Deaccelerators : model.Accelerators).Add(accelerator);
        }

        return model;

        WardleyNode AddNode(string id, string label, WardleyNodeKind kind, double visibility, double evolution, double? offsetX, double? offsetY)
        {
            if (nodes.TryGetValue(id, out var existing))
            {
                existing.Label = label;
                existing.Kind = kind;
                existing.Visibility = visibility;
                existing.Evolution = evolution;
                existing.LabelOffsetX = offsetX ?? existing.LabelOffsetX;
                existing.LabelOffsetY = offsetY ?? existing.LabelOffsetY;
                return existing;
            }

            var node = new WardleyNode
            {
                Id = id,
                Label = label,
                Kind = kind,
                Visibility = visibility,
                Evolution = evolution,
                LabelOffsetX = offsetX,
                LabelOffsetY = offsetY
            };
            nodes.Add(id, node);
            model.Nodes.Add(node);
            return node;
        }

        // A link or an evolve names a node by its id, or failing that by its label, which is how a pipeline component
        // (whose id has its pipeline's name in front) is reached.
        string ResolveNodeId(string name)
        {
            if (nodes.ContainsKey(name))
            {
                return name;
            }

            return model.Nodes.FirstOrDefault(_ => _.Label == name)?.Id ?? name;
        }
    }

    public DiagramParseResult<WardleyModel> Parse(string input) => parser.ParseDiagram(input);

    interface IContent;

    readonly record struct Stage(string Name, double? Boundary);

    readonly record struct Decoration(double? OffsetX, double? OffsetY, WardleySourceStrategy? Strategy, bool Inertia);

    readonly record struct PipelineComponent(string Name, double Evolution, double? OffsetX, double? OffsetY);

    readonly record struct Arrow(bool Dashed, WardleyFlow Flow, string? Label);

    sealed record TitleItem(string Text) : IContent;

    sealed record SizeItem(double Width, double Height) : IContent;

    sealed record EvolutionItem(IReadOnlyList<Stage> Stages) : IContent;

    sealed record NodeItem(WardleyNodeKind Kind, string Name, double Visibility, double Evolution, IReadOnlyList<Decoration> Decorations) : IContent;

    sealed record PipelineItem(string Parent, List<PipelineComponent> Components) : IContent;

    sealed record NoteItem(string Text, double Visibility, double Evolution) : IContent;

    sealed record AnnotationsItem(double Visibility, double Evolution) : IContent;

    sealed record AnnotationItem(int Number, double Visibility, double Evolution, string? Text) : IContent;

    sealed record AcceleratorItem(string Name, double Visibility, double Evolution, bool Decelerating) : IContent;

    sealed record EvolveItem(string Name, double Target) : IContent;

    sealed record LinkItem(string Source, string Target, bool Dashed, WardleyFlow Flow, string? Label) : IContent;
}
