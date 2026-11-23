/// <summary>
/// The input each snapshot test renders, read from the test's own source: the first string literal in a
/// <c>[Test]</c> method that calls <c>VerifySvg</c>, or the shared sample constant it passes. The docs
/// generator and the layout lint both work from this, so a snapshot test is in both as soon as it is
/// written, with no second copy of its input to keep in step.
/// </summary>
static class SnapshotInputs
{
    public record Snapshot(string ClassName, string MethodName, string? Input);

    // Every snapshot test in the file, in source order. The input is null when the test builds it in a
    // way that cannot be read from the source.
    public static List<Snapshot> Read(string testFile)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(testFile)).GetRoot();

        var methods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(_ => _.AttributeLists
                .SelectMany(_ => _.Attributes)
                .Any(_ => _.Name.ToString() == "Test"));

        var snapshots = new List<Snapshot>();
        foreach (var method in methods)
        {
            var body = method.Body?.ToString() ?? method.ExpressionBody?.ToString() ?? "";
            if (!body.Contains("VerifySvg"))
            {
                continue;
            }

            var className = method.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text ?? "";

            var input = ExtractInputString(method) ?? ResolveSharedSample(method, testFile);
            snapshots.Add(new(className, method.Identifier.Text, input));
        }

        return snapshots;
    }

    /// <summary>
    /// Resolves an input passed as a shared constant, e.g. <c>VerifySvg(StateSamples.Simple)</c>. Without
    /// this the test looks input-less, its whole category comes out empty, and the category's page is
    /// deleted by the rebuild.
    /// </summary>
    static string? ResolveSharedSample(MethodDeclarationSyntax method, string testFilePath)
    {
        var access = method.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(_ => _.Expression is IdentifierNameSyntax);

        if (access?.Expression is not IdentifierNameSyntax owner)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(testFilePath);
        if (directory is null)
        {
            return null;
        }

        var samplesPath = Path.Combine(directory, $"{owner.Identifier.Text}.cs");
        if (!File.Exists(samplesPath))
        {
            return null;
        }

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(samplesPath)).GetRoot();
        var wanted = access.Name.Identifier.Text;

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var declarator in field.Declaration.Variables)
            {
                if (declarator.Identifier.Text != wanted)
                {
                    continue;
                }

                if (declarator.Initializer?.Value is LiteralExpressionSyntax literal)
                {
                    return literal.Token.ValueText;
                }
            }
        }

        return null;
    }

    static string? ExtractInputString(MethodDeclarationSyntax method)
    {
        // Find raw string literals (""" ... """)
        var rawStrings = method.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(_ => _.IsKind(SyntaxKind.Utf8StringLiteralExpression) ||
                        _.Token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) ||
                        _.Token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            .ToList();

        if (rawStrings.Count > 0)
        {
            return rawStrings[0].Token.ValueText;
        }

        // Fall back to regular string literals
        var stringLiterals = method.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(_ => _.IsKind(SyntaxKind.StringLiteralExpression))
            .ToList();

        if (stringLiterals.Count > 0)
        {
            return stringLiterals[0].Token.ValueText;
        }

        // Try interpolated strings
        var interpolated = method.DescendantNodes()
            .OfType<InterpolatedStringExpressionSyntax>()
            .FirstOrDefault();

        return interpolated?
            .Contents
            .OfType<InterpolatedStringTextSyntax>()
            .Select(_ => _.TextToken.ValueText)
            .FirstOrDefault();
    }
}
