namespace Naiad;

static class CommonParsers
{
    // Whitespace
    public static readonly Parser<TextSpan> RequiredWhitespace;

    public static readonly Parser<TextSpan> InlineWhitespace;

    // Line handling
    public static readonly Parser<TextSpan> Newline;

    public static readonly Parser<TextSpan> LineEnd;

    // The end of the input. Matches nothing, and only where nothing is left.
    public static readonly Parser<TextSpan> End;

    // Comments (Mermaid uses %% for comments)
    public static readonly Parser<TextSpan> Comment;

    // Identifiers
    public static readonly Parser<string> Identifier;

    // Quoted strings
    public static readonly Parser<string> DoubleQuotedString;

    public static readonly Parser<string> QuotedString;

    // Numbers
    public static readonly Parser<double> Number;

    public static readonly Parser<int> Integer;

    // An unsigned base-10 integer (\d+), parsed straight from the matched span with no intermediate string.
    public static readonly Parser<int> UnsignedInt;

    // Mermaid's numeric token [+-]?(?:\d+(?:\.\d+)?|\.\d+): an optional sign, then digits with an
    // optional fraction, or a bare-dot fraction like .5. No exponent (Mermaid rejects 1e5) and the
    // parse is culture-invariant. Shared by the value-bearing diagrams (xychart, sankey, radar, treemap).
    public static readonly Parser<double> SignedDecimal;

    // Direction parsing (TB, BT, LR, RL, TD)
    public static readonly Parser<Direction> DirectionParser;

    // Indentation for hierarchical diagrams (mindmap, timeline)
    public static readonly Parser<int> Indentation;

    /// <summary>
    /// The opening keyword of a diagram that Mermaid introduced with a <c>-beta</c> suffix, accepted with
    /// or without it (<c>packet-beta</c> and <c>packet</c>). The suffixed spelling has to be tried first:
    /// the plain one matches the start of it and would leave the suffix behind.
    /// </summary>
    internal static Parser<string> BetaKeyword(string keyword) =>
        TextIgnoreCase($"{keyword}-beta")
            .Or(TextIgnoreCase(keyword));

    static CommonParsers()
    {
        RequiredWhitespace =
            Literals.Pattern(char.IsWhiteSpace);

        InlineWhitespace =
            Literals.AnyOf(" \t", minSize: 0);

        Newline =
            new LineBreak(newline: true, end: false);

        LineEnd =
            new LineBreak(newline: true, end: true);

        End =
            new LineBreak(newline: false, end: true);

        Comment =
            Literals.Text("%%")
                .SkipAnd(Literals.NoneOf("\r\n", minSize: 0))
                .AndSkip(LineEnd.ZeroOrOne());

        Identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AsString();

        DoubleQuotedString =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        var singleQuotedString =
            Between(Literals.Char('\''), Literals.NoneOf("'", minSize: 0), Literals.Char('\''))
                .AsString();

        QuotedString =
            DoubleQuotedString.Or(singleQuotedString);

        // The numeric tokens are matched structurally and the matched span is parsed where it lies, so a
        // number costs no string. Parlot's own decimal literal is not used for them: it reads the dot of
        // `5.` as part of the number, which neither token does.
        var digits = Literals.AnyOf("0123456789");
        var sign = Literals.AnyOf("+-", minSize: 0, maxSize: 1);
        var fraction = Capture(Literals.Char('.').And(digits));
        var digitsThenFraction = Capture(digits.And(fraction.Optional()));
        var exponent = Literals.AnyOf("eE", minSize: 1, maxSize: 1).And(sign).And(digits);

        // [+-]?(?:\.\d+|\d+(?:\.\d+)?)(?:[eE][+-]?\d+)?
        Number =
            Capture(sign.And(fraction.Or(digitsThenFraction)).And(exponent.Optional()))
                .Then(static _ => double.Parse(_.Span, NumberStyles.Float, CultureInfo.InvariantCulture));

        Integer =
            Literals.Number<int>(NumberOptions.AllowLeadingSign);

        UnsignedInt =
            Literals.Number<int>(NumberOptions.None);

        SignedDecimal =
            Capture(sign.And(digitsThenFraction.Or(fraction)))
                .Then(static _ => double.Parse(_.Span, NumberStyles.Float, CultureInfo.InvariantCulture));

        DirectionParser =
            OneOf(
                Literals.Text("TB").Then(Direction.TopToBottom),
                Literals.Text("TD").Then(Direction.TopToBottom),
                Literals.Text("BT").Then(Direction.BottomToTop),
                Literals.Text("LR").Then(Direction.LeftToRight),
                Literals.Text("RL").Then(Direction.RightToLeft));

        Indentation =
            InlineWhitespace.Then(
                static _ =>
                {
                    var level = 0;
                    foreach (var c in _.Span)
                    {
                        level += c == '\t' ? 4 : 1;
                    }

                    return level;
                });
    }

    // A line break, the end of the input, or either. A statement that is not followed by the line break it
    // needs is the usual way for a parse to fail, so that mismatch is noted as a place it may have gone
    // wrong. The end of the input on its own notes nothing: the list of statements before it already has,
    // and it is also used to look ahead, where not being at the end is no failure at all.
    sealed class LineBreak(bool newline, bool end) :
        Parser<TextSpan>
    {
        public override bool Parse(ParseContext context, ref ParseResult<TextSpan> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Offset;
            var length = 0;
            if (newline)
            {
                length = NewlineLength(cursor);
            }

            if (length == 0 &&
                !(end && cursor.Eof))
            {
                if (newline)
                {
                    DiagramParseContext.Failed(context, start);
                }

                return false;
            }

            cursor.Advance(length);
            result.Set(start, start + length, new(cursor.Buffer, start, length));
            return true;
        }

        // \n and \r\n are line breaks. A \r on its own is not.
        static int NewlineLength(Cursor cursor)
        {
            if (cursor.Current == '\n')
            {
                return 1;
            }

            if (cursor.Current == '\r' &&
                cursor.PeekNext() == '\n')
            {
                return 2;
            }

            return 0;
        }
    }
}
