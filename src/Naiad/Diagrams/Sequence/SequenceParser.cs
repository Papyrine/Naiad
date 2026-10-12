using NotePosition = Naiad.Diagrams.Sequence.NotePosition;
using Rect = Naiad.Diagrams.Sequence.Rect;

class SequenceParser : IDiagramParser<SequenceModel>
{
    static readonly Parser<SequenceModel> parser;

    static SequenceParser()
    {
        // Sequence diagram identifier: letters, digits and underscores, plus a hyphen between two of them
        // (`web-app`). A hyphen that starts an arrow is not part of the name - and, as in Mermaid, neither
        // is one followed by `x`, which is how the `-x` arrow is spelled.
        var idChars =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_');

        var idHyphen =
            Literals.Char('-')
                .WhenFollowedBy(
                    Literals.Pattern(static _ => (char.IsLetterOrDigit(_) || _ == '_') && _ != 'x', minSize: 1, maxSize: 1));

        var seqIdentifier =
            Capture(idChars.And(ZeroOrMany(idHyphen.And(idChars))))
                .AsString();

        // The rest of the line as it is read, and as the string kept from it.
        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine =
            restOfLineText.AsString();

        var inlineGap =
            Literals.AnyOf(" \t");

        // Participant declaration: participant/actor Name as Alias, optionally introduced by `create`.
        var participantParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("create").SkipAnd(CommonParsers.RequiredWhitespace).Optional())
                .And(
                    OneOf(
                        Literals.Text("actor").Then(ParticipantType.Actor),
                        Literals.Text("participant").Then(ParticipantType.Participant)
                    ))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(seqIdentifier)
                .And(
                    CommonParsers.RequiredWhitespace
                        .SkipAnd(Literals.Text("as"))
                        .SkipAnd(CommonParsers.RequiredWhitespace)
                        .SkipAnd(Literals.NoneOf("\r\n").AsString())
                        .Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (created, type, id, alias) =>
                    new Participant
                    {
                        Id = id,
                        Alias = alias.HasValue ? Unquote(alias.Value.Trim()) : null,
                        Type = type,
                        IsCreated = created.HasValue
                    });

        // Message arrows
        var messageArrowParser =
            OneOf(
                Literals.Text("<<-->>").Then(MessageType.DottedBiDirectional),
                Literals.Text("<<->>").Then(MessageType.BiDirectional),
                Literals.Text("-->>").Then(MessageType.DottedArrow),
                Literals.Text("->>").Then(MessageType.SolidArrow),
                Literals.Text("--x").Then(MessageType.DottedCross),
                Literals.Text("-x").Then(MessageType.SolidCross),
                Literals.Text("--)").Then(MessageType.DottedAsync),
                Literals.Text("-)").Then(MessageType.SolidAsync),
                Literals.Text("-->").Then(MessageType.DottedOpen),
                Literals.Text("->").Then(MessageType.SolidOpen)
            );

        // Message: From->>To: Text
        var messageParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(seqIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(messageArrowParser)
                .And(Literals.Char('+').Optional())
                .And(Literals.Char('-').Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(seqIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(
                    Literals.Char(':')
                        .SkipAnd(CommonParsers.InlineWhitespace)
                        .SkipAnd(restOfLine)
                        .Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (fromId, arrow, activate, deactivate, toId, text) =>
                    new Message
                    {
                        FromId = fromId,
                        ToId = toId,
                        Text = text.OrNull(),
                        Type = arrow,
                        Activate = activate.HasValue,
                        Deactivate = deactivate.HasValue
                    });

        // Note: Note right of/left of/over Participant: Text
        var noteParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("Note").Or(Literals.Text("note")))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(
                    OneOf(
                        Literals.Text("right of").Then(NotePosition.RightOf),
                        Literals.Text("left of").Then(NotePosition.LeftOf),
                        Literals.Text("over").Then(NotePosition.Over)
                    ))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(seqIdentifier)
                .And(
                    Literals.Char(',')
                        .SkipAnd(CommonParsers.InlineWhitespace)
                        .SkipAnd(seqIdentifier)
                        .Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (position, participantId, participant2, text) =>
                    new Note
                    {
                        Text = text,
                        Position = position,
                        ParticipantId = participantId,
                        OverParticipantId2 = participant2.OrNull()
                    });

        var activationParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Text("activate").Then(true),
                        Literals.Text("deactivate").Then(false)
                    ))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(seqIdentifier)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (isActivate, participantId) =>
                    new Activation
                    {
                        ParticipantId = participantId,
                        IsActivate = isActivate
                    });

        // autonumber, autonumber 10, autonumber 10 5, autonumber off
        var autoNumberParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("autonumber"))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then(ParseAutoNumber);

        // destroy Name
        var destroyParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("destroy"))
                .SkipAnd(inlineGap)
                .SkipAnd(seqIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => new DestroyItem(_));

        // What follows a keyword on its line: text set off from it by a gap, or nothing at all.
        var keywordText =
            OneOf(
                inlineGap.SkipAnd(restOfLineText.AsTrimmedString()),
                Always("").WhenFollowedBy(CommonParsers.LineEnd));

        // box, box Title, box Aqua Title, box rgb(33,66,99) Title
        var boxParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("box"))
                .SkipAnd(keywordText)
                .AndSkip(CommonParsers.LineEnd)
                .Then(ParseBox);

        // Menus and tooltips attached to a participant; they have no place in a static rendering.
        var ignoredParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Text("links"),
                        Literals.Text("link"),
                        Literals.Text("properties"),
                        Literals.Text("details")))
                .SkipAnd(inlineGap)
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd);

        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("title"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        // Block markers: `loop`, `alt`/`else`, `opt`, `par`/`and`, `critical`/`option`, `break` and `rect`
        // open or continue a block, and `end` closes one. The keyword has to stand alone as a word, so a
        // line that merely starts with one (`participant`, `android->>B`) is not taken for a marker.
        var blockMarkerParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Text("alt"),
                        Literals.Text("else"),
                        Literals.Text("loop"),
                        Literals.Text("par"),
                        Literals.Text("and"),
                        Literals.Text("option"),
                        Literals.Text("opt"),
                        Literals.Text("critical"),
                        Literals.Text("break"),
                        Literals.Text("rect"),
                        Literals.Text("end")
                    ))
                .And(keywordText)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (keyword, text) => new BlockMarkerItem(keyword, text));

        var skipLine =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                participantParser.Then<ISequenceContent?>(static _ => new ParticipantItem(_)),
                messageParser.Then<ISequenceContent?>(static _ => new MessageItem(_)),
                noteParser.Then<ISequenceContent?>(static _ => new NoteItem(_)),
                activationParser.Then<ISequenceContent?>(static _ => new ActivationItem(_)),
                autoNumberParser.Then<ISequenceContent?>(static _ => _),
                destroyParser.Then<ISequenceContent?>(static _ => _),
                boxParser.Then<ISequenceContent?>(static _ => _),
                ignoredParser.Then<ISequenceContent?>(static _ => null),
                titleParser.Then<ISequenceContent?>(static _ => new TitleItem(_)),
                blockMarkerParser.Then<ISequenceContent?>(static _ => _),
                skipLine.Then<ISequenceContent?>(static _ => null)
            ).Many();

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("sequenceDiagram"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(parseContent)
                // Nothing may be left over: without this a line no rule matches ends the list quietly and the
                // rest of the diagram is dropped instead of being reported.
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
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

    public DiagramParseResult<SequenceModel> Parse(string input) => parser.ParseDiagram(input);

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
