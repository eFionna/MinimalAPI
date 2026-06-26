using System.Net;

namespace MinimalAPI.Routing;

/// <summary>
/// Marker interface for route handlers. A handler declares only the HTTP verbs
/// it supports — implement <c>GET</c>, <c>POST</c>, <c>PUT</c> and/or
/// <c>DELETE</c> as public methods with the signature below. Verbs that are not
/// declared on the concrete type are bound at construction time as "unsupported"
/// and yield <c>405 Method Not Allowed</c> at request time.
/// </summary>
/// <remarks>
/// These are default interface methods so that implementers need not declare
/// every verb. They are intentionally never invoked: <see cref="Route"/> binds
/// verbs via reflection on the concrete type, which does not surface default
/// interface implementations, so an undeclared verb is simply absent from the
/// dispatch table.
/// </remarks>
public interface IRoute
{
    /// <summary>Handles <c>GET</c> requests for the route. Override to support the verb.</summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="routeParams">Values captured from <c>{name}</c> path segments.</param>
    /// <param name="query">Parsed query-string parameters.</param>
    Task GET(HttpListenerContext ctx, Dictionary<string, string> routeParams, Dictionary<string, string> query)
        => throw new NotSupportedException();

    /// <summary>Handles <c>POST</c> requests for the route. Override to support the verb.</summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="routeParams">Values captured from <c>{name}</c> path segments.</param>
    /// <param name="query">Parsed query-string parameters.</param>
    Task POST(HttpListenerContext ctx, Dictionary<string, string> routeParams, Dictionary<string, string> query)
        => throw new NotSupportedException();

    /// <summary>Handles <c>PUT</c> requests for the route. Override to support the verb.</summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="routeParams">Values captured from <c>{name}</c> path segments.</param>
    /// <param name="query">Parsed query-string parameters.</param>
    Task PUT(HttpListenerContext ctx, Dictionary<string, string> routeParams, Dictionary<string, string> query)
        => throw new NotSupportedException();

    /// <summary>Handles <c>DELETE</c> requests for the route. Override to support the verb.</summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="routeParams">Values captured from <c>{name}</c> path segments.</param>
    /// <param name="query">Parsed query-string parameters.</param>
    Task DELETE(HttpListenerContext ctx, Dictionary<string, string> routeParams, Dictionary<string, string> query)
        => throw new NotSupportedException();
}
