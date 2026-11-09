// The `gitGraph` options Mermaid takes from front matter and %%{init}%% directives.
public class GitGraphConfigTests : TestBase
{
    // Mermaid's docs example: the main branch renamed, and checked out by its new name.
    [Test]
    public Task MainBranchName()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                mainBranchName: 'MetroLine1'
            ---
            gitGraph
              commit id:"NewYork"
              commit id:"Dallas"
              branch MetroLine2
              commit id:"LosAngeles"
              commit id:"Chicago"
              branch MetroLine3
              commit id:"Phoenix"
              commit type: HIGHLIGHT id:"Denver"
              checkout MetroLine1
              commit id:"Atlanta"
              merge MetroLine3
              commit id:"Miami"
              merge MetroLine2 tag:"MY JUNCTION"
              commit type:REVERSE id:"SanFrancisco"
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task MainBranchName_OldNameIsNoBranch()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                mainBranchName: trunk
            ---
            gitGraph
              commit
              checkout main
              commit
            """;

        await Assert.That(() => Mermaid.Render(input))
            .Throws<MermaidParseException>()
            .WithMessage("Failed to parse git graph: Trying to checkout branch which is not yet created. (Help try using \"branch main\") at line 8, col 12");
    }

    // Lanes follow `order:`; a branch given none keeps its place among the others given none, after the
    // main branch.
    [Test]
    public Task BranchOrder()
    {
        const string input =
            """
            gitGraph
              commit
              branch test1 order: 3
              branch test2 order: 2
              branch test3 order: 1
            """;

        return VerifySvg(input);
    }

    // Mermaid's docs example: main moved to order 2, behind the unordered branches and test4.
    [Test]
    public Task MainBranchOrder()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                mainBranchOrder: 2
            ---
            gitGraph
              commit
              branch test1 order: 3
              commit
              branch test2
              commit
              branch test3
              commit
              branch test4 order: 1
              commit
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HideBranches()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                showBranches: false
            ---
            gitGraph
              commit
              branch hotfix
              checkout hotfix
              commit
              branch develop
              checkout develop
              commit id:"ash" tag:"abc"
              checkout main
              merge hotfix
              checkout develop
              merge hotfix
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HideCommitLabels()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                showCommitLabel: false
            ---
            gitGraph
              commit
              branch develop
              commit id:"ash" tag:"abc"
              checkout main
              commit
              merge develop
            """;

        return VerifySvg(input);
    }

    // Captions run at 45 degrees by default, as in Mermaid; `rotateCommitLabel: false` lays them flat.
    [Test]
    public Task UprightCommitLabels()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                rotateCommitLabel: false
            ---
            gitGraph
              commit id: "a long commit id"
              branch develop
              commit id: "another long one"
              checkout main
              commit id: "short"
              merge develop
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RotatedCommitLabelsTopToBottom()
    {
        const string input =
            """
            gitGraph TB:
              commit id: "a long commit id"
              branch develop
              commit id: "another long one"
              checkout main
              commit id: "short"
              merge develop
            """;

        return VerifySvg(input);
    }

    // Mermaid's docs example: develop's commits line up with main's rather than coming after them.
    [Test]
    public Task ParallelCommits()
    {
        const string input =
            """
            ---
            config:
              gitGraph:
                parallelCommits: true
            ---
            gitGraph:
              commit
              branch develop
              commit
              commit
              checkout main
              commit
              commit
            """;

        return VerifySvg(input);
    }

    // An init directive is merged over the front matter: both options apply, and the directive wins
    // where they disagree.
    [Test]
    public async Task InitDirective_MergesOverFrontMatter()
    {
        const string merged =
            """
            ---
            config:
              gitGraph:
                showBranches: true
                showCommitLabel: false
            ---
            %%{init: { 'gitGraph': { 'showBranches': false } } }%%
            gitGraph
              commit
              branch develop
              commit
            """;

        const string expected =
            """
            ---
            config:
              gitGraph:
                showBranches: false
                showCommitLabel: false
            ---
            gitGraph
              commit
              branch develop
              commit
            """;

        await Assert.That(Mermaid.Render(merged)).IsEqualTo(Mermaid.Render(expected));
    }

    // With a theme, branches take its git0 to git7, and captions and tags its label colours.
    [Test]
    public Task ForestTheme() =>
        VerifySvg(Themed("forest"));

    [Test]
    public Task NeutralTheme() =>
        VerifySvg(Themed("neutral"));

    // Mermaid's docs example: branch colours set directly.
    [Test]
    public Task ThemeVariables()
    {
        const string input =
            """
            ---
            config:
              theme: 'default'
              themeVariables:
                'git0': '#ff0000'
                'git1': '#00ff00'
                'git2': '#0000ff'
                commitLabelColor: '#ff0000'
                commitLabelBackground: '#00ff00'
                tagLabelBackground: '#00ff00'
            ---
            gitGraph
              commit
              branch develop
              commit tag:"v1.0"
              branch feature
              commit
              checkout main
              merge develop
            """;

        return VerifySvg(input);
    }

    static string Themed(string theme) =>
        $$$"""
           %%{init: {"theme": "{{{theme}}}"}}%%
           gitGraph
             commit
             branch develop
             commit tag:"v1.0"
             branch feature
             commit
             checkout main
             merge develop
           """;
}
