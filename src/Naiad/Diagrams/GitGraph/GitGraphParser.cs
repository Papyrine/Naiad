class GitGraphParser : IDiagramParser<GitGraphModel>
{
    static Parser<GitGraphModel> parser;

    static GitGraphParser()
    {
        // Identifiers
        var branchName =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-' || _ == '/')
                .AsString();

        var commitId =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AsString();

        var commitTypeParser =
            OneOf(
                TextIgnoreCase("REVERSE").Then(CommitType.Reverse),
                TextIgnoreCase("HIGHLIGHT").Then(CommitType.Highlight),
                TextIgnoreCase("NORMAL").Then(CommitType.Normal)
            );

        // Attribute parsers
        var idAttribute =
            TextIgnoreCase("id")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.QuotedString.Or(commitId));

        var messageAttribute =
            TextIgnoreCase("msg")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.QuotedString);

        var tagAttribute =
            TextIgnoreCase("tag")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.QuotedString);

        var typeAttribute =
            TextIgnoreCase("type")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(commitTypeParser);

        var orderAttribute =
            TextIgnoreCase("order")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.Integer);

        var parseCommitAttributes =
            OneOf(
                CommonParsers.InlineWhitespace.SkipAnd(idAttribute).Then<ICommitAttr>(static _ => new IdAttr(_)),
                CommonParsers.InlineWhitespace.SkipAnd(messageAttribute).Then<ICommitAttr>(static _ => new MsgAttr(_)),
                CommonParsers.InlineWhitespace.SkipAnd(tagAttribute).Then<ICommitAttr>(static _ => new TagAttr(_)),
                CommonParsers.InlineWhitespace.SkipAnd(typeAttribute).Then<ICommitAttr>(static _ => new TypeAttr(_))
            ).Many();

        // Commit: commit id: "abc" msg: "message" tag: "v1.0" type: NORMAL
        var commitParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("commit"))
                .SkipAnd(parseCommitAttributes)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(CreateCommit);

        // Branch: branch develop order: 1
        var branchParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("branch"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(branchName)
                .And(CommonParsers.InlineWhitespace.SkipAnd(orderAttribute).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (name, order) =>
                    new BranchOperation
                    {
                        Name = name,
                        BranchOrder = order.ToNullable()
                    });

        // Checkout: checkout develop
        var checkoutParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("checkout"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(branchName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => new CheckoutOperation { BranchName = _ });

        var parseMergeAttributes =
            OneOf(
                CommonParsers.InlineWhitespace.SkipAnd(idAttribute).Then<ICommitAttr>(static _ => new IdAttr(_)),
                CommonParsers.InlineWhitespace.SkipAnd(tagAttribute).Then<ICommitAttr>(static _ => new TagAttr(_)),
                CommonParsers.InlineWhitespace.SkipAnd(typeAttribute).Then<ICommitAttr>(static _ => new TypeAttr(_))
            ).Many();

        // Merge: merge develop id: "merge1" tag: "v1.0" type: NORMAL
        var mergeParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("merge"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(branchName)
                .And(parseMergeAttributes)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(CreateMerge);

        // Cherry-pick: cherry-pick id: "abc" tag: "v1.0"
        var cherryPickParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("cherry-pick"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(idAttribute)
                .And(CommonParsers.InlineWhitespace.SkipAnd(tagAttribute).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, tag) =>
                    new CherryPickOperation
                    {
                        CommitId = id,
                        Tag = tag.OrNull()
                    });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Main content parser
        var parseContent =
            OneOf(
                commitParser.Then<GitOperation?>(static _ => _),
                branchParser.Then<GitOperation?>(static _ => _),
                checkoutParser.Then<GitOperation?>(static _ => _),
                mergeParser.Then<GitOperation?>(static _ => _),
                cherryPickParser.Then<GitOperation?>(static _ => _),
                skipLine.Then<GitOperation?>(static _ => null)
            ).Many()
            .Then(static _ => _.Where(static _ => _ != null).Cast<GitOperation>().ToList());

        // Options parser (gitGraph TB: or gitGraph LR:)
        var optionsParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Text("TB"),
                        Literals.Text("BT"),
                        Literals.Text("LR"),
                        Literals.Text("RL")
                    ).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':').Optional())
                .Then(static _ => (direction: _.OrNull(), mainBranch: (string?)null));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("gitGraph"))
                .SkipAnd(optionsParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .And(parseContent)
                // Nothing may be left over: without this a line no rule matches ends the list quietly and the
                // rest of the diagram is dropped instead of being reported.
                .AndSkip(CommonParsers.End)
                .Map(static (options, operations) => BuildModel(operations, options));
    }

    static CommitOperation CreateCommit(IEnumerable<ICommitAttr> attrs)
    {
        var commit = new CommitOperation();
        foreach (var attr in attrs)
        {
            switch (attr)
            {
                case IdAttr id: commit.Id = id.Value; break;
                case MsgAttr msg: commit.Message = msg.Value; break;
                case TagAttr tag: commit.Tag = tag.Value; break;
                case TypeAttr type: commit.Type = type.Value; break;
            }
        }
        return commit;
    }

    static MergeOperation CreateMerge(string name, IEnumerable<ICommitAttr> attrs)
    {
        var merge = new MergeOperation { BranchName = name };
        foreach (var attr in attrs)
        {
            switch (attr)
            {
                case IdAttr id: merge.Id = id.Value; break;
                case TagAttr tag: merge.Tag = tag.Value; break;
                case TypeAttr type: merge.Type = type.Value; break;
            }
        }
        return merge;
    }

    static GitGraphModel BuildModel(List<GitOperation> operations, (string? direction, string? mainBranch) options)
    {
        var model = new GitGraphModel();

        if (options.direction != null)
        {
            model.Direction = options.direction switch
            {
                "TB" or "TD" => Direction.TopToBottom,
                "BT" => Direction.BottomToTop,
                "LR" => Direction.LeftToRight,
                "RL" => Direction.RightToLeft,
                _ => Direction.LeftToRight
            };
        }
        else
        {
            model.Direction = Direction.LeftToRight; // Git graphs default to LR
        }

        if (options.mainBranch != null)
        {
            model.MainBranchName = options.mainBranch;
        }

        var order = 0;
        foreach (var op in operations)
        {
            op.Order = order++;
            model.Operations.Add(op);
        }

        return model;
    }

    public DiagramParseResult<GitGraphModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface ICommitAttr;
    readonly record struct IdAttr(string Value) : ICommitAttr;
    readonly record struct MsgAttr(string Value) : ICommitAttr;
    readonly record struct TagAttr(string Value) : ICommitAttr;
    readonly record struct TypeAttr(CommitType Value) : ICommitAttr;
}
