namespace Naiad.Diagrams.Railroad;

/// <summary>
/// What the four railroad grammars share. Unlike most diagrams here they are not line-oriented: Mermaid's
/// grammars skip line breaks as whitespace, so a rule may run over several lines and several rules may share
/// one. Each token is therefore preceded by a gap of whitespace and comments, which each notation extends
/// with its own comment syntax.
/// </summary>
static class RailroadGrammar
{
    static readonly Parser<TextSpan> anyChar = Literals.Pattern(static _ => true, maxSize: 1);

    static readonly Parser<TextSpan> restOfLine = Literals.NoneOf("\r\n", minSize: 0);

    /// <summary>The rest of a line up to a <c>%%</c> comment, which Mermaid's title and accessibility text stop at.</summary>
    static readonly Parser<TextSpan> restOfLineBeforeComment =
        Capture(ZeroOrMany(Not(Literals.Text("%%")).SkipAnd(Literals.NoneOf("\r\n", maxSize: 1))));

    /// <summary>A comment from <paramref name="start"/> to the end of the line.</summary>
    public static Parser<TextSpan> LineComment(string start) =>
        Capture(Literals.Text(start).And(restOfLine));

    /// <summary>A comment between <paramref name="open"/> and <paramref name="close"/>, which may span lines.</summary>
    public static Parser<TextSpan> BlockComment(string open, string close) =>
        Capture(
            Literals.Text(open)
                .And(ZeroOrMany(Not(Literals.Text(close)).SkipAnd(anyChar)))
                .And(Literals.Text(close)));

    /// <summary>Whitespace, line breaks, Mermaid's <c>%%</c> comments and any of <paramref name="comments"/>.</summary>
    public static Parser<TextSpan> Gap(params Parser<TextSpan>[] comments) =>
        Capture(ZeroOrMany(OneOf([Literals.AnyOf(" \t\r\n"), LineComment("%%"), .. comments])));

    /// <summary>
    /// A name made of a first character <paramref name="isStart"/> accepts and any number <paramref name="isPart"/> accepts.
    /// </summary>
    public static Parser<string> Name(Func<char, bool> isStart, Func<char, bool> isPart) =>
        Capture(Literals.Pattern(isStart, maxSize: 1).And(Literals.Pattern(isPart).Optional()))
            .AsString();

    /// <summary>Mermaid's <c>[A-Z_a-z][\w-]*</c>: the rule names of the EBNF, PEG and constructor notations.</summary>
    public static readonly Parser<string> Identifier =
        Name(
            static _ => char.IsAsciiLetter(_) || _ == '_',
            static _ => char.IsAsciiLetterOrDigit(_) || _ is '_' or '-');

    /// <summary>
    /// A string in double or single quotes in which a backslash escapes the next character, as
    /// <c>\n</c>, <c>\r</c> and <c>\t</c> do the usual control characters. The value has the escapes decoded.
    /// </summary>
    public static readonly Parser<string> EscapedString =
        QuotedEscaped('"').Or(QuotedEscaped('\''));

    static Parser<string> QuotedEscaped(char quote) =>
        Literals.Char(quote)
            .SkipAnd(
                Capture(
                    ZeroOrMany(
                        Capture(Literals.Char('\\').And(anyChar))
                            .Or(Literals.NoneOf($"{quote}\\")))))
            .AndSkip(Literals.Char(quote))
            .Then(static _ => Unescape(_.Span));

    static string Unescape(CharSpan content)
    {
        if (!content.Contains('\\'))
        {
            return content.ToString();
        }

        var builder = new StringBuilder(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '\\' &&
                i + 1 < content.Length)
            {
                i++;
                builder.Append(
                    content[i] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        var escaped => escaped
                    });
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The statements that may come between the keyword and the first rule: a title, which is the rest of its
    /// line (quotes around it are dropped), and the accessibility title and description, which are accepted and
    /// dropped as other diagrams here do. Returns the last title given, if any.
    /// </summary>
    public static Parser<string?> Header(Parser<TextSpan> gap, bool unescapeTitle)
    {
        // `title` with nothing after it is an empty title; anything else straight after the keyword makes it
        // part of a longer word, such as a rule called `titles`.
        var title =
            Literals.Text("title")
                .SkipAnd(
                    Literals.AnyOf(" \t").SkipAnd(restOfLineBeforeComment)
                        .Or(Not(Literals.NoneOf("\r\n%", maxSize: 1)).Then(static _ => new TextSpan(""))))
                .Then<string?>(_ => TitleText(_.Span.Trim(), unescapeTitle));

        var accessibilityTitle =
            Literals.Text("accTitle")
                .SkipAnd(Literals.AnyOf(" \t", minSize: 0))
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(restOfLineBeforeComment)
                .Then<string?>(static _ => null);

        var accessibilityDescription =
            Literals.Text("accDescr")
                .SkipAnd(
                    Literals.AnyOf(" \t", minSize: 0)
                        .SkipAnd(Literals.Char(':'))
                        .SkipAnd(restOfLineBeforeComment)
                        .Or(
                            Literals.AnyOf(" \t\r\n", minSize: 0)
                                .SkipAnd(Literals.Char('{'))
                                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                                .AndSkip(Literals.Char('}'))))
                .Then<string?>(static _ => null);

        return gap
            .SkipAnd(OneOf(title, accessibilityTitle, accessibilityDescription))
            .Many()
            .Then(static _ => _.LastOrDefault(static _ => _ is not null));
    }

    static string TitleText(CharSpan text, bool unescape)
    {
        if (text.Length >= 2 &&
            text[0] is '"' or '\'' &&
            text[^1] == text[0])
        {
            var content = text[1..^1];
            return unescape ? Unescape(content) : content.ToString();
        }

        return text.ToString();
    }

    /// <summary>
    /// A whole diagram: the keyword, the header statements, then the rules up to the end of the input. The
    /// rules keep the order they are written in, and a name defined twice is drawn twice, as Mermaid draws it.
    /// </summary>
    public static Parser<RailroadModel> Diagram(string keyword, Parser<TextSpan> gap, Parser<RailroadRule> rule, bool unescapeTitle) =>
        gap
            .SkipAnd(TextIgnoreCase(keyword))
            .SkipAnd(Header(gap, unescapeTitle))
            .And(gap.SkipAnd(rule).Many())
            .AndSkip(gap)
            .AndSkip(CommonParsers.End)
            .Then(
                static _ =>
                {
                    var model = new RailroadModel
                    {
                        Title = _.Item1
                    };
                    model.Rules.AddRange(_.Item2);
                    return model;
                });

    /// <summary>A sequence of one item is just that item, as Mermaid simplifies it.</summary>
    public static RailroadExpression Sequence(IReadOnlyList<RailroadExpression> items) =>
        items.Count == 1 ? items[0] : new RailroadSequence(items);

    /// <summary>A choice of one alternative is just that alternative, as Mermaid simplifies it.</summary>
    public static RailroadExpression Choice(IReadOnlyList<RailroadExpression> alternatives) =>
        alternatives.Count == 1 ? alternatives[0] : new RailroadChoice(alternatives);

    /// <summary>One or more <paramref name="item"/>, each after the first preceded by <paramref name="separator"/>.</summary>
    public static Parser<IReadOnlyList<T>> SeparatedBy<T, TSeparator>(Parser<T> item, Parser<TSeparator> separator) =>
        item
            .And(separator.SkipAnd(item).Many())
            .Then(static _ => (IReadOnlyList<T>) [_.Item1, .. _.Item2]);
}
