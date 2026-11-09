namespace Naiad.Diagrams.Railroad;

/// <summary>
/// Mermaid's <c>railroad-peg-beta</c>: a parsing expression grammar with rules written
/// <c>Name &lt;- definition ;</c>, ordered choice with <c>/</c>, the <c>?</c>, <c>*</c> and <c>+</c> suffixes,
/// the <c>&amp;</c> and <c>!</c> predicates, <c>( )</c> groups, <c>.</c> for any character and <c>#</c> comments.
/// </summary>
class RailroadPegParser : IDiagramParser<RailroadModel>
{
    static Parser<RailroadModel> parser;

    static RailroadPegParser()
    {
        var gap = RailroadGrammar.Gap(RailroadGrammar.LineComment("#"));

        Parser<char> Symbol(char symbol) => gap.SkipAnd(Literals.Char(symbol));

        var orderedChoice = Deferred<RailroadExpression>();

        var primary =
            gap.SkipAnd(
                OneOf(
                    RailroadGrammar.EscapedString.Then<RailroadExpression>(static _ => new RailroadTerminal(_)),
                    RailroadGrammar.Identifier.Then<RailroadExpression>(static _ => new RailroadNonTerminal(_)),
                    Literals.Char('(').SkipAnd(orderedChoice).AndSkip(Symbol(')')),
                    Literals.Char('.').Then<RailroadExpression>(static _ => new RailroadSpecial("."))));

        var suffix =
            primary
                .And(OneOf(Symbol('?'), Symbol('*'), Symbol('+')).Optional())
                .Then(
                    static _ =>
                    {
                        if (!_.Item2.HasValue)
                        {
                            return _.Item1;
                        }

                        return _.Item2.Value switch
                        {
                            '?' => new RailroadOptional(_.Item1),
                            '*' => new RailroadRepetition(_.Item1, 0, null),
                            _ => new RailroadRepetition(_.Item1, 1, null)
                        };
                    });

        // A predicate matches without consuming anything, so it has no track of its own to draw. Mermaid
        // shows it as a special box labelled with the operator and what it looks at.
        var prefix =
            OneOf(Symbol('&'), Symbol('!')).Optional()
                .And(suffix)
                .Then(
                    static _ => _.Item1.HasValue
                        ? new RailroadSpecial($"{_.Item1.Value}{Label(_.Item2)}")
                        : _.Item2);

        var sequence =
            prefix
                .And(prefix.Many())
                .Then(static _ => RailroadGrammar.Sequence([_.Item1, .. _.Item2]));

        orderedChoice.Parser =
            RailroadGrammar.SeparatedBy(sequence, Symbol('/'))
                .Then(RailroadGrammar.Choice);

        var rule =
            RailroadGrammar.Identifier
                .AndSkip(gap.SkipAnd(Literals.Text("<-")))
                .And(orderedChoice)
                .AndSkip(Symbol(';'))
                .Map(static (name, definition) => new RailroadRule(name, definition));

        parser = RailroadGrammar.Diagram("railroad-peg-beta", gap, rule, unescapeTitle: true);
    }

    static string Label(RailroadExpression expression) =>
        expression switch
        {
            RailroadTerminal terminal => $"\"{terminal.Text}\"",
            RailroadNonTerminal nonTerminal => nonTerminal.Name,
            RailroadSpecial special => special.Text,
            _ => "(...)"
        };

    public DiagramParseResult<RailroadModel> Parse(string input) => parser.ParseDiagram(input);
}
