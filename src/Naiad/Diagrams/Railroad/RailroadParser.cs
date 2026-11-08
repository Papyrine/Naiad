namespace Naiad.Diagrams.Railroad;

/// <summary>
/// Mermaid's <c>railroad-beta</c>: rules written <c>name = expression ;</c>, where an expression is one of
/// the constructors <c>terminal("…")</c>, <c>nonterminal("…")</c>, <c>special("…")</c>,
/// <c>sequence(…)</c>, <c>choice(…)</c>, <c>optional(…)</c>, <c>zeroOrMore(…)</c> and <c>oneOrMore(…)</c>.
/// </summary>
class RailroadParser : IDiagramParser<RailroadModel>
{
    static Parser<RailroadModel> parser;

    static RailroadParser()
    {
        var gap = RailroadGrammar.Gap(RailroadGrammar.BlockComment("/*", "*/"));

        Parser<char> Symbol(char symbol) => gap.SkipAnd(Literals.Char(symbol));

        var expression = Deferred<RailroadExpression>();

        Parser<T> Call<T>(string name, Parser<T> arguments) =>
            gap.SkipAnd(Literals.Text(name))
                .SkipAnd(Symbol('('))
                .SkipAnd(arguments)
                .AndSkip(Symbol(')'));

        var text = gap.SkipAnd(RailroadGrammar.EscapedString);
        var list = RailroadGrammar.SeparatedBy(expression, Symbol(','));

        // The constructor names are case-sensitive, as in Mermaid's grammar.
        expression.Parser =
            OneOf(
                Call("terminal", text).Then<RailroadExpression>(static _ => new RailroadTerminal(_)),
                Call("nonterminal", text).Then<RailroadExpression>(static _ => new RailroadNonTerminal(_)),
                Call("special", text).Then<RailroadExpression>(static _ => new RailroadSpecial(_)),
                Call("sequence", list).Then(RailroadGrammar.Sequence),
                Call("choice", list).Then(RailroadGrammar.Choice),
                Call("optional", expression).Then<RailroadExpression>(static _ => new RailroadOptional(_)),
                Call("zeroOrMore", expression).Then<RailroadExpression>(static _ => new RailroadRepetition(_, 0, null)),
                Call("oneOrMore", expression).Then<RailroadExpression>(static _ => new RailroadRepetition(_, 1, null)));

        var rule =
            RailroadGrammar.Identifier
                .AndSkip(Symbol('='))
                .And(expression)
                .AndSkip(Symbol(';'))
                .Map(static (name, definition) => new RailroadRule(name, definition));

        parser = RailroadGrammar.Diagram("railroad-beta", gap, rule, unescapeTitle: true);
    }

    public DiagramParseResult<RailroadModel> Parse(string input) => parser.ParseDiagram(input);
}
