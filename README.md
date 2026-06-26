# MinimalAPI

A tiny, dependency-light HTTP framework for .NET built on `System.Net.HttpListener`.
You declare route handlers as classes decorated with `[Route]`; the server discovers
them by reflection at startup and dispatches each request to the matching pattern and
HTTP verb.

- **Attribute routing** with `{name}` path parameters
- **Verb dispatch** by implementing `GET` / `POST` / `PUT` / `DELETE` (only what you need)
- **Query-string parsing** handed to your handler as a dictionary
- **Configurable API prefix** (e.g. everything under `/api`)
- Concurrent request handling, graceful stop, `IDisposable`

Targets **.NET 9**. The only runtime dependency is [NLog](https://nlog-project.org/).

## Getting started

This is a class library, so reference it from your own host project:

```xml
<ItemGroup>
  <ProjectReference Include="..\MinimalAPI\MinimalAPI.csproj" />
</ItemGroup>
```

### 1. Define a route

```csharp
using System.Net;
using MinimalAPI.Attributes;
using MinimalAPI.Routing;

[Route("/users/{id}")]
public class UserRoute : IRoute
{
    public async Task GET(
        HttpListenerContext ctx,
        Dictionary<string, string> routeParams,
        Dictionary<string, string> query)
    {
        string id = routeParams["id"];                       // from the path
        string format = query.GetValueOrDefault("format");   // from ?format=...

        byte[] body = System.Text.Encoding.UTF8.GetBytes($"User {id} ({format})");
        ctx.Response.ContentType = "text/plain";
        ctx.Response.ContentLength64 = body.Length;
        await using var output = ctx.Response.OutputStream;
        await output.WriteAsync(body);
    }
}
```

Implement only the verbs you support. A request for an unimplemented verb gets
`405 Method Not Allowed`; an unmatched path gets `404 Not Found`.

### 2. Start the server

```csharp
using MinimalAPI;

// Serves the route above at: http://localhost:8080/api/users/{id}
var server = new APIServer(port: 8080, address: "localhost", apiPrefix: "api");
await server.RunAsync();
```

By default `APIServer` scans the **calling assembly** for `[Route]` classes, so your
route definitions just need to live in the project that constructs the server. To scan
a different assembly, pass `routeAssembly`.

## Configuration

The `APIServer` constructor:

```csharp
new APIServer(
    int port,                       // TCP port to listen on
    string address,                 // host/IP to bind, e.g. "localhost" or "+"
    string apiPrefix = "api",       // path prefix; null/empty for none
    bool https = false,             // register an https:// prefix
    Assembly? routeAssembly = null  // assembly to scan; defaults to caller
);
```

Leading/trailing slashes in `apiPrefix` are ignored. With `apiPrefix: "api"`, a route
declared as `/users/{id}` is served at `/api/users/{id}`. Pass `apiPrefix: ""` to serve
routes at the root.

## Lifecycle

```csharp
var server = new APIServer(8080, "localhost");

var run = server.RunAsync();   // accept loop runs until stopped
// ...
server.Stop();                 // stop the loop; can be restarted with RunAsync()
await run;

server.Dispose();              // release the listener permanently (IDisposable)
```

## Notes

- Routes are matched in discovery order; the **first** matching pattern wins.
- Path parameters match a single segment (`/users/{id}` matches `/users/42`, not `/users/42/posts`).
- Handlers own the response — write your status code and body via `ctx.Response`.
  The framework only emits its own plain-text replies for `400/404/405/500`.
