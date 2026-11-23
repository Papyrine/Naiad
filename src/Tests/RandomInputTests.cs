// Every snapshot test's input, mutated at random: tokens of the grammars inserted, text deleted, lines dropped,
// doubled, swapped or cut short, characters no input should hold put in. A mutated input must either render to
// well-formed SVG with no NaN or infinite coordinate, or be turned down with a MermaidException. Anything else
// (another exception, or a render that does not finish) is a bug, reported with the input that found it.
//
// The seed is fixed so a run reproduces. NAIAD_FUZZ_CASES and NAIAD_FUZZ_SEED widen a run locally, and a
// failure is worth keeping as a case of its own once fixed.
public class RandomInputTests
{
    static readonly TimeSpan renderTimeout = TimeSpan.FromSeconds(10);

    static readonly string[] fragments =
    [
        "-->", "---", "==>", "-.->", "<-->", "--x", "--o", "->>", "-->>", "|", "||", "|o", "}|", ":", ":::", ";",
        ",", "\"", "'", "`", "[", "]", "(", ")", "{", "}", "((", "))", "{{", "}}", "[[", "]]", "[(", ")]", ">", "<",
        "<<", ">>", "&", "#", "%%", "%%{", "}%%", "\n", "\n    ", "\n\n", "\t", " ", "end", "end\n", "subgraph ",
        "subgraph x\n", "style ", "classDef ", "class ", "linkStyle ", "linkStyle 99 ", "click ", "note ",
        "note right of ", "accTitle: x", "accDescr {", "accDescr: x", "title ", "section ", "direction LR",
        "\0", "\u0001", "\u001F", "￿", "￾", "\uD800", "\uDC00", "​", "‮", "é", "😀",
        "#lt;", "#65535;", "#0;", "#quot;", "#9999999999;", "-1", "0", "-0", "1e309", "NaN", "Infinity",
        "99999999999999999999", "0.0000001", "...", "@{", "@{ shape: ", "::icon(", "<br/>", "<br>", "**", "_",
        "~", "$", "*", "+", "=", "\\", "/", "?", "!", "^", "×", "fill:", "stroke-width:", "0-15:", "+5:",
        "commit", "branch ", "checkout ", "merge ", "loop ", "alt ", "else ", "opt ", "par ", "and ", "rect ",
        "participant ", "actor ", "state ", "[*]", "--", "..", "<|--", "*--", "o--", "o{", "|{", "||--o{"
    ];

