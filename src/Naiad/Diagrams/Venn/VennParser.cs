class VennParser : IDiagramParser<VennModel>
{
    static Parser<VennModel> parser;

    static VennParser()
    {
        var space = CommonParsers.InlineWhitespace;
        var requiredSpace = Literals.AnyOf(" \t");
        var lineTail = space.SkipAnd(CommonParsers.Comment.Or(CommonParsers.LineEnd));
        var comma = space.SkipAnd(Literals.Char(',')).AndSkip(space);

        // Mermaid's identifier token: [A-Za-z_][A-Za-z0-9\-_]*, or a double-quoted string.
        var bareIdentifier =
            Capture(
                    Literals.Pattern(static _ => char.IsLetter(_) || _ == '_', maxSize: 1)
                        .And(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-', minSize: 0)))
                .AsString();
        var identifier = CommonParsers.DoubleQuotedString.Or(bareIdentifier);
        var identifierList = Separated(comma, identifier);

        // A text node's own identifier may also be a number.
        var nodeId =
            identifier.Or(
                Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '-' or '.' or '+').AsString());

        // ["quoted label"] keeps its text as written; [bare label] is trimmed.
        var bracketLabel =
            Literals.Text("[\"")
                .SkipAnd(Literals.NoneOf("\"\r\n", minSize: 0))
                .AndSkip(Literals.Text("\"]"))
                .Or(
                    Literals.Char('[')
                        .SkipAnd(Literals.NoneOf("]\"\r\n"))
                        .AndSkip(Literals.Char(']')))
                .Then(static _ => Unquote(_.Span.ToString()));
        var optionalLabel = space.SkipAnd(bracketLabel).Optional();

        var size =
            space.SkipAnd(Literals.Char(':'))
                .SkipAnd(space)
                .SkipAnd(CommonParsers.SignedDecimal)
                .Optional();

        var setStatement =
            TextIgnoreCase("set")
                .SkipAnd(requiredSpace)
                .SkipAnd(identifier)
                .And(optionalLabel)
                .And(size)
                .Map<string, Option<string>, Option<double>, IStatement>(
                    static (id, label, value) => new AreaStatement([id], label.OrNull(), value.ToNullable()));

        var unionStatement =
            TextIgnoreCase("union")
                .SkipAnd(requiredSpace)
                .SkipAnd(identifierList.Assert(static _ => _.Count >= 2, "union requires multiple identifiers"))
                .And(optionalLabel)
                .And(size)
                .Map<IReadOnlyList<string>, Option<string>, Option<double>, IStatement>(
                    static (ids, label, value) => new AreaStatement(ids, label.OrNull(), value.ToNullable()));

        // `text A,B id["label"]` names the area it goes in; `text id["label"]`, indented under a set or
        // union, goes in that one. The explicit form has to be tried first, since the short form would
        // take its first identifier as the node's.
        var explicitText =
            identifierList
                .AndSkip(requiredSpace)
                .And(nodeId)
                .And(optionalLabel)
                .Map<IReadOnlyList<string>, string, Option<string>, IStatement>(
                    static (sets, id, label) => new TextStatement(sets, id, label.OrNull()));
        var implicitText =
            nodeId
                .And(optionalLabel)
                .Map<string, Option<string>, IStatement>(static (id, label) => new TextStatement(null, id, label.OrNull()));
        var textStatement =
            TextIgnoreCase("text")
                .SkipAnd(requiredSpace)
                .SkipAnd(explicitText.Or(implicitText));

        // A style value is a quoted string or a run of words such as `#ff6b6b`, `2px` or `rgb(1, 2, 3)`;
        // the commas inside a colour function belong to it rather than separating properties.
        var valueToken =
            Capture(
                    Literals.Pattern(static _ => !char.IsWhiteSpace(_) && _ is not (',' or ':' or '"' or '(' or ')' or '%'))
                        .And(
                            Literals.Char('(')
                                .And(Literals.NoneOf(")\r\n", minSize: 0))
                                .And(Literals.Char(')'))
                                .Optional()))
                .AsString();
        var styleValue =
            CommonParsers.DoubleQuotedString.Or(
                Separated(requiredSpace, valueToken).Then(static _ => string.Join(' ', _)));
        var styleField =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '-' or '_')
                .AsString()
                .AndSkip(space)
                .AndSkip(Literals.Char(':'))
                .AndSkip(space)
                .And(styleValue);
        var styleStatement =
            TextIgnoreCase("style")
                .SkipAnd(requiredSpace)
                .SkipAnd(identifierList)
                .AndSkip(requiredSpace)
                .And(Separated(comma, styleField))
                .Map<IReadOnlyList<string>, IReadOnlyList<(string, string)>, IStatement>(
                    static (targets, fields) => new StyleStatement(targets, fields));

        var titleStatement =
            TextIgnoreCase("title")
                .SkipAnd(requiredSpace)
                .SkipAnd(Literals.NoneOf("\r\n", minSize: 0))
                .Then<IStatement>(static _ => new TitleStatement(Unquote(_.Span.ToString())));

        var statementLine =
            space.SkipAnd(OneOf(titleStatement, setStatement, unionStatement, textStatement, styleStatement))
                .AndSkip(lineTail)
                .Then<IStatement?>(static _ => _);

        var skipLine =
            space.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline))
                .Then<IStatement?>(static _ => null);

        parser =
            space
                .SkipAnd(TextIgnoreCase("venn-beta"))
                .SkipAnd(lineTail)
                .SkipAnd(skipLine.Or(statementLine).Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_));
    }

    /// <summary>
    /// Applies the statements in order, as Mermaid's database does: a union may only name sets declared
    /// before it, and a text node without sets of its own goes in the set or union declared last.
    /// </summary>
    static VennModel BuildModel(IReadOnlyList<IStatement?> statements)
    {
        var model = new VennModel();
        var knownSets = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<string>? currentSets = null;

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TitleStatement title:
                    model.Title = title.Text;
                    break;

                case AreaStatement area:
                    if (area.Sets.Count > 1)
                    {
                        var unknown = area.Sets.Where(_ => !knownSets.Contains(_)).ToList();
                        if (unknown.Count > 0)
                        {
                            throw new MermaidParseException($"unknown set identifier: {string.Join(", ", unknown)}");
                        }
                    }
                    else
                    {
                        knownSets.Add(area.Sets[0]);
                    }

                    var sets = Sorted(area.Sets);
                    currentSets = sets;
                    model.Areas.Add(
                        new()
                        {
                            Sets = sets,
                            Label = string.IsNullOrEmpty(area.Label) ? null : area.Label,
                            Size = area.Size ?? 10 / Math.Pow(sets.Length, 2)
                        });
                    break;

                case TextStatement text:
                    var target = text.Sets is null ? currentSets : Sorted(text.Sets);
                    if (target is null)
                    {
                        throw new MermaidParseException($"text '{text.Id}' requires a set or union to go in");
                    }

                    model.TextNodes.Add(
                        new()
                        {
                            Sets = target,
                            Id = text.Id,
                            Label = string.IsNullOrEmpty(text.Label) ? null : text.Label
                        });
                    break;

                case StyleStatement style:
                    var entry = new VennStyle
                    {
                        Targets = Sorted(style.Targets)
                    };
                    foreach (var (key, value) in style.Fields)
                    {
                        entry.Properties[key] = Unquote(value);
                    }

                    model.Styles.Add(entry);
                    break;
            }
        }

        return model;
    }

    // Mermaid sorts identifiers with JavaScript's default sort, which compares UTF-16 code units.
    static string[] Sorted(IEnumerable<string> identifiers) =>
        identifiers.Order(StringComparer.Ordinal).ToArray();

    static string Unquote(string text)
    {
        var trimmed = text.Trim();
        return trimmed is ['"', .., '"'] ? trimmed[1..^1] : trimmed;
    }

    public DiagramParseResult<VennModel> Parse(string input) => parser.ParseDiagram(input);

    interface IStatement;

    sealed record TitleStatement(string Text) : IStatement;

    sealed record AreaStatement(IReadOnlyList<string> Sets, string? Label, double? Size) : IStatement;

    sealed record TextStatement(IReadOnlyList<string>? Sets, string Id, string? Label) : IStatement;

    sealed record StyleStatement(IReadOnlyList<string> Targets, IReadOnlyList<(string, string)> Fields) : IStatement;
}
