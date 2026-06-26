namespace MinimalAPI.Attributes;

/// <summary>
/// Marks a class as a route handler and declares the URL template it serves.
/// The class must implement <see cref="Routing.IRoute"/>. At startup
/// <see cref="APIServer"/> scans the target assembly for types carrying this
/// attribute, instantiates each one, and registers it against <see cref="Path"/>.
/// </summary>
/// <example>
/// A path may contain <c>{name}</c> placeholders, which are captured as route
/// parameters and passed to the handler:
/// <code>
/// [Route("/users/{id}")]
/// public class UserRoute : IRoute { /* ... */ }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class RouteAttribute(string path) : Attribute
{
    /// <summary>
    /// The URL template for this route. Must start with <c>'/'</c>; routes whose
    /// path does not are skipped during discovery. Segments wrapped in braces
    /// (e.g. <c>{id}</c>) become named route parameters.
    /// </summary>
    public string Path { get; } = path;
}
