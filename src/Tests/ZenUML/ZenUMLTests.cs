using Naiad.Diagrams.Sequence;

public class ZenUMLTests : TestBase
{
    [Test]
    public Task AsyncMessages()
    {
        const string input =
            """
            zenuml
                title Demo
                Alice->John: Hello John, how are you?
                John->Alice: Great!
                Alice->John: See you later!
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ParticipantsAnnotatorsAndAliases()
    {
        const string input =
            """
            zenuml
                @Actor Alice
                @Database Bob
                J as John
                J->Alice: Hi Alice
                Alice->Bob: Hi Bob
            """;

        return VerifySvg(input);
    }

    // A call nobody is said to make comes in from the left, from a starter that is not drawn.
    [Test]
    public Task SyncMessages()
    {
        const string input =
            """
            zenuml
                title Sync message
                A.SyncMessage
                A.SyncMessage(with, parameters) {
                  B.nestedSyncMessage()
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CreationAndReplies()
    {
        const string input =
            """
            zenuml
                Client->Service.handle() {
                    repo = new Repository(connection)
                    SomeType order = repo.load(id)
                    return order
                }
            """;

        return VerifySvg(input);
    }

    // Mermaid's reply example: a return inside an if, and an @return that replies one level up.
    [Test]
    public Task ReplyMessages()
    {
        const string input =
            """
            zenuml
                title Reply message
                Client->A.method() {
                  B.method() {
                    if(condition) {
                      return x1
                      // return early
                      @return
                      A->Client: x11
                    }
                  }
                  return x2
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Fragments()
    {
        const string input =
            """
            zenuml
                Alice->Bob: Hello Bob, how are you?
                if(is_sick) {
                  Bob->Alice: Not so good :(
                } else if(is_tired) {
                  Bob->Alice: Sleepy
                } else {
                  Bob->Alice: Feeling fresh like a daisy
                }
                opt {
                  Bob->Alice: Thanks for asking
                }
                while(true) {
                  John->Alice: Great!
                }
                par {
                    Alice->Bob: Hello guys!
                    Alice->John: Hello guys!
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TryCatchFinally()
    {
        const string input =
            """
            zenuml
                try {
                  Consumer->API: Book something
                  API->BookingService: Start booking process
                } catch(BookingError) {
                  API->Consumer: show failure
                } finally {
                  API->BookingService: rollback status
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Comments()
    {
        const string input =
            """
            zenuml
                // a comment on a participant will not be rendered
                BookService
                // a comment on a message.
                // **Markdown** is supported.
                BookService.getBook()
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_NestedCallsAnswerTheirCaller()
    {
        var model = Parse(
            """
            zenuml
                Client->A.method() {
                  b = B.query()
                  return done
                }
            """);

        await Assert.That(Describe(model.Elements))
            .IsEqualTo("Client->A:method()+|A->B:query()|B-->A:b|A-->Client:done-");
        await Assert.That(model.Participants.Any(static _ => _.IsHidden)).IsFalse();
    }

    // A condition or argument list ended at the first `)`, so one holding a call or a string with a `)`
    // in it failed to parse.
    [Test]
    public async Task Parse_ParenthesesNestAndHoldStrings()
    {
        var model = Parse(
            """
            zenuml
                if(user.isValid()) {
                  A.save(order.getId(), "a)b")
                }
                while(it's raining) {
                  A.wait('(')
                }
            """);

        var alt = model.Elements.OfType<Alt>().Single();
        await Assert.That(alt.Condition).IsEqualTo("user.isValid()");
        await Assert.That(Describe(alt.Elements)).IsEqualTo("_STARTER_->A:save(order.getId(), \"a)b\")");
        var loop = model.Elements.OfType<Loop>().Single();
        await Assert.That(loop.Label).IsEqualTo("it's raining");
        await Assert.That(Describe(loop.Elements)).IsEqualTo("_STARTER_->A:wait('(')");
    }

    [Test]
    public async Task Parse_StarterIsHiddenAndFirst()
    {
        var model = Parse(
            """
            zenuml
                Bob
                A.run()
            """);

        await Assert.That(string.Join(",", model.Participants.Select(static _ => $"{_.Id}:{_.IsHidden}")))
            .IsEqualTo("_STARTER_:True,Bob:False,A:False");
    }

    [Test]
    public async Task Parse_NamedStarter()
    {
        var model = Parse(
            """
            zenuml
                @Starter(User)
                A.run()
            """);

        await Assert.That(Describe(model.Elements)).IsEqualTo("User->A:run()");
        await Assert.That(model.Participants.Any(static _ => _.IsHidden)).IsFalse();
    }

    [Test]
    public async Task Parse_ElseWithoutIf_Fails() =>
        await Assert.That(() => new ZenUMLParser().Parse(
                """
                zenuml
                    opt {
                      A->B: x
                    } else {
                      A->B: y
                    }
                """))
            .Throws<MermaidParseException>();

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("zenuml\n  A->B: hi", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.ZenUML);
    }

    static SequenceModel Parse(string input)
    {
        var result = new ZenUMLParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }

    static string Describe(List<SequenceElement> elements) =>
        string.Join(
            "|",
            elements.OfType<Message>().Select(static _ =>
            {
                var arrow = _.Type is MessageType.DottedArrow or MessageType.DottedAsync ? "-->" : "->";
                return $"{_.FromId}{arrow}{_.ToId}:{_.Text}{(_.Activate ? "+" : "")}{(_.Deactivate ? "-" : "")}";
            }));
}