    [Test]
    public async Task MutatedInputs_RenderOrAreTurnedDown()
    {
        var corpus = Corpus();
        var cases = int.Parse(Environment.GetEnvironmentVariable("NAIAD_FUZZ_CASES") ?? "1500", CultureInfo.InvariantCulture);
        var seed = int.Parse(Environment.GetEnvironmentVariable("NAIAD_FUZZ_SEED") ?? "1", CultureInfo.InvariantCulture);
        var maxFailures = int.Parse(Environment.GetEnvironmentVariable("NAIAD_FUZZ_MAX_FAILURES") ?? "5", CultureInfo.InvariantCulture);

        var random = new Random(seed);
        var failures = new List<string>();
        for (var i = 0; i < cases && failures.Count < maxFailures; i++)
        {
            var (name, input) = corpus[random.Next(corpus.Count)];
            var mutated = Mutate(input, random);
            if (Check(mutated) is { } failure)
            {
                failures.Add($"case {i}, from {name}: {failure}\n{Escape(mutated)}");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    // Why the input fails, or null when it renders or is turned down as it should be.
    static string? Check(string input)
    {
        string? svg = null;
        Exception? failure = null;
        var render = Task.Run(
            () =>
            {
                try
                {
                    svg = Mermaid.Render(input);
                }
                catch (MermaidException)
                {
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

        if (!render.Wait(renderTimeout))
        {
            return $"did not finish within {renderTimeout.TotalSeconds}s";
        }

        if (failure is not null)
        {
            var frame = failure.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
            return $"{failure.GetType().Name}: {failure.Message} {frame}";
        }

        if (svg is null)
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(svg);
        }
        catch (XmlException exception)
        {
            return $"the SVG is not well-formed: {exception.Message}";
        }

        // Only where numbers go: a label may well say NaN or Infinity.
        var broken = document
            .Descendants()
            .SelectMany(_ => _.Attributes())
            .FirstOrDefault(_ => numeric.Contains(_.Name.LocalName) &&
                                 (_.Value.Contains("NaN", StringComparison.Ordinal) ||
                                  _.Value.Contains("Infinity", StringComparison.Ordinal)));
        if (broken is not null)
        {
            return $"the SVG holds a NaN or infinite number: {broken.Name.LocalName}='{broken.Value}'";
        }

        var rootStyle = (string?) document.Root?.Attribute("style") ?? "";
        if (rootStyle.Contains("NaN", StringComparison.Ordinal) ||
            rootStyle.Contains("Infinity", StringComparison.Ordinal))
        {
            return $"the SVG's size is not a number: {rootStyle}";
        }

        return null;
    }

    // The attributes that hold only numbers and geometry.
    static readonly HashSet<string> numeric =
    [
        "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "width", "height", "d", "points",
        "transform", "viewBox", "refX", "refY", "markerWidth", "markerHeight", "stroke-width", "offset",
        "font-size", "dx", "dy"
    ];

    // One to three changes, each at a random place.
    static string Mutate(string input, Random random)
    {
        var text = input;
        var changes = random.Next(1, 4);
        for (var change = 0; change < changes; change++)
        {
            text = Change(text, random);
        }

        return text;
    }

    static string Change(string text, Random random)
    {
        var at = random.Next(text.Length + 1);
        switch (random.Next(8))
        {
            case 0:
            case 1:
                return text.Insert(at, fragments[random.Next(fragments.Length)]);
            case 2:
                if (text.Length == 0)
                {
                    return text;
                }

                at = Math.Min(at, text.Length - 1);
                return text.Remove(at, random.Next(1, Math.Min(10, text.Length - at) + 1));
            case 3:
                return text[..at];
            case 4:
            {
                var lines = text.Split('\n').ToList();
                var line = random.Next(lines.Count);
                lines.Insert(line, lines[line]);
                return string.Join('\n', lines);
            }
            case 5:
            {
                var lines = text.Split('\n').ToList();
                if (lines.Count > 1)
                {
                    lines.RemoveAt(random.Next(1, lines.Count));
                }

                return string.Join('\n', lines);
            }
            case 6:
            {
                var lines = text.Split('\n');
                var a = random.Next(lines.Length);
                var b = random.Next(lines.Length);
                (lines[a], lines[b]) = (lines[b], lines[a]);
                return string.Join('\n', lines);
            }
            default:
            {
                // A piece of the text repeated, which nests and lengthens what the grammar reads.
                if (text.Length == 0)
                {
                    return text;
                }

                at = Math.Min(at, text.Length - 1);
                var piece = text.Substring(at, random.Next(1, Math.Min(20, text.Length - at) + 1));
                return text.Insert(at, string.Concat(Enumerable.Repeat(piece, random.Next(2, 30))));
            }
        }
    }

    // The input as a C# string literal would hold it, so a failure can be pasted into a test.
    static string Escape(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            builder.Append(
                c switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ when char.IsControl(c) || char.IsSurrogate(c) || c > '~' =>
                        string.Create(CultureInfo.InvariantCulture, $"\\u{(int) c:X4}"),
                    _ => c.ToString()
                });
        }

        return builder.Append('"').ToString();
    }

    // The input of every snapshot test, from all the diagram folders.
    static List<(string Name, string Input)> Corpus()
    {
        var corpus = new List<(string Name, string Input)>();
        var files = Directory.GetFiles(ProjectFiles.ProjectDirectory, "*Tests.cs", SearchOption.AllDirectories)
            .Where(_ => !_.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !_.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var snapshot in SnapshotInputs.Read(file))
            {
                if (snapshot.Input is not null)
                {
                    corpus.Add(($"{snapshot.ClassName}.{snapshot.MethodName}", snapshot.Input));
                }
            }
        }

        return corpus;
    }
}
