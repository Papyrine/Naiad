class ClassParser : IDiagramParser<ClassModel>
{
    static Parser<ClassModel> parser;

    // One member line, as written in a class body or after `ClassName :`.
    static Parser<IClassBodyContent> memberLine;

    static ClassParser()
    {
        // Spaces and tabs only. Whitespace that may span a newline would let a member's type bind to the
        // name on the *following* line, merging bodies such as an enumeration's one-value-per-line list.
        var inlineGap =
            Literals.AnyOf(" \t");

        // Letters, digits and underscores, plus a hyphen between two of them (`Order-Line`). A hyphen that
        // starts a relationship line is not part of the name.
        var idChars =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_');

        var identifier =
            Capture(idChars.And(ZeroOrMany(Literals.Char('-').And(idChars))))
                .AsString();

        // A name in backticks may hold anything, spaces and punctuation included.
        var backtickName =
            Between(Literals.Char('`'), Literals.NoneOf("`\r\n"), Literals.Char('`'))
                .AsString();

        // Mermaid spells generics ~T~. The tilde form is display only, so a class is keyed on its bare
        // name and `IRepository~T~` in a relationship resolves to the `class IRepository~T~` declaration.
        var genericArgument =
            Between(Literals.Char('~'), Literals.NoneOf("~\r\n"), Literals.Char('~'))
                .AsString();

        var className =
            backtickName.Then(static _ => new ClassName(_, null))
                .Or(
                    identifier
                        .And(genericArgument.Optional())
                        .Map(static (id, generic) =>
                            new ClassName(id, generic.HasValue ? $"{id}<{generic.Value}>" : null)));

        var visibilityParser =
            OneOf(
                Literals.Char('+').Then(Visibility.Public),
                Literals.Char('-').Then(Visibility.Private),
                Literals.Char('#').Then(Visibility.Protected),
                Literals.Char('~').Then(Visibility.PackagePrivate)
            );

        // A type name, including a ~T~ generic argument, which is normalised to angle brackets.
        var typeName =
            Literals.Pattern(
                    static _ => char.IsLetterOrDigit(_) || _ is '_' or '<' or '>' or '[' or ']' or ',' or '~')
                .AsString()
                .Then(NormalizeGenerics);

        // Type annotation like : String or : int
        var typeAnnotation =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(typeName);

        // Mermaid's classifier suffix: $ marks a static member, * an abstract one. It trails the
        // declaration, after the parentheses for a method (`+validate()$ bool`).
        var classifier = OneOf(Literals.Char('$'), Literals.Char('*'));

        // Method parameters like (String name, int age) or (id: int)
        var parametersParser =
            Between(Literals.Char('('), Literals.NoneOf(")\r\n", minSize: 0), Literals.Char(')'))
                .AsString()
                .Then(ParseParameters);

        // Member: +String name (type first), +name : String (type after colon) or a bare enumeration value.
        // The first word is the type when a name follows it, and the name otherwise.
        var memberParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(visibilityParser.Optional())
                .And(typeName)
                .And(
                    OneOf(
                        // Type first format: +String name
                        inlineGap
                            .SkipAnd(idChars)
                            .Then(static _ => (Name: (string?) _.ToString(), Type: (string?) null)),
                        // Name only or name : Type format
                        typeAnnotation.Optional()
                            .Then(static _ => (Name: (string?) null, Type: _.OrNull()))))
                .And(classifier.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (visibility, firstWord, rest, suffix) =>
                    new ClassMember
                    {
                        Name = rest.Name ?? firstWord,
                        Type = rest.Name is null ? rest.Type : firstWord,
                        Visibility = visibility.OrSome(Visibility.Public),
                        IsStatic = suffix is {HasValue: true, Value: '$'}
                    });

        // Method: +makeSound(), +move(int distance) : void, +getId() int or +validate()$ bool
        var methodParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(visibilityParser.Optional())
                .And(idChars.AsString())
                .And(parametersParser)
                .And(classifier.Optional())
                .And(
                    OneOf(
                        // `: void` and the bare `void` of `+getId() int` are both Mermaid return types.
                        typeAnnotation,
                        inlineGap.SkipAnd(typeName)
                    ).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (visibility, name, parameters, suffix, returnType) =>
                    CreateMethod(
                        name,
                        parameters,
                        returnType.OrNull(),
                        visibility.OrSome(Visibility.Public),
                        suffix));

        // Class annotation: <<interface>>, <<abstract>>, or any other text, such as <<Entity>>.
        var annotationText =
            Between(Literals.Text("<<"), Literals.NoneOf(">\r\n"), Literals.Text(">>"))
                .AsString();

        var annotationParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(annotationText)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd);

        // One line of a class body, in the spellings that are understood structurally. A line that fits
        // none of them is still a member (see ParseLooseMember), so nothing in a body can fail the parse.
        memberLine =
            OneOf(
                annotationParser.Then<IClassBodyContent>(static _ => new AnnotationItem(_)),
                methodParser.Then<IClassBodyContent>(static _ => new MethodItem(_)),
                memberParser.Then<IClassBodyContent>(static _ => new MemberItem(_)));

        // Class body: { ... }. As in Mermaid everything up to the closing brace belongs to the body, one
        // member per line, so the braces may share a line with the members (`class A { +x }`) and may be
        // followed by trailing whitespace.
        var classBody =
            Between(Literals.Char('{'), Literals.NoneOf("}", minSize: 0), Literals.Char('}'))
                .AsString()
                .Then(static _ => ParseBody(_, memberLine));

        // Display label: class Animal["Animal with a label"]. Unquoted text does not start with a quote, so
        // a label that opens one is a quoted string or it is not a label.
        var classLabel =
            Between(
                Literals.Char('['),
                CommonParsers.DoubleQuotedString
                    .Or(Not(Literals.Char('"')).SkipAnd(Literals.NoneOf("]\r\n", minSize: 0).AsString())),
                Literals.Char(']'));

        // Class definition: class ClassName, class ClassName { ... }, class ClassName["Label"] or
        // class ClassName:::cssClass. Styling is not applied to class diagrams, so the css class is read
        // and dropped.
        var classDefinitionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("class"))
                .SkipAnd(inlineGap)
                .SkipAnd(className)
                .And(CommonParsers.InlineWhitespace.SkipAnd(classLabel).Optional())
                .AndSkip(Literals.Text(":::").SkipAnd(CommonParsers.Identifier).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(classBody.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(CreateClassDefinition);

        // A marker token on each side of the line, so `<|--`, `--|>`, `*--`, `--o` and two-sided forms
        // such as `<|--|>` each keep their glyph on the end the author wrote it on.
        var fromMarker =
            OneOf(
                Literals.Text("<|").Then(RelationshipMarker.Triangle),
                Literals.Text("()").Then(RelationshipMarker.Lollipop),
                Literals.Text("*").Then(RelationshipMarker.FilledDiamond),
                Literals.Text("o").Then(RelationshipMarker.HollowDiamond),
                Literals.Text("<").Then(RelationshipMarker.Arrow)
            );

        var toMarker =
            OneOf(
                Literals.Text("|>").Then(RelationshipMarker.Triangle),
                Literals.Text("()").Then(RelationshipMarker.Lollipop),
                Literals.Text("*").Then(RelationshipMarker.FilledDiamond),
                Literals.Text("o").Then(RelationshipMarker.HollowDiamond),
                Literals.Text(">").Then(RelationshipMarker.Arrow)
            );

        var relationshipLine =
            OneOf(
                Literals.Text("--").Then(false),
                Literals.Text("..").Then(true)
            );

        var relationshipArrowParser =
            fromMarker.Optional()
                .And(relationshipLine)
                .And(toMarker.Optional())
                .Map(static (start, dashed, end) =>
                    new RelationshipArrow(
                        start.OrSome(RelationshipMarker.None),
                        end.OrSome(RelationshipMarker.None),
                        dashed));

        // Cardinality like "1", "0..1", "1..*", "*"
        var cardinalityParser =
            Between(Literals.Char('"'), Literals.NoneOf("\""), Literals.Char('"'))
                .AsString();

        // Relationship: ClassA "1" <|-- "*" ClassB : label
        var relationshipParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(className)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(cardinalityParser.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(relationshipArrowParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(cardinalityParser.AndSkip(inlineGap).Optional())
                .And(className)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(
                    Literals.Char(':')
                        .SkipAnd(CommonParsers.InlineWhitespace)
                        .SkipAnd(Literals.NoneOf("\r\n", minSize: 0))
                        .AsTrimmedString()
                        .Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (fromId, fromCardinality, arrow, toCardinality, toId, label) =>
                    new RelationshipItem(
                        new()
                        {
                            FromId = fromId.Id,
                            ToId = toId.Id,
                            Type = Classify(arrow),
                            FromMarker = arrow.From,
                            ToMarker = arrow.To,
                            IsDashed = arrow.Dashed,
                            Label = label.OrNull(),
                            FromCardinality = fromCardinality.OrNull(),
                            ToCardinality = toCardinality.OrNull()
                        },
                        fromId,
                        toId));

        // Direction directive
        var directionDirectiveParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("direction"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(CommonParsers.DirectionParser)
                .AndSkip(CommonParsers.LineEnd);

        // The rest of the line as it is read, and as the string kept from it.
        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine =
            restOfLineText.AsString();

        // Member added from outside the class body: `Animal : +int age`, `Animal: +isMammal()`
        var memberStatementParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(className)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (owner, text) => new MemberStatementItem(owner, text));

        // Annotation given on its own line: `<<interface>> Shape`
        var annotationStatementParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(annotationText)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(className)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (text, owner) => new AnnotationStatementItem(owner, text));

        // note "text" or note for ClassName "text"
        var noteParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("note"))
                .SkipAnd(inlineGap)
                .SkipAnd(Literals.Text("for").SkipAnd(inlineGap).SkipAnd(className).AndSkip(inlineGap).Optional())
                .And(CommonParsers.DoubleQuotedString)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (owner, text) => new NoteItem(owner.ToNullable(), text));

        // namespace Name { ... } groups the classes declared inside it.
        var namespaceStartParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("namespace"))
                .SkipAnd(inlineGap)
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '.' or '-'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => new NamespaceStartItem(_.ToString()));

        var namespaceEndParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char('}'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .Then(static _ => new NamespaceEndItem());

        // Styling and interaction, neither of which a class diagram here acts on.
        var ignoredParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Text("classDef"),
                        Literals.Text("cssClass"),
                        Literals.Text("style"),
                        Literals.Text("click"),
                        Literals.Text("link"),
                        Literals.Text("callback")))
                .SkipAnd(inlineGap)
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd);

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                directionDirectiveParser.Then<IClassContent?>(static _ => new DirectionItem(_)),
                classDefinitionParser.Then<IClassContent?>(static _ => new ClassDefinitionItem(_)),
                relationshipParser.Then<IClassContent?>(static _ => _),
                noteParser.Then<IClassContent?>(static _ => _),
                namespaceStartParser.Then<IClassContent?>(static _ => _),
                namespaceEndParser.Then<IClassContent?>(static _ => _),
                annotationStatementParser.Then<IClassContent?>(static _ => _),
                ignoredParser.Then<IClassContent?>(static _ => null),
                memberStatementParser.Then<IClassContent?>(static _ => _),
                skipLine.Then<IClassContent?>(static _ => null)
            );

        // The statements must account for the whole input. Without the end-of-input check a line no rule
        // matches would just end the list, and everything after it would be dropped without an error.
        var endOfInput =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.End);

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("classDiagram"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(parseContent.Many())
                .AndSkip(endOfInput)
                .Then(BuildModel);
    }

    /// <summary>
    /// Rewrites Mermaid's tilde-delimited generics as angle brackets, so <c>List~Item~</c> displays as
    /// <c>List&lt;Item&gt;</c>. Tildes alternate open/close, matching how Mermaid pairs them.
    /// </summary>
    static string NormalizeGenerics(string text)
    {
        if (!text.Contains('~'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var open = true;
        foreach (var ch in text)
        {
            if (ch == '~')
            {
                builder.Append(open ? '<' : '>');
                open = !open;
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    static RelationshipType Classify(RelationshipArrow arrow)
    {
        if (arrow.From is RelationshipMarker.Triangle || arrow.To is RelationshipMarker.Triangle)
        {
            if (arrow.Dashed)
            {
                return RelationshipType.Realization;
            }

            return RelationshipType.Inheritance;
        }

        if (arrow.From is RelationshipMarker.FilledDiamond || arrow.To is RelationshipMarker.FilledDiamond)
        {
            return RelationshipType.Composition;
        }

        if (arrow.From is RelationshipMarker.HollowDiamond || arrow.To is RelationshipMarker.HollowDiamond)
        {
            return RelationshipType.Aggregation;
        }

        if (arrow.From is RelationshipMarker.Arrow)
        {
            if (arrow.Dashed)
            {
                return RelationshipType.DependencyLeft;
            }

            return RelationshipType.Association;
        }

        if (arrow.To is RelationshipMarker.Arrow)
        {
            if (arrow.Dashed)
            {
                return RelationshipType.DependencyRight;
            }

            return RelationshipType.Association;
        }

        return RelationshipType.Link;
    }

    static ClassMethod CreateMethod(
        string name,
        List<MethodParameter> parameters,
        string? returnType,
        Visibility visibility,
        Option<char> classifier)
    {
        var method = new ClassMethod
        {
            Name = name,
            ReturnType = returnType,
            Visibility = visibility,
            IsStatic = classifier is {HasValue: true, Value: '$'},
            IsAbstract = classifier is {HasValue: true, Value: '*'}
        };
        method.Parameters.AddRange(parameters);
        return method;
    }

    static List<MethodParameter> ParseParameters(string paramStr)
    {
        var parameters = new List<MethodParameter>();
        if (string.IsNullOrWhiteSpace(paramStr))
        {
            return parameters;
        }

        foreach (var param in SplitParameters(paramStr))
        {
            var text = param.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            // Both `int age` and Mermaid's `age: int` spelling reach here.
            var colon = text.IndexOf(':');
            if (colon >= 0)
            {
                parameters.Add(
                    new()
                    {
                        Name = NormalizeGenerics(text[..colon].Trim()),
                        Type = NormalizeGenerics(text[(colon + 1)..].Trim())
                    });
                continue;
            }

            // `int age`: the name is the last word and what precedes it the type, which may itself hold
            // spaces (`Map<string, int> index`).
            var space = text.LastIndexOf(' ');
            if (space < 0)
            {
                parameters.Add(
                    new()
                    {
                        Name = NormalizeGenerics(text)
                    });
                continue;
            }

            parameters.Add(
                new()
                {
                    Name = NormalizeGenerics(text[(space + 1)..]),
                    Type = NormalizeGenerics(text[..space].TrimEnd())
                });
        }

        return parameters;
    }

    static (string? annotation, List<ClassMember> members, List<ClassMethod> methods) ParseBody(
        string text,
        Parser<IClassBodyContent> bodyLine)
    {
        string? annotation = null;
        var members = new List<ClassMember>();
        var methods = new List<ClassMethod>();

        foreach (var range in text.AsSpan().Split('\n'))
        {
            var line = text.AsSpan(range).Trim();
            if (line.IsEmpty ||
                line.StartsWith("%%"))
            {
                continue;
            }

            var item = bodyLine.TryMatch(line.ToString(), out var parsed) ? parsed : ParseLooseMember(line);
            switch (item)
            {
                case AnnotationItem a:
                    annotation = a.Value;
                    break;
                case MemberItem m:
                    members.Add(m.Value);
                    break;
                case MethodItem m:
                    methods.Add(m.Value);
                    break;
            }
        }

        return (annotation, members, methods);
    }

    /// <summary>
    /// Reads a body line the structured parsers could not, the way Mermaid does: every line is a member,
    /// and one with parentheses is a method. Only the visibility prefix, the parameter list and the
    /// classifier suffix are picked apart; the rest is kept as written, so a default value
    /// (<c>+int count = 0</c>), a modifier (<c>+static create()</c>) or a type with spaces in it
    /// (<c>-Dictionary&lt;string, int&gt; map</c>) is shown rather than failing the diagram.
    /// </summary>
    static IClassBodyContent ParseLooseMember(CharSpan line)
    {
        var visibility = Visibility.Public;
        if (TryGetVisibility(line[0], out var marked))
        {
            visibility = marked;
            line = line[1..].TrimStart();
        }

        var open = line.IndexOf('(');
        var close = line.LastIndexOf(')');
        if (open < 0 ||
            close < open)
        {
            var isStatic = line.EndsWith("$");
            if (isStatic)
            {
                line = line[..^1].TrimEnd();
            }

            return new MemberItem(
                new()
                {
                    Name = NormalizeNestedGenerics(line),
                    Visibility = visibility,
                    IsStatic = isStatic
                });
        }

        var suffix = line[(close + 1)..].TrimStart();
        var classifier = suffix.IsEmpty ? '\0' : suffix[0];
        if (classifier is '$' or '*')
        {
            suffix = suffix[1..];
        }

        var returnType = suffix.TrimStart().TrimStart(':').Trim();
        var method = new ClassMethod
        {
            Name = NormalizeNestedGenerics(line[..open].TrimEnd()),
            ReturnType = returnType.IsEmpty ? null : NormalizeNestedGenerics(returnType),
            Visibility = visibility,
            IsStatic = classifier == '$',
            IsAbstract = classifier == '*'
        };
        method.Parameters.AddRange(ParseParameters(line[(open + 1)..close].ToString()));
        return new MethodItem(method);
    }

    static bool TryGetVisibility(char ch, out Visibility visibility)
    {
        switch (ch)
        {
            case '+':
                visibility = Visibility.Public;
                return true;
            case '-':
                visibility = Visibility.Private;
                return true;
            case '#':
                visibility = Visibility.Protected;
                return true;
            case '~':
                visibility = Visibility.PackagePrivate;
                return true;
            default:
                visibility = default;
                return false;
        }
    }

    /// <summary>
    /// Rewrites tilde generics that may nest, such as <c>List~Map~String, int~~</c>. A tilde opens an
    /// argument list when a name follows it and closes one otherwise, which is what tells the two
    /// adjacent closing tildes there apart from an opening one.
    /// </summary>
    static string NormalizeNestedGenerics(CharSpan text)
    {
        if (!text.Contains('~'))
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                builder.Append(text[i]);
                continue;
            }

            var opens = i + 1 < text.Length &&
                        (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_');
            builder.Append(opens ? '<' : '>');
        }

        return builder.ToString();
    }

    // Splits a parameter list on its top-level commas, leaving those inside <>, () and [] alone so a
    // parameter typed `Map<string, int>` stays in one piece.
    static List<string> SplitParameters(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<' or '(' or '[':
                    depth++;
                    break;
                // The `>` of a `=>` arrow closes nothing.
                case '>' when i > 0 && text[i - 1] == '=':
                    break;
                case '>' or ')' or ']' when depth > 0:
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    static ClassDefinition CreateClassDefinition(
        ClassName name,
        Option<string> label,
        Option<(string? annotation, List<ClassMember> members, List<ClassMethod> methods)> body)
    {
        var classDef = new ClassDefinition
        {
            Id = name.Id,
            DisplayName = label.HasValue ? label.Value : name.DisplayName
        };

        if (body.HasValue)
        {
            if (body.Value.annotation is { } annotation)
            {
                SetAnnotation(classDef, annotation);
            }

            classDef.Members.AddRange(body.Value.members);
            classDef.Methods.AddRange(body.Value.methods);
        }

        return classDef;
    }

    // The built-in annotations are kept as their enum value, which is what picks the box colour; anything
    // else is kept as written.
    static void SetAnnotation(ClassDefinition classDef, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0 &&
            char.IsLetter(trimmed[0]) &&
            Enum.TryParse<ClassAnnotation>(trimmed, true, out var known))
        {
            classDef.Annotation = known;
            classDef.AnnotationText = null;
            return;
        }

        classDef.Annotation = null;
        classDef.AnnotationText = trimmed;
    }

    static ClassModel BuildModel(IEnumerable<IClassContent?> content)
    {
        var model = new ClassModel();
        var classIds = new Dictionary<string, ClassDefinition>();
        string? currentNamespace = null;

        ClassDefinition Class(ClassName name)
        {
            AddPlaceholder(name);
            return classIds[name.Id];
        }

        foreach (var item in content)
        {
            switch (item)
            {
                case DirectionItem d:
                    model.Direction = d.Value;
                    break;

                case ClassDefinitionItem cdef:
                    var c = cdef.Value;
                    if (classIds.TryGetValue(c.Id, out var existing))
                    {
                        // A class referenced by an earlier relationship is a placeholder; the later
                        // declaration is what carries the members, so fill the placeholder in.
                        existing.DisplayName ??= c.DisplayName;
                        if (existing.Annotation is null &&
                            existing.AnnotationText is null)
                        {
                            existing.Annotation = c.Annotation;
                            existing.AnnotationText = c.AnnotationText;
                        }

                        existing.Namespace ??= currentNamespace;
                        existing.Members.AddRange(c.Members);
                        existing.Methods.AddRange(c.Methods);
                    }
                    else
                    {
                        c.Namespace = currentNamespace;
                        model.Classes.Add(c);
                        classIds.Add(c.Id, c);
                    }

                    break;

                case MemberStatementItem member:
                    var body = ParseBody(member.Text, memberLine);
                    var owner = Class(member.Owner);
                    if (body.annotation is { } memberAnnotation)
                    {
                        SetAnnotation(owner, memberAnnotation);
                    }

                    owner.Members.AddRange(body.members);
                    owner.Methods.AddRange(body.methods);
                    break;

                case AnnotationStatementItem annotation:
                    SetAnnotation(Class(annotation.Owner), annotation.Text);
                    break;

                case NoteItem note:
                    model.Notes.Add(
                        new()
                        {
                            // Mermaid writes a line break in a note as a literal backslash-n.
                            Text = note.Text.Replace("\\n", "<br/>"),
                            ForClassId = note.Owner is { } noteOwner ? Class(noteOwner).Id : null
                        });
                    break;

                case NamespaceStartItem start:
                    if (currentNamespace is not null)
                    {
                        throw new MermaidParseException(
                            "Failed to parse class diagram: a namespace cannot be declared inside another");
                    }

                    currentNamespace = start.Name;
                    break;

                case NamespaceEndItem:
                    if (currentNamespace is null)
                    {
                        throw new MermaidParseException(
                            "Failed to parse class diagram: '}' without a namespace to close");
                    }

                    currentNamespace = null;
                    break;

                case RelationshipItem rel:
                    // Auto-add classes from relationships
                    AddPlaceholder(rel.From);
                    AddPlaceholder(rel.To);
                    model.Relationships.Add(rel.Value);
                    break;
            }
        }

        if (currentNamespace is not null)
        {
            throw new MermaidParseException(
                $"Failed to parse class diagram: namespace '{currentNamespace}' is missing its closing '}}'");
        }

        return model;

        void AddPlaceholder(ClassName name)
        {
            if (classIds.ContainsKey(name.Id))
            {
                return;
            }

            var placeholder = new ClassDefinition
            {
                Id = name.Id,
                DisplayName = name.DisplayName
            };
            model.Classes.Add(placeholder);
            classIds.Add(name.Id, placeholder);
        }
    }

    public DiagramParseResult<ClassModel> Parse(string input) => parser.ParseDiagram(input);

    readonly record struct ClassName(string Id, string? DisplayName);

    readonly record struct RelationshipArrow(RelationshipMarker From, RelationshipMarker To, bool Dashed);

    interface IClassBodyContent;

    readonly record struct AnnotationItem(string Value) : IClassBodyContent;

    readonly record struct MemberItem(ClassMember Value) : IClassBodyContent;

    readonly record struct MethodItem(ClassMethod Value) : IClassBodyContent;

    internal interface IClassContent;

    readonly record struct DirectionItem(Direction Value) : IClassContent;

    readonly record struct MemberStatementItem(ClassName Owner, string Text) : IClassContent;

    readonly record struct AnnotationStatementItem(ClassName Owner, string Text) : IClassContent;

    readonly record struct NoteItem(ClassName? Owner, string Text) : IClassContent;

    readonly record struct NamespaceStartItem(string Name) : IClassContent;

    readonly record struct NamespaceEndItem : IClassContent;

    readonly record struct ClassDefinitionItem(ClassDefinition Value) : IClassContent;

    readonly record struct RelationshipItem(ClassRelationship Value, ClassName From, ClassName To) : IClassContent;
}
