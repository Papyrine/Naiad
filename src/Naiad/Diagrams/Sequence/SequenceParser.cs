using NotePosition = Naiad.Diagrams.Sequence.NotePosition;

class SequenceParser : IDiagramParser<SequenceModel>
{
    static readonly Parser<char, SequenceModel> parser;

    static SequenceParser()
    {
        // Sequence diagram identifier (no dash to avoid conflicts with arrows)
        var seqIdentifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_')
                .AtLeastOnceString()
                .Labelled("identifier");

        // Participant declaration: participant/actor Name as Alias
        var participantParser =
            from _ in CommonParsers.InlineWhitespace
            from type in OneOf(
                Try(String("actor")).ThenReturn(ParticipantType.Actor),
                String("participant").ThenReturn(ParticipantType.Participant)
            )
            from __ in CommonParsers.RequiredWhitespace
            from id in seqIdentifier
            from alias in Try(
                CommonParsers.RequiredWhitespace
                    .Then(String("as"))
                    .Then(CommonParsers.RequiredWhitespace)
                    .Then(Token(_ => _ != '\r' && _ != '\n').AtLeastOnceString())
            ).Optional()
            from ___ in CommonParsers.LineEnd
            select new Participant
            {
                Id = id,
                Alias = alias.HasValue ? alias.Value : null,
                Type = type
            };

        // Message arrows
        var messageArrowParser =
            OneOf(
                Try(String("-->>")).ThenReturn(MessageType.DottedArrow),
                Try(String("->>")).ThenReturn(MessageType.SolidArrow),
                Try(String("--x")).ThenReturn(MessageType.DottedCross),
                Try(String("-x")).ThenReturn(MessageType.SolidCross),
                Try(String("--)")).ThenReturn(MessageType.DottedAsync),
                Try(String("-)")).ThenReturn(MessageType.SolidAsync),
                Try(String("-->")).ThenReturn(MessageType.DottedOpen),
                String("->").ThenReturn(MessageType.SolidOpen)
            );

        // Message: From->>To: Text
        var messageParser =
            from _ in CommonParsers.InlineWhitespace
            from fromId in seqIdentifier
            from __ in CommonParsers.InlineWhitespace
            from arrow in messageArrowParser
            from activate in Char('+').Optional()
            from deactivate in Char('-').Optional()
            from ___ in CommonParsers.InlineWhitespace
            from toId in seqIdentifier
            from ____ in CommonParsers.InlineWhitespace
            from text in Try(
                Char(':')
                    .Then(CommonParsers.InlineWhitespace)
                    .Then(Token(_ => _ != '\r' && _ != '\n').ManyString())
            ).Optional()
            from _____ in CommonParsers.LineEnd
            select new Message
            {
                FromId = fromId,
                ToId = toId,
                Text = text.HasValue ? text.Value : null,
                Type = arrow,
                Activate = activate.HasValue,
                Deactivate = deactivate.HasValue
            };

        // Note: Note right of/left of/over Participant: Text
        var noteParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in Try(String("Note")).Or(String("note"))
            from __ in CommonParsers.RequiredWhitespace
            from position in OneOf(
                Try(String("right of")).ThenReturn(NotePosition.RightOf),
                Try(String("left of")).ThenReturn(NotePosition.LeftOf),
                String("over").ThenReturn(NotePosition.Over)
            )
            from ___ in CommonParsers.RequiredWhitespace
            from participantId in seqIdentifier
            from participant2 in Try(
                Char(',')
                    .Then(CommonParsers.InlineWhitespace)
                    .Then(seqIdentifier)
            ).Optional()
            from ____ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from _____ in CommonParsers.InlineWhitespace
            from text in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from ______ in CommonParsers.LineEnd
            select new Note
            {
                Text = text,
                Position = position,
                ParticipantId = participantId,
                OverParticipantId2 = participant2.HasValue ? participant2.Value : null
            };

        var activationParser =
            from _ in CommonParsers.InlineWhitespace
            from isActivate in OneOf(
                String("activate").ThenReturn(true),
                String("deactivate").ThenReturn(false)
            )
            from __ in CommonParsers.RequiredWhitespace
            from participantId in seqIdentifier
            from ___ in CommonParsers.LineEnd
            select new Activation
            {
                ParticipantId = participantId,
                IsActivate = isActivate
            };

        var autoNumberParser =
            CommonParsers.InlineWhitespace
                .Then(String("autonumber"))
                .Then(CommonParsers.LineEnd)
                .ThenReturn(true);

        var titleParser =
            CommonParsers.InlineWhitespace
                .Then(String("title"))
                .Then(CommonParsers.InlineWhitespace)
                .Then(Token(_ => _ != '\r' && _ != '\n').ManyString())
                .Before(CommonParsers.LineEnd);

        // Block markers (alt/else/end, loop, par/and, opt, critical, break, rect)
        // These are skipped for now - content renders without visual grouping
        var blockStartParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in OneOf(
                Try(String("alt")),
                Try(String("else")),
                Try(String("loop")),
                Try(String("par")),
                Try(String("and")),
                Try(String("opt")),
                Try(String("critical")),
                Try(String("break")),
                Try(String("rect")),
                String("end")
            )
            from __ in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from ___ in CommonParsers.LineEnd
            select Unit.Value;

        var skipLine =
            OneOf(
                Try(blockStartParser),
                CommonParsers.InlineWhitespace.Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline))
            );

        var parseContent =
            OneOf(
                Try(participantParser.Select<ISequenceContent?>(_ => new ParticipantItem(_))),
                Try(messageParser.Select<ISequenceContent?>(_ => new MessageItem(_))),
                Try(noteParser.Select<ISequenceContent?>(_ => new NoteItem(_))),
                Try(activationParser.Select<ISequenceContent?>(_ => new ActivationItem(_))),
                Try(autoNumberParser.Select<ISequenceContent?>(_ => new AutoNumberItem(_))),
                Try(titleParser.Select<ISequenceContent?>(_ => new TitleItem(_))),
                skipLine.ThenReturn<ISequenceContent?>(null)
            ).Many();

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("sequenceDiagram")
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from content in parseContent
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(content);
    }

    static SequenceModel BuildModel(IEnumerable<ISequenceContent?> content)
    {
        var model = new SequenceModel();
        var participantIds = new HashSet<string>();

        foreach (var item in content)
        {
            switch (item)
            {
                case ParticipantItem participant:
                    var p = participant.Value;
                    model.Participants.Add(p);
                    participantIds.Add(p.Id);
                    break;

                case MessageItem message:
                    var m = message.Value;
                    // Auto-add participants from messages
                    if (!participantIds.Contains(m.FromId))
                    {
                        model.Participants.Add(
                            new()
                            {
                                Id = m.FromId
                            });
                        participantIds.Add(m.FromId);
                    }
                    if (!participantIds.Contains(m.ToId))
                    {
                        model.Participants.Add(
                            new()
                            {
                                Id = m.ToId
                            });
                        participantIds.Add(m.ToId);
                    }
                    model.Elements.Add(m);
                    break;

                case NoteItem note:
                    model.Elements.Add(note.Value);
                    break;

                case ActivationItem activation:
                    model.Elements.Add(activation.Value);
                    break;

                case AutoNumberItem autoNumber:
                    model.AutoNumber = autoNumber.Value;
                    break;

                case TitleItem title:
                    model.Title = title.Value;
                    break;
            }
        }

        return model;
    }

    public Result<char, SequenceModel> Parse(string input) => parser.Parse(input);

    interface ISequenceContent;
    readonly record struct ParticipantItem(Participant Value) : ISequenceContent;
    readonly record struct MessageItem(Message Value) : ISequenceContent;
    readonly record struct NoteItem(Note Value) : ISequenceContent;
    readonly record struct ActivationItem(Activation Value) : ISequenceContent;
    readonly record struct AutoNumberItem(bool Value) : ISequenceContent;
    readonly record struct TitleItem(string Value) : ISequenceContent;
}
