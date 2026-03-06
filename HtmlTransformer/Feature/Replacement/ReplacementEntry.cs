namespace HtmlTransformer.Feature.Replacement;

public record struct ReplacementEntry(string Placeholder, Func<Stream, CancellationToken, Task> WriteAsync, ReplacementScope Scope);