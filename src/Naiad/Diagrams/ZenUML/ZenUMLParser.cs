using NotePosition = Naiad.Diagrams.Sequence.NotePosition;

/// <summary>
/// Reads ZenUML, Mermaid's code-like way of writing a sequence diagram, into a <see cref="SequenceModel"/>
/// so it is drawn by the sequence renderer. Calls nest in braces (<c>A.method() { B.call() }</c>), and
/// fragments are written as control flow: <c>if</c>/<c>else</c> is an alt, a loop keyword a loop,
/// <c>try</c>/<c>catch</c>/<c>finally</c> a critical region. Each statement is read as a line, and the
/// lines are then folded into the model by following the braces.
/// </summary>
class ZenUMLParser : IDiagramParser<SequenceModel>
{
    /// <summary>
    /// The participant a call comes from when nothing says who makes it: ZenUML draws such a call coming in
    /// from the left edge, where no participant is shown.
    /// </summary>
    internal const string StarterId = "_STARTER_";

    interface ILine;

    record TitleLine(string Text) : ILine;

    record CommentLine(string Text) : ILine;

    record DeclarationLine(string Id, string? Label, string? Annotator) : ILine;

    record StarterLine(string Id) : ILine;

    record ReplyAnnotationLine : ILine;

    record ReturnLine(string? Text) : ILine;

    record AsyncLine(string From, string To, string? Text) : ILine;

    /// <summary>
    /// A sync call (<c>A.method(args)</c>) or, with <see cref="IsCreation"/>, a creation
    /// (<c>new A(args)</c>), optionally assigned to a variable and optionally opening a block.
    /// </summary>
    record CallLine(string? Assignee, string? From, string Target, string Text, bool IsCreation, bool Opens, bool Closes) : ILine;

    record OpenLine(string Keyword, string? Condition) : ILine;

    /// <summary>A closing brace, optionally carrying on into the next branch: <c>} else {</c>, <c>} catch {</c>.</summary>
    record CloseLine(string? Continuation, string? Condition) : ILine;

    static readonly Parser<SequenceModel> parser;

    static ZenUMLParser()
    {
        var gap = CommonParsers.InlineWhitespace;
        var requiredGap = Literals.AnyOf(" \t");
        var lineEnd = gap.SkipAnd(CommonParsers.LineEnd);

        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '$')
                .AsString();

