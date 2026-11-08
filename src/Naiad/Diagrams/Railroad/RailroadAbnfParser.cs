namespace Naiad.Diagrams.Railroad;

/// <summary>
/// Mermaid's <c>railroad-abnf-beta</c>: ABNF (RFC 5234) with rules written <c>name = definition ;</c>,
/// alternatives separated by <c>/</c>, repetition prefixes (<c>*A</c>, <c>1*A</c>, <c>2*4A</c>, <c>3A</c>),
/// <c>( )</c> groups, <c>[ ]</c> optional groups, quoted strings and numeric values such as <c>%x30-39</c>.
/// </summary>
class RailroadAbnfParser : IDiagramParser<RailroadModel>
{
    static Parser<RailroadModel> parser;

    static RailroadAbnfParser()
    {
        // A `;` ends a rule when nothing but spaces follows it on its line, and otherwise starts a comment
        // that runs to the end of the line. That is how Mermaid's lexer tells them apart: it takes the longer
        // token, and the comment is only longer when there is text after the `;`.
        var inlineSpace = Literals.AnyOf(" \t", minSize: 0);
        var ruleEnd =
            Literals.Char(';')
                .AndSkip(inlineSpace)
                .AndSkip(Not(Literals.NoneOf("\r\n", maxSize: 1)));

        var gap = RailroadGrammar.Gap(Not(ruleEnd).SkipAnd(RailroadGrammar.LineComment(";")));

        Parser<char> Symbol(char symbol) => gap.SkipAnd(Literals.Char(symbol));

        var alternation = Deferred<RailroadExpression>();

        var ruleName =
            RailroadGrammar.Name(
                static _ => char.IsAsciiLetter(_),
                static _ => char.IsAsciiLetterOrDigit(_) || _ == '-');

        var hexDigits = Literals.Pattern(char.IsAsciiHexDigit);
        var numericValue =
            Capture(
                Literals.Char('%')
                    .And(Literals.AnyOf("xXdDbB", maxSize: 1))
                    .And(hexDigits)
                    .And(ZeroOrMany(Literals.AnyOf("-.", maxSize: 1).And(hexDigits))));

        var primary =
            gap.SkipAnd(
                OneOf(
                    Literals.Char('"')
                        .SkipAnd(Literals.NoneOf("\"", minSize: 0))
                        .AndSkip(Literals.Char('"'))
                        .Then<RailroadExpression>(static _ => new RailroadTerminal(_.ToString())),
                    numericValue.Then<RailroadExpression>(static _ => new RailroadTerminal(_.ToString())),
                    ruleName.Then<RailroadExpression>(static _ => new RailroadNonTerminal(_)),
                    Literals.Char('(').SkipAnd(alternation).AndSkip(Symbol(')')),
                    Literals.Char('[').SkipAnd(alternation).AndSkip(Symbol(']'))
                        .Then<RailroadExpression>(static _ => new RailroadOptional(_))));

        // `min*max` with either side left out, or a bare count for exactly that many.
        var digits = Literals.Pattern(char.IsAsciiDigit);
        var repeat =
            gap.SkipAnd(
                digits.Optional()
                    .AndSkip(Literals.Char('*'))
                    .And(digits.Optional())
                    .Then(static _ => (min: _.Item1.HasValue ? Count(_.Item1.Value) : 0, max: _.Item2.HasValue ? Count(_.Item2.Value) : (int?) null))
                    .Or(digits.Then(static _ => (min: Count(_), max: (int?) Count(_)))));

        // Zero or one is drawn as an optional, as Mermaid draws it.
        var element =
            repeat.Optional()
                .And(primary)
                .Then(
                    static _ =>
                    {
                        if (!_.Item1.HasValue)
                        {
                            return _.Item2;
                        }

                        var (min, max) = _.Item1.Value;
                        if (min == 0 &&
                            max == 1)
                        {
                            return new RailroadOptional(_.Item2);
                        }

                        return new RailroadRepetition(_.Item2, min, max);
                    });

        var concatenation =
            element
                .And(element.Many())
                .Then(static _ => RailroadGrammar.Sequence([_.Item1, .. _.Item2]));

        alternation.Parser =
            RailroadGrammar.SeparatedBy(concatenation, Symbol('/'))
                .Then(RailroadGrammar.Choice);

        var rule =
            ruleName
                .AndSkip(Symbol('='))
                .And(alternation)
                .AndSkip(gap)
                .AndSkip(ruleEnd)
                .Map(static (name, definition) => new RailroadRule(name, definition));

        parser = RailroadGrammar.Diagram("railroad-abnf-beta", gap, rule, unescapeTitle: false);
    }

    /// <summary>A repeat count, held to what fits in an int so a run of digits cannot overflow.</summary>
    static int Count(TextSpan digits) =>
        int.TryParse(digits.Span, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : int.MaxValue;

    public DiagramParseResult<RailroadModel> Parse(string input) => parser.ParseDiagram(input);
}
