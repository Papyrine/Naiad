namespace Naiad.Diagrams.Railroad;

/// <summary>
/// Mermaid's <c>railroad-ebnf-beta</c>: EBNF in both its W3C style (<c>A B</c>, <c>A?</c>, <c>A*</c>,
/// <c>A+</c>) and its ISO 14977 style (<c>A , B</c>, <c>[ A ]</c>, <c>{ A }</c>, <c>? special ?</c>), mixed
/// freely, with rules written <c>name = definition ;</c> or <c>name ::= definition ;</c>.
/// </summary>
class RailroadEbnfParser : IDiagramParser<RailroadModel>
{
    static Parser<RailroadModel> parser;

    static RailroadEbnfParser()
    {
        var gap = RailroadGrammar.Gap(
            RailroadGrammar.BlockComment("/*", "*/"),
            RailroadGrammar.BlockComment("(*", "*)"));

        Parser<char> Symbol(char symbol) => gap.SkipAnd(Literals.Char(symbol));

        var choice = Deferred<RailroadExpression>();

        // A special sequence holds at least one visible character and no `?` or `;`, so it cannot swallow
        // the rest of a rule, and a `?` that does not open one is left for the optional postfix.
        var special =
            Literals.Char('?')
                .SkipAnd(Literals.NoneOf("?;", minSize: 0))
                .AndSkip(Literals.Char('?'))
                .Then(static _ => _.Span.Trim().ToString())
                .Assert(static _ => _.Length > 0, "Expected text in the special sequence")
                .Then<RailroadExpression>(static _ => new RailroadSpecial(_));

        var primary =
            gap.SkipAnd(
                OneOf(
                    RailroadGrammar.EscapedString.Then<RailroadExpression>(static _ => new RailroadTerminal(_)),
                    special,
                    RailroadGrammar.Identifier.Then<RailroadExpression>(static _ => new RailroadNonTerminal(_)),
                    Literals.Char('(').SkipAnd(choice).AndSkip(Symbol(')')),
                    Literals.Char('[').SkipAnd(choice).AndSkip(Symbol(']'))
                        .Then<RailroadExpression>(static _ => new RailroadOptional(_)),
                    Literals.Char('{').SkipAnd(choice).AndSkip(Symbol('}'))
                        .Then<RailroadExpression>(static _ => new RailroadRepetition(_, 0, null))));

        // Postfixes apply left to right, so `A?*` repeats an optional A. An exception `A - B` is drawn as A,
        // a `-` and B in a row, as Mermaid draws it. A `?` that opens a special sequence is not a postfix:
        // Mermaid's lexer prefers the longer token, so `A ? B ?` is A followed by the special sequence B.
        var postfix =
            OneOf(
                gap.SkipAnd(Not(special)).SkipAnd(Literals.Char('?')).Then<Func<RailroadExpression, RailroadExpression>>(static _ => static item => new RailroadOptional(item)),
                Symbol('*').Then<Func<RailroadExpression, RailroadExpression>>(static _ => static item => new RailroadRepetition(item, 0, null)),
                Symbol('+').Then<Func<RailroadExpression, RailroadExpression>>(static _ => static item => new RailroadRepetition(item, 1, null)),
                Symbol('-').SkipAnd(primary).Then<Func<RailroadExpression, RailroadExpression>>(
                    static except => item => new RailroadSequence([item, new RailroadTerminal("-"), except])));

        var term =
            primary
                .And(postfix.Many())
                .Then(static _ => _.Item2.Aggregate(_.Item1, static (item, apply) => apply(item)));

        // Items follow one another with or without a comma between them.
        var sequence =
            term
                .And(Symbol(',').Optional().SkipAnd(term).Many())
                .Then(static _ => RailroadGrammar.Sequence([_.Item1, .. _.Item2]));

        choice.Parser =
            RailroadGrammar.SeparatedBy(sequence, Symbol('|'))
                .Then(RailroadGrammar.Choice);

        var rule =
            RailroadGrammar.Identifier
                .AndSkip(gap.SkipAnd(Literals.Text("::=").Or(Literals.Text("="))))
                .And(choice)
                .AndSkip(Symbol(';'))
                .Map(static (name, definition) => new RailroadRule(name, definition));

        parser = RailroadGrammar.Diagram("railroad-ebnf-beta", gap, rule, unescapeTitle: true);
    }

    public DiagramParseResult<RailroadModel> Parse(string input) => parser.ParseDiagram(input);
}