        var name = CommonParsers.DoubleQuotedString.Or(identifier);

        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0)
                .AsTrimmedString();

        // A parenthesised condition or argument list, kept without its parentheses. It may hold calls and
        // strings of its own: `if(user.isValid())`, `save(order.getId())`, `m("a)b")`.
        var parenthesised = new ParenthesisedParser();

        var openBrace = gap.SkipAnd(Literals.Char('{'));

        var title =
            gap.SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(requiredGap)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<ILine>(static _ => new TitleLine(_));

        var comment =
            gap.SkipAnd(Literals.Text("//"))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<ILine>(static _ => new CommentLine(_));

        // `}` alone, or leading into the next branch of the block it closes.
        var continuation =
            OneOf(
                Literals.Text("else")
                    .SkipAnd(requiredGap)
                    .SkipAnd(Literals.Text("if"))
                    .SkipAnd(gap)
                    .SkipAnd(parenthesised)
                    .Then(static _ => (keyword: "else if", condition: (string?) _)),
                Literals.Text("else").Then(static _ => (keyword: "else", condition: (string?) null)),
                Literals.Text("catch")
                    .SkipAnd(gap)
                    .SkipAnd(parenthesised.Optional())
                    .Then(static _ => (keyword: "catch", condition: _.OrNull())),
                Literals.Text("finally").Then(static _ => (keyword: "finally", condition: (string?) null)));

        var close =
            gap.SkipAnd(Literals.Char('}'))
                .SkipAnd(
                    gap.SkipAnd(continuation)
                        .AndSkip(openBrace)
                        .Optional())
                .AndSkip(lineEnd)
                .Then<ILine>(static _ => _.HasValue
                    ? new CloseLine(_.Value.keyword, _.Value.condition)
                    : new CloseLine(null, null));

        // A fragment: the keyword has to be followed by its condition or its brace, so a call on a
        // participant whose name merely starts with one (`format.run()`) is not taken for it.
        var open =
            gap.SkipAnd(
                    OneOf(
                        Literals.Text("if"),
                        Literals.Text("while"),
                        Literals.Text("forEach"),
                        Literals.Text("foreach"),
                        Literals.Text("for"),
                        Literals.Text("loop"),
                        Literals.Text("opt"),
                        Literals.Text("par"),
                        Literals.Text("try"),
                        Literals.Text("critical")))
                .AndSkip(gap)
                .And(parenthesised.Optional())
                .AndSkip(openBrace)
                .AndSkip(lineEnd)
                .Then<ILine>(static _ => new OpenLine(_.Item1.ToString(), _.Item2.OrNull()));

        var replyAnnotation =
            gap.SkipAnd(Literals.Char('@'))
                .SkipAnd(Literals.Text("return").Or(Literals.Text("reply")))
                .AndSkip(lineEnd)
                .Then<ILine>(static _ => new ReplyAnnotationLine());

        var returnStatement =
            gap.SkipAnd(Literals.Text("return"))
                .SkipAnd(
                    requiredGap.SkipAnd(restOfLine)
                        .Then(static _ => _.Length == 0 ? null : (string?) _)
                        .Or(gap.Then(static _ => (string?) null)))
                .AndSkip(CommonParsers.LineEnd)
                .Then<ILine>(static _ => new ReturnLine(_));

        // `a = ` or `SomeType a = ` ahead of a call.
        // An optional rule is not tried again without what it matched, so the typed form is an alternative
        // of its own: otherwise the type would take the variable's name in `a = `.
        var equals =
            gap.SkipAnd(Literals.Char('='))
                .AndSkip(gap);

        var assignment =
            identifier.AndSkip(requiredGap)
                .SkipAnd(identifier)
                .AndSkip(equals)
                .Or(identifier.AndSkip(equals));

        // `{`, `{}` or nothing after a call.
        var block =
            openBrace
                .SkipAnd(gap.SkipAnd(Literals.Char('}')).Optional())
                .Then(static _ => (opens: true, closes: _.HasValue))
                .Or(gap.Then(static _ => (opens: false, closes: false)));

        var sync =
            gap.SkipAnd(assignment.Optional())
                .And(name.AndSkip(gap).AndSkip(Literals.Text("->")).AndSkip(gap).Optional())
                .And(name)
                .AndSkip(Literals.Char('.'))
                .And(identifier)
                .And(parenthesised.Optional())
                .And(block)
                .AndSkip(lineEnd)
                .Then<ILine>(static _ =>
                {
                    var (assignee, from, target, method, arguments, (opens, closes)) = _;
                    var text = arguments.HasValue ? $"{method}({arguments.Value})" : method;
                    return new CallLine(assignee.OrNull(), from.OrNull(), target, text, IsCreation: false, opens, closes);
                });

        var creation =
            gap.SkipAnd(assignment.Optional())
                .AndSkip(Literals.Text("new"))
                .AndSkip(requiredGap)
                .And(name)
                .And(parenthesised.Optional())
                .And(block)
                .AndSkip(lineEnd)
                .Then<ILine>(static _ =>
                {
                    var (assignee, target, _, (opens, closes)) = _;
                    return new CallLine(assignee.OrNull(), null, target, "«create»", IsCreation: true, opens, closes);
                });

        var async =
            gap.SkipAnd(name)
                .AndSkip(gap)
                .AndSkip(Literals.Text("->"))
                .AndSkip(gap)
                .And(name)
                .And(gap.SkipAnd(Literals.Char(':')).SkipAnd(restOfLine).Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Then<ILine>(static _ => new AsyncLine(_.Item1, _.Item2, _.Item3.OrNull()));

        // `@Starter(Alice)` names who the uncalled-for calls come from.
        var starter =
            gap.SkipAnd(Literals.Text("@Starter"))
                .SkipAnd(gap)
                .SkipAnd(Between(Literals.Char('('), gap.SkipAnd(name).AndSkip(gap), Literals.Char(')')))
                .AndSkip(lineEnd)
                .Then<ILine>(static _ => new StarterLine(_));

        // `Bob`, `A as Alice`, `@Actor Alice`.
        var declaration =
            gap.SkipAnd(Literals.Char('@').SkipAnd(identifier).AndSkip(requiredGap).Optional())
                .And(name)
                .And(
                    requiredGap
                        .SkipAnd(Literals.Text("as"))
                        .SkipAnd(requiredGap)
                        .SkipAnd(restOfLine)
                        .Optional())
                .AndSkip(lineEnd)
                .Then<ILine>(static _ => new DeclarationLine(_.Item2, _.Item3.OrNull(), _.Item1.OrNull()));

        var skipLine =
            gap.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline))
                .Then<ILine?>(static _ => null);

        var line =
            OneOf(
                skipLine,
                comment.Then<ILine?>(static _ => _),
                close.Then<ILine?>(static _ => _),
                open.Then<ILine?>(static _ => _),
                replyAnnotation.Then<ILine?>(static _ => _),
                starter.Then<ILine?>(static _ => _),
                returnStatement.Then<ILine?>(static _ => _),
                creation.Then<ILine?>(static _ => _),
                sync.Then<ILine?>(static _ => _),
                async.Then<ILine?>(static _ => _),
                // After the calls, so a variable named title is assigned (`title = A.get()`) rather than
                // taking the rest of its line as the title.
                title.Then<ILine?>(static _ => _),
                declaration.Then<ILine?>(static _ => _));

        parser =
            gap.SkipAnd(TextIgnoreCase("zenuml"))
                .SkipAnd(lineEnd)
                .SkipAnd(line.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => new ModelBuilder().Build(_.OfType<ILine>()));
    }

    public DiagramParseResult<SequenceModel> Parse(string input) => parser.ParseDiagram(input);

    /// <summary>
    /// One open pair of braces. A call's block runs on the lifeline of the participant called, which
    /// replies to the caller; a fragment's block carries on the lifeline it sits on.
    /// </summary>
    sealed class Frame(List<SequenceElement> elements, string current, string? caller)
    {
        public List<SequenceElement> Elements { get; set; } = elements;
        public string Current { get; } = current;
        public string? Caller { get; } = caller;

        /// <summary>The call that opened the block, which is answered when it closes.</summary>
        public CallLine? Call { get; init; }

        /// <summary>The fragment the block is a branch of, which <c>} else {</c> and the like continue.</summary>
        public SequenceElement? Fragment { get; init; }

        public bool Returned { get; set; }

        /// <summary>
        /// A return inside one of the block's fragments answered the call, though its activation runs on to
        /// the end of the block.
        /// </summary>
        public bool Answered { get; set; }
    }

    sealed class ModelBuilder
    {
        SequenceModel model = new();
        HashSet<string> known = [];
        Stack<Frame> frames = new();
        List<string> pendingComments = [];
        bool replyNext;
        string starter = StarterId;

        public SequenceModel Build(IEnumerable<ILine> lines)
        {
            frames.Push(new(model.Elements, starter, null));

            foreach (var line in lines)
            {
                switch (line)
                {
                    case TitleLine title:
                        model.Title = title.Text;
                        break;
                    case CommentLine comment:
                        pendingComments.Add(comment.Text);
                        break;
                    case StarterLine starterLine:
                        starter = starterLine.Id;
                        Participant(starter);
                        break;
                    case DeclarationLine declaration:
                        Declare(declaration);
                        break;
                    case ReplyAnnotationLine:
                        replyNext = true;
                        break;
                    case AsyncLine async:
                        Message(Resolve(async.From), Resolve(async.To), async.Text, replyNext ? MessageType.DottedAsync : MessageType.SolidAsync);
                        replyNext = false;
                        break;
                    case ReturnLine returnLine:
                        Return(returnLine);
                        break;
                    case CallLine call:
                        Call(call);
                        break;
                    case OpenLine openLine:
                        Open(openLine);
                        break;
                    case CloseLine closeLine:
                        Close(closeLine);
                        break;
                }
            }

            while (frames.Count > 1)
            {
                Close(new(null, null));
            }

            // The starter takes a column only when something comes from it.
            if (known.Contains(StarterId))
            {
                var hidden = model.Participants.First(static _ => _.Id == StarterId);
                hidden.IsHidden = true;
                model.Participants.Remove(hidden);
                model.Participants.Insert(0, hidden);
            }

            return model;
        }

        Frame Top => frames.Peek();

        string Current => Top.Current == StarterId ? starter : Top.Current;

        Participant Participant(string id)
        {
            if (known.Add(id))
            {
                var participant = new Participant
                {
                    Id = id
                };
                model.Participants.Add(participant);
                return participant;
            }

            return model.Participants.First(_ => _.Id == id);
        }

        void Declare(DeclarationLine declaration)
        {
            var participant = Participant(declaration.Id);
            if (declaration.Label is { } label)
            {
                participant.Alias = label.Trim('"');
            }

            if (string.Equals(declaration.Annotator, "Actor", StringComparison.OrdinalIgnoreCase))
            {
                participant.Type = ParticipantType.Actor;
            }

            // Comments on a participant are not drawn.
            pendingComments.Clear();
        }

        /// <summary>Comments just above a message are drawn as a note over it.</summary>
        void Message(string from, string to, string? text, MessageType type, bool activate = false, bool deactivate = false)
        {
            Participant(from);
            Participant(to);
            if (pendingComments.Count > 0)
            {
                var hidden = from == StarterId;
                Top.Elements.Add(new Note
                {
                    Text = string.Join("<br/>", pendingComments),
                    Position = NotePosition.Over,
                    ParticipantId = hidden ? to : from,
                    OverParticipantId2 = hidden || from == to ? null : to
                });
                pendingComments.Clear();
            }

            Top.Elements.Add(new Message
            {
                FromId = from,
                ToId = to,
                Text = text,
                Type = type,
                Activate = activate,
                Deactivate = deactivate
            });
        }

        /// <summary>Variables that hold a created participant, which calls on the variable go to.</summary>
        Dictionary<string, string> instances = [];

        string Resolve(string id) => instances.GetValueOrDefault(id, id);

        void Call(CallLine call)
        {
            var from = call.From is { } explicitFrom ? Resolve(explicitFrom) : Current;
            var target = Resolve(call.Target);

            // A created participant answers nothing: what is kept is the participant itself, which is shown
            // as `variable:Type` and is what later calls on the variable go to.
            var assignee = call.Assignee;
            if (call.IsCreation)
            {
                var created = Participant(target);
                created.IsCreated = true;
                if (assignee is not null)
                {
                    created.Alias ??= $"{assignee}:{created.DisplayName}";
                    instances[assignee] = target;
                    assignee = null;
                }
            }

            if (!call.Opens || call.Closes)
            {
                // A call with no block (or an empty one) is answered straight away if its result is kept.
                Message(from, target, call.Text, MessageType.SolidArrow);
                if (assignee is not null)
                {
                    Message(target, from, assignee, MessageType.DottedArrow);
                }

                return;
            }

            Message(from, target, call.Text, MessageType.SolidArrow, activate: true);
            frames.Push(new(Top.Elements, target, from)
            {
                Call = call with
                {
                    Assignee = assignee
                }
            });
        }

        /// <summary>
        /// A return answers the call whose block it is in. Inside a fragment it still answers that call,
        /// but the call's activation runs on until its block closes.
        /// </summary>
        void Return(ReturnLine returnLine)
        {
            var callFrame = frames.FirstOrDefault(static _ => _.Call is not null);
            var to = callFrame?.Caller ?? starter;
            var direct = callFrame is not null && ReferenceEquals(callFrame, Top);
            Message(Current, to, returnLine.Text, MessageType.DottedArrow, deactivate: direct);
            if (direct)
            {
                Top.Returned = true;
            }
            else
            {
                callFrame?.Answered = true;
            }
        }

        void Open(OpenLine openLine)
        {
            var condition = openLine.Condition is { Length: > 0 } text ? text : null;
            List<SequenceElement> body;
            SequenceElement fragment;
            switch (openLine.Keyword)
            {
                case "if":
                    var alt = new Alt
                    {
                        Condition = condition
                    };
                    (fragment, body) = (alt, alt.Elements);
                    break;
                case "opt":
                    var opt = new Opt
                    {
                        Condition = condition
                    };
                    (fragment, body) = (opt, opt.Elements);
                    break;
                case "par":
                    var par = new Par
                    {
                        Label = condition
                    };
                    (fragment, body) = (par, par.Elements);
                    break;
                case "try":
                    var tryBlock = new Critical
                    {
                        Label = "try"
                    };
                    (fragment, body) = (tryBlock, tryBlock.Elements);
                    break;
                case "critical":
                    var critical = new Critical
                    {
                        Label = condition
                    };
                    (fragment, body) = (critical, critical.Elements);
                    break;
                default:
                    var loop = new Loop
                    {
                        Label = condition ?? (openLine.Keyword == "loop" ? null : openLine.Keyword)
                    };
                    (fragment, body) = (loop, loop.Elements);
                    break;
            }

            pendingComments.Clear();
            Top.Elements.Add(fragment);
            frames.Push(new(body, Top.Current, Top.Caller)
            {
                Fragment = fragment
            });
        }

        void Close(CloseLine closeLine)
        {
            // A stray closing brace has nothing to close.
            if (frames.Count == 1)
            {
                return;
            }

            var frame = frames.Pop();
            if (frame.Call is { } call &&
                !frame.Returned)
            {
                // The block ends the call's activation, answering it if its result is kept and no return
                // inside a fragment has answered it already.
                if (call.Assignee is { } assignee &&
                    !frame.Answered)
                {
                    Message(frame.Current, frame.Caller!, assignee, MessageType.DottedArrow, deactivate: true);
                }
                else
                {
                    Top.Elements.Add(new Activation
                    {
                        ParticipantId = frame.Current,
                        IsActivate = false
                    });
                }
            }

            if (closeLine.Continuation is null)
            {
                return;
            }

            // A call's block has no branches: `} else {` after one is as wrong as after an `opt`. Dropping
            // it would leave its `{` to be closed by the `}` meant for the block around it.
            var condition = closeLine.Condition is { Length: > 0 } text ? text : null;
            var body = (frame.Fragment, closeLine.Continuation) switch
            {
                (Alt alt, "else" or "else if") => AddBranch(alt.ElseBranches, new AltElse
                {
                    Condition = condition
                }).Elements,
                (Critical critical, "catch") => AddBranch(critical.OptionBranches, new CriticalOption
                {
                    Label = condition is null ? "catch" : $"catch({condition})"
                }).Elements,
                (Critical critical, "finally") => AddBranch(critical.OptionBranches, new CriticalOption
                {
                    Label = "finally"
                }).Elements,
                _ => (List<SequenceElement>?) null
            };

            if (body is null)
            {
                throw new MermaidParseException(
                    $"Failed to parse ZenUML diagram: '{closeLine.Continuation}' does not continue the block it follows");
            }

            frames.Push(new(body, frame.Current, frame.Caller)
            {
                Fragment = frame.Fragment
            });
        }

        static T AddBranch<T>(List<T> branches, T branch)
        {
            branches.Add(branch);
            return branch;
        }
    }

    /// <summary>
    /// <c>(</c>, then everything up to the <c>)</c> that matches it on the same line, which is left out along
    /// with the opening one and the space inside them. Parentheses nest, and a quoted string may hold
    /// either kind, with a backslash escaping its quote. A quote that is never closed on the line is just a
    /// character, as the apostrophe in <c>if(it's raining)</c> is.
    /// </summary>
    sealed class ParenthesisedParser : Parser<string>
    {
        public override bool Parse(ParseContext context, ref ParseResult<string> result)
        {
            var cursor = context.Scanner.Cursor;
            var span = cursor.Span;
            if (span.IsEmpty ||
                span[0] != '(')
            {
                return false;
            }

            var close = Close(span, quotes: true);
            if (close < 0)
            {
                close = Close(span, quotes: false);
                if (close < 0)
                {
                    return false;
                }
            }

            var start = cursor.Offset;
            var inner = span[1..close].Trim().ToString();
            cursor.Advance(close + 1);
            result.Set(start, start + close + 1, inner);
            return true;
        }

        // Where the parenthesis that opens the text closes, or -1 if it does not on this line.
        static int Close(ReadOnlySpan<char> span, bool quotes)
        {
            var depth = 0;
            char? quote = null;
            for (var index = 0; index < span.Length; index++)
            {
                var c = span[index];
                if (c is '\r' or '\n')
                {
                    return -1;
                }

                if (quote is not null)
                {
                    if (c == '\\')
                    {
                        index++;
                    }
                    else if (c == quote)
                    {
                        quote = null;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"' or '\'' when quotes:
                        quote = c;
                        break;
                    case '(':
                        depth++;
                        break;
                    case ')' when --depth == 0:
                        return index;
                }
            }

            return -1;
        }
    }
}
