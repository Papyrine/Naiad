using Naiad.Diagrams.Cynefin;

public class CynefinTests : TestBase
{
    // The basic example from Mermaid's documentation.
    [Test]
    public Task Simple()
    {
        const string input =
            """
            cynefin-beta
              title Incident Response

              complex
                "Investigate root cause"
                "Run chaos experiment"

              complicated
                "Analyze performance data"
                "Expert review needed"

              clear
                "Restart service"
                "Apply known fix"

              chaotic
                "Page on-call immediately"

              confusion
                "Unknown failure mode"
            """;

        return VerifySvg(input);
    }

    // The transitions example from Mermaid's documentation.
    [Test]
    public Task Transitions()
    {
        const string input =
            """
            cynefin-beta
              title Strategy Categorization

              complex
                "Market research"

              complicated
                "Competitive analysis"

              clear
                "Standard pricing"

              chaotic
                "Crisis management"

              complex --> complicated : "Pattern identified"
              complicated --> clear : "Best practice codified"
              clear --> chaotic : "Complacency"
              chaotic --> complex : "Stabilized"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EmptyFramework()
    {
        const string input =
            """
            cynefin-beta
              title Cynefin Framework

              complex
              complicated
              clear
              chaotic
            """;

        return VerifySvg(input);
    }

    // Only the keyword: every domain is still drawn, and there is no title.
    [Test]
    public Task KeywordOnly() =>
        VerifySvg("cynefin-beta");

    // More items than fit Mermaid's fixed canvas, a long label, and more Confusion items than its ellipse
    // shows, which leaves a "+N more" badge.
    [Test]
    public Task CrowdedDomains()
    {
        const string input =
            """
            cynefin-beta
              complex
                "Discovery spike"
                "A/B experiment"
                "Customer interviews"
                "Prototype"
                "Safe-to-fail probe"
              complicated
                "A considerably longer item label that needs a wide badge"
              clear
                "Runbook"
              chaotic
                "Outage"
              confusion
                "Unknown one"
                "Unknown two"
                "Unknown three"
                "Unknown four"
                "Unknown five"
            """;

        return VerifySvg(input);
    }

    // Transitions to and from Confusion, across the diagonals, and one without a label.
    [Test]
    public Task TransitionsThroughConfusion()
    {
        const string input =
            """
            cynefin-beta
              confusion
                "Unclear request"
              confusion --> complex : "Probe it"
              complicated --> confusion
              chaotic --> complicated : "Diagonal"
              complex --> clear
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_DomainsAndItems()
    {
        var model = Parse(
            """
            cynefin-beta:
            title  My Title  %% a trailing comment
            complex
              "One"
              'Two'

            %% a comment between blocks
            clear "Inline" "Pair"
              "Three" %% after an item
            chaotic
            """);

        await Assert.That(model.Title).IsEqualTo("My Title");
        await Assert.That(model.Domains.Count).IsEqualTo(3);
        await Assert.That(model.Domains[CynefinDomainName.Complex].Items).IsEquivalentTo(["One", "Two"]);
        await Assert.That(model.Domains[CynefinDomainName.Clear].Items).IsEquivalentTo(["Inline", "Pair", "Three"]);
        await Assert.That(model.Domains[CynefinDomainName.Chaotic].Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_Escapes()
    {
        var model = Parse(
            """
            cynefin-beta
              complex
                "Say \"hi\""
                'It\'s'
            """);

        await Assert.That(model.Domains[CynefinDomainName.Complex].Items).IsEquivalentTo(["Say \"hi\"", "It's"]);
    }

    [Test]
    public async Task Parse_Transitions()
    {
        var model = Parse(
            """
            cynefin-beta
              complex
                "A"
              complex-->complicated:"Pattern identified"
              clear --> chaotic
              complex --> complex : "Ignored"
            """);

        await Assert.That(model.Transitions.Count).IsEqualTo(2);
        await Assert.That(model.Transitions[0].From).IsEqualTo(CynefinDomainName.Complex);
        await Assert.That(model.Transitions[0].To).IsEqualTo(CynefinDomainName.Complicated);
        await Assert.That(model.Transitions[0].Label).IsEqualTo("Pattern identified");
        await Assert.That(model.Transitions[1].From).IsEqualTo(CynefinDomainName.Clear);
        await Assert.That(model.Transitions[1].To).IsEqualTo(CynefinDomainName.Chaotic);
        await Assert.That(model.Transitions[1].Label).IsNull();
        await Assert.That(model.Domains[CynefinDomainName.Complex].Items).IsEquivalentTo(["A"]);
    }

    // A domain declared again replaces the items of the earlier block, as Mermaid's database does.
    [Test]
    public async Task Parse_RepeatedDomainKeepsLastBlock()
    {
        var model = Parse(
            """
            cynefin-beta
              complex
                "First"
              clear
              complex
                "Second"
            """);

        await Assert.That(model.Domains[CynefinDomainName.Complex].Items).IsEquivalentTo(["Second"]);
    }

    [Test]
    public async Task Parse_AccessibilityIgnored()
    {
        var model = Parse(
            """
            cynefin-beta
              accTitle: Cynefin framework
              accDescr: A Cynefin map
              accDescr {
                over several lines
              }
              complex
                "New feature discovery"
            """);

        await Assert.That(model.Title).IsNull();
        await Assert.That(model.Domains[CynefinDomainName.Complex].Items).IsEquivalentTo(["New feature discovery"]);
    }

    [Test]
    [Arguments("cynefin-beta\n  \"Item outside a domain\"")]
    [Arguments("cynefin-beta\n  complexity")]
    [Arguments("cynefin-beta\n  Complex")]
    [Arguments("cynefin-beta\n  complex --> elsewhere")]
    [Arguments("cynefin-beta\n  complex\n    unquoted item")]
    [Arguments("cynefin\n  complex")]
    public async Task Parse_Rejects(string input)
    {
        var result = new CynefinParser().Parse(input);
        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("cynefin-beta\n  complex", out var beta)).IsTrue();
        await Assert.That(beta).IsEqualTo(DiagramType.Cynefin);
        await Assert.That(Mermaid.TryDetectType("cynefin-beta:\n  complex", out var colon)).IsTrue();
        await Assert.That(colon).IsEqualTo(DiagramType.Cynefin);
        await Assert.That(Mermaid.TryDetectType("cynefin-beta", out var bare)).IsTrue();
        await Assert.That(bare).IsEqualTo(DiagramType.Cynefin);
        await Assert.That(Mermaid.TryDetectType("cynefin\n  complex", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("cynefin-betas\n  complex", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("CYNEFIN-BETA\n  complex", out _)).IsFalse();
    }

    static CynefinModel Parse(string input)
    {
        var result = new CynefinParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
