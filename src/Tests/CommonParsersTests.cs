using Parlot;
using Parlot.Fluent;

public class CommonParsersTests
{
    [Test]
    public async Task SignedDecimalAcceptsMermaidNumberForms()
    {
        // Mermaid's numeric token is [+-]?(?:\d+(?:\.\d+)?|\.\d+): a leading + and bare-dot decimals
        // like .5 are valid and must parse (the old per-diagram parsers rejected both).
        await Assert.That(Parse(CommonParsers.SignedDecimal, "5")).IsEqualTo(5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "-5")).IsEqualTo(-5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "+5")).IsEqualTo(5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "5.5")).IsEqualTo(5.5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "-5.5")).IsEqualTo(-5.5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, ".5")).IsEqualTo(0.5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "+.5")).IsEqualTo(0.5d);
        await Assert.That(Parse(CommonParsers.SignedDecimal, "-.5")).IsEqualTo(-0.5d);
    }

    [Test]
    public async Task NumberAlsoAcceptsAnExponent()
    {
        await Assert.That(Parse(CommonParsers.Number, "5")).IsEqualTo(5d);
        await Assert.That(Parse(CommonParsers.Number, "-5.5")).IsEqualTo(-5.5d);
        await Assert.That(Parse(CommonParsers.Number, "+.5")).IsEqualTo(0.5d);
        await Assert.That(Parse(CommonParsers.Number, "1e3")).IsEqualTo(1000d);
        await Assert.That(Parse(CommonParsers.Number, "1.5E-2")).IsEqualTo(0.015d);
    }

    // A dot with no digits after it, or an exponent with none, belongs to whatever follows the number.
    [Test]
    public async Task NumbersStopBeforeWhatIsNotPartOfThem()
    {
        await Assert.That(Unread(CommonParsers.SignedDecimal, "5.")).IsEqualTo(".");
        await Assert.That(Unread(CommonParsers.SignedDecimal, "1e5")).IsEqualTo("e5");
        await Assert.That(Unread(CommonParsers.Number, "5.")).IsEqualTo(".");
        await Assert.That(Unread(CommonParsers.Number, "5e")).IsEqualTo("e");
        await Assert.That(Unread(CommonParsers.Integer, "12.5")).IsEqualTo(".5");
    }

    [Test]
    public async Task Integers()
    {
        await Assert.That(Parse(CommonParsers.Integer, "-12")).IsEqualTo(-12);
        await Assert.That(Parse(CommonParsers.Integer, "+7")).IsEqualTo(7);
        await Assert.That(Parse(CommonParsers.UnsignedInt, "42")).IsEqualTo(42);
        await Assert.That(CommonParsers.UnsignedInt.TryParse("-5", out _)).IsFalse();
    }

    // More digits than an int holds is a number the grammar does not read, not an exception.
    [Test]
    public async Task IntegerTooLargeToHold_DoesNotParse()
    {
        await Assert.That(CommonParsers.UnsignedInt.TryParse("99999999999", out _)).IsFalse();
        await Assert.That(CommonParsers.Integer.TryParse("-99999999999", out _)).IsFalse();
    }

    // Parlot's own case-insensitive literal picks between alternatives by the characters each can start
    // with, and takes those from the culture the grammar is built under. Under a Turkish one, where the
    // upper case of i is İ, an alternative starting with i was skipped for input starting with I.
    [Test]
    [Arguments("id")]
    [Arguments("ID")]
    [Arguments("Id")]
    [Arguments("iD")]
    public async Task KeywordInAnyCase_MatchesWhateverCultureTheGrammarIsBuiltUnder(string input)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("tr-TR");
        try
        {
            var keyword = Parsers.OneOf(GrammarExtensions.TextIgnoreCase("id"), GrammarExtensions.TextIgnoreCase("x"));

            await Assert.That(Parse(keyword, input)).IsEqualTo(input);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    static T Parse<T>(Parser<T> parser, string input)
    {
        if (!parser.TryParse(input, out var value))
        {
            throw new($"Failed to parse '{input}'");
        }

        return value!;
    }

    // What a parser leaves of its input.
    static string Unread<T>(Parser<T> parser, string input)
    {
        var context = new ParseContext(new Scanner(input));
        var result = new ParseResult<T>();
        if (!parser.Parse(context, ref result))
        {
            throw new($"Failed to parse '{input}'");
        }

        return input[context.Scanner.Cursor.Offset..];
    }
}
