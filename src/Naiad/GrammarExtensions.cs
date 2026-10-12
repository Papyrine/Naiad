namespace Naiad;

// What the diagram grammars share that Parlot does not come with.
static class GrammarExtensions
{
    // Runs a diagram's grammar over its text.
    internal static DiagramParseResult<TModel> ParseDiagram<TModel>(this Parser<TModel> parser, string input)
        where TModel : DiagramBase
    {
        var context = new DiagramParseContext(input);
        var result = new ParseResult<TModel>();
        if (parser.Parse(context, ref result))
        {
            return new(result.Value);
        }

        return new(context.Describe());
    }

    // Runs a rule over a piece of text that is parsed on its own, as the lines of a class body are. Unlike
    // Parlot's TryParse this never compiles the rule, whatever ParseContext.DefaultCompilationThreshold a
    // host application has set.
    internal static bool TryMatch<T>(this Parser<T> parser, string input, [MaybeNullWhen(false)] out T value)
    {
        var result = new ParseResult<T>();
        if (parser.Parse(new(new(input)), ref result))
        {
            value = result.Value;
            return true;
        }

        value = default;
        return false;
    }

    // The text a rule matched, as a string.
    internal static Parser<string> AsString(this Parser<TextSpan> parser) =>
        parser.Then(static _ => _.ToString());

    // The text a rule matched, without the whitespace around it.
    internal static Parser<string> AsTrimmedString(this Parser<TextSpan> parser) =>
        parser.Then(static _ => _.Span.Trim().ToString());

    // A keyword written in any mix of upper and lower case, returned as it was written. It stands in for
    // Literals.Text with caseInsensitive, which works out the characters a keyword can start with from the
    // culture the grammar happens to be built under and uses them to pick between alternatives: built
    // under a Turkish culture, an alternative starting with `i` is no longer tried for `I`. This one does
    // not say what it starts with, so it is always tried, and the comparison is ordinal.
    internal static Parser<string> TextIgnoreCase(string text) =>
        new IgnoreCaseLiteral(text);

    // Zero or more characters a predicate accepts. Parlot's Pattern does not match nothing. Where the
    // characters can be listed, Literals.AnyOf and Literals.NoneOf with a minSize of 0 do the same faster.
    internal static Parser<TextSpan> ZeroOrMore(Func<char, bool> predicate) =>
        Literals.Pattern(predicate).Else(new TextSpan(""));

    // Zero or more of a rule. As Parlot's ZeroOrMany, except that a rule matching without consuming
    // anything ends the list rather than repeating forever, and where the list stops is noted as a place
    // the parse may have gone wrong. Repetition inside a single token (the hyphenated parts of a name)
    // uses ZeroOrMany instead: a name ending is not a statement going wrong, and noting it would report
    // an unreadable line part-way through its first word.
    internal static Parser<IReadOnlyList<T>> Many<T>(this Parser<T> parser) =>
        new ManyParser<T>(parser);

    // As When, and a rule that turns down what it read says why. The reason is reported if nothing gets
    // further.
    internal static Parser<T> Assert<T>(this Parser<T> parser, Func<T, bool> predicate, string message) =>
        new AssertParser<T>(parser, predicate, _ => message);

    internal static Parser<T> Assert<T>(this Parser<T> parser, Func<T, bool> predicate, Func<T, string> message) =>
        new AssertParser<T>(parser, predicate, message);

    // The rest of the line up to where a rule first matches, which is left unread. Fails if the line ends
    // before the rule matches. Parlot's AnyCharBefore reads on across lines.
    internal static Parser<TextSpan> LineBefore<T>(Parser<T> terminator) =>
        new LineBeforeParser<T>(terminator);

    // An optional value as a nullable one.
    internal static T? OrNull<T>(this Option<T> option)
        where T : class
    {
        if (option.HasValue)
        {
            return option.Value;
        }

        return null;
    }

    internal static T? ToNullable<T>(this Option<T> option)
        where T : struct
    {
        if (option.HasValue)
        {
            return option.Value;
        }

        return null;
    }

