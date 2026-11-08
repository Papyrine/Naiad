using Naiad.Diagrams.Railroad;

public class RailroadTests : TestBase
{
    // The constructor notation.

    // The example from Mermaid's documentation.
    [Test]
    public Task Constructors()
    {
        const string input =
            """
            railroad-beta
            title Expression Grammar

            expression = sequence(
                nonterminal("term"),
                zeroOrMore(sequence(
                    choice(terminal("+"), terminal("-")),
                    nonterminal("term")
                ))
            ) ;
            term = sequence(
                nonterminal("factor"),
                zeroOrMore(sequence(
                    choice(terminal("*"), terminal("/")),
                    nonterminal("factor")
                ))
            ) ;
            factor = choice(
                nonterminal("number"),
                sequence(terminal("("), nonterminal("expression"), terminal(")"))
            ) ;
            number = oneOrMore(nonterminal("digit")) ;
            digit = choice(terminal("0"), terminal("1"), terminal("2"), terminal("3"), terminal("4"), terminal("5"), terminal("6"), terminal("7"), terminal("8"), terminal("9")) ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ConstructorsOptionalAndSpecial()
    {
        const string input =
            """
            railroad-beta
            %% a comment
            statement = sequence(
                optional(terminal("export")),
                choice(nonterminal("declaration"), special("any expression")),
                optional(sequence(terminal(";"), optional(nonterminal("trailing_comment"))))
            ) ; /* trailing */
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_Constructors()
    {
        var model = Parse(
            new RailroadParser(),
            """
            railroad-beta
            title "A \"quoted\" title"
            accTitle: Accessible
            rule = sequence(terminal("a\tb"), choice(nonterminal("x"), special("s")),
                optional(terminal('c')), zeroOrMore(terminal("d")), oneOrMore(sequence(terminal("e")))) ;
            other = choice(terminal("only")) ;
            """);

        await Assert.That(model.Title).IsEqualTo("A \"quoted\" title");
        await Assert.That(Describe(model)).IsEqualTo(
            "rule = seq(\"a\tb\", choice(x | ?s?), opt(\"c\"), rep0(\"d\"), rep1(\"e\")); other = \"only\"");
    }

    [Test]
    public async Task Parse_ConstructorsReject_MissingSemicolon()
    {
        var result = new RailroadParser().Parse("railroad-beta\nrule = terminal(\"a\")\n");
        await Assert.That(result.Success).IsFalse();
    }

    // EBNF.

    // Mermaid's arithmetic expression example.
    [Test]
    public Task Ebnf()
    {
        const string input =
            """
            railroad-ebnf-beta
            title "Arithmetic Expression Grammar"

            expression = term ( ( "+" | "-" ) term )* ;
            term = factor ( ( "*" | "/" ) factor )* ;
            factor = number | "(" expression ")" ;
            number = digit+ ;
            digit = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
            """;

        return VerifySvg(input);
    }

    // Mermaid's JSON example, mixing the ISO optional with W3C repetition.
    [Test]
    public Task EbnfJson()
    {
        const string input =
            """
            railroad-ebnf-beta
            title "JSON Grammar"

            json = element ;
            element = object | array | string | number | "true" | "false" | "null" ;
            object = "{" [ member ( "," member )* ] "}" ;
            array = "[" [ element ( "," element )* ] "]" ;
            member = string ":" element ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EbnfIsoStyle()
    {
        const string input =
            """
            railroad-ebnf-beta
            (* ISO 14977 style, with commas between items *)
            a_rather_long_rule_name_for_layout ::= [ sign ] , { digit } , ? any visible character ? ;
            identifier = letter ( letter | digit )* - keyword ;
            sign = "+" | "-" ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_Ebnf()
    {
        var model = Parse(
            new RailroadEbnfParser(),
            """
            railroad-ebnf-beta
            title "Grammar" %% comment
            /* W3C comment */ number = sign? digit+ ( "." digit* )+ ;
            list ::= "[" , [ item , { "," , item } ] , "]" ;
            special = ? anything ? | 'x' - "y" ;
            spanning =
                "a"
                | "b" ;
            """);

        await Assert.That(model.Title).IsEqualTo("Grammar");
        await Assert.That(Describe(model)).IsEqualTo(
            "number = seq(opt(sign), rep1(digit), rep1(seq(\".\", rep0(digit)))); " +
            "list = seq(\"[\", opt(seq(item, rep0(seq(\",\", item)))), \"]\"); " +
            "special = choice(?anything? | seq(\"x\", \"-\", \"y\")); " +
            "spanning = choice(\"a\" | \"b\")");
    }

    // A `?` that opens a special sequence is not an optional postfix, as Mermaid's lexer takes the longer token.
    [Test]
    public async Task Parse_EbnfSpecialAfterItem()
    {
        var model = Parse(new RailroadEbnfParser(), "railroad-ebnf-beta\nrule = a ? b ? ;");
        await Assert.That(Describe(model)).IsEqualTo("rule = seq(a, ?b?)");
    }

    // That holds even for an optional postfix written against its item: a later `?` in the same rule closes
    // a special sequence opened by the first, as it does in Mermaid.
    [Test]
    public async Task Parse_EbnfTwoOptionalPostfixesOpenASpecialSequence()
    {
        var model = Parse(new RailroadEbnfParser(), "railroad-ebnf-beta\nrule = a? b? ;");
        await Assert.That(Describe(model)).IsEqualTo("rule = seq(a, ?b?)");
    }

    [Test]
    public async Task Parse_EbnfRejectsMissingSemicolon()
    {
        var result = new RailroadEbnfParser().Parse("railroad-ebnf-beta\nrule = \"a\"");
        await Assert.That(result.Success).IsFalse();
    }

    // ABNF.

    // Mermaid's email address example.
    [Test]
    public Task Abnf()
    {
        const string input =
            """
            railroad-abnf-beta
            title "Email Address"

            address = local-part "@" domain ;
            local-part = 1*( ALPHA / DIGIT / "." / "-" ) ;
            domain = label *( "." label ) ;
            label = 1*( ALPHA / DIGIT / "-" ) ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AbnfBoundedRepetition()
    {
        const string input =
            """
            railroad-abnf-beta
            ; Counted repetitions and numeric values
            phone = [ "+" country-code ] subscriber ;
            country-code = 1*3DIGIT ;
            subscriber = 3DIGIT "-" 4DIGIT ;
            hex = %x30-39 / %x41-46 ;
            year = 2*4DIGIT / *2%d48 ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_Abnf()
    {
        var model = Parse(
            new RailroadAbnfParser(),
            """
            railroad-abnf-beta
            title 'Single quotes are kept as written'
            ; a comment line
            rule = "a" / b-c
                 ; a comment inside the rule
                 / [ "d" ] ;
            counts = *x 1*y 2*4z 3w *1v ;
            values = %x41 %d65.66 %b1000001 %x30-39 ;
            """);

        await Assert.That(model.Title).IsEqualTo("Single quotes are kept as written");
        await Assert.That(Describe(model)).IsEqualTo(
            "rule = choice(\"a\" | b-c | opt(\"d\")); " +
            "counts = seq(rep0(x), rep1(y), rep2-4(z), rep3-3(w), opt(v)); " +
            "values = seq(\"%x41\", \"%d65.66\", \"%b1000001\", \"%x30-39\")");
    }

    // ABNF has no escapes in its strings, so the quotes around a title are dropped as they are.
    [Test]
    public async Task Parse_AbnfTitleWithoutEscapes()
    {
        var model = Parse(new RailroadAbnfParser(), "railroad-abnf-beta\ntitle \"a\\tb\"\nr = \"x\" ;");
        await Assert.That(model.Title).IsEqualTo("a\\tb");
    }

    // A `;` with text after it on its line is a comment, so the rule it was meant to end has not ended.
    [Test]
    public async Task Parse_AbnfSemicolonFollowedByTextIsComment()
    {
        var result = new RailroadAbnfParser().Parse("railroad-abnf-beta\na = \"x\" ; b = \"y\" ;\n");
        await Assert.That(result.Success).IsFalse();
    }

    // PEG.

    // Mermaid's calculator example.
    [Test]
    public Task Peg()
    {
        const string input =
            """
            railroad-peg-beta
            title "Calculator Grammar"

            Expression <- Term (("+" / "-") Term)* ;
            Term <- Factor (("*" / "/") Factor)* ;
            Factor <- Number / "(" Expression ")" ;
            Number <- Digit+ ;
            Digit <- "0" / "1" / "2" / "3" / "4" / "5" / "6" / "7" / "8" / "9" ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task PegPredicates()
    {
        const string input =
            """
            railroad-peg-beta
            title "Identifiers (keywords excluded)"

            Identifier <- !Keyword Letter Letter* ;  # a comment
            Keyword <- "if" / "else" / "while" ;
            Letter <- "a" / "b" / "c" / "_" ;
            Comment <- "/*" (!"*/" .)* "*/" ;
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_Peg()
    {
        var model = Parse(
            new RailroadPegParser(),
            """
            railroad-peg-beta
            # comment
            A <- &B C? D* E+ / !"x" . / !(F G) ;
            """);

        await Assert.That(model.Title).IsNull();
        await Assert.That(Describe(model)).IsEqualTo(
            "A = choice(seq(?&B?, opt(C), rep0(D), rep1(E)) | seq(?!\"x\"?, ?.?) | ?!(...)?)");
    }

    // Shared behaviour.

    [Test]
    public Task NoRules()
    {
        const string input =
            """
            railroad-ebnf-beta
            title "Nothing yet"
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task FrontMatterTitle()
    {
        const string input =
            """
            ---
            title: From front matter
            ---
            railroad-ebnf-beta
            rule = "a" ;
            """;

        await Assert.That(Mermaid.Render(input)).Contains("From front matter");
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Detect("railroad-beta\nrule = terminal(\"a\") ;")).IsEqualTo(DiagramType.Railroad);
        await Assert.That(Detect("railroad-abnf-beta\nrule = \"a\" ;")).IsEqualTo(DiagramType.RailroadAbnf);
        await Assert.That(Detect("railroad-ebnf-beta\nrule = \"a\" ;")).IsEqualTo(DiagramType.RailroadEbnf);
        await Assert.That(Detect("railroad-peg-beta\nRule <- \"a\" ;")).IsEqualTo(DiagramType.RailroadPeg);
        await Assert.That(Detect("RAILROAD-EBNF-BETA\nrule = \"a\" ;")).IsEqualTo(DiagramType.RailroadEbnf);
        await Assert.That(Mermaid.TryDetectType("railroad\nrule = \"a\" ;", out _)).IsFalse();

        static DiagramType? Detect(string input) =>
            Mermaid.TryDetectType(input, out var type) ? type : null;
    }

    static RailroadModel Parse(IDiagramParser<RailroadModel> parser, string input)
    {
        var result = parser.Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }

    static string Describe(RailroadModel model) =>
        string.Join("; ", model.Rules.Select(_ => $"{_.Name} = {Describe(_.Definition)}"));

    static string Describe(RailroadExpression expression) =>
        expression switch
        {
            RailroadTerminal terminal => $"\"{terminal.Text}\"",
            RailroadNonTerminal nonTerminal => nonTerminal.Name,
            RailroadSpecial special => $"?{special.Text}?",
            RailroadSequence sequence => $"seq({string.Join(", ", sequence.Items.Select(Describe))})",
            RailroadChoice choice => $"choice({string.Join(" | ", choice.Alternatives.Select(Describe))})",
            RailroadOptional optional => $"opt({Describe(optional.Item)})",
            RailroadRepetition { Max: null } repetition => $"rep{repetition.Min}({Describe(repetition.Item)})",
            RailroadRepetition repetition => $"rep{repetition.Min}-{repetition.Max}({Describe(repetition.Item)})",
            _ => "?"
        };
}
