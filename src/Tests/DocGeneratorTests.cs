public class DocGeneratorTests
{
    [Test]
    [Explicit]
    public async Task Generate()
    {
        var testsDir = ProjectFiles.ProjectDirectory;
        var outputDir = Path.Combine(ProjectFiles.SolutionDirectory, "test-renders");
        Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        var testFiles = Directory.GetFiles(testsDir, "*Tests.cs", SearchOption.AllDirectories)
            .Where(_ => !_.Contains("DocumentationGenerator"))
            .OrderBy(_ => _);

        // Group tests by category
        var testsByCategory = new Dictionary<string, List<TestInfo>>();

        foreach (var testFile in testFiles)
        {
            var tests = ExtractTestsFromFile(testFile);
            if (tests.Count == 0)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(testsDir, testFile);
            var category = Path.GetDirectoryName(relativePath)?.Replace("\\", "/");

            // Diagram-type tests live in a per-type subfolder; the folder name is the category.
            // Root-level test files (e.g. AllowHtmlElementsTests) cover cross-cutting features
            // rather than a diagram type, so they don't belong in the renders index.
            if (string.IsNullOrEmpty(category))
            {
                continue;
            }

            if (!testsByCategory.TryGetValue(category, out var value))
            {
                value = [];
                testsByCategory[category] = value;
            }

            value.AddRange(tests);
        }

        // Determine which categories are beta (first test's first line ends with -beta)
        var betaCategories = new HashSet<string>();
        foreach (var (category, tests) in testsByCategory)
        {
            if (tests.Count > 0 && IsBeta(tests[0].Input))
            {
                betaCategories.Add(category);
            }
        }

        // Write one file per category
        foreach (var (category, tests) in testsByCategory)
        {
            var isBeta = betaCategories.Contains(category);
            var markdown = new StringBuilder();
            markdown.AppendLine($"# {category}");
            markdown.AppendLine();

            foreach (var test in tests)
            {
                markdown.AppendLine($"## {test.Name}");
                markdown.AppendLine();

                markdown.AppendLine("**Input:**");
                markdown.AppendLine("```");
                markdown.AppendLine(test.Input);
                markdown.AppendLine("```");

                var relativePngPath = Path
                    .GetRelativePath(outputDir, test.VerifiedPngPath)
                    .Replace("\\", "/");
                markdown.AppendLine("**Rendered by Naiad:**");
                markdown.AppendLine(
                    $"""

                     <p align="center">
                       <img src="{relativePngPath}" />
                     </p>

                     """);

                // Only render mermaid block for non-beta (GitHub doesn't support beta syntax)
                if (!isBeta)
                {
                    markdown.AppendLine("**Rendered by Mermaid:**");
                    markdown.AppendLine("```mermaid");
                    markdown.AppendLine(test.Input);
                    markdown.AppendLine("```");
                }

                markdown.AppendLine();
                markdown.AppendLine($"[Open in Mermaid Live]({GetMermaidLiveUrl(test.Input)})");
                markdown.AppendLine();
            }

            var outputPath = Path.Combine(outputDir, $"{category}.md");
            await WriteWithLfAsync(outputPath, markdown.ToString());
        }

        // Generate index page
        var index = new StringBuilder(
            """
            ## Test Renders

            Auto-generated documentation from the test suite.

            """);

        var stableCategories = testsByCategory.Keys.Where(_ => !betaCategories.Contains(_)).OrderBy(_ => _);
        var betaCategoriesSorted = betaCategories.OrderBy(_ => _);

        foreach (var category in stableCategories)
        {
            index.AppendLine($"- [{category}](/src/test-renders/{category}.md)");
        }

        if (betaCategories.Count > 0)
        {
            index.AppendLine(
                """

                ### Beta diagram types

                """);
            foreach (var category in betaCategoriesSorted)
            {
                index.AppendLine($"- [{category}]({category}.md)");
            }
        }

        var indexPath = Path.Combine(outputDir, "renders.include.md");
        await WriteWithLfAsync(indexPath, index.ToString());
    }

    static Task WriteWithLfAsync(string path, string content)
    {
        var lfContent = content.ReplaceLineEndings("\n");
        return File.WriteAllTextAsync(path, lfContent);
    }

    static bool IsBeta(string input)
    {
        var firstLine = input.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return firstLine.TrimEnd().EndsWith("-beta");
    }

    static List<TestInfo> ExtractTestsFromFile(string filePath) =>
        SnapshotInputs.Read(filePath)
            .Where(_ => !string.IsNullOrEmpty(_.Input))
            .Select(_ => new TestInfo
            {
                Name = _.MethodName,
                ClassName = _.ClassName,
                Input = _.Input!,
                VerifiedPngPath = FindVerifiedFile(filePath, _.ClassName, _.MethodName, ".verified.png")
            })
            .ToList();

    static string FindVerifiedFile(string testFile, string className, string testName, string extension)
    {
        var dir = Path.GetDirectoryName(testFile)!;

        var file = Path.Combine(dir, $"{className}.{testName}{extension}");
        if (File.Exists(file))
        {
            return file;
        }

        throw new($"Could not find: {file}");
    }

    static string GetMermaidLiveUrl(string code)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { code, mermaid = new { theme = "default" } });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return $"https://mermaid.live/edit#base64:{base64}";
    }

    private class TestInfo
    {
        public string Name { get; init; } = "";
        public string ClassName { get; set; } = "";
        public string Input { get; init; } = "";
        public required string VerifiedPngPath { get; init; }
    }
}