    // Then, with a sequence's values handed over one by one instead of as a tuple.
    internal static Parser<TResult> Map<T1, T2, TResult>(
        this Parser<(T1, T2)> parser,
        Func<T1, T2, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2));

    internal static Parser<TResult> Map<T1, T2, T3, TResult>(
        this Parser<(T1, T2, T3)> parser,
        Func<T1, T2, T3, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2, _.Item3));

    internal static Parser<TResult> Map<T1, T2, T3, T4, TResult>(
        this Parser<(T1, T2, T3, T4)> parser,
        Func<T1, T2, T3, T4, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2, _.Item3, _.Item4));

    internal static Parser<TResult> Map<T1, T2, T3, T4, T5, TResult>(
        this Parser<(T1, T2, T3, T4, T5)> parser,
        Func<T1, T2, T3, T4, T5, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2, _.Item3, _.Item4, _.Item5));

    internal static Parser<TResult> Map<T1, T2, T3, T4, T5, T6, TResult>(
        this Parser<(T1, T2, T3, T4, T5, T6)> parser,
        Func<T1, T2, T3, T4, T5, T6, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2, _.Item3, _.Item4, _.Item5, _.Item6));

    internal static Parser<TResult> Map<T1, T2, T3, T4, T5, T6, T7, TResult>(
        this Parser<(T1, T2, T3, T4, T5, T6, T7)> parser,
        Func<T1, T2, T3, T4, T5, T6, T7, TResult> map) =>
        parser.Then(_ => map(_.Item1, _.Item2, _.Item3, _.Item4, _.Item5, _.Item6, _.Item7));

    sealed class IgnoreCaseLiteral(string text) :
        Parser<string>
    {
        public override bool Parse(ParseContext context, ref ParseResult<string> result)
        {
            var cursor = context.Scanner.Cursor;
            var span = cursor.Span;
            if (!span.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Written exactly as the keyword is spelled, which is the usual case, it costs no new string.
            var written = span[..text.Length];
            var value = text;
            if (!written.SequenceEqual(text))
            {
                value = written.ToString();
            }

            var start = cursor.Offset;
            cursor.Advance(text.Length);
            result.Set(start, start + text.Length, value);
            return true;
        }
    }

    sealed class ManyParser<T>(Parser<T> parser) :
        Parser<IReadOnlyList<T>>
    {
        public override bool Parse(ParseContext context, ref ParseResult<IReadOnlyList<T>> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Offset;
            List<T>? items = null;
            var parsed = new ParseResult<T>();
            while (true)
            {
                var before = cursor.Offset;
                if (!parser.Parse(context, ref parsed) ||
                    cursor.Offset == before)
                {
                    break;
                }

                items ??= [];
                items.Add(parsed.Value);
            }

            DiagramParseContext.Failed(context, cursor.Offset);
            result.Set(start, cursor.Offset, items ?? (IReadOnlyList<T>)[]);
            return true;
        }
    }

    sealed class AssertParser<T>(Parser<T> parser, Func<T, bool> predicate, Func<T, string> message) :
        Parser<T>
    {
        public override bool Parse(ParseContext context, ref ParseResult<T> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Position;
            if (!parser.Parse(context, ref result))
            {
                return false;
            }

            if (predicate(result.Value))
            {
                return true;
            }

            DiagramParseContext.Failed(context, start.Offset, message(result.Value));
            cursor.ResetPosition(start);
            return false;
        }
    }

    sealed class LineBeforeParser<T>(Parser<T> terminator) :
        Parser<TextSpan>
    {
        public override bool Parse(ParseContext context, ref ParseResult<TextSpan> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Position;
            var parsed = new ParseResult<T>();
            while (true)
            {
                var here = cursor.Position;
                if (terminator.Parse(context, ref parsed))
                {
                    cursor.ResetPosition(here);
                    result.Set(start.Offset, here.Offset, new(cursor.Buffer, start.Offset, here.Offset - start.Offset));
                    return true;
                }

                if (cursor.Eof ||
                    cursor.Current is '\r' or '\n')
                {
                    cursor.ResetPosition(start);
                    return false;
                }

                cursor.Advance();
            }
        }
    }
}
