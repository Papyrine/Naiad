class ClassParser : IDiagramParser<ClassModel>
{
    static Parser<char, ClassModel> parser;

    static ClassParser()
    {
        // Spaces and tabs only. Whitespace that may span a newline would let a member's type bind to the
        // name on the *following* line, merging bodies such as an enumeration's one-value-per-line list.
        var inlineGap =
            Token(_ => _ is ' ' or '\t').SkipAtLeastOnce();

        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_')
                .AtLeastOnceString()
                .Labelled("class identifier");

        // Mermaid spells generics ~T~. The tilde form is display only, so a class is keyed on its bare
        // name and `IRepository~T~` in a relationship resolves to the `class IRepository~T~` declaration.
        var genericArgument =
            Char('~')
                .Then(Token(_ => _ != '~' && _ != '\r' && _ != '\n').AtLeastOnceString())
                .Before(Char('~'));

        var className =
            from id in identifier
            from generic in Try(genericArgument).Optional()
            select new ClassName(id, generic.HasValue ? $"{id}<{generic.Value}>" : null);

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

        // Class annotation: <<interface>>, <<abstract>>, etc.
        var annotationParser =
            CommonParsers.InlineWhitespace
                .Then(String("<<"))
                .Then(OneOf(
                    Try(String("interface")).ThenReturn(ClassAnnotation.Interface),
                    Try(String("abstract")).ThenReturn(ClassAnnotation.Abstract),
                    Try(String("service")).ThenReturn(ClassAnnotation.Service),
                    String("enumeration").ThenReturn(ClassAnnotation.Enumeration)
                ))
                .Before(String(">>"))
                .Before(CommonParsers.InlineWhitespace)
                .Before(CommonParsers.LineEnd);

        // Class body content: { ... }
        Parser<char, (ClassAnnotation? annotation, List<ClassMember> members, List<ClassMethod> methods)> parseClassBody;
        {
            var annotationLine = Try(annotationParser.Select<IClassBodyContent?>(_ => new AnnotationItem(_)));
            var methodLine = Try(methodParser.Select<IClassBodyContent?>(_ => new MethodItem(_)));
            var memberLine = Try(memberParser.Select<IClassBodyContent?>(_ => new MemberItem(_)));

            // Try, and a newline rather than LineEnd: the indent before the closing brace must be given
            // back so the body ends cleanly, and a parser that can match at EOF without consuming would
            // spin here forever.
            var emptyLine = Try(
                CommonParsers.InlineWhitespace
                    .Then(CommonParsers.Newline)
                    .ThenReturn<IClassBodyContent?>(null));

            var contentLine = OneOf(annotationLine, methodLine, memberLine, emptyLine);

            parseClassBody = contentLine.Many().Select(items =>
            {
                ClassAnnotation? annotation = null;
                var members = new List<ClassMember>();
                var methods = new List<ClassMethod>();

                foreach (var item in items)
                {
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
            });
        }

        // Class definition: class ClassName { ... } or class ClassName
        var classDefinitionParser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("class")
            from __ in CommonParsers.RequiredWhitespace
            from name in className
            from ___ in CommonParsers.InlineWhitespace
            from body in Try(
                from open in Char('{')
                from ____ in CommonParsers.LineEnd
                from content in parseClassBody
                from _____ in CommonParsers.InlineWhitespace
                from close in Char('}')
                from ______ in CommonParsers.LineEnd
                select content
            ).Optional()
            from _______ in CommonParsers.LineEnd.Optional()
            select CreateClassDefinition(name, body);

        // A marker token on each side of the line, so `<|--`, `--|>`, `*--`, `--o` and two-sided forms
        // such as `<|--|>` each keep their glyph on the end the author wrote it on.
        var fromMarker =
            OneOf(
                Try(String("<|")).ThenReturn(RelationshipMarker.Triangle),
                Try(String("*")).ThenReturn(RelationshipMarker.FilledDiamond),
                Try(String("o")).ThenReturn(RelationshipMarker.HollowDiamond),
                Try(String("<")).ThenReturn(RelationshipMarker.Arrow)
            );

        var toMarker =
            OneOf(
                Try(String("|>")).ThenReturn(RelationshipMarker.Triangle),
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

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                Try(directionDirectiveParser.Select<IClassContent?>(_ => new DirectionItem(_))),
                Try(classDefinitionParser.Select<IClassContent?>(_ => new ClassDefinitionItem(_))),
                Try(relationshipParser.Select<IClassContent?>(_ => _)),
                skipLine.ThenReturn<IClassContent?>(null)
            ).Many();

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("classDiagram")
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from content in parseContent
            select BuildModel(content);
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

        foreach (var param in paramStr.Split(','))
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

            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var typed = parts.Length >= 2;
            parameters.Add(
                new()
                {
                    Name = NormalizeGenerics(typed ? parts[1] : parts[0]),
                    Type = typed ? NormalizeGenerics(parts[0]) : null
                });
        }

        return parameters;
    }

    static ClassDefinition CreateClassDefinition(
        ClassName name,
        Maybe<(ClassAnnotation? annotation, List<ClassMember> members, List<ClassMethod> methods)> body)
    {
        var classDef = new ClassDefinition
        {
            Id = name.Id,
            DisplayName = name.DisplayName
        };

        if (body.HasValue)
        {
            if (body.Value.annotation.HasValue)
                classDef.Annotation = body.Value.annotation;
            classDef.Members.AddRange(body.Value.members);
            classDef.Methods.AddRange(body.Value.methods);
        }

        return classDef;
    }

    static ClassModel BuildModel(IEnumerable<IClassContent?> content)
    {
        var model = new ClassModel();
        var classIds = new Dictionary<string, ClassDefinition>();

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
                        existing.Annotation ??= c.Annotation;
                        existing.Members.AddRange(c.Members);
                        existing.Methods.AddRange(c.Methods);
                    }
                    else
                    {
                        model.Classes.Add(c);
                        classIds.Add(c.Id, c);
                    }

                    break;

                case RelationshipItem rel:
                    // Auto-add classes from relationships
                    AddPlaceholder(rel.From);
                    AddPlaceholder(rel.To);
                    model.Relationships.Add(rel.Value);
                    break;
            }
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

    readonly record struct AnnotationItem(ClassAnnotation Value) : IClassBodyContent;

    readonly record struct MemberItem(ClassMember Value) : IClassBodyContent;

    readonly record struct MethodItem(ClassMethod Value) : IClassBodyContent;

    internal interface IClassContent;

    readonly record struct DirectionItem(Direction Value) : IClassContent;

    readonly record struct ClassDefinitionItem(ClassDefinition Value) : IClassContent;

    readonly record struct RelationshipItem(ClassRelationship Value, ClassName From, ClassName To) : IClassContent;
}
