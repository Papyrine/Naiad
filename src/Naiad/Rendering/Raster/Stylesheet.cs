/// <summary>One element's identity for selector matching: its tag, id and classes.</summary>
readonly record struct ElementMatch(string Tag, string? Id, IReadOnlyList<string> Classes);

/// <summary>A single declaration that matched an element, carried with the data the cascade needs.</summary>
readonly record struct MatchedDeclaration(string Property, string Value, bool Important, int Specificity, int Order);

/// <summary>
/// A parsed CSS stylesheet supporting the selector subset Naiad/Mermaid emit: type, class and id
/// simple selectors (and their compounds like <c>rect.text</c> or <c>.marker.cross</c>), grouped with
/// commas and combined with the descendant combinator. <c>@</c>-rules (keyframes, media, import) and
/// pseudo bits are skipped. <see cref="Match"/> returns every declaration that applies to an element
/// given its ancestor chain, tagged with specificity and source order so the caller can run the cascade.
/// </summary>
sealed class Stylesheet
{
    List<StyleRule> rules;

    Stylesheet(List<StyleRule> rules) =>
        this.rules = rules;

    public static Stylesheet Parse(string? css)
    {
        var rules = new List<StyleRule>();
        if (string.IsNullOrWhiteSpace(css))
        {
            return new(rules);
        }

        var order = 0;
        var i = 0;
        while (i < css.Length)
        {
            // Selector prelude runs up to the next '{'.
            var braceOpen = css.IndexOf('{', i);
            if (braceOpen < 0)
            {
                break;
            }

            var prelude = css[i..braceOpen].Trim();
            var braceClose = MatchingBrace(css, braceOpen);

            if (prelude.StartsWith('@'))
            {
                // @keyframes/@media/@font-face etc. — skip the whole (possibly brace-nested) block.
                i = braceClose + 1;
                continue;
            }

            var body = css[(braceOpen + 1)..braceClose];
            var declarations = ParseDeclarations(body);
            if (declarations.Count > 0)
            {
                foreach (var selectorText in prelude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (ParseSelector(selectorText) is { } chain)
                    {
                        rules.Add(new(chain, declarations, order));
                    }
                }
            }

            order++;
            i = braceClose + 1;
        }

        return new(rules);
    }

    // Fills the caller-supplied list (cleared first) rather than allocating a new one, so the rasterizer
    // can reuse a single buffer across every element's cascade.
    public void Match(IReadOnlyList<ElementMatch> chain, List<MatchedDeclaration> into)
    {
        into.Clear();
        foreach (var rule in rules)
        {
            if (!rule.Matches(chain))
            {
                continue;
            }

            foreach (var declaration in rule.Declarations)
            {
                into.Add(new(declaration.Property, declaration.Value, declaration.Important, rule.Specificity, rule.Order));
            }
        }
    }

    static int MatchingBrace(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return css.Length - 1;
    }

    static List<Declaration> ParseDeclarations(string body)
    {
        var declarations = new List<Declaration>();
        foreach (var piece in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = piece.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var property = piece[..colon].Trim().ToLowerInvariant();
            var value = piece[(colon + 1)..].Trim();
            var important = false;
            var bang = value.IndexOf('!');
            if (bang >= 0)
            {
                important = value[bang..].Contains("important", StringComparison.OrdinalIgnoreCase);
                value = value[..bang].Trim();
            }

            if (property.Length > 0 && value.Length > 0)
            {
                declarations.Add(new(property, value, important));
            }
        }

        return declarations;
    }

    static SimpleSelector[]? ParseSelector(string selector)
    {
        var parts = selector.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var simples = new List<SimpleSelector>();
        foreach (var part in parts)
        {
            // Treat the child combinator as a descendant — close enough for the stylesheets here and
            // avoids modelling combinator kinds.
            if (part == ">")
            {
                continue;
            }

            if (ParseSimple(part) is { } simple)
            {
                simples.Add(simple);
            }
            else
            {
                return null;
            }
        }

        if (simples.Count == 0)
        {
            return null;
        }

        return [.. simples];
    }

    static SimpleSelector? ParseSimple(string token)
    {
        string? tag = null;
        string? id = null;
        List<string>? classes = null;
        var i = 0;

        // Optional leading type selector.
        if (i < token.Length && (char.IsLetter(token[i]) || token[i] == '*'))
        {
            var start = i;
            while (i < token.Length && token[i] is not ('.' or '#' or ':'))
            {
                i++;
            }

            var name = token[start..i];
            if (name != "*")
            {
                tag = name.ToLowerInvariant();
            }
        }

        while (i < token.Length)
        {
            var marker = token[i];
            i++;
            var start = i;
            while (i < token.Length && token[i] is not ('.' or '#' or ':'))
            {
                i++;
            }

            var name = token[start..i];
            switch (marker)
            {
                case '.':
                    (classes ??= []).Add(name);
                    break;
                case '#':
                    id = name;
                    break;
                // ':' pseudo-classes/elements are ignored (their name is consumed above).
            }
        }

        if (tag == null && id == null && classes == null)
        {
            return null;
        }

        return new(tag, id, classes);
    }

    sealed class StyleRule(SimpleSelector[] chain, List<Declaration> declarations, int order)
    {
        public List<Declaration> Declarations { get; } = declarations;

        public int Order { get; } = order;

        public int Specificity { get; } = Sum(chain);

        public bool Matches(IReadOnlyList<ElementMatch> elementChain)
        {
            var si = chain.Length - 1;
            var ei = elementChain.Count - 1;
            if (ei < 0 || !chain[si].Matches(elementChain[ei]))
            {
                return false;
            }

            si--;
            ei--;
            while (si >= 0)
            {
                var matched = false;
                while (ei >= 0)
                {
                    if (chain[si].Matches(elementChain[ei]))
                    {
                        matched = true;
                        ei--;
                        break;
                    }

                    ei--;
                }

                if (!matched)
                {
                    return false;
                }

                si--;
            }

            return true;
        }

        static int Sum(SimpleSelector[] chain)
        {
            var total = 0;
            foreach (var simple in chain)
            {
                total += simple.Specificity;
            }

            return total;
        }
    }

    sealed class SimpleSelector(string? tag, string? id, List<string>? classes)
    {
        public bool Matches(ElementMatch element)
        {
            if (tag != null && !string.Equals(tag, element.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (id != null && !string.Equals(id, element.Id, StringComparison.Ordinal))
            {
                return false;
            }

            if (classes != null)
            {
                foreach (var className in classes)
                {
                    if (!Contains(element.Classes, className))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public int Specificity { get; } =
            (id != null ? 10000 : 0) + (classes?.Count ?? 0) * 100 + (tag != null ? 1 : 0);

        static bool Contains(IReadOnlyList<string> values, string target)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    readonly record struct Declaration(string Property, string Value, bool Important);
}
