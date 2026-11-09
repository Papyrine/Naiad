/// <summary>
/// Builds a pre-filled GitHub "new issue" URL when an unexpected exception escapes a render, so a user
/// can report it in a single click. Instead of opening the URL itself (there is no browser shell to call
/// from WASM) it hands the URL back to be surfaced as a link the user can choose to follow.
/// </summary>
public static class IssueLauncher
{
    const string newIssue = "https://github.com/Papyrine/Naiad/issues/new";

    /// <summary>
    /// Produces a `…/issues/new?title=…&amp;body=…` URL describing <paramref name="exception"/>.
    /// <paramref name="action"/> is the user-facing operation that failed (it becomes the issue title);
    /// <paramref name="environment"/> is an optional block of pre-formatted Markdown bullet lines
    /// (e.g. app version, user agent) included verbatim in the body. <paramref name="source"/> is the
    /// Mermaid markup that triggered the failure, included in a fenced block so the report reproduces.
    /// </summary>
    public static string ForException(string action, Exception exception, string? environment = null, string? source = null)
    {
        var title = WebUtility.UrlEncode($"{action}: {exception.GetType().Name}");

        var lines = new List<string>
        {
            "An unexpected error occurred in the Naiad web app.",
            "",
            $"* Action: {action}",
        };

        if (!string.IsNullOrWhiteSpace(environment))
        {
            lines.Add(environment);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            lines.Add("* Diagram source:");
            lines.Add("```");
            lines.Add(source);
            lines.Add("```");
        }

        lines.Add("* Exception:");
        lines.Add("```");
        lines.Add(exception.ToString());
        lines.Add("```");

        var body = WebUtility.UrlEncode(string.Join("\n", lines));

        return $"{newIssue}?title={title}&body={body}";
    }
}
