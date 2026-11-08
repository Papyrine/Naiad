using Naiad.Diagrams.EventModeling;

public class EventModelingTests : TestBase
{
    // The state change pattern from Mermaid's documentation.
    [Test]
    public Task StateChange()
    {
        const string input =
            """
            eventmodeling

            tf 01 ui CartUI
            tf 02 cmd AddItem
            tf 03 evt ItemAdded
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RelaxedNotationWithInlineData()
    {
        const string input =
            """
            eventmodeling

            timeframe 01 ui CartUI
            timeframe 02 command AddItem { description: string }
            timeframe 03 event ItemAdded { description: string }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DataBlocks()
    {
        const string input =
            """
            eventmodeling

            tf 01 ui CartUI
            tf 02 cmd AddItem [[AddItem01]]
            tf 03 evt ItemAdded [[ItemAdded]]
            tf 04 cmd AddItem [[AddItem02]]
            tf 05 evt ItemAdded [[ItemAdded]]

            data AddItem01 {
              description: 'john'
              image: 'avatar_john'
              price: 20.4
            }

            data AddItem02 {
              description: 'jack'
              image: 'avatar_jack'
              price: 12.5
            }

            data ItemAdded {
              description: string
              image: string
              price: number
            }
            """;

        return VerifySvg(input);
    }

    // A reset frame breaks the inferred chain, and the translation pattern follows it.
    [Test]
    public Task ResetFrame()
    {
        const string input =
            """
            eventmodeling

            tf 01 ui CartUI
            tf 02 cmd AddItem
            tf 03 evt ItemAdded

            rf 04 evt External.InventoryChanged
            tf 05 pcr InventoryProcessor
            tf 06 cmd ChangeInventory
            tf 07 evt Cart.InventoryChanged
            """;

        return VerifySvg(input);
    }

    // A read model built from several events, named with ->>.
    [Test]
    public Task MultipleRelations()
    {
        const string input =
            """
            eventmodeling

            rf 02 evt CartCreated
            rf 03 evt ItemAdded
            rf 04 evt ItemRemoved
            rf 05 evt CartCleared
            tf 01 rmo CartUI ->> 02 ->> 03 ->> 04 ->> 05
            """;

        return VerifySvg(input);
    }

    // The timeline comes back to lanes it has already used, with the state view pattern, and has a title.
    [Test]
    public Task FullTimelineWithTitle()
    {
        const string input =
            """
            eventmodeling
            title Shopping cart
            %% A comment between statements.
            tf 01 ui CartUI
            tf 02 cmd AddItem { description: string, price: number }
            tf 03 evt ItemAdded
            tf 04 rmo CartItems
            tf 05 ui CartUI
            tf 06 cmd RemoveItem
            tf 07 evt ItemRemoved
            tf 08 rmo CartItems ->> 03 ->> 07
            tf 09 ui CheckoutUI
            """;

        return VerifySvg(input);
    }

    // Each combination of namespace and kind gets a lane, after the kind's own lane.
    [Test]
    public Task Namespaces()
    {
        const string input =
            """
            eventmodeling

            rf 01 evt Inventory.InventoryChanged
            rf 02 evt External.InventoryChanged
            tf 03 pcr Inventory.Restocker
            tf 04 cmd Inventory.Restock
            tf 05 evt Inventory.Restocked
            tf 06 evt Inventory.InventoryChanged
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task LongNamesAndJsonData()
    {
        const string input =
            """
            eventmodeling

            tf 01 ui ACustomerFacingCheckoutScreenWithAVeryLongNameThatHasToWrapSomewhere
            tf 02 cmd PlaceOrder [[Order]]
            tf 03 evt OrderPlaced

            data Order `json`{
              "customer": {
                "name": "Alice",
                "email": "alice@example.com"
              },
              "items": 3
            }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Empty()
    {
        const string input =
            """
            eventmodeling
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_CompactAndRelaxedFrames()
    {
        var model = Parse(
            """
            eventmodeling
            tf 01 ui CartUI
            timeframe 02 command AddItem
            rf 03 evt ItemAdded
            resetframe 04 readmodel Cart.Items ->> 03
            tf 05 pcr Processor
            tf 06 processor Other
            """);

        await Assert.That(model.Frames.Select(_ => _.Id)).IsEquivalentTo(["01", "02", "03", "04", "05", "06"]);
        await Assert.That(model.Frames.Select(_ => _.Kind)).IsEquivalentTo(
        [
            EventModelingEntityKind.Ui,
            EventModelingEntityKind.Command,
            EventModelingEntityKind.Event,
            EventModelingEntityKind.ReadModel,
            EventModelingEntityKind.Processor,
            EventModelingEntityKind.Processor
        ]);
        await Assert.That(model.Frames.Select(_ => _.IsReset)).IsEquivalentTo([false, false, true, true, false, false]);

        var readModel = model.Frames[3];
        await Assert.That(readModel.Namespace).IsEqualTo("Cart");
        await Assert.That(readModel.Name).IsEqualTo("Items");
        await Assert.That(readModel.SourceFrameIds).IsEquivalentTo(["03"]);
    }

    [Test]
    public async Task Parse_InlineDataAndReferences()
    {
        var model = Parse(
            """
            eventmodeling
            tf 01 cmd AddItem { productId: 7 }
            tf 02 rmo UserAdded `json`{ "name": "foo" }
            tf 03 evt ItemAdded [[ItemAddedData]]
            tf 04 evt Quoted "some text"
            """);

        await Assert.That(model.Frames[0].InlineData).IsEqualTo("productId: 7");
        await Assert.That(model.Frames[1].DataType).IsEqualTo("json");
        await Assert.That(model.Frames[1].InlineData).IsEqualTo("\"name\": \"foo\"");
        await Assert.That(model.Frames[2].DataReference).IsEqualTo("ItemAddedData");
        await Assert.That(model.Frames[2].InlineData).IsNull();
        await Assert.That(model.Frames[3].InlineData).IsEqualTo("some text");
    }

    // The opening brace may be on the next line, and a nested closing brace does not end the block.
    [Test]
    public async Task Parse_DataBlocks()
    {
        var model = Parse(
            """
            eventmodeling
                tf 01 cmd AddItem
                tf 02 evt ItemAdded [[ItemAddedData]]

                data ItemAddedData
                {
                  productId: 7
                }
                data Nested `json`{
                  "a": {
                    "b": 1
                  }
                }
                note 02 {
                  A note
                }
            """);

        await Assert.That(model.DataBlocks.Count).IsEqualTo(2);
        await Assert.That(model.DataBlocks[0].Name).IsEqualTo("ItemAddedData");
        await Assert.That(model.DataBlocks[0].Content).IsEqualTo("productId: 7");
        await Assert.That(model.DataBlocks[1].DataType).IsEqualTo("json");
        await Assert.That(model.DataBlocks[1].Content).IsEqualTo("\"a\": {\n  \"b\": 1\n}");
        await Assert.That(model.Notes.Single().FrameId).IsEqualTo("02");
        await Assert.That(model.Notes.Single().Content).IsEqualTo("A note");
    }

    // Title, accessibility, comments of each kind, entity and gwt are all accepted.
    [Test]
    public async Task Parse_OtherStatements()
    {
        var model = Parse(
            """
            eventmodeling
            title My model
            accTitle: Accessible title
            accDescr { A longer
            description }
            %% Mermaid comment
            // C-style comment
            /* block comment */
            entity ItemAdded
            entity AddItem
            tf 01 cmd AddItem
            tf 02 evt ItemAdded
            gwt 02 given
              evt ItemAdded
            when
              cmd AddItem
            then
              evt ItemAdded
            """);

        await Assert.That(model.Title).IsEqualTo("My model");
        await Assert.That(model.Frames.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_DuplicateFrameIdFails()
    {
        var exception = Assert.Throws<MermaidParseException>(() => new EventModelingParser().Parse(
            """
            eventmodeling
            tf 01 ui UI
            tf 02 cmd Command
            rf 01 evt Event
            """));

        await Assert.That(exception.Message).Contains("Duplicate event modeling frame ID \"01\" on line 4");
    }

    [Test]
    public async Task Parse_UnknownEntityTypeFails()
    {
        var result = new EventModelingParser().Parse(
            """
            eventmodeling
            tf 01 widget CartUI
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("eventmodeling\ntf 01 ui CartUI", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.EventModeling);
        await Assert.That(Mermaid.TryDetectType("%% comment\neventmodeling", out var afterComment)).IsTrue();
        await Assert.That(afterComment).IsEqualTo(DiagramType.EventModeling);
    }

    static EventModelingModel Parse(string input)
    {
        var result = new EventModelingParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
