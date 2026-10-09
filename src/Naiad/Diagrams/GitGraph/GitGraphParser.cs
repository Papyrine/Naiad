class GitGraphParser : IDiagramParser<GitGraphModel>
{
    static Parser<char, GitGraphModel> parser;

    static GitGraphParser()
    {
        // Identifiers
        var branchName =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-' || _ == '/')
                .AtLeastOnceString()
                .Labelled("branch name");

        var commitId =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-')
                .AtLeastOnceString()
                .Labelled("commit id");

        var commitTypeParser =
            OneOf(
                Try(CIString("REVERSE")).ThenReturn(CommitType.Reverse),
                Try(CIString("HIGHLIGHT")).ThenReturn(CommitType.Highlight),
                CIString("NORMAL").ThenReturn(CommitType.Normal)
            );

        // Attribute parsers
        var idAttribute =
            from _ in Try(CIString("id"))
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from id in CommonParsers.QuotedString.Or(commitId)
            select id;

        var messageAttribute =
            from _ in Try(CIString("msg"))
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from msg in CommonParsers.QuotedString
            select msg;

        var tagAttribute =
            from _ in Try(CIString("tag"))
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from tag in CommonParsers.QuotedString
            select tag;

        var typeAttribute =
            from _ in Try(CIString("type"))
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from type in commitTypeParser
            select type;

        var orderAttribute =
            from _ in Try(CIString("order"))
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from order in CommonParsers.Integer
            select order;

        var parseCommitAttributes =
            OneOf(
                Try(from __ in CommonParsers.InlineWhitespace from a in idAttribute select (ICommitAttr)new IdAttr(a)),
                Try(from __ in CommonParsers.InlineWhitespace from a in messageAttribute select (ICommitAttr)new MsgAttr(a)),
                Try(from __ in CommonParsers.InlineWhitespace from a in tagAttribute select (ICommitAttr)new TagAttr(a)),
                Try(from __ in CommonParsers.InlineWhitespace from a in typeAttribute select (ICommitAttr)new TypeAttr(a))
            ).Many();

        // Commit: commit id: "abc" msg: "message" tag: "v1.0" type: NORMAL
        var commitParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("commit")
            from attrs in parseCommitAttributes
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            select CreateCommit(attrs);

        // Branch: branch develop order: 1
        var branchParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("branch")
            from ___ in CommonParsers.RequiredWhitespace
            from name in branchName
            from order in Try(
                from ____ in CommonParsers.InlineWhitespace
                from o in orderAttribute
                select o
            ).Optional()
            from _____ in CommonParsers.InlineWhitespace
            from ______ in CommonParsers.LineEnd
            select new BranchOperation
            {
                Name = name,
                BranchOrder = order.HasValue ? order.Value : null
            };

        // Checkout: checkout develop
        var checkoutParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("checkout")
            from ___ in CommonParsers.RequiredWhitespace
            from name in branchName
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new CheckoutOperation { BranchName = name };

        var parseMergeAttributes =
            OneOf(
                Try(from __ in CommonParsers.InlineWhitespace from a in idAttribute select (ICommitAttr)new IdAttr(a)),
                Try(from __ in CommonParsers.InlineWhitespace from a in tagAttribute select (ICommitAttr)new TagAttr(a)),
                Try(from __ in CommonParsers.InlineWhitespace from a in typeAttribute select (ICommitAttr)new TypeAttr(a))
            ).Many();

        // Merge: merge develop id: "merge1" tag: "v1.0" type: NORMAL
        var mergeParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("merge")
            from ___ in CommonParsers.RequiredWhitespace
            from name in branchName
            from attrs in parseMergeAttributes
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select CreateMerge(name, attrs);

        // Cherry-pick: cherry-pick id: "abc" tag: "v1.0"
        var cherryPickParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("cherry-pick")
            from ___ in CommonParsers.InlineWhitespace
            from id in idAttribute
            from tag in Try(
                from ____ in CommonParsers.InlineWhitespace
                from t in tagAttribute
                select t
            ).Optional()
            from _____ in CommonParsers.InlineWhitespace
            from ______ in CommonParsers.LineEnd
            select new CherryPickOperation
            {
                CommitId = id,
                Tag = tag.HasValue ? tag.Value : null
            };

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        // Main content parser
        var parseContent =
            OneOf(
                Try(commitParser.Select<GitOperation?>(_ => _)),
                Try(branchParser.Select<GitOperation?>(_ => _)),
                Try(checkoutParser.Select<GitOperation?>(_ => _)),
                Try(mergeParser.Select<GitOperation?>(_ => _)),
                Try(cherryPickParser.Select<GitOperation?>(_ => _)),
                skipLine.ThenReturn<GitOperation?>(null)
            ).Many()
            .Select(_ => _.Where(_ => _ != null).Cast<GitOperation>().ToList());

        // Options parser (gitGraph TB: or gitGraph LR:)
        var optionsParser =
            from _ in CommonParsers.InlineWhitespace
            from options in Try(
                from dir in OneOf(
                    Try(String("TB")).ThenReturn("TB"),
                    Try(String("BT")).ThenReturn("BT"),
                    Try(String("LR")).ThenReturn("LR"),
                    String("RL").ThenReturn("RL")
                ).Optional()
                from __ in CommonParsers.InlineWhitespace
                from ___ in Char(':').Optional()
                select (direction: dir.HasValue ? dir.Value : null, mainBranch: (string?)null)
            ).Optional()
            select options.HasValue ? options.Value : (direction: null, mainBranch: null);

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in CIString("gitGraph")
            from options in optionsParser
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from operations in parseContent
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(operations, options);
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

    public Result<char, GitGraphModel> Parse(string input) => parser.Parse(input);

    internal interface ICommitAttr;
    readonly record struct IdAttr(string Value) : ICommitAttr;
    readonly record struct MsgAttr(string Value) : ICommitAttr;
    readonly record struct TagAttr(string Value) : ICommitAttr;
    readonly record struct TypeAttr(CommitType Value) : ICommitAttr;
}
