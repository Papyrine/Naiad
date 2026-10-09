class GanttParser : IDiagramParser<GanttModel>
{
    static Parser<char, GanttModel> parser;

    static GanttParser()
    {
        // Basic parsers
        var restOfLine =
            Token(_ => _ != '\r' &&
                       _ != '\n')
                .ManyString();

        // Title: title My Chart Title
        var titleParser =
            from inlineWhitespace in CommonParsers.InlineWhitespace
            from title in CIString("title")
            from requiredWhitespace in CommonParsers.RequiredWhitespace
            from innerTitle in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select innerTitle.Trim();

        // Date format: dateFormat YYYY-MM-DD
        var dateFormatParser =
            from inlineWhitespace in CommonParsers.InlineWhitespace
            from dateFormat in CIString("dateFormat")
            from requiredWhitespace in CommonParsers.RequiredWhitespace
            from format in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select format.Trim();

        // Axis format: axisFormat %Y-%m-%d
        var axisFormatParser =
            from inlineWhitespace in CommonParsers.InlineWhitespace
            from axisFormat in CIString("axisFormat")
            from requiredWhitespace in CommonParsers.RequiredWhitespace
            from format in restOfLine
            from lienEnd in CommonParsers.LineEnd
            select format.Trim();

        // Excludes: excludes weekends
        var excludesParser =
            from whitespace in CommonParsers.InlineWhitespace
            from excludes in CIString("excludes")
            from requiredWhitespace in CommonParsers.RequiredWhitespace
            from innerExcludes in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select ParseExcludes(innerExcludes);

        // Section: section Section Name
        var sectionParser =
            from inlienWhitespace in CommonParsers.InlineWhitespace
            from section in CIString("section")
            from requiredWhitespace in CommonParsers.RequiredWhitespace
            from name in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select name.Trim();

        // Task line parser - handles multiple formats
        // Format: Task name :modifiers, id, start, duration
        // Examples:
        //   Task A :a1, 2024-01-01, 30d
        //   Task B :done, after a1, 20d
        //   Task C :crit, milestone, 2024-02-01, 0d
        var taskParser =
            from _ in CommonParsers.InlineWhitespace
            from name in Token(_ => _ != ':' && _ != '\r' && _ != '\n').AtLeastOnceString()
            from __ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from parts in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from lineEnd in CommonParsers.LineEnd
            select ParseTaskLine(name.Trim(), parts.AsSpan().Trim());

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        // Content item
        var contentItem =
            OneOf(
                Try(titleParser.Select<IGanttContent?>(_ => new TitleItem(_))),
                Try(dateFormatParser.Select<IGanttContent?>(_ => new DateFormatItem(_))),
                Try(axisFormatParser.Select<IGanttContent?>(_ => new AxisFormatItem(_))),
                Try(excludesParser.Select<IGanttContent?>(_ => new ExcludesItem(_))),
                Try(sectionParser.Select<IGanttContent?>(_ => new SectionItem(_))),
                Try(taskParser.Select<IGanttContent?>(_ => new TaskItem(_))),
                skipLine.ThenReturn<IGanttContent?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("gantt")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from content in contentItem.Many()
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(content);
    }

    static GanttTask ParseTaskLine(string name, CharSpan parts)
    {
        var task = new GanttTask
        {
            Name = name
        };

        foreach (var range in parts.Split(','))
        {
            var part = parts[range].Trim();
            if (part.IsEmpty)
            {
                continue;
            }

            // Check for modifiers
            if (part.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                task.Status = GanttTaskStatus.Active;
                continue;
            }

            if (part.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                task.Status = GanttTaskStatus.Done;
                continue;
            }

            if (part.Equals("crit", StringComparison.OrdinalIgnoreCase))
            {
                task.IsCritical = true;
                continue;
            }

            if (part.Equals("milestone", StringComparison.OrdinalIgnoreCase))
            {
                task.IsMilestone = true;
                continue;
            }

            // Check for after reference
            if (part.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
            {
                task.AfterTaskId = part[6..].Trim().ToString();
                continue;
            }

            // Check for duration (ends with d, w, h)
            if (part.Length > 1 &&
                char.IsDigit(part[0]) &&
                char.IsLetter(part[^1]))
            {
                var digitEnd = 0;
                while (digitEnd < part.Length && char.IsAsciiDigit(part[digitEnd]))
                {
                    digitEnd++;
                }
                var unit = part[^1];
                if (int.TryParse(part[..digitEnd], out var num))
                {
                    task.Duration = unit switch
                    {
                        'd' => TimeSpan.FromDays(num),
                        'w' => TimeSpan.FromDays(num * 7),
                        'h' => TimeSpan.FromHours(num),
                        _ => TimeSpan.FromDays(num)
                    };
                    continue;
                }
            }

            // Check for date (YYYY-MM-DD)
            if (DateTime.TryParseExact(
                    part,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                if (task.StartDate == null &&
                    task.AfterTaskId == null)
                {
                    task.StartDate = date;
                }
                else
                {
                    task.EndDate = date;
                }

                continue;
            }

            // Must be an ID (alphanumeric identifier)
            if (IsIdentifier(part))
            {
                task.Id ??= part.ToString();
            }
        }

        return task;
    }

    static List<string> ParseExcludes(string excludes)
    {
        var span = excludes.AsSpan().Trim();
        var result = new List<string>();
        foreach (var range in span.Split(','))
        {
            result.Add(span[range].Trim().ToString());
        }

        return result;
    }

    static bool IsIdentifier(CharSpan value)
    {
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) &&
                ch != '_' && ch != '-')
            {
                return false;
            }
        }

        return true;
    }

    static GanttModel BuildModel(IEnumerable<IGanttContent?> content)
    {
        var model = new GanttModel();
        GanttSection? currentSection = null;

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case DateFormatItem df:
                    model.DateFormat = df.Value;
                    break;

                case AxisFormatItem af:
                    model.AxisFormat = af.Value;
                    break;

                case ExcludesItem excludes:
                    foreach (var exclude in excludes.Values)
                    {
                        if (exclude.Equals("weekends", StringComparison.InvariantCultureIgnoreCase))
                        {
                            model.ExcludeWeekends = true;
                        }
                        else
                        {
                            model.ExcludeDays.Add(exclude);
                        }
                    }

                    break;

                case SectionItem section:
                    currentSection = new()
                    {
                        Name = section.Name
                    };
                    model.Sections.Add(currentSection);
                    break;

                case TaskItem taskItem:
                    if (currentSection == null)
                    {
                        currentSection = new()
                        {
                            Name = ""
                        };
                        model.Sections.Add(currentSection);
                    }

                    taskItem.Task.SectionName = currentSection.Name;
                    currentSection.Tasks.Add(taskItem.Task);
                    break;
            }
        }

        return model;
    }

    public Result<char, GanttModel> Parse(string input) => parser.Parse(input);

    interface IGanttContent;
    readonly record struct TitleItem(string Value) : IGanttContent;
    readonly record struct DateFormatItem(string Value) : IGanttContent;
    readonly record struct AxisFormatItem(string Value) : IGanttContent;
    readonly record struct ExcludesItem(List<string> Values) : IGanttContent;
    readonly record struct SectionItem(string Name) : IGanttContent;
    readonly record struct TaskItem(GanttTask Task) : IGanttContent;
}