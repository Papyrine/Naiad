using NotePosition = Naiad.Diagrams.State.NotePosition;

class StateParser : IDiagramParser<StateModel>
{
    static Parser<StateModel> parser;

    static StateParser()
    {
        // The rest of the line as it is read, and as the string kept from it.
        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine = restOfLineText.AsString();

        // Trailing spaces are allowed before every line end.
        var endOfLine = CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.LineEnd);

        // State identifier (alphanumeric, underscore, or [*] for start/end)
        var bareIdentifier =
            Literals.Text("[*]").Or(
                Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_')
                    .AsString()
            );

        // `Moving:::fast` attaches a style class to a state wherever the state is named. The class is read
        // and dropped: styling is not drawn, but the state it sits on still is.
        var styleClass =
            Literals.Text(":::")
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '-'));

        var stateIdentifier = bareIdentifier.AndSkip(styleClass.Optional());

        // State type annotations: <<fork>> or [[fork]]
        var stateTypeName =
            OneOf(
                Literals.Text("fork").Then(StateType.Fork),
                Literals.Text("join").Then(StateType.Join),
                Literals.Text("choice").Then(StateType.Choice));

        var stateTypeAnnotation =
            Literals.Text("<<").SkipAnd(stateTypeName).AndSkip(Literals.Text(">>"))
                .Or(Literals.Text("[[").SkipAnd(stateTypeName).AndSkip(Literals.Text("]]")));

        // Transition arrow
        var transitionArrow =
            Literals.Text("-->");

        // The `"Description" as StateName` part of a declaration.
        var describedIdentifier =
            CommonParsers.DoubleQuotedString
                .AndSkip(CommonParsers.RequiredWhitespace)
                .AndSkip(Literals.Text("as"))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(stateIdentifier)
                .Map(static (description, id) => (id, description: (string?)description));

        var plainIdentifier = stateIdentifier.Then(static _ => (id: _, description: (string?)null));

        // State declaration: state "Description" as StateName
        var stateDeclarationWithAlias =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("state"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(describedIdentifier)
                .AndSkip(endOfLine)
                .Map(static (id, description) =>
                    new State
                    {
                        Id = id,
                        Description = description
                    });

        // State declaration with type: state StateName <<fork>>
        var stateDeclarationWithType =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("state"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(stateIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(stateTypeAnnotation)
                .AndSkip(endOfLine)
                .Map(static (id, stateType) =>
                    new State
                    {
                        Id = id,
                        Type = stateType
                    });

        // Simple state declaration: state StateName
        var simpleStateDeclaration =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("state"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(stateIdentifier)
                .AndSkip(endOfLine)
                .Then(static _ => new State { Id = _ });

        // A state named on a line of its own for the sake of its style class: StateName:::fast
        var styledStateDeclaration =
            CommonParsers.InlineWhitespace
                .SkipAnd(bareIdentifier)
                .AndSkip(styleClass)
                .AndSkip(endOfLine)
                .Then(static _ => new State { Id = _ });

        // State with description on same line: StateName : Description
        var stateWithDescription =
            CommonParsers.InlineWhitespace
                .SkipAnd(stateIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .And(restOfLineText.AsTrimmedString())
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, description) =>
                    new State
                    {
                        Id = id,
                        Description = description
                    });

        // Transition: StateA --> StateB : label
        var transitionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(stateIdentifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(transitionArrow)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(stateIdentifier)
                .And(
                    CommonParsers.InlineWhitespace
                        .SkipAnd(Literals.Char(':'))
                        .SkipAnd(restOfLine)
                        .Optional())
                .AndSkip(endOfLine)
                .Map(static (fromId, toId, label) =>
                    new StateTransition
                    {
                        FromId = fromId,
                        ToId = toId,
                        Label = label.HasValue && !string.IsNullOrWhiteSpace(label.Value) ? label.Value.Trim() : null
                    });

        var notePosition =
            OneOf(
                Literals.Text("right of").Then(NotePosition.RightOf),
                Literals.Text("left of").Then(NotePosition.LeftOf));

        var noteHeader =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("note"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(notePosition)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(stateIdentifier);

        // Note: note right of State : Text
        var singleLineNote =
            noteHeader
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (position, stateId, text) =>
                    new StateNote
                    {
                        StateId = stateId,
                        Text = NoteText([text]),
                        Position = position
                    });

        var endNote =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("end note"))
                .SkipAnd(endOfLine);

        // Note written as a block: every line up to `end note` is a line of the note.
        var multiLineNote =
            noteHeader
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.Newline)
                .And(Not(endNote).SkipAnd(restOfLine).AndSkip(CommonParsers.Newline).Many())
                .AndSkip(endNote)
                .Map(static (position, stateId, lines) =>
                    new StateNote
                    {
                        StateId = stateId,
                        Text = NoteText(lines),
                        Position = position
                    });

        var noteParser = singleLineNote.Or(multiLineNote);

        var directionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("direction"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(CommonParsers.DirectionParser)
                .AndSkip(endOfLine);

        // Lines that are valid Mermaid but change nothing in what is drawn here: styling (`classDef`,
        // `class`, `style`), interaction (`click`), the accessibility title and description, and the
        // `hide empty description` / `scale 350 width` settings. Naming them keeps them from failing the
        // parse, and keeps `accTitle: x` from being read as a state called accTitle.
        var ignoredKeyword =
            OneOf(
                Literals.Text("classDef"),
                Literals.Text("class"),
                Literals.Text("style"),
                Literals.Text("click"),
                Literals.Text("accTitle"),
                Literals.Text("accDescr"),
                Literals.Text("hide empty description"),
                Literals.Text("scale"));

        var keywordBoundary =
            Literals.AnyOf(" \t:\r\n", minSize: 1, maxSize: 1).Or(CommonParsers.End);

        var ignoredDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(ignoredKeyword.WhenFollowedBy(keywordBoundary))
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd);

        // `accDescr { ... }` may run over several lines.
        var multiLineDescription =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accDescr"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char('{'))
                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                .SkipAnd(Literals.Char('}'))
                .SkipAnd(endOfLine);

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Composite state start: state StateName {  or  state "Description" as StateName {
        var compositeStateStart =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("state"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(describedIdentifier.Or(plainIdentifier))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(endOfLine)
                .Map(static (id, description) => new CompositeStartItem(id, description));

        // Composite state end: }
        var compositeStateEnd =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char('}'))
                .SkipAnd(endOfLine);

        // `--` on a line of its own splits a composite into regions that run concurrently.
        var divider =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("--"))
                .SkipAnd(endOfLine);

        var parseContentRecursive =
            OneOf(
                directionParser.Then<IStateContent?>(static _ => new DirectionItem(_)),
                noteParser.Then<IStateContent?>(static _ => new NoteItem(_)),
                multiLineDescription.Then<IStateContent?>(static _ => null),
                ignoredDirective.Then<IStateContent?>(static _ => null),
                stateDeclarationWithAlias.Then<IStateContent?>(static _ => new StateItem(_)),
                stateDeclarationWithType.Then<IStateContent?>(static _ => new StateItem(_)),
                compositeStateStart.Then<IStateContent?>(static _ => _),
                compositeStateEnd.Then<IStateContent?>(new CompositeEndItem()),
                divider.Then<IStateContent?>(new DividerItem()),
                transitionParser.Then<IStateContent?>(static _ => new TransitionItem(_)),
                styledStateDeclaration.Then<IStateContent?>(static _ => new StateItem(_)),
                stateWithDescription.Then<IStateContent?>(static _ => new StateItem(_)),
                simpleStateDeclaration.Then<IStateContent?>(static _ => new StateItem(_)),
                skipLine.Then<IStateContent?>(static _ => null)
            ).Many();

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("stateDiagram-v2").Or(Literals.Text("stateDiagram")))
                .SkipAnd(endOfLine)
                .SkipAnd(parseContentRecursive)
                // Nothing may be left over: without this a line no rule matches ends the list quietly and the
                // rest of the diagram is dropped instead of being reported.
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    // A note's lines as the one `<br/>`-separated label the renderer draws. A literal `\n` breaks a line
    // too, as it does in Mermaid.
    static string NoteText(IEnumerable<string> lines) =>
        string.Join(
            "<br/>",
            lines
                .SelectMany(_ => _.Split("\\n"))
                .Select(_ => _.Trim())
                .Where(_ => _.Length > 0));

    static StateModel BuildModel(IEnumerable<IStateContent?> content)
    {
        var model = new StateModel();
        var stateMap = new Dictionary<string, State>();
        var compositeStack = new Stack<State>();

        foreach (var item in content)
        {
            switch (item)
            {
                case DirectionItem dir:
                    // Inside a composite the direction is that composite's own, not the diagram's.
                    if (compositeStack.TryPeek(out var directed))
                    {
                        directed.Direction = dir.Value;
                    }
                    else
                    {
                        model.Direction = dir.Value;
                    }

                    break;

                case StateItem stateItem:
                    var value = stateItem.Value;
                    if (stateMap.TryGetValue(value.Id, out var existing))
                    {
                        // Update existing state with description/type
                        if (!string.IsNullOrEmpty(value.Description))
                        {
                            existing.Description = value.Description;
                        }
                        if (value.Type != StateType.Normal)
                        {
                            existing.Type = value.Type;
                        }
                    }
                    else
                    {
                        stateMap[value.Id] = value;
                        if (compositeStack.TryPeek(out var parent))
                        {
                            parent.NestedStates.Add(value);
                        }
                        else
                        {
                            model.States.Add(value);
                        }
                    }

                    break;

                case TransitionItem transitionItem:
                    var t = transitionItem.Value;
                    // Handle [*] - create separate start and end states
                    var fromId = t.FromId;
                    var toId = t.ToId;

                    // `[*]` means "the start/end of the enclosing region", so the id has to name that region
                    // too. Keying every marker as plain "[*]_start" made a composite's own initial state and
                    // the diagram's share one entry: the composite's transition then ran from the outer
                    // marker, across the composite's border, to a state inside it.
                    var scope = compositeStack.TryPeek(out var scopeState) ? $"{scopeState.Id}." : "";

                    if (fromId == "[*]")
                    {
                        fromId = $"{scope}[*]_start";
                        EnsureSpecialState(fromId, StateType.Start, stateMap, model, compositeStack);
                    }
                    else
                    {
                        EnsureState(fromId, stateMap, model, compositeStack);
                    }

                    if (toId == "[*]")
                    {
                        toId = $"{scope}[*]_end";
                        EnsureSpecialState(toId, StateType.End, stateMap, model, compositeStack);
                    }
                    else
                    {
                        EnsureState(toId, stateMap, model, compositeStack);
                    }

                    var transition = new StateTransition
                    {
                        FromId = fromId,
                        ToId = toId,
                        Label = t.Label
                    };
                    if (compositeStack.TryPeek(out var transitionParent))
                    {
                        transitionParent.NestedTransitions.Add(transition);
                    }
                    else
                    {
                        model.Transitions.Add(transition);
                    }
                    break;

                case NoteItem note:
                    model.Notes.Add(note.Value);
                    break;

                case CompositeStartItem cs:
                    // A composite block often names a state a transition has already introduced, as in
                    // `[*] --> Outer` above `state Outer { … }`. Reuse that entry the way every other
                    // branch here does: creating a second one left the id in model.States twice, and the
                    // renderer drew it twice - once as a plain state, once as a container.
                    if (!stateMap.TryGetValue(cs.Id, out var compositeState))
                    {
                        compositeState = new()
                        {
                            Id = cs.Id
                        };
                        stateMap[cs.Id] = compositeState;

                        if (compositeStack.TryPeek(out var compositeParent))
                        {
                            compositeParent.NestedStates.Add(compositeState);
                        }
                        else
                        {
                            model.States.Add(compositeState);
                        }
                    }

                    if (cs.Description != null)
                    {
                        compositeState.Description = cs.Description;
                    }

                    compositeStack.Push(compositeState);
                    break;

                case CompositeEndItem:
                    CloseRegion(compositeStack);
                    if (compositeStack.Count > 0)
                    {
                        compositeStack.Pop();
                    }
                    break;

                case DividerItem:
                    // Only a composite can be divided; at the top level there is nothing to split.
                    if (compositeStack.Count == 0)
                    {
                        break;
                    }

                    if (compositeStack.Peek().Type != StateType.Region)
                    {
                        // The first divider: what the composite holds so far becomes its first region.
                        var owner = compositeStack.Peek();
                        var first = NewRegion(owner);
                        first.NestedStates.AddRange(owner.NestedStates);
                        first.NestedTransitions.AddRange(owner.NestedTransitions);
                        owner.NestedStates.Clear();
                        owner.NestedTransitions.Clear();
                        if (first.NestedStates.Count > 0)
                        {
                            owner.NestedStates.Add(first);
                        }
                    }
                    else
                    {
                        CloseRegion(compositeStack);
                    }

                    var region = NewRegion(compositeStack.Peek());
                    compositeStack.Peek().NestedStates.Add(region);
                    compositeStack.Push(region);
                    break;
            }
        }

        return model;
    }

    static State NewRegion(State owner) =>
        new()
        {
            Id = $"{owner.Id}.region{owner.NestedStates.Count + 1}",
            Type = StateType.Region,
            Direction = owner.Direction
        };

    // Leaves the region the stack is in, if it is in one, dropping a region that ended up with nothing in it.
    static void CloseRegion(Stack<State> compositeStack)
    {
        if (!compositeStack.TryPeek(out var region) ||
            region.Type != StateType.Region)
        {
            return;
        }

        compositeStack.Pop();
        if (region.NestedStates.Count == 0)
        {
            compositeStack.Peek().NestedStates.Remove(region);
        }
    }

    static void EnsureState(string id, Dictionary<string, State> stateMap, StateModel model, Stack<State> compositeStack)
    {
        if (stateMap.ContainsKey(id))
        {
            return;
        }

        var stateType = id == "[*]"
            ? compositeStack.Count == 0 ? StateType.Start : StateType.Normal
            : StateType.Normal;

        var state = new State
        {
            Id = id,
            Type = stateType
        };
        stateMap[id] = state;

        if (compositeStack.TryPeek(out var parent))
        {
            parent.NestedStates.Add(state);
        }
        else
        {
            model.States.Add(state);
        }
    }

    static void EnsureSpecialState(string id, StateType type, Dictionary<string, State> stateMap, StateModel model, Stack<State> compositeStack)
    {
        if (stateMap.ContainsKey(id))
        {
            return;
        }

        var state = new State
        {
            Id = id,
            Type = type
        };
        stateMap[id] = state;

        if (compositeStack.TryPeek(out var parent))
        {
            parent.NestedStates.Add(state);
        }
        else
        {
            model.States.Add(state);
        }
    }

    public DiagramParseResult<StateModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IStateContent;
    readonly record struct DirectionItem(Direction Value) : IStateContent;
    readonly record struct StateItem(State Value) : IStateContent;
    readonly record struct TransitionItem(StateTransition Value) : IStateContent;
    readonly record struct NoteItem(StateNote Value) : IStateContent;
    readonly record struct CompositeStartItem(string Id, string? Description) : IStateContent;
    readonly record struct DividerItem : IStateContent;
    readonly record struct CompositeEndItem : IStateContent;
}
