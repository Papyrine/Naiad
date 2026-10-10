class TreemapParser : IDiagramParser<TreemapModel>
{
    // Node line
    internal record NodeLine(int Indent, string Name, double? Value, string? CssClass);

    static Parser<char, TreemapModel> parser;

    static TreemapParser()
    {
        // Quoted string: "text"
        var quotedString =
            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"'));

        // Number
        var number = CommonParsers.SignedDecimal;

        // CSS class: :::className
        var cssClass =
            String(":::").Then(Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString());

        var nodeLineParser =
            from indent in CommonParsers.Indentation
            from name in quotedString
            from value in (
                from _ in CommonParsers.InlineWhitespace
                from __ in Char(':')
                from ___ in CommonParsers.InlineWhitespace
                from v in number
                select v
            ).Optional()
            from cssClassName in (
                from _ in CommonParsers.InlineWhitespace
                from cls in cssClass
                select cls
            ).Optional()
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.LineEnd
            select new NodeLine(indent, name, value.GetValueOrDefault(), cssClassName.GetValueOrDefault());

        // Skip line
        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        // Content item
        var contentItem =
            Try(nodeLineParser.Select<NodeLine?>(_ => _))
                .Or(skipLine.ThenReturn<NodeLine?>(null));

        parser =
            from whitespance in CommonParsers.InlineWhitespace
            from ciString in CommonParsers.BetaKeyword("treemap")
            from innerWhitespace in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            from lines in contentItem.ManyThen(End)
            select BuildModel(lines.Item1.Where(_ => _ != null).ToList());
    }

    static TreemapModel BuildModel(List<NodeLine> lines)
    {
        var model = new TreemapModel();
        var stack = new Stack<(TreemapNode node, int indent)>();

        foreach (var line in lines)
        {
            var node = new TreemapNode
            {
                Name = line.Name,
                Value = line.Value,
                CssClass = line.CssClass
            };

            // Pop nodes from stack until we find parent
            while (stack.TryPeek(out var top) && top.indent >= line.Indent)
            {
                stack.Pop();
            }

            if (stack.TryPeek(out var parent))
            {
                // Child of current parent
                parent.node.Children.Add(node);
            }
            else
            {
                // Root level node
                model.RootNodes.Add(node);
            }

            // Push this node as potential parent
            stack.Push((node, line.Indent));
        }

        return model;
    }

    public Result<char, TreemapModel> Parse(string input) => parser.Parse(input);
}
