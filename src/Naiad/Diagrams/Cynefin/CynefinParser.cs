class CynefinParser : IDiagramParser<CynefinModel>
{
    static Parser<CynefinModel> parser;

    static CynefinParser()
    {
        // Mermaid's grammar is case-sensitive throughout, keywords and domain names alike. A domain name
        // may run straight into `-->`, so only a word character after it makes it part of a longer word.
        var nameCharacter = Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_', maxSize: 1);

        var domainName =
            OneOf(
                    Literals.Text("complicated").Then(CynefinDomainName.Complicated),
                    Literals.Text("complex").Then(CynefinDomainName.Complex),
                    Literals.Text("clear").Then(CynefinDomainName.Clear),
                    Literals.Text("chaotic").Then(CynefinDomainName.Chaotic),
                    Literals.Text("confusion").Then(CynefinDomainName.Confusion))
                .AndSkip(Not(nameCharacter));

        // A string in double or single quotes, in which a backslash escapes the character after it.
        var doubleQuoted =
            Literals.Char('"')
                .And(ZeroOrMany(Literals.NoneOf("\"\\\r\n").Or(Capture(Literals.Char('\\').And(Literals.NoneOf("\r\n", maxSize: 1))))))
                .And(Literals.Char('"'));
        var singleQuoted =
            Literals.Char('\'')
                .And(ZeroOrMany(Literals.NoneOf("'\\\r\n").Or(Capture(Literals.Char('\\').And(Literals.NoneOf("\r\n", maxSize: 1))))))
                .And(Literals.Char('\''));
        var quotedString =
            Capture(doubleQuoted).Or(Capture(singleQuoted))
                .Then(static _ => Unescape(_.Span[1..^1]));

        // A statement may be followed by a comment on the same line.
        var endOfLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.LineEnd));

        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var restOfLine = Literals.NoneOf("\r\n", minSize: 0);

        // `title` on its own sets an empty title. A comment after the text is not part of it.
        var titleStatement =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("title"))
                .SkipAnd(Literals.AnyOf(" \t", maxSize: 1).SkipAnd(restOfLine).Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Then<object?>(static _ => new TitleStatement(StripComment(_.HasValue ? _.Value.Span : [])));

        // The accessibility title and description change nothing that is drawn; naming them keeps them
        // from failing the parse. `accDescr { ... }` may run over several lines.
        var accessibilityStatement =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    Literals.Text("accDescr")
                        .AndSkip(CommonParsers.InlineWhitespace)
                        .AndSkip(Literals.Char('{'))
                        .AndSkip(Literals.NoneOf("}", minSize: 0))
                        .AndSkip(Literals.Char('}'))
                        .AndSkip(endOfLine)
                        .Or(
                            Literals.Text("accTitle").Or(Literals.Text("accDescr"))
                                .AndSkip(CommonParsers.InlineWhitespace)
                                .AndSkip(Literals.Char(':'))
                                .AndSkip(restOfLine)
                                .AndSkip(CommonParsers.LineEnd)))
                .Then<object?>(static _ => null);

        var transitionLabel =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(quotedString);

        var transitionStatement =
            CommonParsers.InlineWhitespace
                .SkipAnd(domainName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Text("-->"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(domainName)
                .And(transitionLabel.Optional())
                .AndSkip(endOfLine)
                .Then<object?>(static _ => new TransitionStatement(_.Item1, _.Item2, _.Item3.OrNull()));

        // Items are quoted strings. Mermaid's grammar ignores whitespace and line breaks between them, so
        // several may share a line, the domain's own line included.
        var itemsOnLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(quotedString)
                .Many();

        var itemLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(quotedString)
                .And(itemsOnLine)
                .AndSkip(endOfLine)
                .Then(static _ => (IReadOnlyList<string>) [_.Item1, .._.Item2]);

        var blockBody =
            itemLine
                .Or(skipLine.Then(static _ => (IReadOnlyList<string>) []))
                .Many();

        var domainBlock =
            CommonParsers.InlineWhitespace
                .SkipAnd(domainName)
                .And(itemsOnLine)
                .AndSkip(endOfLine)
                .And(blockBody)
                .Then<object?>(static _ => new DomainStatement(_.Item1, [.._.Item2, .._.Item3.SelectMany(static _ => _)]));

        // A transition starts with a domain name too, so it is tried before a domain block.
        var statement =
            OneOf(
                titleStatement,
                accessibilityStatement,
                transitionStatement,
                domainBlock,
                skipLine.Then<object?>(static _ => null));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("cynefin-beta"))
                .SkipAnd(Literals.Char(':').Optional())
                .SkipAnd(endOfLine)
                .SkipAnd(statement.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static CynefinModel BuildModel(IReadOnlyList<object?> statements)
    {
        var model = new CynefinModel();
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TitleStatement title:
                    model.Title = title.Text;
                    break;
                case DomainStatement block:
                    // A later block for the same domain replaces the earlier one, as in Mermaid's database.
                    var domain = new CynefinDomain
                    {
                        Name = block.Name
                    };
                    domain.Items.AddRange(block.Items);
                    model.Domains[block.Name] = domain;
                    break;
                // A domain moving into itself means nothing, and Mermaid drops it.
                case TransitionStatement transition when transition.From != transition.To:
                    model.Transitions.Add(
                        new()
                        {
                            From = transition.From,
                            To = transition.To,
                            Label = string.IsNullOrEmpty(transition.Label) ? null : transition.Label
                        });
                    break;
            }
        }

        return model;
    }

    static string StripComment(ReadOnlySpan<char> text)
    {
        var comment = text.IndexOf("%%");
        return (comment < 0 ? text : text[..comment]).Trim().ToString();
    }

    /// <summary>
    /// Resolves backslash escapes as Langium's string converter does: the usual control characters by
    /// letter, and any other escaped character as itself.
    /// </summary>
    static string Unescape(ReadOnlySpan<char> text)
    {
        if (!text.Contains('\\'))
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\' ||
                i == text.Length - 1)
            {
                builder.Append(ch);
                continue;
            }

            var escaped = text[++i];
            builder.Append(
                escaped switch
                {
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '0' => '\0',
                    _ => escaped
                });
        }

        return builder.ToString();
    }

    public DiagramParseResult<CynefinModel> Parse(string input) => parser.ParseDiagram(input);

    record TitleStatement(string Text);

    record DomainStatement(CynefinDomainName Name, List<string> Items);

    record TransitionStatement(CynefinDomainName From, CynefinDomainName To, string? Label);
}
