namespace Naiad;

using static Khroma;

/// <summary>
/// The theme variables of one of Mermaid's themes (<c>default</c>, <c>base</c>, <c>dark</c>, <c>forest</c>
/// or <c>neutral</c>) with the diagram's <c>themeVariables</c> applied, worked out as Mermaid's theme
/// classes work them out. Only the variables the themed renderers read are ported: the base colours, the
/// colour scale (<c>cScale0</c> to <c>cScale11</c> and their label colours), the pie colours and text, and
/// the git graph's branch, tag and commit label colours. The statements keep Mermaid's order, quirks
/// included (the default theme, for one, works its colours out once before the overrides are applied and
/// again after, so <c>cScale</c> and <c>git</c> colours are darkened twice and an overridden
/// <c>primaryColor</c> does not reach them), since that order decides the colours.
/// </summary>
sealed class MermaidTheme
{
    readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    bool darkMode;

    MermaidTheme()
    {
    }

    /// <summary>
    /// The theme a diagram asks for, by <c>theme</c> (under the diagram's own key first, as Mermaid reads
    /// it) or by <c>themeVariables</c> alone, which apply over the default theme. <see langword="null"/>
    /// when it asks for neither, or for <c>theme: 'null'</c>, which turns Mermaid's themes off; the
    /// renderer then keeps its own colours. Themes this port lacks (Mermaid's newer redux and neo themes)
    /// fall back to the default theme.
    /// </summary>
    internal static MermaidTheme? From(MermaidConfig config, string diagramKey)
    {
        var name = config.GetString(diagramKey, "theme") ?? config.GetString("theme");
        var overrides = config.GetMap("themeVariables");
        if (name == "null" ||
            name is null && overrides is null)
        {
            return null;
        }

        var theme = new MermaidTheme();
        var kind = name switch
        {
            "base" or "dark" or "forest" or "neutral" => name,
            _ => "default"
        };

        switch (kind)
        {
            case "base":
                theme.ConstructBase();
                break;
            case "dark":
                theme.ConstructDark();
                break;
            case "forest":
                theme.ConstructForest();
                break;
            case "neutral":
                theme.ConstructNeutral();
                break;
            default:
                theme.ConstructDefault();
                break;
        }

        theme.Calculate(kind, overrides);
        return theme;
    }

    /// <summary>
    /// A variable's value, or <see langword="null"/> when the theme leaves it unset.
    /// </summary>
    internal string? this[string name] =>
        values.GetValueOrDefault(name);

    // Whether a variable has a value, as JavaScript's `this.x || ...` tests it.
    bool Has(string name) =>
        !string.IsNullOrEmpty(values.GetValueOrDefault(name));

    string Get(string name) =>
        values.GetValueOrDefault(name) ?? "";

    void Set(string name, string value) =>
        values[name] = value;

    // `this.name = this.name || value()`.
    void Or(string name, Func<string> value)
    {
        if (!Has(name))
        {
            values[name] = value();
        }
    }

    string MkBorder(string color)
    {
        if (darkMode)
        {
            return Adjust(color, s: -40, l: 10);
        }

        return Adjust(color, s: -40, l: -10);
    }

