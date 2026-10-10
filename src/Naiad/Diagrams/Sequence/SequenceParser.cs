using NotePosition = Naiad.Diagrams.Sequence.NotePosition;
using Rect = Naiad.Diagrams.Sequence.Rect;

class SequenceParser : IDiagramParser<SequenceModel>
{
    static readonly Parser<char, SequenceModel> parser;

    static SequenceParser()
    {
        // Sequence diagram identifier: letters, digits and underscores, plus a hyphen between two of them
        // (`web-app`). A hyphen that starts an arrow is not part of the name - and, as in Mermaid, neither
        // is one followed by `x`, which is how the `-x` arrow is spelled.
        var idChar =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_');

        var seqIdentifier =
            idChar
                .Then(idChar.Or(Try(Char('-').Before(Lookahead(Token(_ => (char.IsLetterOrDigit(_) || _ == '_') && _ != 'x'))))).SkipMany())
                .Slice((span, _) => span.ToString())
                .Labelled("identifier");

        // Participant declaration: participant/actor Name as Alias, optionally introduced by `create`.
        var participantParser =
            from _ in CommonParsers.InlineWhitespace
            from created in Try(String("create").Then(CommonParsers.RequiredWhitespace)).Optional()
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
                Alias = alias.HasValue ? Unquote(alias.Value.Trim()) : null,
                Type = type,
                IsCreated = created.HasValue
            };

        // Message arrows
        var messageArrowParser =
            OneOf(
                Try(String("<<-->>")).ThenReturn(MessageType.DottedBiDirectional),
                Try(String("<<->>")).ThenReturn(MessageType.BiDirectional),
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

        // autonumber, autonumber 10, autonumber 10 5, autonumber off
        var autoNumberParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("autonumber")
            from arguments in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from __ in CommonParsers.LineEnd
            select ParseAutoNumber(arguments);

        // destroy Name
        var destroyParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("destroy")
            from __ in Token(_ => _ is ' ' or '\t').SkipAtLeastOnce()
            from id in seqIdentifier
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            select new DestroyItem(id);

        // box, box Title, box Aqua Title, box rgb(33,66,99) Title
        var boxParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("box")
            from text in OneOf(
                Token(_ => _ is ' ' or '\t').SkipAtLeastOnce()
                    .Then(Token(_ => _ != '\r' && _ != '\n').ManyString()),
                Lookahead(CommonParsers.LineEnd).ThenReturn(""))
            from __ in CommonParsers.LineEnd
            select ParseBox(text.Trim());

        // Menus and tooltips attached to a participant; they have no place in a static rendering.
        var ignoredParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in OneOf(
                Try(String("links")),
                Try(String("link")),
                Try(String("properties")),
                String("details"))
            from __ in Token(_ => _ is ' ' or '\t').SkipAtLeastOnce()
            from rest in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from ___ in CommonParsers.LineEnd
            select Unit.Value;

        var titleParser =
            CommonParsers.InlineWhitespace
                .Then(String("title"))
                .Then(CommonParsers.InlineWhitespace)
                .Then(Token(_ => _ != '\r' && _ != '\n').ManyString())
                .Before(CommonParsers.LineEnd);

        // Block markers: `loop`, `alt`/`else`, `opt`, `par`/`and`, `critical`/`option`, `break` and `rect`
        // open or continue a block, and `end` closes one. The keyword has to stand alone as a word, so a
        // line that merely starts with one (`participant`, `android->>B`) is not taken for a marker.
        var blockMarkerParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in OneOf(
                Try(String("alt")),
                Try(String("else")),
                Try(String("loop")),
                Try(String("par")),
                Try(String("and")),
                Try(String("option")),
                Try(String("opt")),
                Try(String("critical")),
                Try(String("break")),
                Try(String("rect")),
                String("end")
            )
            from text in OneOf(
                Token(_ => _ is ' ' or '\t').SkipAtLeastOnce()
                    .Then(Token(_ => _ != '\r' && _ != '\n').ManyString()),
                Lookahead(CommonParsers.LineEnd).ThenReturn(""))
            from ___ in CommonParsers.LineEnd
            select new BlockMarkerItem(keyword, text.Trim());

        var skipLine =
            CommonParsers.InlineWhitespace.Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                Try(participantParser.Select<ISequenceContent?>(_ => new ParticipantItem(_))),
                Try(messageParser.Select<ISequenceContent?>(_ => new MessageItem(_))),
                Try(noteParser.Select<ISequenceContent?>(_ => new NoteItem(_))),
                Try(activationParser.Select<ISequenceContent?>(_ => new ActivationItem(_))),
                Try(autoNumberParser.Select<ISequenceContent?>(_ => _)),
                Try(destroyParser.Select<ISequenceContent?>(_ => _)),
                Try(boxParser.Select<ISequenceContent?>(_ => _)),
                Try(ignoredParser.ThenReturn<ISequenceContent?>(null)),
                Try(titleParser.Select<ISequenceContent?>(_ => new TitleItem(_))),
                Try(blockMarkerParser.Select<ISequenceContent?>(_ => _)),
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

    static string Unquote(string text)
    {
        if (text.Length >= 2 &&
            text[0] == '"' &&
            text[^1] == '"')
        {
            return text[1..^1];
        }

        return text;
    }

    static AutoNumberItem ParseAutoNumber(string arguments)
    {
        var parts = arguments.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["off"])
        {
            return new(false, 1, 1);
        }

        var start = 1;
        var step = 1;
        if (parts.Length > 0 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first))
        {
            start = first;
        }

