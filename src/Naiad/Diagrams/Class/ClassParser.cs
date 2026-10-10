class ClassParser : IDiagramParser<ClassModel>
{
    static Parser<char, ClassModel> parser;

    // One member line, as written in a class body or after `ClassName :`.
    static Parser<char, IClassBodyContent> memberLine;

    static ClassParser()
    {
        // Spaces and tabs only. Whitespace that may span a newline would let a member's type bind to the
        // name on the *following* line, merging bodies such as an enumeration's one-value-per-line list.
        var inlineGap =
            Token(_ => _ is ' ' or '\t').SkipAtLeastOnce();

        // Letters, digits and underscores, plus a hyphen between two of them (`Order-Line`). A hyphen that
        // starts a relationship line is not part of the name.
        var idChar =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_');

        var identifier =
            idChar
                .Then(idChar.Or(Try(Char('-').Before(Lookahead(idChar)))).SkipMany())
                .Slice((span, _) => span.ToString())
                .Labelled("class identifier");

        // A name in backticks may hold anything, spaces and punctuation included.
        var backtickName =
            Char('`')
                .Then(Token(_ => _ != '`' && _ != '\r' && _ != '\n').AtLeastOnceString())
                .Before(Char('`'));

        // Mermaid spells generics ~T~. The tilde form is display only, so a class is keyed on its bare
        // name and `IRepository~T~` in a relationship resolves to the `class IRepository~T~` declaration.
        var genericArgument =
            Char('~')
                .Then(Token(_ => _ != '~' && _ != '\r' && _ != '\n').AtLeastOnceString())
                .Before(Char('~'));

        var className =
            backtickName.Select(_ => new ClassName(_, null))
                .Or(
                    from id in identifier
                    from generic in Try(genericArgument).Optional()
                    select new ClassName(id, generic.HasValue ? $"{id}<{generic.Value}>" : null));

        var visibilityParser =
            OneOf(
                Char('+').ThenReturn(Visibility.Public),
                Char('-').ThenReturn(Visibility.Private),
                Char('#').ThenReturn(Visibility.Protected),
                Char('~').ThenReturn(Visibility.PackagePrivate)
            );

        // A type name, including a ~T~ generic argument, which is normalised to angle brackets.
        var typeName =
            Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '<' or '>' or '[' or ']' or ',' or '~')
                .AtLeastOnceString()
                .Select(NormalizeGenerics);

        // Type annotation like : String or : int
        var typeAnnotation =
            CommonParsers.InlineWhitespace
                .Then(Char(':'))
                .Then(CommonParsers.InlineWhitespace)
                .Then(typeName);

        // Mermaid's classifier suffix: $ marks a static member, * an abstract one. It trails the
        // declaration, after the parentheses for a method (`+validate()$ bool`).
        var classifier = OneOf(Char('$'), Char('*'));

        // Method parameters like (String name, int age) or (id: int)
        var parametersParser =
            Char('(')
                .Then(
                    Token(_ => _ != ')' && _ != '\r' && _ != '\n')
                        .ManyString()
                )
                .Before(Char(')'))
                .Select(ParseParameters);

        // Member: +String name (type first), +name : String (type after colon) or a bare enumeration value
        var memberParser =
            from _ in CommonParsers.InlineWhitespace
            from visibility in visibilityParser.Optional()
            from firstWord in typeName
            from rest in Try(
                // Type first format: +String name
                from __ in inlineGap
                from memberName in Token(_ => char.IsLetterOrDigit(_) || _ == '_').AtLeastOnceString()
                select (Type: (string?) firstWord, Name: memberName)
            ).Or(
                // Name only or name : Type format
                from annotation in typeAnnotation.Optional()
                select (Type: annotation.HasValue ? annotation.Value : null, Name: firstWord)
            )
            from suffix in classifier.Optional()
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            select new ClassMember
            {
                Name = rest.Name,
                Type = rest.Type,
                Visibility = visibility.HasValue ? visibility.Value : Visibility.Public,
                IsStatic = suffix is {HasValue: true, Value: '$'}
            };

        // Method: +makeSound(), +move(int distance) : void, +getId() int or +validate()$ bool
        var methodParser =
            from _ in CommonParsers.InlineWhitespace
            from visibility in visibilityParser.Optional()
            from name in Token(_ => char.IsLetterOrDigit(_) || _ == '_').AtLeastOnceString()
            from parameters in parametersParser
            from suffix in classifier.Optional()
            from returnType in OneOf(
                // `: void` and the bare `void` of `+getId() int` are both Mermaid return types.
                Try(typeAnnotation),
                Try(inlineGap.Then(typeName))
            ).Optional()
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            select CreateMethod(
                name,
                parameters,
                returnType.HasValue ? returnType.Value : null,
                visibility.HasValue ? visibility.Value : Visibility.Public,
                suffix);

        // Class annotation: <<interface>>, <<abstract>>, or any other text, such as <<Entity>>.
        var annotationText =
            String("<<")
                .Then(Token(_ => _ != '>' && _ != '\r' && _ != '\n').AtLeastOnceString())
                .Before(String(">>"));

        var annotationParser =
            CommonParsers.InlineWhitespace
                .Then(annotationText)
                .Before(CommonParsers.InlineWhitespace)
                .Before(CommonParsers.LineEnd);

        // One line of a class body, in the spellings that are understood structurally. A line that fits
        // none of them is still a member (see ParseLooseMember), so nothing in a body can fail the parse.
        memberLine =
            OneOf(
                Try(annotationParser.Select<IClassBodyContent>(_ => new AnnotationItem(_))),
                Try(methodParser.Select<IClassBodyContent>(_ => new MethodItem(_))),
                Try(memberParser.Select<IClassBodyContent>(_ => new MemberItem(_))));

        // Class body: { ... }. As in Mermaid everything up to the closing brace belongs to the body, one
        // member per line, so the braces may share a line with the members (`class A { +x }`) and may be
        // followed by trailing whitespace.
        var classBody =
            Char('{')
                .Then(Token(_ => _ != '}').ManyString())
                .Before(Char('}'))
                .Select(text => ParseBody(text, memberLine));

        // Display label: class Animal["Animal with a label"]
        var classLabel =
            Char('[')
                .Then(CommonParsers.DoubleQuotedString
                    .Or(Token(_ => _ != ']' && _ != '\r' && _ != '\n').ManyString()))
                .Before(Char(']'));

        // Class definition: class ClassName, class ClassName { ... }, class ClassName["Label"] or
        // class ClassName:::cssClass. Styling is not applied to class diagrams, so the css class is read
        // and dropped.
        var classDefinitionParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("class")
            from __ in inlineGap
            from name in className
            from label in Try(CommonParsers.InlineWhitespace.Then(classLabel)).Optional()
            from cssClass in Try(String(":::").Then(CommonParsers.Identifier)).Optional()
            from ___ in CommonParsers.InlineWhitespace
            from body in Try(classBody).Optional()
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select CreateClassDefinition(name, label, body);

        // A marker token on each side of the line, so `<|--`, `--|>`, `*--`, `--o` and two-sided forms
        // such as `<|--|>` each keep their glyph on the end the author wrote it on.
        var fromMarker =
            OneOf(
                Try(String("<|")).ThenReturn(RelationshipMarker.Triangle),
                Try(String("()")).ThenReturn(RelationshipMarker.Lollipop),
                Try(String("*")).ThenReturn(RelationshipMarker.FilledDiamond),
                Try(String("o")).ThenReturn(RelationshipMarker.HollowDiamond),
                Try(String("<")).ThenReturn(RelationshipMarker.Arrow)
            );

        var toMarker =
            OneOf(
                Try(String("|>")).ThenReturn(RelationshipMarker.Triangle),
                Try(String("()")).ThenReturn(RelationshipMarker.Lollipop),
                Try(String("*")).ThenReturn(RelationshipMarker.FilledDiamond),
                Try(String("o")).ThenReturn(RelationshipMarker.HollowDiamond),
                Try(String(">")).ThenReturn(RelationshipMarker.Arrow)
            );

        var relationshipLine =
            OneOf(
                Try(String("--")).ThenReturn(false),
                String("..").ThenReturn(true)
            );

        var relationshipArrowParser =
            from from_ in Try(fromMarker).Optional()
            from dashed in relationshipLine
            from to in Try(toMarker).Optional()
            select new RelationshipArrow(
                from_.HasValue ? from_.Value : RelationshipMarker.None,
                to.HasValue ? to.Value : RelationshipMarker.None,
                dashed);

        // Cardinality like "1", "0..1", "1..*", "*"
        var cardinalityParser =
            Char('"')
                .Then(Token(_ => _ != '"').AtLeastOnceString())
                .Before(Char('"'));

        // Relationship: ClassA "1" <|-- "*" ClassB : label
        var relationshipParser =
            from _ in CommonParsers.InlineWhitespace
            from fromId in className
            from __ in CommonParsers.InlineWhitespace
            from fromCardinality in Try(cardinalityParser.Before(CommonParsers.InlineWhitespace)).Optional()
            from arrow in relationshipArrowParser
            from ___ in CommonParsers.InlineWhitespace
            from toCardinality in Try(cardinalityParser.Before(inlineGap)).Optional()
            from toId in className
            from ____ in CommonParsers.InlineWhitespace
            from label in Try(
                Char(':')
                    .Then(CommonParsers.InlineWhitespace)
                    .Then(Token(_ => _ != '\r' && _ != '\n').ManyString())
            ).Optional()
            from lineEnd in CommonParsers.LineEnd
            select new RelationshipItem(
                new()
                {
                    FromId = fromId.Id,
                    ToId = toId.Id,
                    Type = Classify(arrow),
                    FromMarker = arrow.From,
                    ToMarker = arrow.To,
                    IsDashed = arrow.Dashed,
                    Label = label.HasValue ? label.Value.Trim() : null,
                    FromCardinality = fromCardinality.HasValue ? fromCardinality.Value : null,
                    ToCardinality = toCardinality.HasValue ? toCardinality.Value : null
                },
                fromId,
                toId);

        // Direction directive
        var directionDirectiveParser =
            CommonParsers.InlineWhitespace
                .Then(String("direction"))
                .Then(CommonParsers.RequiredWhitespace)
                .Then(CommonParsers.DirectionParser)
                .Before(CommonParsers.LineEnd);

        var restOfLine =
            Token(_ => _ != '\r' && _ != '\n').ManyString();

        // Member added from outside the class body: `Animal : +int age`, `Animal: +isMammal()`
        var memberStatementParser =
            from _ in CommonParsers.InlineWhitespace
            from owner in className
            from __ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from text in restOfLine
            from ___ in CommonParsers.LineEnd
            select new MemberStatementItem(owner, text);

        // Annotation given on its own line: `<<interface>> Shape`
        var annotationStatementParser =
            from _ in CommonParsers.InlineWhitespace
            from text in annotationText
            from __ in CommonParsers.InlineWhitespace
            from owner in className
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            select new AnnotationStatementItem(owner, text);

        // note "text" or note for ClassName "text"
        var noteParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("note")
            from __ in inlineGap
            from owner in Try(String("for").Then(inlineGap).Then(className).Before(inlineGap)).Optional()
            from text in CommonParsers.DoubleQuotedString
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            select new NoteItem(owner.HasValue ? owner.Value : (ClassName?) null, text);

        // namespace Name { ... } groups the classes declared inside it.
        var namespaceStartParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("namespace")
            from __ in inlineGap
            from name in Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '.' or '-').AtLeastOnceString()
            from ___ in CommonParsers.InlineWhitespace
            from open in Char('{')
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new NamespaceStartItem(name);

        var namespaceEndParser =
            from _ in CommonParsers.InlineWhitespace
            from close in Char('}')
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            select new NamespaceEndItem();

        // Styling and interaction, neither of which a class diagram here acts on.
        var ignoredParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in OneOf(
                Try(String("classDef")),
                Try(String("cssClass")),
                Try(String("style")),
                Try(String("click")),
                Try(String("link")),
                String("callback"))
            from __ in inlineGap
            from rest in restOfLine
            from ___ in CommonParsers.LineEnd
            select Unit.Value;

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                Try(directionDirectiveParser.Select<IClassContent?>(_ => new DirectionItem(_))),
                Try(classDefinitionParser.Select<IClassContent?>(_ => new ClassDefinitionItem(_))),
                Try(relationshipParser.Select<IClassContent?>(_ => _)),
                Try(noteParser.Select<IClassContent?>(_ => _)),
                Try(namespaceStartParser.Select<IClassContent?>(_ => _)),
                Try(namespaceEndParser.Select<IClassContent?>(_ => _)),
                Try(annotationStatementParser.Select<IClassContent?>(_ => _)),
                Try(ignoredParser.ThenReturn<IClassContent?>(null)),
                Try(memberStatementParser.Select<IClassContent?>(_ => _)),
                skipLine.ThenReturn<IClassContent?>(null)
            );

        // The statements must account for the whole input. Without the end-of-input check a line no rule
        // matches would just end the list, and everything after it would be dropped without an error.
        var endOfInput =
            Try(CommonParsers.InlineWhitespace.Then(End));

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("classDiagram")
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from content in parseContent.ManyThen(endOfInput)
            select BuildModel(content.Item1);
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
        Maybe<char> classifier)
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
        Parser<char, IClassBodyContent> bodyLine)
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

            var parsed = bodyLine.Parse(line.ToString());
            var item = parsed.Success ? parsed.Value : ParseLooseMember(line);
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
        Maybe<string> label,
        Maybe<(string? annotation, List<ClassMember> members, List<ClassMethod> methods)> body)
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
            System.Enum.TryParse<ClassAnnotation>(trimmed, true, out var known))
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

    public Result<char, ClassModel> Parse(string input) => parser.Parse(input);

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
