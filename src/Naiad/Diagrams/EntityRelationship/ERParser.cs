class ERParser : IDiagramParser<ERModel>
{
    static Parser<ERModel> parser;

    static ERParser()
    {
        var restOfLine = Literals.NoneOf("\r\n", minSize: 0);

        // Trailing spaces are allowed before every line end.
        var endOfLine = CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.LineEnd);

        // Entity name: alphanumeric, underscore and hyphen, or anything at all inside double quotes.
        var bareEntityName =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AsString();

        // `CAR:::highlight` attaches style classes to an entity wherever it is named. They are read and
        // dropped: styling is not drawn, but the entity it sits on still is.
        var styleClasses =
            Literals.Text(":::")
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '-' or ','));

        var entityName =
            CommonParsers.DoubleQuotedString
                .Or(bareEntityName)
                .AndSkip(styleClasses.Optional());

        // The cardinalities written out in words, which read the same at either end of a relationship.
        // Longer spellings come first so that `1+` is not taken for `1`.
        var wordCardinality =
            OneOf(
                TextIgnoreCase("zero or one").Then(Cardinality.ZeroOrOne),
                TextIgnoreCase("one or zero").Then(Cardinality.ZeroOrOne),
                TextIgnoreCase("zero or more").Then(Cardinality.ZeroOrMore),
                TextIgnoreCase("zero or many").Then(Cardinality.ZeroOrMore),
                TextIgnoreCase("many(0)").Then(Cardinality.ZeroOrMore),
                Literals.Text("0+").Then(Cardinality.ZeroOrMore),
                TextIgnoreCase("one or more").Then(Cardinality.OneOrMore),
                TextIgnoreCase("one or many").Then(Cardinality.OneOrMore),
                TextIgnoreCase("many(1)").Then(Cardinality.OneOrMore),
                Literals.Text("1+").Then(Cardinality.OneOrMore),
                TextIgnoreCase("only one").Then(Cardinality.ExactlyOne),
                Literals.Text("1").Then(Cardinality.ExactlyOne)
            );

        // Left cardinality markers
        var leftCardinality =
            OneOf(
                Literals.Text("||").Then(Cardinality.ExactlyOne),
                Literals.Text("|o").Then(Cardinality.ZeroOrOne),
                Literals.Text("}|").Then(Cardinality.OneOrMore),
                Literals.Text("}o").Then(Cardinality.ZeroOrMore),
                wordCardinality
            );

        // Right cardinality markers
        var rightCardinality =
            OneOf(
                Literals.Text("||").Then(Cardinality.ExactlyOne),
                Literals.Text("o|").Then(Cardinality.ZeroOrOne),
                Literals.Text("|{").Then(Cardinality.OneOrMore),
                Literals.Text("o{").Then(Cardinality.ZeroOrMore),
                wordCardinality
            );

        // Line style: -- or `to` for identifying, .. or `optionally to` for non-identifying
        var lineStyle =
            OneOf(
                Literals.Text("--").Then(true),
                Literals.Text("..").Then(false),
                TextIgnoreCase("optionally to").Then(false),
                TextIgnoreCase("to").Then(true)
            );

        // A quoted label's delimiters are syntax, so take the string's contents when there is one and
        // fall back to the bare rest of the line otherwise. A quote that is never closed is not a label
        // of either kind.
        var relationshipLabel =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    CommonParsers.DoubleQuotedString
                        .Or(Not(Literals.Char('"')).SkipAnd(Literals.NoneOf("\r\n").AsString())));

        // Relationship: ENTITY1 ||--o{ ENTITY2 : label
        var relationshipParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(entityName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(leftCardinality)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(lineStyle)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(rightCardinality)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(entityName)
                .And(relationshipLabel.Optional())
                .AndSkip(endOfLine)
                .Map(static (fromEntity, leftCard, identifying, rightCard, toEntity, label) =>
                    new Relationship
                    {
                        FromEntity = fromEntity,
                        ToEntity = toEntity,
                        FromCardinality = leftCard,
                        ToCardinality = rightCard,
                        Label = label.HasValue ? label.Value.Trim() : null,
                        Identifying = identifying
                    });

        // Attribute key type. Mermaid's grammar is case-insensitive, so pk is as good as PK.
        var keyTypeParser =
            OneOf(
                TextIgnoreCase("PK").Then(AttributeKeyType.PrimaryKey),
                TextIgnoreCase("FK").Then(AttributeKeyType.ForeignKey),
                TextIgnoreCase("UK").Then(AttributeKeyType.UniqueKey)
            );

        // An attribute may carry several keys: PK, FK
        var keyTypesParser =
            Separated(
                CommonParsers.InlineWhitespace
                    .SkipAnd(Literals.Char(','))
                    .SkipAnd(CommonParsers.InlineWhitespace),
                keyTypeParser);

        // Attribute comment (in quotes)
        var attributeComment =
            CommonParsers.DoubleQuotedString;

        // A type or a name. Beyond identifier characters Mermaid admits hyphens, brackets, parentheses,
        // dots and commas, which is what lets a type carry its size: nvarchar(200), decimal(18,2).
        var attributeWord =
            Literals.Pattern(
                static _ => char.IsLetterOrDigit(_) || _ is '_' or '-' or '[' or ']' or '(' or ')' or '.' or ',' or '*');

        // A type may end in ? to mark it optional.
        var attributeType =
            Capture(attributeWord.And(Literals.Char('?').Optional()))
                .AsString();

        // Entity attribute: type name PK "comment"
        var attributeParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(attributeType)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(attributeWord.AsString())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(keyTypesParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(attributeComment.Optional())
                .AndSkip(endOfLine)
                .Map(static (type, name, keyTypes, comment) =>
                    CreateAttribute(type, name, keyTypes.OrSome([]), comment.OrNull()));

        // Entity body content: attribute lines, with comments and blank lines between them
        var entityBodyParser =
            OneOf(
                attributeParser.Then<EntityAttribute?>(static _ => _),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment)
                    .Then<EntityAttribute?>(static _ => null),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline)
                    .Then<EntityAttribute?>(static _ => null)
            ).Many()
            .Then(static _ => _.Where(static _ => _ != null).Cast<EntityAttribute>().ToList());

        // Entity alias: ["Display text"] or [Display]
        var entityAlias =
            Between(Literals.Char('['), CommonParsers.DoubleQuotedString.Or(bareEntityName), Literals.Char(']'))
                .AndSkip(styleClasses.Optional());

        // Entity definition: EntityName { attributes } or EntityName["alias"] { attributes }
        var entityDefinitionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(entityName)
                .And(CommonParsers.InlineWhitespace.SkipAnd(entityAlias).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.Newline.Optional())
                .And(entityBodyParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('}'))
                .AndSkip(endOfLine)
                .Map(static (name, alias, attributes) => CreateEntity(name, alias.OrNull(), attributes));

        // An entity with no attributes, named on a line of its own: EntityName or EntityName["alias"]
        var bareEntityParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(entityName)
                .And(CommonParsers.InlineWhitespace.SkipAnd(entityAlias).Optional())
                .AndSkip(endOfLine)
                .Map(static (name, alias) => CreateEntity(name, alias.OrNull(), []));

        var directionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("direction"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(CommonParsers.DirectionParser)
                .AndSkip(endOfLine);

        // Lines that are valid Mermaid but change nothing in what is drawn here: styling (`classDef`,
        // `class`, `style`) and the accessibility title and description. Naming them keeps them from
        // failing the parse, and from being read as entities.
        var ignoredKeyword =
            OneOf(
                Literals.Text("classDef"),
                Literals.Text("class"),
                Literals.Text("style"),
                Literals.Text("accTitle"),
                Literals.Text("accDescr"));

        var ignoredDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(ignoredKeyword.WhenFollowedBy(Literals.AnyOf(" \t:", minSize: 1, maxSize: 1)))
                .SkipAnd(restOfLine)
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

        var parseContent =
            OneOf(
                directionParser.Then<IERContent?>(static _ => new DirectionItem(_)),
                multiLineDescription.Then<IERContent?>(static _ => null),
                ignoredDirective.Then<IERContent?>(static _ => null),
                entityDefinitionParser.Then<IERContent?>(static _ => new EntityItem(_)),
                relationshipParser.Then<IERContent?>(static _ => new RelationshipItem(_)),
                bareEntityParser.Then<IERContent?>(static _ => new EntityItem(_)),
                skipLine.Then<IERContent?>(static _ => null)
            ).Many();

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("erDiagram"))
                .SkipAnd(endOfLine)
                .SkipAnd(parseContent)
                // Nothing may be left over: without this a line no rule matches ends the list quietly and the
                // rest of the diagram is dropped instead of being reported.
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
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

    public DiagramParseResult<ERModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IERContent;
    readonly record struct EntityItem(Entity Value) : IERContent;
    readonly record struct RelationshipItem(Relationship Value) : IERContent;
    readonly record struct DirectionItem(Direction Value) : IERContent;
}