        if (parts.Length > 1 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
        {
            step = second;
        }

        return new(true, start, step);
    }

    // The text after `box`: an optional colour - a CSS colour name, `transparent`, or an rgb()/rgba()
    // call - and then the title.
    static BoxItem ParseBox(string text)
    {
        if (text.Length == 0)
        {
            return new(null, null);
        }

        var colorEnd = text.IndexOfAny([' ', '\t']);
        var close = text.IndexOf(')');
        if (close > 0 &&
            text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            colorEnd = close + 1;
        }

        if (colorEnd < 0)
        {
            colorEnd = text.Length;
        }

        var first = text[..colorEnd];
        if (!CssColor.TryParse(first, out _))
        {
            return new(null, text);
        }

        var title = text[colorEnd..].Trim();
        if (title.Length == 0)
        {
            return new(first, null);
        }

        return new(first, title);
    }

    static SequenceModel BuildModel(IEnumerable<ISequenceContent?> content)
    {
        var model = new SequenceModel();
        var participantIds = new HashSet<string>();

        // The blocks currently open, innermost last, each with the list its next element goes into: the
        // block's own body, or the branch its latest `else` / `and` / `option` began.
        var open = new Stack<(SequenceElement Block, List<SequenceElement> Target)>();

        // The `box` whose participants are being declared, if any.
        ParticipantBox? openBox = null;

        void Add(SequenceElement element)
        {
            if (open.TryPeek(out var top))
            {
                top.Target.Add(element);
                return;
            }

            model.Elements.Add(element);
        }

        void Open(SequenceElement block, List<SequenceElement> body)
        {
            Add(block);
            open.Push((block, body));
        }

        // `else`, `and` and `option` each continue one kind of block, which has to be the innermost.
        void Branch(string keyword, string owner, List<SequenceElement>? branch)
        {
            if (branch is null)
            {
                throw new MermaidParseException(
                    $"Failed to parse sequence diagram: '{keyword}' is only valid inside a '{owner}' block");
            }

            var (block, _) = open.Pop();
            open.Push((block, branch));
        }

        static string? TextOrNull(string text)
        {
            if (text.Length == 0)
            {
                return null;
            }

            return text;
        }

        foreach (var item in content)
        {
            switch (item)
            {
                case ParticipantItem participant:
                    var p = participant.Value;
                    if (participantIds.Add(p.Id))
                    {
                        model.Participants.Add(p);
                    }
                    else
                    {
                        // Already known from an earlier message; the declaration fills in the details.
                        var known = model.Participants.First(_ => _.Id == p.Id);
                        known.Alias ??= p.Alias;
                        known.Type = p.Type;
                        known.IsCreated |= p.IsCreated;
                    }

                    openBox?.ParticipantIds.Add(p.Id);
                    break;

                case DestroyItem destroy:
                    foreach (var destroyed in model.Participants.Where(_ => _.Id == destroy.Id))
                    {
                        destroyed.IsDestroyed = true;
                    }

                    break;

                case BoxItem box:
                    openBox = new()
                    {
                        Color = box.Color,
                        Title = box.Title
                    };
                    model.Boxes.Add(openBox);
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
                    Add(m);
                    break;

                case NoteItem note:
                    Add(note.Value);
                    break;

                case ActivationItem activation:
                    Add(activation.Value);
                    break;

                case BlockMarkerItem marker:
                    var text = TextOrNull(marker.Text);
                    var innermost = open.TryPeek(out var current) ? current.Block : null;
                    switch (marker.Keyword)
                    {
                        case "loop":
                            var loop = new Loop { Label = text };
                            Open(loop, loop.Elements);
                            break;
                        case "alt":
                            var alt = new Alt { Condition = text };
                            Open(alt, alt.Elements);
                            break;
                        case "opt":
                            var opt = new Opt { Condition = text };
                            Open(opt, opt.Elements);
                            break;
                        case "par":
                            var par = new Par { Label = text };
                            Open(par, par.Elements);
                            break;
                        case "critical":
                            var critical = new Critical { Label = text };
                            Open(critical, critical.Elements);
                            break;
                        case "break":
                            var breakBlock = new Break { Label = text };
                            Open(breakBlock, breakBlock.Elements);
                            break;
                        case "rect":
                            var rect = new Rect { Color = text };
                            Open(rect, rect.Elements);
                            break;
                        case "else":
                            List<SequenceElement>? elseBranch = null;
                            if (innermost is Alt owningAlt)
                            {
                                var branch = new AltElse { Condition = text };
                                owningAlt.ElseBranches.Add(branch);
                                elseBranch = branch.Elements;
                            }

                            Branch("else", "alt", elseBranch);
                            break;
                        case "and":
                            List<SequenceElement>? andBranch = null;
                            if (innermost is Par owningPar)
                            {
                                var branch = new ParAnd { Label = text };
                                owningPar.AndBranches.Add(branch);
                                andBranch = branch.Elements;
                            }

                            Branch("and", "par", andBranch);
                            break;
                        case "option":
                            List<SequenceElement>? optionBranch = null;
                            if (innermost is Critical owningCritical)
                            {
                                var branch = new CriticalOption { Label = text };
                                owningCritical.OptionBranches.Add(branch);
                                optionBranch = branch.Elements;
                            }

                            Branch("option", "critical", optionBranch);
                            break;
                        default:
                            // `end` closes the innermost block, or failing that the participant box.
                            if (open.Count == 0 &&
                                openBox is not null)
                            {
                                openBox = null;
                                break;
                            }

                            if (!open.TryPop(out _))
                            {
                                throw new MermaidParseException(
                                    "Failed to parse sequence diagram: 'end' without a block to close");
                            }

                            break;
                    }

                    break;

                case AutoNumberItem autoNumber:
                    model.AutoNumber = autoNumber.Enabled;
                    model.AutoNumberStart = autoNumber.Start;
                    model.AutoNumberStep = autoNumber.Step;
                    break;

                case TitleItem title:
                    model.Title = title.Value;
                    break;
            }
        }

        if (open.Count > 0 ||
            openBox is not null)
        {
            throw new MermaidParseException(
                "Failed to parse sequence diagram: a block is missing its closing 'end'");
        }

        return model;
    }

    public Result<char, SequenceModel> Parse(string input) => parser.Parse(input);

    interface ISequenceContent;
    readonly record struct ParticipantItem(Participant Value) : ISequenceContent;
    readonly record struct MessageItem(Message Value) : ISequenceContent;
    readonly record struct NoteItem(Note Value) : ISequenceContent;
    readonly record struct ActivationItem(Activation Value) : ISequenceContent;
    readonly record struct AutoNumberItem(bool Enabled, int Start, int Step) : ISequenceContent;
    readonly record struct DestroyItem(string Id) : ISequenceContent;
    readonly record struct BoxItem(string? Color, string? Title) : ISequenceContent;
    readonly record struct TitleItem(string Value) : ISequenceContent;
    readonly record struct BlockMarkerItem(string Keyword, string Text) : ISequenceContent;
}
