public class UserJourneyParserTests
{
    [Test]
    public async Task DoesNotClampScore()
    {
        // Mermaid imposes no 1-5 range on journey scores, so Naiad preserves the raw value
        // rather than silently clamping it.
        const string input =
            """
            journey
                section Experience
                    Overjoyed: 7: Me
            """;

        var result = new UserJourneyParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Sections[0].Tasks[0].Score).IsEqualTo(7);
    }

    // Mermaid splits a task line on its colons, so a blank before the second colon, or no actors at all,
    // is valid. Both were rejected.
    [Test]
    public async Task BlankBeforeTheColonAndNoActors_AreRead()
    {
        const string input =
            """
            journey
                section Morning
                    Make tea: 5 : Me
                    Drink it: 3
                    Wash up :1:Me, Cat
            """;

        var result = new UserJourneyParser().Parse(input);

        await Assert.That(result.Error).IsNull();
        var tasks = result.Value.Sections[0].Tasks;
        await Assert.That(string.Join("|", tasks.Select(_ => $"{_.Name}:{_.Score}:{string.Join(",", _.Actors)}")))
            .IsEqualTo("Make tea:5:Me|Drink it:3:|Wash up:1:Me,Cat");
    }
}
