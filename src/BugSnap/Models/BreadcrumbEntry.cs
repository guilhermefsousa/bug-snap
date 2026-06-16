namespace BugSnap.Models;

/// <summary>
/// A single user-trail breadcrumb. To honour the PII rule, <see cref="Detail"/>
/// only ever holds a normalized route (no query string) or the literal value of
/// a <c>data-bugsnap-action</c> attribute — never free text, DOM content, or
/// element labels.
/// </summary>
public class BreadcrumbEntry
{
    public string Type { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime TimestampUtc { get; set; }
}
