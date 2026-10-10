class BlockParser : IDiagramParser<BlockModel>
{
    static Parser<char, BlockModel> parser;

    static BlockParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        // Label content (text inside shape brackets)
        var labelContent =
            Token(_ => _ != '"' && _ != ']' && _ != ')' && _ != '}').ManyString();

        var quotedLabel =
            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"'));

        var columnsParser =
            from inlineWhitespace in CommonParsers.InlineWhitespace
            from columns in CIString("columns")
            from rRequiredWhitespace in CommonParsers.RequiredWhitespace
            from num in CommonParsers.UnsignedInt
            from innerInlineWhitespace in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            select num;

        // Rectangle shape: ["label"] or [label]
        var rectangleShape =
            from left in Char('[')
            from label in quotedLabel.Or(labelContent)
            from right in Char(']')
            select (label: label.Trim(), shape: BlockShape.Rectangle);

        // Rounded shape: ("label") or (label)
        var roundedShape =
            from left in Char('(')
            from label in quotedLabel.Or(Token(_ => _ != ')').ManyString())
            from right in Char(')')
            select (label: label.Trim(), shape: BlockShape.Rounded);

        // Stadium shape: (["label"]) or ([label])
        var stadiumShape =
            from left in String("([")
            from label in quotedLabel.Or(Token(_ => _ != ']').ManyString())
            from right in String("])")
            select (label: label.Trim(), shape: BlockShape.Stadium);

        // Circle shape: (("label")) or ((label))
        var circleShape =
            from left in String("((")
            from label in quotedLabel.Or(Token(_ => _ != ')').ManyString())
            from right in String("))")
            select (label: label.Trim(), shape: BlockShape.Circle);

        // Diamond shape: {"label"} or {label}
        var diamondShape =
            from left in Char('{')
            from notDouble in Lookahead(AnyCharExcept('{'))
            from label in quotedLabel.Or(Token(_ => _ != '}').ManyString())
            from right in Char('}')
            select (label: label.Trim(), shape: BlockShape.Diamond);

        // Hexagon shape: {{"label"}} or {{label}}
        var hexagonShape =
            from left in String("{{")
            from label in quotedLabel.Or(Token(_ => _ != '}').ManyString())
            from right in String("}}")
            select (label: label.Trim(), shape: BlockShape.Hexagon);

        // Shape parser (order matters - more specific first)
        var shapeParser =
            OneOf(
                Try(stadiumShape),
                Try(circleShape),
                Try(hexagonShape),
                Try(diamondShape),
                Try(roundedShape),
                Try(rectangleShape)
            );

        // Span: :N
        var spanParser =
            from _ in Char(':')
            from num in CommonParsers.UnsignedInt
            select num;

        // Block element: id["label"]:2
        var elementParser =
            from id in identifier
            from shape in shapeParser.Optional()
            from span in spanParser.Optional()
            select new BlockElement
            {
                Id = id,
                Label = shape.HasValue ? shape.Value.label : id,
                Shape = shape.HasValue ? shape.Value.shape : BlockShape.Rectangle,
                Span = span.GetValueOrDefault(1)
            };

        // Elements on a line (space separated)
        var elementsLineParser =
            from _ in CommonParsers.InlineWhitespace
            from elements in elementParser.SeparatedAtLeastOnce(CommonParsers.RequiredWhitespace)
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            select elements.ToList();

        // Skip line (comments, empty lines)
        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        // Content item
        var contentItem =
            OneOf(
                Try(columnsParser.Select<IBlockContent?>(_ => new ColumnsItem(_))),
                Try(elementsLineParser.Select<IBlockContent?>(_ => new ElementsItem(_))),
                skipLine.ThenReturn<IBlockContent?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.BetaKeyword("block")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1);
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

    public Result<char, BlockModel> Parse(string input) => parser.Parse(input);

    internal interface IBlockContent;
    readonly record struct ColumnsItem(int Count) : IBlockContent;
    readonly record struct ElementsItem(List<BlockElement> Elements) : IBlockContent;
}
