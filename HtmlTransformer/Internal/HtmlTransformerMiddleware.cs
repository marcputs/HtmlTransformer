using HtmlTransformer.Feature;
using HtmlTransformer.Feature.Replacement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace HtmlTransformer.Internal;

public sealed class HtmlTransformerMiddleware
{
    private readonly RequestDelegate _next;

    public HtmlTransformerMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>();
        if (originalFeature is null)
        {
            await _next(context);
            return;
        }

        var injectingStream = new HtmlTransformerStream(
            context.Response,
            originalFeature.Stream,
            injectAtHeadStart: (stream, ct) => WriteInjectionsAsync(context, HtmlInjectionLocation.Head, stream, ct),
            injectAtHeadEnd: (stream, ct) => WriteInjectionsAsync(context, HtmlInjectionLocation.HeadEnd, stream, ct),
            injectAtBodyEnd: (stream, ct) => WriteInjectionsAsync(context, HtmlInjectionLocation.BodyEnd, stream, ct),
            replacements: () => GetReplacements(context));

        var replacementFeature = new HtmlTransformerResponseBodyFeature(originalFeature,
            injectingStream);

        context.Features.Set<IHttpResponseBodyFeature>(replacementFeature);

        try
        {
            await _next(context);
            await replacementFeature.CompleteAsync();
        }
        finally
        {
            context.Features.Set(originalFeature);
            context.Response.Body = originalFeature.Stream;
        }
    }

    private static async Task WriteInjectionsAsync(HttpContext context, HtmlInjectionLocation location, Stream outputStream, CancellationToken ct)
    {
        var feature = context.Features.Get<IHtmlTransformerFeature>();
        if (feature is null || feature.Injections.Count == 0)
        {
            return;
        }

        var injections = feature.Injections.Where(i => i.Location == location);
        foreach (var injection in injections)
        {
            await injection.WriteAsync(outputStream, ct);
        }
    }

    private static IList<ReplacementEntry>? GetReplacements(HttpContext context)
    {
        var feature = context.Features.Get<IHtmlTransformerFeature>();
        return feature?.Replacements;
    }
}