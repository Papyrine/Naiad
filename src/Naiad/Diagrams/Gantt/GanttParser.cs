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
            select new TaskItem(name.Trim(), parts.Trim());

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
                Try(taskParser.Select<IGanttContent?>(_ => _)),
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

    // The data after a task's name: status tags, then one to three positional items, as in Mermaid.
    //   one item      the end (a date or a duration); the task starts where the one before it ended
    //   two items     the start (a date or `after ...`), then the end
    //   three items   an id, the start, the end
    // Two items whose first is neither a date nor `after` are read as an id and an end, which Mermaid
    // rejects but which is plainly what was meant.
    static GanttTask ParseTaskLine(string name, CharSpan parts, string dateFormat)
    {
        var task = new GanttTask
        {
            Name = name
        };

        var items = new List<string>();
        foreach (var range in parts.Split(','))
        {
            var part = parts[range].Trim();
            if (part.IsEmpty)
            {
                continue;
            }

            if (part.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                task.Status = GanttTaskStatus.Active;
            }
            else if (part.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                task.Status = GanttTaskStatus.Done;
            }
            else if (part.Equals("crit", StringComparison.OrdinalIgnoreCase))
            {
                task.IsCritical = true;
            }
            else if (part.Equals("milestone", StringComparison.OrdinalIgnoreCase))
            {
                task.IsMilestone = true;
            }
            else
            {
                items.Add(part.ToString());
            }
        }

        switch (items.Count)
        {
            case 0:
                break;
            case 1:
                SetEnd(task, items[0], dateFormat);
                break;
            case 2:
                if (!TrySetStart(task, items[0], dateFormat))
                {
                    SetId(task, items[0]);
                }

                SetEnd(task, items[1], dateFormat);
                break;
            default:
                SetId(task, items[0]);
                TrySetStart(task, items[1], dateFormat);
                SetEnd(task, items[2], dateFormat);
                break;
        }

        return task;
    }

    static void SetId(GanttTask task, string item)
    {
        if (IsIdentifier(item))
        {
            task.Id = item;
        }
    }

    static bool TrySetStart(GanttTask task, string item, string dateFormat)
    {
        if (item.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
        {
            // One or more ids, separated by spaces; the task starts when the last of them ends.
            task.AfterTaskId = item[6..].Trim();
            return true;
        }

        if (TryParseDate(item, dateFormat, out var date))
        {
            task.StartDate = date;
            return true;
        }

        return false;
    }

    static void SetEnd(GanttTask task, string item, string dateFormat)
    {
        if (TryParseDate(item, dateFormat, out var date))
        {
            task.EndDate = date;
            return;
        }

        if (TryParseDuration(item, out var duration, out var months))
        {
            task.Duration = duration;
            task.DurationMonths = months;
        }
    }

    static bool TryParseDate(string text, string dateFormat, out DateTime date)
    {
        // Seconds or milliseconds since the Unix epoch.
        if (dateFormat is "X" or "x")
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stamp))
            {
                var epoch = DateTime.UnixEpoch;
                date = dateFormat == "X" ? epoch.AddSeconds(stamp) : epoch.AddMilliseconds(stamp);
                return true;
            }

            date = default;
            return false;
        }

        return DateTime.TryParseExact(
            text,
            ToNetDateFormat(dateFormat),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    /// <summary>
    /// Translates a Mermaid <c>dateFormat</c> - day.js tokens such as <c>YYYY-MM-DD</c>, <c>DD/MM/YY</c> or
    /// <c>HH:mm</c> - into the equivalent .NET custom format string.
    /// </summary>
    internal static string ToNetDateFormat(string format)
    {
        var builder = new StringBuilder(format.Length + 2);
        var i = 0;
        while (i < format.Length)
        {
            var ch = format[i];

            // [text] is literal text in day.js.
            if (ch == '[')
            {
                var close = format.IndexOf(']', i);
                if (close > i)
                {
                    builder.Append('\'').Append(format, i + 1, close - i - 1).Append('\'');
                    i = close + 1;
                    continue;
                }
            }

            var run = 1;
            while (i + run < format.Length &&
                   format[i + run] == ch)
            {
                run++;
            }

            switch (ch)
            {
                case 'Y':
                    builder.Append('y', run);
                    break;
                case 'D':
                    builder.Append('d', Math.Min(run, 2));
                    break;
                case 'd':
                    // Day of the week; .NET spells its name with three or four d's.
                    builder.Append('d', Math.Max(run, 3));
                    break;
                case 'S':
                    builder.Append('f', run);
                    break;
                case 'A' or 'a':
                    builder.Append("tt");
                    break;
                case 'Z':
                    builder.Append("zzz");
                    break;
                case 'M' or 'H' or 'h' or 'm' or 's':
                    builder.Append(ch, run);
                    break;
                default:
                    // Anything else stands for itself; escaped where .NET would read it as a token.
                    for (var n = 0; n < run; n++)
                    {
                        if (char.IsLetter(ch) || ch is '%' or '\\' or '\'' or '"' or '/' or ':')
                        {
                            builder.Append('\\');
                        }

                        builder.Append(ch);
                    }

                    break;
            }

            i += run;
        }

        // A lone letter would be taken for one of .NET's standard formats.
        if (builder.Length == 1)
        {
            builder.Insert(0, '%');
        }

        return builder.ToString();
    }

    // A number and a unit: ms, s, m, h, d, w, M (months) or y. Months and years come back as whole
    // calendar months; everything else as a span of time.
    static bool TryParseDuration(CharSpan text, out TimeSpan? duration, out int months)
    {
        duration = null;
        months = 0;

        var digits = 0;
        while (digits < text.Length &&
               (char.IsAsciiDigit(text[digits]) || text[digits] == '.'))
        {
            digits++;
        }

        if (digits == 0 ||
            !double.TryParse(text[..digits], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        switch (text[digits..])
        {
            case "ms":
                duration = TimeSpan.FromMilliseconds(amount);
                return true;
            case "s":
                duration = TimeSpan.FromSeconds(amount);
                return true;
            case "m":
                duration = TimeSpan.FromMinutes(amount);
                return true;
            case "h":
                duration = TimeSpan.FromHours(amount);
                return true;
            case "d":
                duration = TimeSpan.FromDays(amount);
                return true;
            case "w":
                duration = TimeSpan.FromDays(amount * 7);
                return true;
            case "M":
                months = (int) Math.Round(amount);
                return true;
            case "y":
                months = (int) Math.Round(amount * 12);
                return true;
            default:
                return false;
        }
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
        var items = content.ToList();
        var model = new GanttModel();
        GanttSection? currentSection = null;

        // Task dates are written in the chart's dateFormat, which may be declared after the tasks.
        foreach (var item in items)
        {
            if (item is DateFormatItem format)
            {
                model.DateFormat = format.Value;
            }
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
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

                    var task = ParseTaskLine(taskItem.Name, taskItem.Parts, model.DateFormat);
                    task.SectionName = currentSection.Name;
                    currentSection.Tasks.Add(task);
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
    readonly record struct TaskItem(string Name, string Parts) : IGanttContent;
}