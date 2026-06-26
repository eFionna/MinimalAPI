using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text;

namespace MinimalAPI;

/// <summary>
/// A minimal HTTP server built on <see cref="HttpListener"/>. At construction it
/// discovers route handlers via the <see cref="Attributes.RouteAttribute"/>, then
/// <see cref="RunAsync"/> accepts connections and dispatches each request to the
/// handler whose pattern matches and whose verb is implemented.
/// </summary>
public sealed class APIServer : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>All discovered routes, tried in order on each request.</summary>
    private readonly ImmutableArray<Routing.Route> routes;

    /// <summary>The underlying listener bound to the configured prefix.</summary>
    private readonly HttpListener listener;

    /// <summary>Leading path segment (e.g. <c>"/api"</c>) stripped before matching; empty when no prefix.</summary>
    private readonly string pathPrefix;

    /// <summary>Whether the accept loop should keep running; cleared on stop/dispose.</summary>
    private volatile bool running;

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    private bool disposed;

    /// <summary>
    /// Creates a server, discovers its routes, and registers the listener prefix.
    /// The listener is not started until <see cref="RunAsync"/> is called.
    /// </summary>
    /// <param name="port">TCP port to listen on.</param>
    /// <param name="address">Host or IP to bind (e.g. <c>"localhost"</c> or <c>"+"</c>).</param>
    /// <param name="apiPrefix">
    /// Path segment prepended to all routes (e.g. <c>"api"</c> serves <c>/api/...</c>).
    /// Leading/trailing slashes are ignored; pass null or whitespace for no prefix.
    /// </param>
    /// <param name="https">When <c>true</c>, registers an <c>https://</c> prefix.</param>
    /// <param name="routeAssembly">
    /// Assembly to scan for <see cref="Attributes.RouteAttribute"/> classes.
    /// Defaults to the calling assembly, so consumers' route classes are found
    /// rather than this library's own.
    /// </param>
    public APIServer(int port, string address,
        string apiPrefix = "api", bool https = false, Assembly? routeAssembly = null)
    {
        Assembly assembly = routeAssembly ?? Assembly.GetCallingAssembly();

        IEnumerable<Type> routeClasses = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<Attributes.RouteAttribute>() != null);

        routes = [.. routeClasses
            .Select(t =>
            {
                Attributes.RouteAttribute? attr = t.GetCustomAttribute<Attributes.RouteAttribute>();
                if (attr == null) return null;

                if (!attr.Path.StartsWith('/')) return null;

                object? handler = Activator.CreateInstance(t);
                if (handler is not Routing.IRoute route) return null;

                return new Routing.Route(attr.Path, route);
            })
            .Where(r => r is not null)
            .Select(r => r!)];

        // One canonical prefix is derived here and reused for both the listener
        // registration and request-path stripping, so they cannot disagree.
        string trimmed = string.IsNullOrWhiteSpace(apiPrefix) ? "" : apiPrefix.Trim('/');
        pathPrefix = trimmed.Length > 0 ? $"/{trimmed}" : "";
        string urlPrefix = trimmed.Length > 0 ? $"{trimmed}/" : "";
        string url = $"{(https ? "https" : "http")}://{address}:{port}/{urlPrefix}";

        listener = new();
        listener.Prefixes.Add(url);
    }

    /// <summary>
    /// Starts the listener and runs the accept loop until <see cref="Stop"/> or
    /// <see cref="Dispose"/> is called. Each accepted request is dispatched without
    /// awaiting, so concurrent requests are handled independently. Returns when the
    /// loop stops; safe to call only once per running instance.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The server has been disposed.</exception>
    public async Task RunAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (running)
        {
            Logger.Warn("Trying to start server again.");
            return;
        }
        running = true;

        listener.Start();
        Logger.Info("Server running on {Url}.", listener.Prefixes.First());

        try
        {
            while (running)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                // Dispatch without awaiting so a slow handler does not block the
                // accept loop. HandleRequest never throws (see its catch).
                _ = HandleRequest(context);
            }
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 995)
        {
            // Expected on shutdown
        }
        catch (HttpListenerException ex)
        {
            Logger.Error(ex.ToString());
            throw;
        }
        finally
        {
            if (listener.IsListening)
                listener.Stop();
        }
    }

    /// <summary>
    /// Strips the API prefix, finds the first matching route, and invokes its handler
    /// for the request's verb. Responds 400 (bad URL), 404 (no route), 405 (verb not
    /// supported), or 500 (handler threw) as appropriate. This method never throws —
    /// it is launched fire-and-forget by <see cref="RunAsync"/>.
    /// </summary>
    private async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            HttpListenerRequest request = context.Request;
            if (request.Url == null)
            {
                await Respond(context, 400, "Bad request");
                return;
            }

            string method = request.HttpMethod.ToUpperInvariant();
            string absolutePath = request.Url.AbsolutePath;
            string path = pathPrefix.Length > 0
                && absolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)
                ? absolutePath[pathPrefix.Length..]
                : absolutePath;

            if (path.Length == 0)
                path = "/";

            Dictionary<string, string> queryParams = ParseQueryString(request.Url.Query);

            foreach (Routing.Route route in routes)
            {
                if (route.TryMatch(path, out var routeParams))
                {
                    if (route.TryGetHandler(method, out var handlerDelegate))
                    {
                        await handlerDelegate!(context, routeParams, queryParams);
                    }
                    else
                    {
                        await Respond(context, 405, "Method Not Allowed");
                    }

                    return;
                }
            }

            await Respond(context, 404, "Not Found");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            // A handler may have already committed the response, in which case
            // writing a 500 on top of it throws again. Swallow that secondary
            // failure rather than letting it escape this fire-and-forget task.
            try
            {
                await Respond(context, 500, "Internal Server Error");
            }
            catch (Exception respondEx)
            {
                Logger.Warn("Could not send 500 response: {Message}", respondEx.Message);
            }
        }
    }

    /// <summary>
    /// Signals the accept loop to stop and stops the listener. The listener can be
    /// started again with a fresh <see cref="RunAsync"/> call; use <see cref="Dispose"/>
    /// to release it permanently.
    /// </summary>
    public void Stop()
    {
        running = false;
        if (!disposed && listener.IsListening)
            listener.Stop();
    }

    /// <summary>
    /// Stops the loop and closes the listener, releasing its OS resources.
    /// Idempotent; the server cannot be restarted afterwards.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        running = false;
        listener.Close();
    }

    /// <summary>
    /// Writes a plain-text response with the given status code and message, then closes
    /// the output stream. Intended for the framework's own status replies (errors, etc.).
    /// </summary>
    private static async Task Respond(HttpListenerContext ctx, int statusCode, string message)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "text/plain";
        ctx.Response.ContentLength64 = buffer.Length;
        await using var output = ctx.Response.OutputStream;
        await output.WriteAsync(buffer);
    }

    /// <summary>
    /// Parses a raw URL query string (with or without the leading <c>'?'</c>) into a
    /// case-insensitive key/value map. Values are URL-decoded; a key with no <c>'='</c>
    /// maps to an empty string, and on duplicate keys the last value wins.
    /// </summary>
    private static Dictionary<string, string> ParseQueryString(string query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (string kv in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = kv.Split('=', 2);
            string key = WebUtility.UrlDecode(parts[0]);
            string value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
            result[key] = value;
        }

        return result;
    }
}
