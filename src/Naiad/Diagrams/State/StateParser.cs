using NotePosition = Naiad.Diagrams.State.NotePosition;

class StateParser : IDiagramParser<StateModel>
{
    static Parser<char, StateModel> parser;

    static StateParser()
    {
        var restOfLine = Token(_ => _ != '\r' && _ != '\n').ManyString();

        // Trailing spaces are allowed before every line end.
        var endOfLine = CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd);

        // State identifier (alphanumeric, underscore, or [*] for start/end)
        var bareIdentifier =
            Try(String("[*]")).Or(
                Token(_ => char.IsLetterOrDigit(_) || _ == '_')
                    .AtLeastOnceString()
            ).Labelled("state identifier");

        // `Moving:::fast` attaches a style class to a state wherever the state is named. The class is read
        // and dropped: styling is not drawn, but the state it sits on still is.
        var styleClass =
            String(":::")
                .Then(Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '-').SkipAtLeastOnce());

        var stateIdentifier = bareIdentifier.Before(Try(styleClass).Optional());

        // State type annotations: <<fork>> or [[fork]]
        var stateTypeName =
            OneOf(
                Try(String("fork")).ThenReturn(StateType.Fork),
                Try(String("join")).ThenReturn(StateType.Join),
                String("choice").ThenReturn(StateType.Choice));

        var stateTypeAnnotation =
            String("<<").Then(stateTypeName).Before(String(">>"))
                .Or(String("[[").Then(stateTypeName).Before(String("]]")));

        // Transition arrow
        var transitionArrow =
            String("-->").ThenReturn(Unit.Value);

        // The `"Description" as StateName` part of a declaration.
        var describedIdentifier =
            from description in CommonParsers.DoubleQuotedString
            from _ in CommonParsers.RequiredWhitespace
            from asKeyword in String("as")
            from __ in CommonParsers.RequiredWhitespace
            from id in stateIdentifier
            select (id, description: (string?)description);

        var plainIdentifier = stateIdentifier.Select(_ => (id: _, description: (string?)null));

        // State declaration: state "Description" as StateName
        var stateDeclarationWithAlias =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("state")
            from __ in CommonParsers.RequiredWhitespace
            from named in describedIdentifier
            from ___ in endOfLine
            select new State
            {
                Id = named.id,
                Description = named.description
            };

        // State declaration with type: state StateName <<fork>>
        var stateDeclarationWithType =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("state")
            from __ in CommonParsers.RequiredWhitespace
            from id in stateIdentifier
            from ___ in CommonParsers.InlineWhitespace
            from stateType in stateTypeAnnotation
            from ____ in endOfLine
            select new State
            {
                Id = id,
                Type = stateType
            };

        // Simple state declaration: state StateName
        var simpleStateDeclaration =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("state")
            from __ in CommonParsers.RequiredWhitespace
            from id in stateIdentifier
            from ___ in endOfLine
            select new State { Id = id };

        // A state named on a line of its own for the sake of its style class: StateName:::fast
        var styledStateDeclaration =
            from _ in CommonParsers.InlineWhitespace
            from id in bareIdentifier
            from style in styleClass
            from __ in endOfLine
            select new State { Id = id };

        // State with description on same line: StateName : Description
        var stateWithDescription =
            from _ in CommonParsers.InlineWhitespace
            from id in stateIdentifier
            from __ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from description in restOfLine
            from ___ in CommonParsers.LineEnd
            select new State
            {
                Id = id,
                Description = description.Trim()
            };

        // Transition: StateA --> StateB : label
        var transitionParser =
            from _ in CommonParsers.InlineWhitespace
            from fromId in stateIdentifier
            from __ in CommonParsers.InlineWhitespace
            from arrow in transitionArrow
            from ___ in CommonParsers.InlineWhitespace
            from toId in stateIdentifier
            from label in Try(
                CommonParsers.InlineWhitespace
                    .Then(Char(':'))
                    .Then(restOfLine)
            ).Optional()
            from ____ in endOfLine
            select new StateTransition
            {
                FromId = fromId,
                ToId = toId,
                Label = label.HasValue && !string.IsNullOrWhiteSpace(label.Value) ? label.Value.Trim() : null
            };

        var notePosition =
            OneOf(
                Try(String("right of")).ThenReturn(NotePosition.RightOf),
                String("left of").ThenReturn(NotePosition.LeftOf));

        var noteHeader =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("note")
            from __ in CommonParsers.RequiredWhitespace
            from position in notePosition
            from ___ in CommonParsers.RequiredWhitespace
            from stateId in stateIdentifier
            select (position, stateId);

        // Note: note right of State : Text
        var singleLineNote =
            from header in noteHeader
            from _ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from text in restOfLine
            from __ in CommonParsers.LineEnd
            select new StateNote
            {
                StateId = header.stateId,
                Text = NoteText([text]),
                Position = header.position
            };

        var endNote =
            CommonParsers.InlineWhitespace
                .Then(String("end note"))
                .Then(endOfLine);

        // Note written as a block: every line up to `end note` is a line of the note.
        var multiLineNote =
            from header in noteHeader
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.Newline
            from lines in Try(Not(Try(endNote)).Then(restOfLine).Before(CommonParsers.Newline)).Many()
            from ___ in endNote
            select new StateNote
            {
                StateId = header.stateId,
                Text = NoteText(lines),
                Position = header.position
            };

        var noteParser = Try(singleLineNote).Or(multiLineNote);

        var directionParser =
            CommonParsers.InlineWhitespace
                .Then(String("direction"))
                .Then(CommonParsers.RequiredWhitespace)
                .Then(CommonParsers.DirectionParser)
                .Before(endOfLine);

        // Lines that are valid Mermaid but change nothing in what is drawn here: styling (`classDef`,
        // `class`, `style`), interaction (`click`), the accessibility title and description, and the
        // `hide empty description` / `scale 350 width` settings. Naming them keeps them from failing the
        // parse, and keeps `accTitle: x` from being read as a state called accTitle.
        var ignoredKeyword =
            OneOf(
                Try(String("classDef")),
                Try(String("class")),
                Try(String("style")),
                Try(String("click")),
                Try(String("accTitle")),
                Try(String("accDescr")),
                Try(String("hide empty description")),
                String("scale"));

        var ignoredDirective =
            from _ in CommonParsers.InlineWhitespace
            from keyword in ignoredKeyword
            from boundary in Lookahead(Token(_ => _ is ' ' or '\t' or ':' or '\r' or '\n').IgnoreResult().Or(End))
            from rest in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select Unit.Value;

        // `accDescr { ... }` may run over several lines.
        var multiLineDescription =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("accDescr")
            from __ in CommonParsers.InlineWhitespace
            from open in Char('{')
            from text in Token(_ => _ != '}').SkipMany()
            from close in Char('}')
            from ___ in endOfLine
            select Unit.Value;

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        // Composite state start: state StateName {  or  state "Description" as StateName {
        var compositeStateStart =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("state")
            from __ in CommonParsers.RequiredWhitespace
            from named in describedIdentifier.Or(plainIdentifier)
            from ___ in CommonParsers.InlineWhitespace
            from open in Char('{')
            from ____ in endOfLine
            select new CompositeStartItem(named.id, named.description);

        // Composite state end: }
        var compositeStateEnd =
            CommonParsers.InlineWhitespace
                .Then(Char('}'))
                .Then(endOfLine)
                .ThenReturn(Unit.Value);

        // `--` on a line of its own splits a composite into regions that run concurrently.
        var divider =
            CommonParsers.InlineWhitespace
                .Then(String("--"))
                .Then(endOfLine)
                .ThenReturn(Unit.Value);

        var parseContentRecursive =
            OneOf(
                Try(directionParser.Select<IStateContent?>(_ => new DirectionItem(_))),
                Try(noteParser.Select<IStateContent?>(_ => new NoteItem(_))),
                Try(multiLineDescription.ThenReturn<IStateContent?>(null)),
                Try(ignoredDirective.ThenReturn<IStateContent?>(null)),
                Try(stateDeclarationWithAlias.Select<IStateContent?>(_ => new StateItem(_))),
                Try(stateDeclarationWithType.Select<IStateContent?>(_ => new StateItem(_))),
                Try(compositeStateStart.Select<IStateContent?>(_ => _)),
                Try(compositeStateEnd.ThenReturn<IStateContent?>(new CompositeEndItem())),
                Try(divider.ThenReturn<IStateContent?>(new DividerItem())),
                Try(transitionParser.Select<IStateContent?>(_ => new TransitionItem(_))),
                Try(styledStateDeclaration.Select<IStateContent?>(_ => new StateItem(_))),
                Try(stateWithDescription.Select<IStateContent?>(_ => new StateItem(_))),
                Try(simpleStateDeclaration.Select<IStateContent?>(_ => new StateItem(_))),
                skipLine.ThenReturn<IStateContent?>(null)
            ).Many();

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in Try(String("stateDiagram-v2")).Or(String("stateDiagram"))
            from __ in endOfLine
            from content in parseContentRecursive
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(content);
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

    public Result<char, StateModel> Parse(string input) => parser.Parse(input);

    internal interface IStateContent;
    readonly record struct DirectionItem(Direction Value) : IStateContent;
    readonly record struct StateItem(State Value) : IStateContent;
    readonly record struct TransitionItem(StateTransition Value) : IStateContent;
    readonly record struct NoteItem(StateNote Value) : IStateContent;
    readonly record struct CompositeStartItem(string Id, string? Description) : IStateContent;
    readonly record struct DividerItem : IStateContent;
    readonly record struct CompositeEndItem : IStateContent;
}
