class ERParser : IDiagramParser<ERModel>
{
    static Parser<char, ERModel> parser;

    static ERParser()
    {
        var restOfLine = Token(_ => _ != '\r' && _ != '\n').ManyString();

        // Trailing spaces are allowed before every line end.
        var endOfLine = CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd);

        // Entity name: alphanumeric, underscore and hyphen, or anything at all inside double quotes.
        var bareEntityName =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AtLeastOnceString();

        // `CAR:::highlight` attaches style classes to an entity wherever it is named. They are read and
        // dropped: styling is not drawn, but the entity it sits on still is.
        var styleClasses =
            String(":::")
                .Then(Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '-' or ',').SkipAtLeastOnce());

        var entityName =
            CommonParsers.DoubleQuotedString
                .Or(bareEntityName)
                .Before(Try(styleClasses).Optional())
                .Labelled("entity name");

        // The cardinalities written out in words, which read the same at either end of a relationship.
        // Longer spellings come first so that `1+` is not taken for `1`.
        var wordCardinality =
            OneOf(
                Try(CIString("zero or one")).ThenReturn(Cardinality.ZeroOrOne),
                Try(CIString("one or zero")).ThenReturn(Cardinality.ZeroOrOne),
                Try(CIString("zero or more")).ThenReturn(Cardinality.ZeroOrMore),
                Try(CIString("zero or many")).ThenReturn(Cardinality.ZeroOrMore),
                Try(CIString("many(0)")).ThenReturn(Cardinality.ZeroOrMore),
                Try(String("0+")).ThenReturn(Cardinality.ZeroOrMore),
                Try(CIString("one or more")).ThenReturn(Cardinality.OneOrMore),
                Try(CIString("one or many")).ThenReturn(Cardinality.OneOrMore),
                Try(CIString("many(1)")).ThenReturn(Cardinality.OneOrMore),
                Try(String("1+")).ThenReturn(Cardinality.OneOrMore),
                Try(CIString("only one")).ThenReturn(Cardinality.ExactlyOne),
                String("1").ThenReturn(Cardinality.ExactlyOne)
            );

        // Left cardinality markers
        var leftCardinality =
            OneOf(
                Try(String("||")).ThenReturn(Cardinality.ExactlyOne),
                Try(String("|o")).ThenReturn(Cardinality.ZeroOrOne),
                Try(String("}|")).ThenReturn(Cardinality.OneOrMore),
                Try(String("}o")).ThenReturn(Cardinality.ZeroOrMore),
                wordCardinality
            );

        // Right cardinality markers
        var rightCardinality =
            OneOf(
                Try(String("||")).ThenReturn(Cardinality.ExactlyOne),
                Try(String("o|")).ThenReturn(Cardinality.ZeroOrOne),
                Try(String("|{")).ThenReturn(Cardinality.OneOrMore),
                Try(String("o{")).ThenReturn(Cardinality.ZeroOrMore),
                wordCardinality
            );

        // Line style: -- or `to` for identifying, .. or `optionally to` for non-identifying
        var lineStyle =
            OneOf(
                String("--").ThenReturn(true),
                String("..").ThenReturn(false),
                Try(CIString("optionally to")).ThenReturn(false),
                CIString("to").ThenReturn(true)
            );

        // Relationship: ENTITY1 ||--o{ ENTITY2 : label
        var relationshipParser =
            from _ in CommonParsers.InlineWhitespace
            from fromEntity in entityName
            from __ in CommonParsers.InlineWhitespace
            from leftCard in leftCardinality
            from ___ in CommonParsers.InlineWhitespace
            from identifying in lineStyle
            from ____ in CommonParsers.InlineWhitespace
            from rightCard in rightCardinality
            from _____ in CommonParsers.InlineWhitespace
            from toEntity in entityName
            from label in Try(
                CommonParsers.InlineWhitespace
                    .Then(Char(':'))
                    .Then(CommonParsers.InlineWhitespace)
                    // A quoted label's delimiters are syntax, so take the string's contents when there is
                    // one and fall back to the bare rest of the line otherwise.
                    .Then(CommonParsers.DoubleQuotedString
                        .Or(Token(_ => _ != '\r' && _ != '\n').AtLeastOnceString()))
            ).Optional()
            from ______ in endOfLine
            select new Relationship
            {
                FromEntity = fromEntity,
                ToEntity = toEntity,
                FromCardinality = leftCard,
                ToCardinality = rightCard,
                Label = label.HasValue ? label.Value.Trim() : null,
                Identifying = identifying
            };

        // Attribute key type. Mermaid's grammar is case-insensitive, so pk is as good as PK.
        var keyTypeParser =
            OneOf(
                Try(CIString("PK")).ThenReturn(AttributeKeyType.PrimaryKey),
                Try(CIString("FK")).ThenReturn(AttributeKeyType.ForeignKey),
                CIString("UK").ThenReturn(AttributeKeyType.UniqueKey)
            );

        // An attribute may carry several keys: PK, FK
        var keyTypesParser =
            keyTypeParser.SeparatedAtLeastOnce(
                Try(CommonParsers.InlineWhitespace
                    .Then(Char(','))
                    .Then(CommonParsers.InlineWhitespace)));

        // Attribute comment (in quotes)
        var attributeComment =
            CommonParsers.DoubleQuotedString;

        // A type or a name. Beyond identifier characters Mermaid admits hyphens, brackets, parentheses,
        // dots and commas, which is what lets a type carry its size: nvarchar(200), decimal(18,2).
        var attributeWord =
            Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '-' or '[' or ']' or '(' or ')' or '.' or ',' or '*')
                .AtLeastOnceString();

        // A type may end in ? to mark it optional.
        var attributeType =
            from word in attributeWord
            from optional in Char('?').Optional()
            select optional.HasValue ? word + '?' : word;

        // Entity attribute: type name PK "comment"
        var attributeParser =
            from _ in CommonParsers.InlineWhitespace
            from type in attributeType
            from __ in CommonParsers.RequiredWhitespace
            from name in attributeWord
            from ___ in CommonParsers.InlineWhitespace
            from keyTypes in Try(keyTypesParser).Optional()
            from ____ in CommonParsers.InlineWhitespace
            from comment in Try(attributeComment).Optional()
            from _____ in endOfLine
            select CreateAttribute(type, name, keyTypes.HasValue ? keyTypes.Value : [], comment.HasValue ? comment.Value : null);

        // Entity body content: attribute lines, with comments and blank lines between them
        var entityBodyParser =
            OneOf(
                Try(attributeParser.Select<EntityAttribute?>(_ => _)),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                    .ThenReturn<EntityAttribute?>(null),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline))
                    .ThenReturn<EntityAttribute?>(null)
            ).Many()
            .Select(_ => _.Where(_ => _ != null).Cast<EntityAttribute>().ToList());

        // Entity alias: ["Display text"] or [Display]
        var entityAlias =
            Char('[')
                .Then(CommonParsers.DoubleQuotedString.Or(bareEntityName))
                .Before(Char(']'))
                .Before(Try(styleClasses).Optional());

        // Entity definition: EntityName { attributes } or EntityName["alias"] { attributes }
        var entityDefinitionParser =
            Try(
                from _ in CommonParsers.InlineWhitespace
                from name in entityName
                from alias in Try(CommonParsers.InlineWhitespace.Then(entityAlias)).Optional()
                from __ in CommonParsers.InlineWhitespace
                from open in Char('{')
                from ___ in CommonParsers.InlineWhitespace
                from ____ in CommonParsers.Newline.Optional()
                from attributes in entityBodyParser
                from _____ in CommonParsers.InlineWhitespace
                from close in Char('}')
                from ______ in endOfLine
                select CreateEntity(name, alias.HasValue ? alias.Value : null, attributes)
            );

        // An entity with no attributes, named on a line of its own: EntityName or EntityName["alias"]
        var bareEntityParser =
            from _ in CommonParsers.InlineWhitespace
            from name in entityName
            from alias in Try(CommonParsers.InlineWhitespace.Then(entityAlias)).Optional()
            from __ in endOfLine
            select CreateEntity(name, alias.HasValue ? alias.Value : null, []);

        var directionParser =
            CommonParsers.InlineWhitespace
                .Then(String("direction"))
                .Then(CommonParsers.RequiredWhitespace)
                .Then(CommonParsers.DirectionParser)
                .Before(endOfLine);

        // Lines that are valid Mermaid but change nothing in what is drawn here: styling (`classDef`,
        // `class`, `style`) and the accessibility title and description. Naming them keeps them from
        // failing the parse, and from being read as entities.
        var ignoredKeyword =
            OneOf(
                Try(String("classDef")),
                Try(String("class")),
                Try(String("style")),
                Try(String("accTitle")),
                String("accDescr"));

        var ignoredDirective =
            from _ in CommonParsers.InlineWhitespace
            from keyword in ignoredKeyword
            from boundary in Lookahead(Token(_ => _ is ' ' or '\t' or ':'))
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

        var parseContent =
            OneOf(
                Try(directionParser.Select<IERContent?>(_ => new DirectionItem(_))),
                Try(multiLineDescription.ThenReturn<IERContent?>(null)),
                Try(ignoredDirective.ThenReturn<IERContent?>(null)),
                Try(entityDefinitionParser.Select<IERContent?>(_ => new EntityItem(_))),
                Try(relationshipParser.Select<IERContent?>(_ => new RelationshipItem(_))),
                Try(bareEntityParser.Select<IERContent?>(_ => new EntityItem(_))),
                skipLine.ThenReturn<IERContent?>(null)
            ).Many();

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("erDiagram")
            from __ in endOfLine
            from content in parseContent
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(content);
    }

    static EntityAttribute CreateAttribute(string type, string name, IEnumerable<AttributeKeyType> keyTypes, string? comment)
    {
        var attribute = new EntityAttribute
        {
            Name = name,
            Type = type,
            Comment = comment
        };

        foreach (var keyType in keyTypes)
        {
            if (!attribute.KeyTypes.Contains(keyType))
            {
                attribute.KeyTypes.Add(keyType);
            }
        }

        if (attribute.KeyTypes.Count > 0)
        {
            attribute.KeyType = attribute.KeyTypes[0];
        }

        return attribute;
    }

    static Entity CreateEntity(string name, string? alias, List<EntityAttribute> attributes)
    {
        var entity = new Entity { Name = name, Alias = alias };
        entity.Attributes.AddRange(attributes);
        return entity;
    }

    static ERModel BuildModel(IEnumerable<IERContent?> content)
    {
        var model = new ERModel();
        var entityMap = new Dictionary<string, Entity>();

        foreach (var item in content)
        {
            switch (item)
            {
                case EntityItem entity:
                    var e = entity.Value;
                    if (entityMap.TryGetValue(e.Name, out var existing))
                    {
                        // Merge attributes into existing entity
                        existing.Attributes.AddRange(e.Attributes);
                        existing.Alias ??= e.Alias;
                    }
                    else
                    {
                        entityMap[e.Name] = e;
                        model.Entities.Add(e);
                    }

                    break;

                case DirectionItem direction:
                    model.Direction = direction.Value;
                    break;

                case RelationshipItem rel:
                    var r = rel.Value;
                    // Auto-create entities from relationships
                    EnsureEntity(r.FromEntity, entityMap, model);
                    EnsureEntity(r.ToEntity, entityMap, model);
                    model.Relationships.Add(r);
                    break;
            }
        }

        return model;
    }

    static void EnsureEntity(string name, Dictionary<string, Entity> entityMap, ERModel model)
    {
        if (entityMap.ContainsKey(name))
            return;

        var entity = new Entity { Name = name };
        entityMap[name] = entity;
        model.Entities.Add(entity);
    }

    public Result<char, ERModel> Parse(string input) => parser.Parse(input);

    internal interface IERContent;
    readonly record struct EntityItem(Entity Value) : IERContent;
    readonly record struct RelationshipItem(Relationship Value) : IERContent;
    readonly record struct DirectionItem(Direction Value) : IERContent;
}
