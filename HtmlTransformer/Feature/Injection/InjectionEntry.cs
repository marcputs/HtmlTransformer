namespace HtmlTransformer.Feature;

public record struct InjectionEntry(Func<Stream, CancellationToken, Task> WriteAsync, HtmlInjectionLocation Location);