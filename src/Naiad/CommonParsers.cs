namespace Naiad;

public static class CommonParsers
{
    // Whitespace
    public static readonly Parser<char, Unit> RequiredWhitespace;

    public static readonly Parser<char, Unit> InlineWhitespace;

    // Line handling
    public static readonly Parser<char, Unit> Newline;

    public static readonly Parser<char, Unit> LineEnd;

    // Comments (Mermaid uses %% for comments)
    public static readonly Parser<char, Unit> Comment;

    // Identifiers
    public static readonly Parser<char, string> Identifier;

    // Quoted strings
    public static readonly Parser<char, string> DoubleQuotedString;

    public static readonly Parser<char, string> QuotedString;

    // Numbers
    public static readonly Parser<char, double> Number;

    public static readonly Parser<char, int> Integer;

    // An unsigned base-10 integer (\d+), parsed straight from the matched span with no intermediate string.
    public static readonly Parser<char, int> UnsignedInt;

    // Mermaid's numeric token [+-]?(?:\d+(?:\.\d+)?|\.\d+): an optional sign, then digits with an
    // optional fraction, or a bare-dot fraction like .5. No exponent (Mermaid rejects 1e5) and the
    // parse is culture-invariant. Shared by the value-bearing diagrams (xychart, sankey, radar, treemap).
    public static readonly Parser<char, double> SignedDecimal;

    // Direction parsing (TB, BT, LR, RL, TD)
    public static readonly Parser<char, Direction> DirectionParser;

    // Indentation for hierarchical diagrams (mindmap, timeline)
    public static readonly Parser<char, int> Indentation;

    /// <summary>
    /// The opening keyword of a diagram that Mermaid introduced with a <c>-beta</c> suffix, accepted with
    /// or without it (<c>packet-beta</c> and <c>packet</c>). The suffixed spelling has to be tried first
    /// and given back whole when it does not match: left to consume the shared prefix and then fail, it
    /// takes the plain spelling down with it.
    /// </summary>
    internal static Parser<char, string> BetaKeyword(string keyword) =>
        Try(CIString($"{keyword}-beta")).Or(CIString(keyword));

    static CommonParsers()
    {
        RequiredWhitespace =
            Token(char.IsWhiteSpace).SkipAtLeastOnce();

        InlineWhitespace =
            Token(_ => _ is ' ' or '\t').SkipMany();

        Newline =
            Try(String("\r\n")).Or(String("\n")).ThenReturn(Unit.Value);

        LineEnd =
            Newline.Or(End);

        Comment =
            String("%%")
                .Then(Token(_ => _ != '\r' && _ != '\n').SkipMany())
                .Then(LineEnd.Optional())
                .ThenReturn(Unit.Value);

        Identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AtLeastOnceString()
                .Labelled("identifier");

        DoubleQuotedString =
            Char('"')
                .Then(Token(_ => _ != '"').ManyString())
                .Before(Char('"'))
                .Labelled("double-quoted string");

        var singleQuotedString = Char('\'')
            .Then(Token(_ => _ != '\'').ManyString())
            .Before(Char('\''))
            .Labelled("single-quoted string");

        QuotedString =
            DoubleQuotedString.Or(singleQuotedString);

        Number =
            Real.Labelled("number");

        Integer =
            Num.Labelled("integer");

        UnsignedInt =
            Digit.SkipAtLeastOnce()
                .Slice((span, _) => int.Parse(span, CultureInfo.InvariantCulture))
                .Labelled("integer");

        // Match [+-]?(\d+(\.\d+)?|\.\d+) structurally, without building intermediate strings, then parse
        // the matched input span directly. Slice hands the selector the ReadOnlySpan<char> the parser
        // consumed, so a number costs zero string allocations (vs. the previous build-string-then-parse).
        SignedDecimal =
            OneOf(Char('+'), Char('-')).Optional()
                .Then(OneOf(
                    Digit.SkipAtLeastOnce().Before(Try(Char('.').Then(Digit.SkipAtLeastOnce())).Optional()),
                    Char('.').Then(Digit.SkipAtLeastOnce())))
                .Slice((span, _) => double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture))
                .Labelled("number");

        DirectionParser =
            OneOf(
                Try(String("TB")).ThenReturn(Direction.TopToBottom),
                Try(String("TD")).ThenReturn(Direction.TopToBottom),
                Try(String("BT")).ThenReturn(Direction.BottomToTop),
                Try(String("LR")).ThenReturn(Direction.LeftToRight),
                String("RL").ThenReturn(Direction.RightToLeft)
            ).Labelled("direction");

        Indentation =
            Token(_ => _ is ' ' or '\t')
                .Many()
                .Select(chars =>
                {
                    var level = 0;
                    foreach (var c in chars)
                    {
                        level += c == '\t' ? 4 : 1;
                    }

                    return level;
                });
    }
}