    void Calculate(string kind, Dictionary<string, object?>? overrides)
    {
        // Only the default theme clears its placeholders before working the colours out.
        if (kind == "default")
        {
            foreach (var key in values.Where(_ => _.Value == "calculated").Select(_ => _.Key).ToList())
            {
                values.Remove(key);
            }
        }

        var given = new List<(string, string)>();
        foreach (var (key, value) in overrides ?? [])
        {
            if (key == "darkMode")
            {
                darkMode = value is true ||
                           value is string text && text.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var text2 = value switch
            {
                string text => text,
                double number => number.ToString(CultureInfo.InvariantCulture),
                bool flag => flag.ToString().ToLowerInvariant(),
                _ => null
            };

            if (text2 is not null)
            {
                given.Add((key, text2));
            }
        }

        foreach (var (key, value) in given)
        {
            Set(key, value);
        }

        switch (kind)
        {
            case "base":
                UpdateBase();
                break;
            case "dark":
                UpdateDark();
                break;
            case "forest":
                UpdateForest();
                break;
            case "neutral":
                UpdateNeutral();
                break;
            default:
                UpdateDefault();
                break;
        }

        // Again, in case an override was of a derived value.
        foreach (var (key, value) in given)
        {
            Set(key, value);
        }
    }

    // ---- base ----

    void ConstructBase()
    {
        Set("background", "#f4f4f4");
        Set("primaryColor", "#fff4dd");
    }

    void UpdateBase()
    {
        Or("primaryTextColor", () => darkMode ? "#eee" : "#333");
        Or("secondaryColor", () => Adjust(Get("primaryColor"), h: -120));
        Or("tertiaryColor", () => Adjust(Get("primaryColor"), h: 180, l: 5));
        Or("primaryBorderColor", () => MkBorder(Get("primaryColor")));
        Or("secondaryTextColor", () => Invert(Get("secondaryColor")));
        Or("tertiaryTextColor", () => Invert(Get("tertiaryColor")));
        Or("lineColor", () => Invert(Get("background")));
        Or("textColor", () => Get("primaryTextColor"));
        Or("mainBkg", () => Get("primaryColor"));
        Or("actorTextColor", () => Get("primaryTextColor"));
        Or("labelTextColor", () => Get("actorTextColor"));
        Or("taskTextDarkColor", () => Get("textColor"));

        var primary = Get("primaryColor");
        Or("cScale0", () => primary);
        Or("cScale1", () => Get("secondaryColor"));
        Or("cScale2", () => Get("tertiaryColor"));
        Or("cScale3", () => Adjust(primary, h: 30));
        Or("cScale4", () => Adjust(primary, h: 60));
        Or("cScale5", () => Adjust(primary, h: 90));
        Or("cScale6", () => Adjust(primary, h: 120));
        Or("cScale7", () => Adjust(primary, h: 150));
        Or("cScale8", () => Adjust(primary, h: 210, l: 150));
        Or("cScale9", () => Adjust(primary, h: 270));
        Or("cScale10", () => Adjust(primary, h: 300));
        Or("cScale11", () => Adjust(primary, h: 330));
        for (var i = 0; i < 12; i++)
        {
            Set($"cScale{i}", Darken(Get($"cScale{i}"), darkMode ? 75 : 25));
        }

        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScaleInv{i}", () => Invert(Get($"cScale{index}")));
        }

        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScalePeer{i}", () => darkMode ? Lighten(Get($"cScale{index}"), 10) : Darken(Get($"cScale{index}"), 10));
        }

        Or("scaleLabelColor", () => Get("labelTextColor"));
        for (var i = 0; i < 12; i++)
        {
            Or($"cScaleLabel{i}", () => Get("scaleLabelColor"));
        }

        var secondary = Get("secondaryColor");
        var tertiary = Get("tertiaryColor");
        Or("pie1", () => primary);
        Or("pie2", () => secondary);
        Or("pie3", () => tertiary);
        Or("pie4", () => Adjust(primary, l: -10));
        Or("pie5", () => Adjust(secondary, l: -10));
        Or("pie6", () => Adjust(tertiary, l: -10));
        Or("pie7", () => Adjust(primary, h: 60, l: -10));
        Or("pie8", () => Adjust(primary, h: -60, l: -10));
        Or("pie9", () => Adjust(primary, h: 120));
        Or("pie10", () => Adjust(primary, h: 60, l: -20));
        Or("pie11", () => Adjust(primary, h: -60, l: -20));
        Or("pie12", () => Adjust(primary, h: 120, l: -10));
        PieText("taskTextDarkColor", "taskTextDarkColor");

