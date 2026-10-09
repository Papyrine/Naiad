class ERParser : IDiagramParser<ERModel>
{
    static Parser<char, ERModel> parser;

    static ERParser()
    {
        // Entity name (alphanumeric, underscore, hyphen)
        var entityName =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AtLeastOnceString()
                .Labelled("entity name");

        // Left cardinality markers
        var leftCardinality =
            OneOf(
                Try(String("||")).ThenReturn(Cardinality.ExactlyOne),
                Try(String("|o")).ThenReturn(Cardinality.ZeroOrOne),
                Try(String("}|")).ThenReturn(Cardinality.OneOrMore),
                String("}o").ThenReturn(Cardinality.ZeroOrMore)
            );

        // Right cardinality markers
        var rightCardinality =
            OneOf(
                Try(String("||")).ThenReturn(Cardinality.ExactlyOne),
                Try(String("o|")).ThenReturn(Cardinality.ZeroOrOne),
                Try(String("|{")).ThenReturn(Cardinality.OneOrMore),
                String("o{").ThenReturn(Cardinality.ZeroOrMore)
            );

        // Line style (-- for identifying, .. for non-identifying)
        var lineStyle =
            OneOf(
                String("--").ThenReturn(true),
                String("..").ThenReturn(false)
            );

        // Relationship: ENTITY1 ||--o{ ENTITY2 : label
        var relationshipParser =
            from _ in CommonParsers.InlineWhitespace
            from fromEntity in entityName
            from __ in CommonParsers.InlineWhitespace
            from leftCard in leftCardinality
            from identifying in lineStyle
            from rightCard in rightCardinality
            from ___ in CommonParsers.InlineWhitespace
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
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
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
            from keyType in Try(keyTypeParser).Optional()
            from ____ in CommonParsers.InlineWhitespace
            from comment in Try(attributeComment).Optional()
            from _____ in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            select new EntityAttribute
            {
                Name = name,
                Type = type,
                KeyType = keyType.HasValue ? keyType.Value : AttributeKeyType.None,
                Comment = comment.HasValue ? comment.Value : null
            };

        // Entity body content: individual attribute lines
        var entityBodyParser =
            OneOf(
                Try(attributeParser.Select<EntityAttribute?>(_ => _)),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd))
                    .ThenReturn<EntityAttribute?>(null)
            ).Many()
            .Select(_ => _.Where(_ => _ != null).Cast<EntityAttribute>().ToList());

        // Entity alias: ["Display text"] or [Display]
        var entityAlias =
            Char('[')
                .Then(CommonParsers.DoubleQuotedString.Or(entityName))
                .Before(Char(']'));

        // Entity definition: EntityName { attributes } or EntityName["alias"] { attributes }
        var entityDefinitionParser =
            Try(
                from _ in CommonParsers.InlineWhitespace
                from name in entityName
                from alias in Try(CommonParsers.InlineWhitespace.Then(entityAlias)).Optional()
                from __ in CommonParsers.InlineWhitespace
                from open in Char('{')
                from ___ in CommonParsers.LineEnd
                from attributes in entityBodyParser
                from ____ in CommonParsers.InlineWhitespace
                from close in Char('}')
                from _____ in CommonParsers.LineEnd
                select CreateEntity(name, alias.HasValue ? alias.Value : null, attributes)
            );

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        var parseContent =
            OneOf(
                Try(entityDefinitionParser.Select<IERContent?>(_ => new EntityItem(_))),
                Try(relationshipParser.Select<IERContent?>(_ => new RelationshipItem(_))),
                skipLine.ThenReturn<IERContent?>(null)
            ).Many();

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("erDiagram")
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from content in parseContent
            select BuildModel(content);
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
}
