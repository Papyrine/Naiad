namespace Naiad.Diagrams.Gantt;

/// <summary>
/// The d3-time-format specifiers that Mermaid's <c>axisFormat</c> is written in, such as <c>%Y-%m-%d</c> or
/// <c>%b %e</c>, read the way d3's default (en-US) locale reads them. A <c>-</c>, <c>_</c> or <c>0</c> after
/// the <c>%</c> pads with nothing, spaces or zeros instead of the specifier's own padding. A specifier d3
/// does not know is written as its letter alone, and a <c>%</c> at the very end is dropped, as d3 does.
/// </summary>
static class TimeFormat
{
    static readonly string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    static readonly string[] months =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    public static string Format(string specifier, DateTime date)
    {
        var builder = new StringBuilder(specifier.Length * 2);
        Append(builder, specifier, date);
        return builder.ToString();
    }

    static void Append(StringBuilder builder, string specifier, DateTime date)
    {
        for (var i = 0; i < specifier.Length; i++)
        {
            if (specifier[i] != '%')
            {
                builder.Append(specifier[i]);
                continue;
            }

            i++;
            if (i >= specifier.Length)
            {
                return;
            }

            string? pad = null;
            if (specifier[i] is '-' or '_' or '0')
            {
                pad = PadFill(specifier[i]);
                i++;
                if (i >= specifier.Length)
                {
                    return;
                }
            }

            var code = specifier[i];
            AppendField(builder, code, pad ?? DefaultPad(code), date);
        }
    }

    static string PadFill(char modifier)
    {
        if (modifier == '-')
        {
            return "";
        }

        if (modifier == '_')
        {
            return " ";
        }

        return "0";
    }

    // Every number is padded with zeros but the day of the month written as %e, which takes spaces.
    static string DefaultPad(char code)
    {
        if (code == 'e')
        {
            return " ";
        }

        return "0";
    }

    static void AppendField(StringBuilder builder, char code, string pad, DateTime date)
    {
        var weekday = (int) date.DayOfWeek;
        switch (code)
        {
            case 'a':
                builder.Append(days[weekday], 0, 3);
                break;
            case 'A':
                builder.Append(days[weekday]);
                break;
            case 'b':
                builder.Append(months[date.Month - 1], 0, 3);
                break;
            case 'B':
                builder.Append(months[date.Month - 1]);
                break;
            case 'c':
                Append(builder, "%x, %X", date);
                break;
            case 'd':
            case 'e':
                AppendPadded(builder, date.Day, pad, 2);
                break;
            case 'f':
                AppendPadded(builder, date.Millisecond, pad, 3);
                builder.Append("000");
                break;
            case 'g':
                AppendPadded(builder, ISOWeek.GetYear(date) % 100, pad, 2);
                break;
            case 'G':
                AppendPadded(builder, ISOWeek.GetYear(date) % 10000, pad, 4);
                break;
            case 'H':
                AppendPadded(builder, date.Hour, pad, 2);
                break;
            case 'I':
                AppendPadded(builder, Hour12(date), pad, 2);
                break;
            case 'j':
                AppendPadded(builder, date.DayOfYear, pad, 3);
                break;
            case 'L':
                AppendPadded(builder, date.Millisecond, pad, 3);
                break;
            case 'm':
                AppendPadded(builder, date.Month, pad, 2);
                break;
            case 'M':
                AppendPadded(builder, date.Minute, pad, 2);
                break;
            case 'p':
                builder.Append(date.Hour >= 12 ? "PM" : "AM");
                break;
            case 'q':
                AppendPadded(builder, 1 + (date.Month - 1) / 3, "", 1);
                break;
            case 'Q':
                builder.Append(((long) Math.Floor((date - DateTime.UnixEpoch).TotalMilliseconds)).ToString(CultureInfo.InvariantCulture));
                break;
            case 's':
                builder.Append(((long) Math.Floor((date - DateTime.UnixEpoch).TotalSeconds)).ToString(CultureInfo.InvariantCulture));
                break;
            case 'S':
                AppendPadded(builder, date.Second, pad, 2);
                break;
            case 'u':
                AppendPadded(builder, weekday == 0 ? 7 : weekday, "", 1);
                break;
            case 'U':
                // Weeks start on Sunday, and the days before the year's first Sunday are week 00.
                AppendPadded(builder, (date.DayOfYear - 1 + 7 - weekday) / 7, pad, 2);
                break;
            case 'V':
                AppendPadded(builder, ISOWeek.GetWeekOfYear(date), pad, 2);
                break;
            case 'w':
                AppendPadded(builder, weekday, "", 1);
                break;
            case 'W':
                // Weeks start on Monday, and the days before the year's first Monday are week 00.
                AppendPadded(builder, (date.DayOfYear - 1 + 7 - (weekday + 6) % 7) / 7, pad, 2);
                break;
            case 'x':
                Append(builder, "%-m/%-d/%Y", date);
                break;
            case 'X':
                Append(builder, "%-I:%M:%S %p", date);
                break;
            case 'y':
                AppendPadded(builder, date.Year % 100, pad, 2);
                break;
            case 'Y':
                AppendPadded(builder, date.Year % 10000, pad, 4);
                break;
            case 'Z':
                // The dates carry no time zone, so they are written as UTC.
                builder.Append("+0000");
                break;
            default:
                // `%%` is a percent sign, and d3 writes any other unknown specifier as its letter.
                builder.Append(code);
                break;
        }
    }

    static int Hour12(DateTime date)
    {
        var hour = date.Hour % 12;
        if (hour == 0)
        {
            return 12;
        }

        return hour;
    }

    static void AppendPadded(StringBuilder builder, int value, string pad, int width)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        for (var i = digits.Length; i < width && pad.Length > 0; i++)
        {
            builder.Append(pad);
        }

        builder.Append(digits);
    }
}
