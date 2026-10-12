class BlockParser : IDiagramParser<BlockModel>
{
    static Parser<BlockModel> parser;

    static BlockParser()
    {
        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString();

        // Label content (text inside shape brackets)
        var labelContent =
            Literals.NoneOf("\"])}", minSize: 0).AsString();

        var quotedLabel =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        // A label that opens with a quote is a quoted one: left unclosed, it is not read as plain text
        // instead.
        var unquoted =
            Not(Literals.Char('"'));

        // `columns auto` is the default spelled out: no fixed count.
        var columnsParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("columns"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(CommonParsers.UnsignedInt.Or(TextIgnoreCase("auto").Then(0)))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd);

        // Rectangle shape: ["label"] or [label]
        var rectangleShape =
            Literals.Char('[')
                .SkipAnd(quotedLabel.Or(labelContent))
                .AndSkip(Literals.Char(']'))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Rectangle));

        // Rounded shape: ("label") or (label)
        var roundedShape =
            Literals.Char('(')
                .SkipAnd(quotedLabel.Or(unquoted.SkipAnd(Literals.NoneOf(")", minSize: 0).AsString())))
                .AndSkip(Literals.Char(')'))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Rounded));

        // Stadium shape: (["label"]) or ([label])
        var stadiumShape =
            Literals.Text("([")
                .SkipAnd(quotedLabel.Or(unquoted.SkipAnd(Literals.NoneOf("]", minSize: 0).AsString())))
                .AndSkip(Literals.Text("])"))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Stadium));

        // Circle shape: (("label")) or ((label))
        var circleShape =
            Literals.Text("((")
                .SkipAnd(quotedLabel.Or(unquoted.SkipAnd(Literals.NoneOf(")", minSize: 0).AsString())))
                .AndSkip(Literals.Text("))"))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Circle));

        // Diamond shape: {"label"} or {label}
        var diamondShape =
            Literals.Char('{')
                .WhenFollowedBy(Literals.NoneOf("{", minSize: 1, maxSize: 1))
                .SkipAnd(quotedLabel.Or(unquoted.SkipAnd(Literals.NoneOf("}", minSize: 0).AsString())))
                .AndSkip(Literals.Char('}'))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Diamond));

        // Hexagon shape: {{"label"}} or {{label}}
        var hexagonShape =
            Literals.Text("{{")
                .SkipAnd(quotedLabel.Or(unquoted.SkipAnd(Literals.NoneOf("}", minSize: 0).AsString())))
                .AndSkip(Literals.Text("}}"))
                .Then(static _ => (label: _.Trim(), shape: BlockShape.Hexagon));

        // Shape parser (order matters - more specific first)
        var shapeParser =
            OneOf(
                stadiumShape,
                circleShape,
                hexagonShape,
                diamondShape,
                roundedShape,
                rectangleShape
            );

        // Span: :N
        var spanParser =
            Literals.Char(':').SkipAnd(CommonParsers.UnsignedInt);

        // Block element: id["label"]:2. A bare `space` or `space:2` is a gap of that many columns.
        var elementParser =
            identifier
                .And(shapeParser.Optional())
                .And(spanParser.Optional())
                .Map((id, shape, span) =>
                    new BlockElement
                    {
                        Id = id,
                        Label = shape.HasValue ? shape.Value.label : id,
                        Shape = shape.HasValue ? shape.Value.shape : BlockShape.Rectangle,
                        Span = Math.Max(1, span.OrSome(1)),
                        IsSpace = !shape.HasValue && id == "space"
                    });

        // Elements, separated by whitespace. A line break is whitespace too, so the elements of the lines
        // that follow join the same list, and the list has to run to the end of the input: whatever comes
        // after an element and is not another element fails the parse.
        var elementsLineParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Separated(CommonParsers.RequiredWhitespace, elementParser))
                .AndSkip(CommonParsers.End)
                .Then(static _ => _.ToList());

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Content item
        var contentItem =
            OneOf(
                columnsParser.Then<IBlockContent?>(static _ => new ColumnsItem(_)),
                elementsLineParser.Then<IBlockContent?>(static _ => new ElementsItem(_)),
                skipLine.Then<IBlockContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("block"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static BlockModel BuildModel(IEnumerable<IBlockContent?> content)
    {
        var model = new BlockModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case ColumnsItem columns:
                    model.Columns = columns.Count;
                    break;

                case ElementsItem elements:
                    model.Elements.AddRange(elements.Elements);
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<BlockModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IBlockContent;
    readonly record struct ColumnsItem(int Count) : IBlockContent;
    readonly record struct ElementsItem(List<BlockElement> Elements) : IBlockContent;
}