        Or("git0", () => primary);
        Or("git1", () => secondary);
        Or("git2", () => tertiary);
        Or("git3", () => Adjust(primary, h: -30));
        Or("git4", () => Adjust(primary, h: -60));
        Or("git5", () => Adjust(primary, h: -90));
        Or("git6", () => Adjust(primary, h: 60));
        Or("git7", () => Adjust(primary, h: 120));
        ShadeGit();
        GitInverses(false);
        Or("branchLabelColor", () => darkMode ? "black" : Get("labelTextColor"));
        for (var i = 0; i < 8; i++)
        {
            Or($"gitBranchLabel{i}", () => Get("branchLabelColor"));
        }

        GitLabels();
    }

    // ---- default ----

    void ConstructDefault()
    {
        Set("background", "#f4f4f4");
        Set("primaryColor", "#ECECFF");
        Set("secondaryColor", "#ffffde");
        Set("tertiaryColor", Adjust(Get("primaryColor"), h: -160));
        Set("primaryBorderColor", MkBorder(Get("primaryColor")));
        Set("primaryTextColor", Invert(Get("primaryColor")));
        Set("secondaryTextColor", Invert(Get("secondaryColor")));
        Set("tertiaryTextColor", Invert(Get("tertiaryColor")));
        Set("background", "white");
        Set("mainBkg", "#ECECFF");
        Set("lineColor", "#333333");
        Set("textColor", "#333");
        Set("actorTextColor", "black");
        Set("labelTextColor", "calculated");
        Set("taskTextDarkColor", "black");
        UpdateDefault();
    }

    void UpdateDefault()
    {
        LightScale();

        Set("scaleLabelColor", Has("scaleLabelColor") && Get("scaleLabelColor") != "calculated" ? Get("scaleLabelColor") : Get("labelTextColor"));
        if (Get("labelTextColor") != "calculated")
        {
            Or("cScaleLabel0", () => Invert(Get("labelTextColor")));
            Or("cScaleLabel3", () => Invert(Get("labelTextColor")));
            for (var i = 0; i < 12; i++)
            {
                Or($"cScaleLabel{i}", () => Get("labelTextColor"));
            }
        }

        Set("labelTextColor", Get("actorTextColor"));

        var primary = Get("primaryColor");
        var secondary = Get("secondaryColor");
        var tertiary = Get("tertiaryColor");
        Or("pie1", () => primary);
        Or("pie2", () => secondary);
        Or("pie3", () => Adjust(tertiary, l: -40));
        Or("pie4", () => Adjust(primary, l: -10));
        Or("pie5", () => Adjust(secondary, l: -30));
        Or("pie6", () => Adjust(tertiary, l: -20));
        Or("pie7", () => Adjust(primary, h: 60, l: -20));
        Or("pie8", () => Adjust(primary, h: -60, l: -40));
        Or("pie9", () => Adjust(primary, h: 120, l: -40));
        Or("pie10", () => Adjust(primary, h: 60, l: -40));
        Or("pie11", () => Adjust(primary, h: -90, l: -40));
        Or("pie12", () => Adjust(primary, h: 120, l: -30));
        PieText("taskTextDarkColor", "taskTextDarkColor");

        HueGit(primary, secondary, tertiary);
        ShadeGit();
        GitInverses(true);
        StandardBranchLabels();
        GitLabels();
    }

    // ---- forest ----

    void ConstructForest()
    {
        Set("background", "#f4f4f4");
        Set("primaryColor", "#cde498");
        Set("secondaryColor", "#cdffb2");
        Set("background", "white");
        Set("mainBkg", "#cde498");
        Set("lineColor", "green");
        Set("tertiaryColor", Lighten("#cde498", 10));
        Set("primaryBorderColor", MkBorder(Get("primaryColor")));
        Set("primaryTextColor", Invert(Get("primaryColor")));
        Set("secondaryTextColor", Invert(Get("secondaryColor")));
        Set("tertiaryTextColor", Invert(Get("primaryColor")));
        Set("lineColor", Invert(Get("background")));
        Set("textColor", Invert(Get("background")));
        Set("actorTextColor", "black");
        Set("labelTextColor", "calculated");
        Set("taskTextDarkColor", "black");
    }

    void UpdateForest()
    {
        Set("labelTextColor", Get("actorTextColor"));

        LightScale();
        Set("scaleLabelColor", Has("scaleLabelColor") && Get("scaleLabelColor") != "calculated" ? Get("scaleLabelColor") : Get("labelTextColor"));
        for (var i = 0; i < 12; i++)
        {
            Or($"cScaleLabel{i}", () => Get("scaleLabelColor"));
        }

        var primary = Get("primaryColor");
        var secondary = Get("secondaryColor");
        var tertiary = Get("tertiaryColor");
        Or("pie1", () => primary);
        Or("pie2", () => secondary);
        Or("pie3", () => tertiary);
        Or("pie4", () => Adjust(primary, l: -30));
        Or("pie5", () => Adjust(secondary, l: -30));
        Or("pie6", () => Adjust(tertiary, h: 40, l: -40));
        Or("pie7", () => Adjust(primary, h: 60, l: -10));
        Or("pie8", () => Adjust(primary, h: -60, l: -10));
        Or("pie9", () => Adjust(primary, h: 120));
        Or("pie10", () => Adjust(primary, h: 60, l: -50));
        Or("pie11", () => Adjust(primary, h: -60, l: -50));
        Or("pie12", () => Adjust(primary, h: 120, l: -50));
        PieText("taskTextDarkColor", "taskTextDarkColor");

        HueGit(primary, secondary, tertiary);
        ShadeGit();
        GitInverses(false);
        StandardBranchLabels();
        GitLabels();
    }

    // ---- dark ----

    void ConstructDark()
    {
        Set("background", "#333");
        Set("primaryColor", "#1f2020");
        Set("secondaryColor", Lighten(Get("primaryColor"), 16));
        Set("tertiaryColor", Adjust(Get("primaryColor"), h: -160));
        Set("primaryBorderColor", Invert(Get("background")));
        Set("primaryTextColor", Invert(Get("primaryColor")));
        Set("secondaryTextColor", Invert(Get("secondaryColor")));
        Set("tertiaryTextColor", Invert(Get("tertiaryColor")));
        Set("lineColor", Invert(Get("background")));
        Set("mainBkg", "#1f2020");
        Set("mainContrastColor", "lightgrey");
        Set("textColor", "#ccc");
        Set("labelTextColor", "calculated");
        Set("taskTextDarkColor", "calculated");
    }

    void UpdateDark()
    {
        Set("lineColor", Get("mainContrastColor"));
        Set("labelTextColor", Get("mainContrastColor"));
        Set("doneTaskBkgColor", Get("mainContrastColor"));
        Set("taskTextDarkColor", Invert(Get("doneTaskBkgColor")));

        var primary = Get("primaryColor");
        Or("cScale1", () => "#0b0000");
        Or("cScale2", () => "#4d1037");
        Or("cScale3", () => "#3f5258");
        Or("cScale4", () => "#4f2f1b");
        Or("cScale5", () => "#6e0a0a");
        Or("cScale6", () => "#3b0048");
        Or("cScale7", () => "#995a01");
        Or("cScale8", () => "#154706");
        Or("cScale9", () => "#161722");
        Or("cScale10", () => "#00296f");
        Or("cScale11", () => "#01629c");
        Or("cScale0", () => primary);
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScaleInv{i}", () => Invert(Get($"cScale{index}")));
            Or($"cScalePeer{i}", () => Lighten(Get($"cScale{index}"), 10));
        }

        Or("scaleLabelColor", () => darkMode ? "black" : Get("labelTextColor"));
        for (var i = 0; i < 12; i++)
        {
            Or($"cScaleLabel{i}", () => Get("scaleLabelColor"));
        }

        for (var i = 0; i < 12; i++)
        {
            Set($"pie{i}", Get($"cScale{i}"));
        }

        PieText("mainContrastColor", "mainContrastColor");

        var secondary = Get("secondaryColor");
        Set("git0", Lighten(secondary, 20));
        Set("git1", Lighten(Has("pie2") ? Get("pie2") : secondary, 20));
        Set("git2", Lighten(Has("pie3") ? Get("pie3") : Get("tertiaryColor"), 20));
        Set("git3", Lighten(Has("pie4") ? Get("pie4") : Adjust(primary, h: -30), 20));
        Set("git4", Lighten(Has("pie5") ? Get("pie5") : Adjust(primary, h: -60), 20));
        Set("git5", Lighten(Has("pie6") ? Get("pie6") : Adjust(primary, h: -90), 10));
        Set("git6", Lighten(Has("pie7") ? Get("pie7") : Adjust(primary, h: 60), 10));
        Set("git7", Lighten(Has("pie8") ? Get("pie8") : Adjust(primary, h: 120), 20));
        GitInverses(false);
        StandardBranchLabels();
        GitLabels();
    }

    // ---- neutral ----

    void ConstructNeutral()
    {
        Set("primaryColor", "#eee");
        Set("contrast", "#707070");
        Set("secondaryColor", Lighten(Get("contrast"), 55));
        Set("background", "#ffffff");
        Set("tertiaryColor", Adjust(Get("primaryColor"), h: -160));
        Set("primaryBorderColor", MkBorder(Get("primaryColor")));
        Set("primaryTextColor", Invert(Get("primaryColor")));
        Set("secondaryTextColor", Invert(Get("secondaryColor")));
        Set("tertiaryTextColor", Invert(Get("tertiaryColor")));
        Set("textColor", Invert(Get("background")));
        Set("mainBkg", "#eee");
        Set("lineColor", "#666");
        Set("text", "#333");
        Set("labelTextColor", "calculated");
        Set("taskTextDarkColor", "calculated");
    }

    void UpdateNeutral()
    {
        Set("labelTextColor", Get("text"));

        string[] scale = ["#555", "#F4F4F4", "#555", "#BBB", "#777", "#999", "#DDD", "#FFF", "#DDD", "#BBB", "#999", "#777"];
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScale{i}", () => scale[index]);
        }

        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScaleInv{i}", () => Invert(Get($"cScale{index}")));
            Or($"cScalePeer{i}", () => darkMode ? Lighten(Get($"cScale{index}"), 10) : Darken(Get($"cScale{index}"), 10));
        }

        Or("scaleLabelColor", () => darkMode ? "black" : Get("labelTextColor"));
        Or("cScaleLabel0", () => Get("cScale1"));
        Or("cScaleLabel2", () => Get("cScale1"));
        for (var i = 0; i < 12; i++)
        {
            Or($"cScaleLabel{i}", () => Get("scaleLabelColor"));
        }

        Set("taskTextDarkColor", Get("text"));
        for (var i = 0; i < 12; i++)
        {
            Set($"pie{i}", Get($"cScale{i}"));
        }

        Set("pie12", Get("pie0"));
        PieText("taskTextDarkColor", "taskTextDarkColor");

        var primary = Get("primaryColor");
        Set("git0", Darken(Get("pie1"), 25));
        Set("git1", Has("pie2") ? Get("pie2") : Get("secondaryColor"));
        Set("git2", Has("pie3") ? Get("pie3") : Get("tertiaryColor"));
        Set("git3", Has("pie4") ? Get("pie4") : Adjust(primary, h: -30));
        Set("git4", Has("pie5") ? Get("pie5") : Adjust(primary, h: -60));
        Set("git5", Has("pie6") ? Get("pie6") : Adjust(primary, h: -90));
        Set("git6", Has("pie7") ? Get("pie7") : Adjust(primary, h: 60));
        Set("git7", Has("pie8") ? Get("pie8") : Adjust(primary, h: 120));
        GitInverses(false);
        Or("branchLabelColor", () => Get("labelTextColor"));
        for (var i = 0; i < 8; i++)
        {
            Set($"gitBranchLabel{i}", i is 1 or 3 ? "white" : Get("branchLabelColor"));
        }

        GitLabels();
    }

    // ---- shared pieces ----

    // The default and forest themes' colour scale: hues round the primary colour, darkened by 10.
    void LightScale()
    {
        var primary = Get("primaryColor");
        Or("cScale0", () => primary);
        Or("cScale1", () => Get("secondaryColor"));
        Or("cScale2", () => Get("tertiaryColor"));
        Or("cScale3", () => Adjust(primary, h: 30));
        Or("cScale4", () => Adjust(primary, h: 60));
        Or("cScale5", () => Adjust(primary, h: 90));
        Or("cScale6", () => Adjust(primary, h: 120));
        Or("cScale7", () => Adjust(primary, h: 150));
        Or("cScale8", () => Adjust(primary, h: 210));
        Or("cScale9", () => Adjust(primary, h: 270));
        Or("cScale10", () => Adjust(primary, h: 300));
        Or("cScale11", () => Adjust(primary, h: 330));
        Or("cScalePeer1", () => Darken(Get("secondaryColor"), 45));
        Or("cScalePeer2", () => Darken(Get("tertiaryColor"), 40));
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Set($"cScale{i}", Darken(Get($"cScale{i}"), 10));
            Or($"cScalePeer{i}", () => Darken(Get($"cScale{index}"), 25));
        }

        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Or($"cScaleInv{i}", () => Adjust(Get($"cScale{index}"), h: 180));
        }
    }

    void PieText(string titleColor, string legendColor)
    {
        Or("pieTitleTextSize", () => "25px");
        Or("pieTitleTextColor", () => Get(titleColor));
        Or("pieSectionTextSize", () => "17px");
        Or("pieSectionTextColor", () => Get("textColor"));
        Or("pieLegendTextSize", () => "17px");
        Or("pieLegendTextColor", () => Get(legendColor));
        Or("pieStrokeColor", () => "black");
        Or("pieStrokeWidth", () => "2px");
        Or("pieOuterStrokeWidth", () => "2px");
        Or("pieOuterStrokeColor", () => "black");
        Or("pieOpacity", () => "0.7");
    }

    void HueGit(string primary, string secondary, string tertiary)
    {
        Or("git0", () => primary);
        Or("git1", () => secondary);
        Or("git2", () => tertiary);
        Or("git3", () => Adjust(primary, h: -30));
        Or("git4", () => Adjust(primary, h: -60));
        Or("git5", () => Adjust(primary, h: -90));
        Or("git6", () => Adjust(primary, h: 60));
        Or("git7", () => Adjust(primary, h: 120));
    }

    void ShadeGit()
    {
        for (var i = 0; i < 8; i++)
        {
            if (darkMode)
            {
                Set($"git{i}", Lighten(Get($"git{i}"), 25));
            }
            else
            {
                Set($"git{i}", Darken(Get($"git{i}"), 25));
            }
        }
    }

    void GitInverses(bool darkenFirst)
    {
        for (var i = 0; i < 8; i++)
        {
            var index = i;
            if (darkenFirst && i == 0)
            {
                Or("gitInv0", () => Darken(Invert(Get("git0")), 25));
                continue;
            }

            Or($"gitInv{i}", () => Invert(Get($"git{index}")));
        }
    }

    // Light label text on the first and fourth branches, the theme's label colour on the rest.
    void StandardBranchLabels()
    {
        for (var i = 0; i < 8; i++)
        {
            if (i is 0 or 3)
            {
                Or($"gitBranchLabel{i}", () => Invert(Get("labelTextColor")));
                continue;
            }

            Or($"gitBranchLabel{i}", () => Get("labelTextColor"));
        }
    }

    void GitLabels()
    {
        Or("tagLabelColor", () => Get("primaryTextColor"));
        Or("tagLabelBackground", () => Get("primaryColor"));
        Set("tagLabelBorder", Has("tagBorder") ? Get("tagBorder") : Get("primaryBorderColor"));
        Or("commitLabelColor", () => Get("secondaryTextColor"));
        Or("commitLabelBackground", () => Get("secondaryColor"));
    }
}
