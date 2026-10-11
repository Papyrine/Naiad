public class ArchitectureTests : TestBase
{
    // A minimal icon pack in the iconify JSON format, used to exercise the
    // IconPack.Load API. "box" is fill-based, "ring" is stroke-based; both use
    // currentColor so they pick up the service's accent colour.
    const string SamplePack =
        """
        {
          "prefix": "sample",
          "width": 24,
          "height": 24,
          "icons": {
            "box": {"body": "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"3\" fill=\"currentColor\"/>"},
            "ring": {"body": "<circle cx=\"12\" cy=\"12\" r=\"8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/>"}
          }
        }
        """;

    [Test]
    public Task BasicService()
    {
        var input =
            """
            architecture-beta
            service db(database)[Database]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ServiceWithDifferentIcons()
    {
        var input =
            """
            architecture-beta
            service db(database)[Database]
            service srv(server)[Server]
            service disk1(disk)[Storage]
            service cloud1(cloud)[Cloud]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task IconPackFromStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SamplePack));
        IconPack.Load(stream);

        var input =
            """
            architecture-beta
            service a(sample:box)[Box]
            service b(sample:ring)[Ring]
            a:R -- L:b
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task IconPackFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"naiad-iconpack-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, SamplePack);
        try
        {
            var prefix = IconPack.Load(path);
            await Assert.That(prefix).IsEqualTo("sample");
        }
        finally
        {
            File.Delete(path);
        }

        var svg = Mermaid.Render(
            """
            architecture-beta
            service a(sample:box)[Box]
            """);
        await Assert.That(svg).Contains("width=\"18\"");
    }

    [Test]
    public async Task IconPackLoadAfterRenderThrows()
    {
        // Packs may only be registered before the first render.
        Mermaid.Render(
            """
            architecture-beta
            service db(database)[Database]
            """);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SamplePack));
        await Assert.That(() => IconPack.Load(stream)).Throws<MermaidException>();
    }

    [Test]
    public Task ServiceWithGroup()
    {
        var input =
            """
            architecture-beta
            group api(cloud)[API]
            service db(database)[Database] in api
            service server(server)[Server] in api
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ServicesWithEdge()
    {
        var input =
            """
            architecture-beta
            service db(database)[Database]
            service server(server)[Server]
            db:R -- L:server
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        var input =
            """
            architecture-beta
            group api(cloud)[API Layer]
            service server(server)[API Server] in api
            service db(database)[Database]
            service disk1(disk)[Storage]
            server:B -- T:db
            server:R -- L:disk1
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EdgeWithArrows()
    {
        var input =
            """
            architecture-beta
            service client(internet)[Client]
            service server(server)[Server]
            service db(database)[Database]
            <client:R -- L>:server
            server:B -- T>:db
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task GroupEdge()
    {
        // The {group} flag on an edge endpoint anchors it to the boundary of the group the
        // service belongs to, rather than to the service itself.
        var input =
            """
            architecture-beta
            group api(cloud)[API]
            service db(database)[Database] in api
            service server(server)[Server] in api
            service gateway(internet)[Gateway]
            db:L -- R:server
            server{group}:B -- T>:gateway
            """;

        return VerifySvg(input);
    }

    // Mermaid puts the arrowheads on the line, not on the ids, and a label inside it.
    [Test]
    public Task ArrowsAndLabelsOnTheLine()
    {
        const string input =
            """
            architecture-beta
                service gateway(internet)[Gateway]
                service server(server)[Server]
                service db(database)[Database]
                service disk(disk)[Storage]

                gateway:R --> L:server
                server:R <--> L:db
                db:B <-[backs up]- T:disk
            """;

        return VerifySvg(input);
    }
}