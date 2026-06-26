using System.Net;
using System.Text.RegularExpressions;

namespace MinimalAPI.Routing;

/// <summary>
/// A single registered route: the compiled pattern that matches request paths
/// plus the per-verb handler delegates bound from the owning <see cref="IRoute"/>.
/// One instance is created per discovered route at server startup.
/// </summary>
internal sealed partial class Route
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Signature shared by every verb handler.</summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="routeParams">Values captured from <c>{name}</c> path segments.</param>
    /// <param name="query">Parsed query-string parameters.</param>
    public delegate Task Handler(
        HttpListenerContext ctx,
        Dictionary<string, string> routeParams,
        Dictionary<string, string> query);

    /// <summary>HTTP verbs probed on each handler when binding delegates.</summary>
    private static readonly string[] Verbs = ["GET", "POST", "PUT", "DELETE"];

    /// <summary>Anchored, case-insensitive regex compiled from the route pattern.</summary>
    private readonly Regex regex;

    /// <summary>Verb (e.g. <c>"GET"</c>) to the handler bound for it; only declared verbs are present.</summary>
    private readonly Dictionary<string, Handler> delegateCache;

    /// <summary>
    /// Compiles <paramref name="pattern"/> into a matcher and binds a delegate for
    /// each HTTP verb the <paramref name="handler"/> actually declares. Verbs that
    /// throw while binding are logged and skipped (the route simply won't serve them).
    /// </summary>
    /// <param name="pattern">URL template, e.g. <c>/users/{id}</c>.</param>
    /// <param name="handler">The route handler instance to bind verbs from.</param>
    public Route(string pattern, IRoute handler)
    {
        regex = new(
            "^" + BuildRegexPattern(pattern) + "$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        delegateCache = new(StringComparer.OrdinalIgnoreCase);

        foreach (string verb in Verbs)
        {
            var methodInfo = handler.GetType().GetMethod(verb);
            if (methodInfo == null)
                continue;

            try
            {
                var del = (Handler)Delegate.CreateDelegate(typeof(Handler), handler, methodInfo);
                delegateCache[verb] = del;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to bind {Verb} on {Handler}: {Message}",
                    verb, handler.GetType().Name, ex.Message);
            }
        }
    }

    /// <summary>
    /// Tests whether <paramref name="path"/> matches this route and, if so, extracts
    /// its named route parameters.
    /// </summary>
    /// <param name="path">The request path (with any API prefix already stripped).</param>
    /// <param name="routeParams">
    /// On a match, the captured <c>{name}</c> values; otherwise an empty dictionary.
    /// </param>
    /// <returns><c>true</c> if the path matches this route.</returns>
    public bool TryMatch(string path, out Dictionary<string, string> routeParams)
    {
        routeParams = [];

        Match match = regex.Match(path);
        if (!match.Success) return false;

        routeParams = regex.GetGroupNames()
            .Where(name => name != "0")
            .ToDictionary(name => name, name => match.Groups[name].Value);

        return true;
    }

    /// <summary>
    /// Looks up the handler bound for an HTTP <paramref name="method"/> (case-insensitive).
    /// A missing entry means the route does not support that verb (caller responds 405).
    /// </summary>
    /// <returns><c>true</c> if a handler is registered for the method.</returns>
    public bool TryGetHandler(string method, out Handler? handler)
        => delegateCache.TryGetValue(method, out handler);

    /// <summary>
    /// Translates a route template into a regex body by escaping all literal text and
    /// then turning each <c>{name}</c> placeholder into a named capture group that
    /// matches a single path segment (<c>[^/]+</c>).
    /// </summary>
    private static string BuildRegexPattern(string pattern)
    {
        // Escape() runs over the already-escaped text, so the braces it looks for
        // appear as "\{name\}" — hence the doubled backslashes in its pattern.
        string escaped = Regex.Escape(pattern);
        escaped = Escape().Replace(escaped, @"(?<$1>[^/]+)");
        return escaped;
    }

    /// <summary>Matches an escaped <c>\{name\}</c> placeholder, capturing the name.</summary>
    [GeneratedRegex(@"\\\{(\w+)\\\}")]
    private static partial Regex Escape();
}
