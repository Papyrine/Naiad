partial class UseCaseParser : IDiagramParser<UseCaseModel>
{
    static Parser<UseCaseModel> parser;

    static UseCaseParser()
    {
        var whitespace = CommonParsers.InlineWhitespace;
        var requiredWhitespace = Literals.AnyOf(" \t");

        // Identifiers are diagram-wide and may start with a digit (`1`, `3rd`).
        var identifier =
            Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ == '_').AsString();

        // A keyword ends where the word does, so `actorX` and `notes` stay identifiers.
        static Parser<string> Keyword(string text) =>
            Literals.Text(text).WhenNotFollowedBy(Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ == '_'));

        // A plain string stays on one line, and a backslash in it escapes nothing.
        var plainString =
            Between(Literals.Char('"'), Literals.NoneOf("\"\r\n", minSize: 0), Literals.Char('"'))
                .Or(Between(Literals.Char('\''), Literals.NoneOf("'\r\n", minSize: 0), Literals.Char('\'')))
                .Then(static _ => EntityCodes.Decode(_.ToString()));

        // A markdown string may run over several lines. It keeps its backticks, which is how the renderer
        // tells it from plain text.
        var markdownString =
            Literals.Text("\"`")
                .SkipAnd(AnyCharBefore(Literals.Text("`\""), canBeEmpty: true))
                .AndSkip(Literals.Text("`\""))
                .Then(static _ => $"`{EntityCodes.Decode(_.ToString())}`");

        var quotedLabel = markdownString.Or(plainString);
        var label = quotedLabel.Or(new UnquotedLabel());

        // `@{ type: hollow, business: true }`, its properties separated by commas or line breaks.
        var metadata =
            Literals.Text("@{")
                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                .AndSkip(Literals.Char('}'))
                .Then(static _ => ParseMetadata(_.ToString()));

        var stereotype =
            Literals.Text("<<")
                .SkipAnd(Literals.NoneOf(">\r\n"))
                .AndSkip(Literals.Text(">>"))
                .Then(static _ => EntityCodes.Decode(_.Span.Trim().ToString()));

        var className =
            Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ is '_' or '-').AsString();

        var classNames =
            Separated(whitespace.SkipAnd(Literals.Char(',')).AndSkip(whitespace), className)
                .Then(static _ => _.ToList());

        var classSuffix =
            Literals.Text(":::").SkipAnd(classNames);

        // The suffixes come in a fixed order: metadata, then a stereotype, then classes.
        var suffixes =
            whitespace.SkipAnd(metadata).Optional()
                .And(whitespace.SkipAnd(stereotype).Optional())
                .And(whitespace.SkipAnd(classSuffix).Optional())
                .Then(static _ => new Suffixes(_.Item1.OrNull(), _.Item2.OrNull(), _.Item3.OrNull() ?? []));

        // An actor's label goes in parentheses only.
        var actorLabel =
            whitespace.SkipAnd(Literals.Char('('))
                .SkipAnd(whitespace)
                .SkipAnd(label)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char(')'));

        var actorName =
            identifier.And(actorLabel.Optional())
                .Then(static _ => new NamePart(_.Item1, _.Item2.OrNull(), null))
                .Or(quotedLabel.Then(static _ => new NamePart(IdFromLabel(_), _, null)));

        var actorItem =
            actorName.And(suffixes)
                .Then(static _ => new NodeReference(_.Item1, _.Item2));

        // A use case's brackets choose its shape: parentheses an ellipse, square brackets a rectangle.
        var bracketLabel =
            whitespace.SkipAnd(
                Literals.Char('(')
                    .SkipAnd(whitespace)
                    .SkipAnd(label)
                    .AndSkip(whitespace)
                    .AndSkip(Literals.Char(')'))
                    .Then(static _ => (label: _, shape: UseCaseShape.Ellipse))
                    .Or(
                        Literals.Char('[')
                            .SkipAnd(whitespace)
                            .SkipAnd(label)
                            .AndSkip(whitespace)
                            .AndSkip(Literals.Char(']'))
                            .Then(static _ => (label: _, shape: UseCaseShape.Rectangle))));

        var entityName =
            identifier.And(bracketLabel.Optional())
                .Then(static _ => _.Item2.HasValue
                    ? new(_.Item1, _.Item2.Value.label, _.Item2.Value.shape)
                    : new NamePart(_.Item1, null, null))
                .Or(quotedLabel.Then(static _ => new NamePart(IdFromLabel(_), _, UseCaseShape.Ellipse)));

        var entityItem =
            entityName.And(suffixes)
                .Then(static _ => new NodeReference(_.Item1, _.Item2));

        var arrow = BuildArrow(whitespace, quotedLabel);

        // `id@` in front of the operator names the edge so it can be styled.
        var edgeId =
            identifier.AndSkip(Literals.Char('@'));

        var relationTail =
            whitespace.SkipAnd(edgeId.Optional())
                .And(arrow)
                .AndSkip(whitespace)
                .And(entityItem)
                .Then(static _ => new RelationTail(_.Item1.OrNull(), _.Item2, _.Item3));

        var lineEnd =
            whitespace.SkipAnd(CommonParsers.LineEnd);

        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsTrimmedString();

        var accTitle =
            Keyword("accTitle")
                .SkipAnd(whitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IStatement>(static _ => new AccessibilityTitle(_));

        var accDescrBlock =
            Literals.Char('{')
                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                .AndSkip(Literals.Char('}'))
                .Then(static _ => string.Join('\n', _.ToString().Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0)));

        var accDescr =
            Keyword("accDescr")
                .SkipAnd(whitespace)
                .SkipAnd(
                    Literals.Char(':').SkipAnd(restOfLine)
                        .Or(accDescrBlock.AndSkip(whitespace)))
                .AndSkip(CommonParsers.LineEnd)
                .Then<IStatement>(static _ => new AccessibilityDescription(_));

        var direction =
            Keyword("direction")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(CommonParsers.DirectionParser)
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new DirectionStatement(_));

        // `actor A, B` declares several actors; `actor A --> B` declares one and links it.
        var moreActors =
            whitespace.SkipAnd(Literals.Char(','))
                .SkipAnd(whitespace)
                .SkipAnd(actorItem)
                .Many();

        var actorDeclarationOnly =
            Keyword("actor")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(actorItem)
                .And(moreActors)
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new ActorStatement([_.Item1, .._.Item2], null));

        var actorWithRelation =
            Keyword("actor")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(actorItem)
                .And(relationTail)
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new ActorStatement([_.Item1], _.Item2));

        var entityStatement =
            entityItem.And(relationTail.Optional())
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new EntityStatement(_.Item1, _.Item2.OrNull()));

        var entityDeclarationOnly =
            entityItem.AndSkip(lineEnd)
                .Then<IStatement>(static _ => new EntityStatement(_, null));

        var skipLine =
            whitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline))
                .Then<IStatement?>(static _ => null);

        var boundaryEnd =
            whitespace.SkipAnd(Keyword("end")).AndSkip(lineEnd);

        // A boundary holds actor and use case declarations only.
        var boundaryMember =
            skipLine
                .Or(Not(boundaryEnd).SkipAnd(whitespace).SkipAnd(actorDeclarationOnly.Or(entityDeclarationOnly)).Then<IStatement?>(static _ => _));

        // The title can go in either kind of bracket; unlike a use case's, they choose no shape.
        var boundaryName =
            identifier.And(bracketLabel.Optional())
                .Then(static _ => new NamePart(_.Item1, _.Item2.HasValue ? _.Item2.Value.label : null, null))
                .Or(quotedLabel.Then(static _ => new NamePart(IdFromLabel(_), _, null)));

        var systemBoundary =
            Keyword("systemBoundary")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(boundaryName)
                .And(whitespace.SkipAnd(metadata).Optional())
                .And(whitespace.SkipAnd(classSuffix).Optional())
                .AndSkip(lineEnd)
                .And(boundaryMember.Many())
                .AndSkip(boundaryEnd)
                .Then<IStatement>(static _ => new BoundaryStatement(
                    _.Item1,
                    _.Item2.OrNull(),
                    _.Item3.OrNull() ?? [],
                    _.Item4.Where(static member => member is not null).Select(static member => member!).ToList()));

        var note =
            Keyword("note")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(Keyword("for"))
                .SkipAnd(requiredWhitespace)
                .SkipAnd(identifier)
                .AndSkip(requiredWhitespace)
                .And(label)
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new NoteStatement(_.Item1, _.Item2));

        var json =
            Keyword("json")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(identifier)
                .AndSkip(whitespace)
                .AndSkip(Literals.Char('@'))
                .AndSkip(whitespace)
                .And(new JsonObjectText().Then(static _ => FlattenJson(_)).Assert(static _ => _ is not null, "Expected a JSON object"))
                .And(whitespace.SkipAnd(classSuffix).Optional())
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new JsonStatement(_.Item1, _.Item2!, _.Item3.OrNull() ?? []));

        var identifiers =
            Separated(whitespace.SkipAnd(Literals.Char(',')).AndSkip(whitespace), identifier)
                .Then(static _ => _.ToList());

        var classDef =
            Keyword("classDef")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(classNames)
                .AndSkip(requiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IStatement>(static _ => new ClassDefStatement(_.Item1, ParseStyle(_.Item2)));

        var classStatement =
            Keyword("class")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(identifiers)
                .AndSkip(requiredWhitespace)
                .And(classNames)
                .AndSkip(lineEnd)
                .Then<IStatement>(static _ => new ClassStatement(_.Item1, _.Item2));

        var style =
            Keyword("style")
                .SkipAnd(requiredWhitespace)
                .SkipAnd(identifier)
                .AndSkip(requiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IStatement>(static _ => new StyleStatement(_.Item1, ParseStyle(_.Item2)));

        // A keyword statement that does not parse as one may still be a use case of the same name
        // (`note --> Archive`), so the entity statement is tried last rather than ruled out.
        var statement =
            whitespace.SkipAnd(
                OneOf(
                    accTitle,
                    accDescr,
                    direction,
                    actorDeclarationOnly,
                    actorWithRelation,
                    systemBoundary,
                    note,
                    json,
                    classDef,
                    classStatement,
                    style,
                    entityStatement));

        var line =
            skipLine.Or(statement.Then<IStatement?>(static _ => _));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("usecase-beta"))
                .SkipAnd(lineEnd)
                .SkipAnd(line.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => new ModelBuilder().Build(_.Where(static statement => statement is not null).Select(static statement => statement!)));
    }

    /// <summary>
    /// The relationship operators. The fixed ones come before the runs of dashes they start with, and
    /// a labelled association is only tried after an operator of exactly two dashes, which is where
    /// Mermaid allows the label to go: <c>A -- label --&gt; B</c>, <c>A &lt;-- label -- B</c>.
    /// </summary>
    static Parser<Arrow> BuildArrow(Parser<TextSpan> whitespace, Parser<string> quotedLabel)
    {
        var wordCharacter = Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ == '_');
        var dashes = Literals.AnyOf("-", minSize: 2).Then(static _ => _.Length);

        var semantic =
            Literals.Text("..>")
                .SkipAnd(whitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(whitespace)
                .SkipAnd(
                    TextIgnoreCase("include").Then(UseCaseRelationshipType.Include)
                        .Or(TextIgnoreCase("extend").Then(UseCaseRelationshipType.Extend)))
                .AndSkip(Not(wordCharacter))
                .Then(static _ => new Arrow(_, UseCaseMarker.None, UseCaseMarker.Arrow, null, 1));

        var generalization =
            Literals.Text("--|>")
                .Then(static _ => new Arrow(UseCaseRelationshipType.Generalization, UseCaseMarker.None, UseCaseMarker.Extension, null, 1));

        var forwardCircle =
            Literals.Text("--o").AndSkip(Not(wordCharacter))
                .Then(static _ => Association(UseCaseMarker.None, UseCaseMarker.Circle, 1));

        var forwardCross =
            Literals.Text("--x").AndSkip(Not(wordCharacter))
                .Then(static _ => Association(UseCaseMarker.None, UseCaseMarker.Cross, 1));

        var forwardSolid =
            dashes.AndSkip(Literals.Char('>'))
                .Then(static _ => Association(UseCaseMarker.None, UseCaseMarker.Arrow, _ - 1));

        var markerless =
            dashes.AndSkip(Not(Literals.Char('>')))
                .Then(static _ => Association(UseCaseMarker.None, UseCaseMarker.None, _ - 1));

        var edgeLabel =
            whitespace.SkipAnd(quotedLabel.Or(new UnquotedLabel())).AndSkip(whitespace);

        // `-- label` then the closing operator, which says which marker the target end gets.
        var forwardLabelled =
            Literals.Text("--").AndSkip(Not(Literals.AnyOf("->", minSize: 1, maxSize: 1)))
                .SkipAnd(edgeLabel)
                .And(OneOf(forwardCircle, forwardCross, forwardSolid, markerless))
                .Then(static _ => _.Item2 with { Label = _.Item1 });

        // A reversed operator takes its label before a closing run of plain dashes.
        static Parser<Arrow> Reversed(Parser<string> opening, UseCaseMarker marker, Parser<string> edgeLabel, Parser<int> dashes) =>
            opening
                .SkipAnd(
                    edgeLabel.And(dashes.AndSkip(Not(Literals.AnyOf(">ox", minSize: 1, maxSize: 1))))
                        .Then(static _ => (label: (string?) _.Item1, length: _.Item2 - 1))
                        .Optional())
                .Then(_ => _.HasValue
                    ? Association(marker, UseCaseMarker.None, _.Value.length) with { Label = _.Value.label }
                    : Association(marker, UseCaseMarker.None, 1));

        var backwardSolid =
            Literals.Char('<').SkipAnd(dashes)
                .When(static (_, length) => length > 2)
                .Then(static _ => Association(UseCaseMarker.Arrow, UseCaseMarker.None, _ - 1))
                .Or(Reversed(Literals.Text("<--"), UseCaseMarker.Arrow, edgeLabel, dashes));

        var backwardCircle = Reversed(Literals.Text("o--"), UseCaseMarker.Circle, edgeLabel, dashes);
        var backwardCross = Reversed(Literals.Text("x--"), UseCaseMarker.Cross, edgeLabel, dashes);

        return OneOf(
            semantic,
            generalization,
            forwardCircle,
            forwardCross,
            forwardSolid,
            backwardSolid,
            backwardCircle,
            backwardCross,
            forwardLabelled,
            markerless);
    }

    static Arrow Association(UseCaseMarker start, UseCaseMarker end, int minLength) =>
        new(UseCaseRelationshipType.Association, start, end, null, minLength);

    /// <summary>
    /// A quoted declaration with no identifier of its own gets one from its label, each character that
    /// cannot be in an identifier becoming <c>_</c>: <c>"Reset password"</c> is <c>Reset_password</c>. Letters
    /// and digits of any script are kept, so two labels in another script do not both come out as the same
    /// run of underscores and merge into one element.
    /// </summary>
    static string IdFromLabel(string label)
    {
        var text = MarkdownLabel.IsMarkdown(label) ? label.Trim()[1..^1] : label;
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.ToString();
    }

    // The properties of a metadata block: `key: value` pairs, split on commas and line breaks.
    static Dictionary<string, string> ParseMetadata(string text)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            properties[Unquote(part[..colon].Trim())] = Unquote(part[(colon + 1)..].Trim());
        }

        return properties;
    }

    static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            value[0] == value[^1] &&
            value[0] is '"' or '\'')
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>
    /// Comma-separated <c>property:value</c> declarations. <c>\,</c> is a comma inside a value. Only the
    /// properties the renderer draws with are kept.
    /// </summary>
    static NodeStyle ParseStyle(string text)
    {
        var style = new NodeStyle();
        var parts = text.Replace("\\,", "\u0001").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim().Replace('\u0001', ',');
            if (value.Length == 0)
            {
                continue;
            }

            switch (key)
            {
                case "fill":
                    style.Fill = value;
                    break;
                case "stroke":
                    style.Stroke = value;
                    break;
                case "stroke-width":
                    var number = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
                    if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                    {
                        style.StrokeWidth = width;
                    }

                    break;
                case "color":
                    style.Color = value;
                    break;
                case "stroke-dasharray":
                    style.StrokeDasharray = value;
                    break;
            }
        }

        return style;
    }

    /// <summary>
    /// Flattens a JSON object into rows keyed by each leaf's path. Properties keep their source order; a
    /// repeated property keeps its first place but takes its last value. An empty object or array is a
    /// row of its own. Null when the text is not a JSON object.
    /// </summary>
    static List<JsonRow>? FlattenJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var rows = new List<JsonRow>();
            AddRows(rows, document.RootElement, "");
            return rows;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static void AddRows(List<JsonRow> rows, JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new List<(string name, JsonElement value)>();
                var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (indexes.TryGetValue(property.Name, out var index))
                    {
                        properties[index] = (property.Name, property.Value);
                        continue;
                    }

                    indexes[property.Name] = properties.Count;
                    properties.Add((property.Name, property.Value));
                }

                if (properties.Count == 0 &&
                    path.Length > 0)
                {
                    rows.Add(new(path, "{}"));
                }

                foreach (var (name, value) in properties)
                {
                    AddRows(rows, value, path.Length == 0 ? name : $"{path}.{name}");
                }

                break;
            case JsonValueKind.Array:
                // As Mermaid lists them, an object or array in an array is keyed by its index (`items[0].name`),
                // while plain values are a list under the array's own key: the first row carries the key and
                // the rest leave it blank.
                var count = 0;
                var keyShown = false;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        AddRows(rows, item, $"{path}[{count.ToString(CultureInfo.InvariantCulture)}]");
                    }
                    else
                    {
                        AddRows(rows, item, keyShown ? "" : path);
                        keyShown = true;
                    }

                    count++;
                }

                if (count == 0)
                {
                    rows.Add(new(path, "[]"));
                }

                break;
            case JsonValueKind.String:
                rows.Add(new(path, element.GetString() ?? ""));
                break;
            default:
                // Numbers, booleans and null keep their JSON spelling.
                rows.Add(new(path, element.GetRawText()));
                break;
        }
    }

    public DiagramParseResult<UseCaseModel> Parse(string input) => parser.ParseDiagram(input);

    /// <summary>
    /// Text that is not quoted, as a label in brackets or on an edge holds it: it runs to the end of the
    /// line or to the first delimiter (<c>( ) [ ] { } " '</c>) or operator (<c>--</c>, <c>..&gt;</c>,
    /// <c>:::</c>, <c>@{</c>, <c>&lt;&lt;</c>), and its surrounding spaces are dropped. Empty text is no
    /// label.
    /// </summary>
    sealed class UnquotedLabel : Parser<string>
    {
        static readonly string[] reservedSequences = ["--", "..>", ":::", "@{", "<<"];

        public override bool Parse(ParseContext context, ref ParseResult<string> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Offset;
            var span = cursor.Span;
            var length = 0;
            while (length < span.Length)
            {
                var character = span[length];
                if (character is '\r' or '\n' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '\'' ||
                    StartsReserved(span[length..]))
                {
                    break;
                }

                length++;
            }

            var text = span[..length].Trim();
            if (text.IsEmpty)
            {
                return false;
            }

            cursor.Advance(length);
            result.Set(start, start + length, EntityCodes.Decode(text.ToString()));
            return true;
        }

        static bool StartsReserved(CharSpan text)
        {
            foreach (var sequence in reservedSequences)
            {
                if (text.StartsWith(sequence, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A JSON object, from its opening brace to the brace that closes it, across lines. Braces inside
    /// strings are not counted. Whether it is valid JSON is left to the JSON parser.
    /// </summary>
    sealed class JsonObjectText : Parser<string>
    {
        public override bool Parse(ParseContext context, ref ParseResult<string> result)
        {
            var cursor = context.Scanner.Cursor;
            var span = cursor.Span;
            if (span.IsEmpty ||
                span[0] != '{')
            {
                return false;
            }

            var depth = 0;
            var inString = false;
            for (var i = 0; i < span.Length; i++)
            {
                var character = span[i];
                if (inString)
                {
                    if (character == '\\')
                    {
                        i++;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                    case '[':
                        depth++;
                        break;
                    case '}':
                    case ']':
                        depth--;
                        if (depth == 0)
                        {
                            var start = cursor.Offset;
                            var text = span[..(i + 1)].ToString();
                            cursor.Advance(i + 1);
                            result.Set(start, start + i + 1, text);
                            return true;
                        }

                        break;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Turns the statements into the model. Declarations may come after the relationships that name
    /// them, so the kind, label and shape of each element are settled only once every statement has
    /// been read, and an endpoint never declared at all becomes an ellipse use case.
    /// </summary>
    sealed class ModelBuilder
    {
        UseCaseModel model = new();
        Dictionary<string, UseCaseActor> actors = new(StringComparer.Ordinal);
        Dictionary<string, UseCase> useCases = new(StringComparer.Ordinal);
        Dictionary<string, SystemBoundary> boundaries = new(StringComparer.Ordinal);
        Dictionary<string, JsonTable> jsonTables = new(StringComparer.Ordinal);
        Dictionary<string, UseCaseRelationship> namedEdges = new(StringComparer.Ordinal);
        Dictionary<string, NodeStyle> classDefinitions = new(StringComparer.Ordinal);
        Dictionary<string, NodeStyle> directStyles = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> assignedClasses = new(StringComparer.Ordinal);
        List<(NodeReference reference, string? boundaryId)> entities = [];
        List<string> referenced = [];
        HashSet<string> referencedIds = new(StringComparer.Ordinal);
        HashSet<string> labelled = new(StringComparer.Ordinal);
        List<(string id, Dictionary<string, string> properties)> metadataStatements = [];

        public UseCaseModel Build(IEnumerable<IStatement> statements)
        {
            var relations = new List<(string source, RelationTail tail)>();
            var notes = new List<NoteStatement>();

            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case AccessibilityTitle title:
                        model.AccessibilityTitle = title.Text;
                        break;
                    case AccessibilityDescription description:
                        model.AccessibilityDescription = description.Text;
                        break;
                    case DirectionStatement direction:
                        model.Direction = direction.Direction;
                        break;
                    case ActorStatement actorStatement:
                        foreach (var item in actorStatement.Items)
                        {
                            DeclareActor(item, null);
                        }

                        if (actorStatement.Relation is { } actorRelation)
                        {
                            relations.Add((actorStatement.Items[0].Name.Id, actorRelation));
                        }

                        break;
                    case EntityStatement entity:
                        DeclareEntity(entity.Node, null, entity.Relation is null);
                        if (entity.Relation is { } relation)
                        {
                            relations.Add((entity.Node.Name.Id, relation));
                        }

                        break;
                    case BoundaryStatement boundaryStatement:
                        DeclareBoundary(boundaryStatement);
                        break;
                    case NoteStatement note:
                        notes.Add(note);
                        Reference(note.TargetId);
                        break;
                    case JsonStatement json:
                        var table = new JsonTable { Id = json.Id };
                        table.Rows.AddRange(json.Rows);
                        table.Classes.AddRange(json.Classes);
                        jsonTables[json.Id] = table;
                        model.JsonTables.Add(table);
                        break;
                    case ClassDefStatement classDef:
                        foreach (var name in classDef.Names)
                        {
                            classDefinitions[name] = classDefinitions.TryGetValue(name, out var existing)
                                ? existing.MergedWith(classDef.Style)
                                : classDef.Style;
                        }

                        break;
                    case ClassStatement classStatement:
                        foreach (var id in classStatement.Ids)
                        {
                            if (!assignedClasses.TryGetValue(id, out var list))
                            {
                                list = [];
                                assignedClasses[id] = list;
                            }

                            list.AddRange(classStatement.Classes);
                        }

                        break;
                    case StyleStatement style:
                        directStyles[style.Id] = directStyles.TryGetValue(style.Id, out var previous)
                            ? previous.MergedWith(style.Style)
                            : style.Style;
                        break;
                }
            }

            ResolveEntities();

            foreach (var (source, tail) in relations)
            {
                AddRelationship(source, tail);
            }

            // Endpoints and note targets nobody declared are use cases, in the order they were first named.
            foreach (var id in referenced)
            {
                if (!IsDeclared(id))
                {
                    AddUseCase(id, id, UseCaseShape.Ellipse, null);
                }
            }

            ApplyMetadata();

            var noteCount = 0;
            foreach (var note in notes)
            {
                model.Notes.Add(
                    new()
                    {
                        Id = $"note-{(noteCount++).ToString(CultureInfo.InvariantCulture)}",
                        TargetId = note.TargetId,
                        Text = note.Text
                    });
            }

            ResolveStyles();
            return model;
        }

        void DeclareActor(NodeReference item, string? boundaryId)
        {
            var name = item.Name;
            if (!actors.TryGetValue(name.Id, out var actor))
            {
                actor = new()
                {
                    Id = name.Id,
                    Label = name.Label ?? name.Id
                };
                actors[name.Id] = actor;
                model.Actors.Add(actor);
            }
            else if (name.Label is not null)
            {
                actor.Label = name.Label;
            }

            ApplySuffixes(name.Id, item.Suffixes, actor.Classes, _ => actor.Stereotype = _);
            actor.BoundaryId ??= boundaryId;
        }

        // An entity is held back until every statement is read: `actor A` further down still makes `A`
        // an actor, and a bare `A@{ type: package }` may be aimed at a boundary or an edge.
        void DeclareEntity(NodeReference reference, string? boundaryId, bool declarationOnly)
        {
            if (declarationOnly &&
                reference is
                {
                    Name:
                    {
                        Label: null,
                        Shape: null
                    },
                    Suffixes:
                    {
                        Metadata: { } properties,
                        Stereotype: null,
                        Classes.Count: 0
                    }
                })
            {
                metadataStatements.Add((reference.Name.Id, properties));
                Reference(reference.Name.Id);
                return;
            }

            entities.Add((reference, boundaryId));
            Reference(reference.Name.Id);
        }

        void DeclareBoundary(BoundaryStatement statement)
        {
            // A boundary opened again under the same id adds to the first: its members join, and a title it
            // gives replaces the earlier one. Two boundaries under one id would be one box in the layout.
            if (boundaries.TryGetValue(statement.Name.Id, out var boundary))
            {
                if (statement.Name.Label is not null)
                {
                    boundary.Title = statement.Name.Label;
                }
            }
            else
            {
                boundary = new()
                {
                    Id = statement.Name.Id,
                    Title = statement.Name.Label ?? statement.Name.Id
                };
                boundaries[boundary.Id] = boundary;
                model.Boundaries.Add(boundary);
            }

            boundary.Classes.AddRange(statement.Classes);
            if (statement.Metadata is not null)
            {
                metadataStatements.Insert(0, (boundary.Id, statement.Metadata));
            }

            foreach (var member in statement.Members)
            {
                switch (member)
                {
                    case ActorStatement actorStatement:
                        foreach (var item in actorStatement.Items)
                        {
                            DeclareActor(item, boundary.Id);
                            AddMember(boundary, item.Name.Id);
                        }

                        break;
                    case EntityStatement entity:
                        entities.Add((entity.Node, boundary.Id));
                        Reference(entity.Node.Name.Id);
                        AddMember(boundary, entity.Node.Name.Id);
                        break;
                }
            }
        }

        // An element listed in two boundaries stays in the first, which is also the one its BoundaryId
        // names. Sitting in two sibling boxes at once, it pulled them over each other.
        void AddMember(SystemBoundary boundary, string id)
        {
            if (!model.Boundaries.Exists(_ => _.MemberIds.Contains(id)))
            {
                boundary.MemberIds.Add(id);
            }
        }

        void ResolveEntities()
        {
            foreach (var (reference, boundaryId) in entities)
            {
                Resolve(reference, boundaryId);
            }
        }

        // Settles what a named element is. Named again without `actor`, an actor is still an actor, and
        // whatever else the line says about it still counts. Anything that is not an actor, boundary or
        // JSON table is a use case, whose first explicit label and shape win wherever they are written.
        void Resolve(NodeReference reference, string? boundaryId)
        {
            var name = reference.Name;
            if (actors.TryGetValue(name.Id, out var actor))
            {
                ApplySuffixes(name.Id, reference.Suffixes, actor.Classes, _ => actor.Stereotype = _);
                actor.BoundaryId ??= boundaryId;
                return;
            }

            if (boundaries.ContainsKey(name.Id) ||
                jsonTables.ContainsKey(name.Id))
            {
                return;
            }

            if (!useCases.TryGetValue(name.Id, out var useCase))
            {
                useCase = AddUseCase(name.Id, name.Id, UseCaseShape.Ellipse, boundaryId);
            }

            if (name.Label is not null &&
                labelled.Add(name.Id))
            {
                useCase.Label = name.Label;
                useCase.Shape = name.Shape ?? UseCaseShape.Ellipse;
            }

            useCase.BoundaryId ??= boundaryId;
            ApplySuffixes(name.Id, reference.Suffixes, useCase.Classes, _ => useCase.Stereotype = _);
        }

        UseCase AddUseCase(string id, string label, UseCaseShape shape, string? boundaryId)
        {
            var useCase = new UseCase
            {
                Id = id,
                Label = label,
                Shape = shape,
                BoundaryId = boundaryId
            };
            useCases[id] = useCase;
            model.UseCases.Add(useCase);
            return useCase;
        }

        void ApplySuffixes(string id, Suffixes suffixes, List<string> classes, Action<string> setStereotype)
        {
            if (suffixes.Metadata is not null)
            {
                metadataStatements.Insert(0, (id, suffixes.Metadata));
            }

            if (suffixes.Stereotype is not null)
            {
                setStereotype(suffixes.Stereotype);
            }

            foreach (var name in suffixes.Classes)
            {
                if (!classes.Contains(name))
                {
                    classes.Add(name);
                }
            }
        }

        void AddRelationship(string source, RelationTail tail)
        {
            // The target of a relationship can carry a declaration of its own (`A --> B("Label")`).
            Resolve(tail.Target, null);
            var arrow = tail.Arrow;
            var relationship = new UseCaseRelationship
            {
                Id = tail.EdgeId,
                SourceId = source,
                TargetId = tail.Target.Name.Id,
                Type = arrow.Type,
                StartMarker = arrow.Start,
                EndMarker = arrow.End,
                Label = arrow.Label,
                MinLength = Math.Max(1, arrow.MinLength)
            };
            model.Relationships.Add(relationship);
            if (tail.EdgeId is not null)
            {
                namedEdges[tail.EdgeId] = relationship;
            }
        }

        void Reference(string id)
        {
            if (referencedIds.Add(id))
            {
                referenced.Add(id);
            }
        }

        bool IsDeclared(string id) =>
            actors.ContainsKey(id) ||
            useCases.ContainsKey(id) ||
            boundaries.ContainsKey(id) ||
            jsonTables.ContainsKey(id) ||
            namedEdges.ContainsKey(id);

        // Inline metadata goes first and the statements after it in document order, so a later
        // statement overrides inline metadata for the same key.
        void ApplyMetadata()
        {
            foreach (var (id, properties) in metadataStatements)
            {
                if (actors.TryGetValue(id, out var actor))
                {
                    ApplyActorMetadata(actor, properties);
                }
                else if (useCases.TryGetValue(id, out var useCase))
                {
                    if (properties.TryGetValue("business", out var business))
                    {
                        useCase.Business = business == "true";
                    }
                }
                else if (boundaries.TryGetValue(id, out var boundary) &&
                         properties.TryGetValue("type", out var type))
                {
                    boundary.Type = type == "package" ? BoundaryType.Package : BoundaryType.Rect;
                }

                // An edge's metadata only sets its animation, which a static picture has no use for.
            }
        }

        static void ApplyActorMetadata(UseCaseActor actor, Dictionary<string, string> properties)
        {
            if (properties.TryGetValue("type", out var type))
            {
                actor.Type = type switch
                {
                    "hollow" => ActorType.Hollow,
                    "awesome" => ActorType.Awesome,
                    _ => ActorType.Normal
                };
            }

            if (properties.TryGetValue("icon", out var icon) &&
                icon.Length > 0)
            {
                actor.Icon = icon;
                actor.Type = ActorType.Icon;
            }

            if (properties.TryGetValue("business", out var business))
            {
                actor.Business = business == "true";
            }
        }

        // Theme first, then the `default` class, then the element's classes in the order they were given,
        // then its direct `style`; each layer wins over the ones before it, property by property.
        void ResolveStyles()
        {
            foreach (var actor in model.Actors)
            {
                actor.Style = ResolveStyle(actor.Id, actor.Classes);
            }

            foreach (var useCase in model.UseCases)
            {
                useCase.Style = ResolveStyle(useCase.Id, useCase.Classes);
            }

            foreach (var boundary in model.Boundaries)
            {
                boundary.Style = ResolveStyle(boundary.Id, boundary.Classes);
            }

            foreach (var table in model.JsonTables)
            {
                table.Style = ResolveStyle(table.Id, table.Classes);
            }

            foreach (var (id, relationship) in namedEdges)
            {
                relationship.Style = ResolveStyle(id, relationship.Classes);
            }
        }

        NodeStyle? ResolveStyle(string id, List<string> classes)
        {
            if (assignedClasses.TryGetValue(id, out var assigned))
            {
                foreach (var name in assigned)
                {
                    if (!classes.Contains(name))
                    {
                        classes.Add(name);
                    }
                }
            }

            var style = classDefinitions.GetValueOrDefault("default");
            foreach (var name in classes)
            {
                if (classDefinitions.TryGetValue(name, out var definition))
                {
                    style = style?.MergedWith(definition) ?? definition;
                }
            }

            if (directStyles.TryGetValue(id, out var direct))
            {
                style = style?.MergedWith(direct) ?? direct;
            }

            return style;
        }
    }

    interface IStatement;

    sealed record AccessibilityTitle(string Text) : IStatement;

    sealed record AccessibilityDescription(string Text) : IStatement;

    sealed record DirectionStatement(Direction Direction) : IStatement;

    sealed record ActorStatement(List<NodeReference> Items, RelationTail? Relation) : IStatement;

    sealed record EntityStatement(NodeReference Node, RelationTail? Relation) : IStatement;

    sealed record BoundaryStatement(
        NamePart Name,
        Dictionary<string, string>? Metadata,
        List<string> Classes,
        List<IStatement> Members) : IStatement;

    sealed record NoteStatement(string TargetId, string Text) : IStatement;

    sealed record JsonStatement(string Id, List<JsonRow> Rows, List<string> Classes) : IStatement;

    sealed record ClassDefStatement(List<string> Names, NodeStyle Style) : IStatement;

    sealed record ClassStatement(List<string> Ids, List<string> Classes) : IStatement;

    sealed record StyleStatement(string Id, NodeStyle Style) : IStatement;

    // How an element is named: its identifier, and the label and shape its brackets give it, if any.
    sealed record NamePart(string Id, string? Label, UseCaseShape? Shape);

    sealed record Suffixes(Dictionary<string, string>? Metadata, string? Stereotype, List<string> Classes);

    sealed record NodeReference(NamePart Name, Suffixes Suffixes);

    sealed record RelationTail(string? EdgeId, Arrow Arrow, NodeReference Target);

    sealed record Arrow(
        UseCaseRelationshipType Type,
        UseCaseMarker Start,
        UseCaseMarker End,
        string? Label,
        int MinLength);
}
