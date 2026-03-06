using System.Text;
using HtmlTransformer.Feature;
using HtmlTransformer.Feature.Replacement;
using Microsoft.AspNetCore.Http;

namespace HtmlTransformer;

public static class HttpContextExtensions
{
    extension(HttpContext context)
    {
        private IHtmlTransformerFeature GetHtmlInjectionFeature()
        {
            var feature = context.Features.Get<IHtmlTransformerFeature>();
            if (feature is null)
            {
                feature = new HtmlTransformerFeature();
                context.Features.Set(feature);
            }

            return feature;
        }

        public void InjectHtml(string html, HtmlInjectionLocation where)
        {
            context.GetHtmlInjectionFeature().Injections.Add(new InjectionEntry(
                async (stream, ct) => await stream.WriteAsync(Encoding.UTF8.GetBytes(html), ct),
                where));
        }

        public void InjectHtml(byte[] html, HtmlInjectionLocation where)
        {
            context.GetHtmlInjectionFeature().Injections.Add(new InjectionEntry(
                async (stream, ct) => await stream.WriteAsync(html, ct),
                where));
        }

        public void InjectHtml(Stream htmlStream, HtmlInjectionLocation where)
        {
            context.GetHtmlInjectionFeature().Injections.Add(new InjectionEntry(
                async (stream, ct) => await htmlStream.CopyToAsync(stream, ct),
                where));
        }

        public void InjectHtml(Func<CancellationToken, Task<string>> htmlFactory, HtmlInjectionLocation where)
        {
            context.GetHtmlInjectionFeature().Injections.Add(new InjectionEntry(
                async (stream, ct) =>
                {
                    var html = await htmlFactory(ct);
                    if (!string.IsNullOrEmpty(html))
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(html), ct);
                    }
                },
                where));
        }

        public void Replace(string placeholder, string replacement, ReplacementScope scope = ReplacementScope.First)
        {
            context.GetHtmlInjectionFeature().Replacements.Add(new ReplacementEntry(
                placeholder,
                async (stream, ct) => await stream.WriteAsync(Encoding.UTF8.GetBytes(replacement), ct),
                scope));
        }

        public void Replace(string placeholder, byte[] replacement, ReplacementScope scope = ReplacementScope.First)
        {
            context.GetHtmlInjectionFeature().Replacements.Add(new ReplacementEntry(
                placeholder,
                async (stream, ct) => await stream.WriteAsync(replacement, ct),
                scope));
        }

        public void Replace(string placeholder, Stream replacementStream, ReplacementScope scope = ReplacementScope.First)
        {
            context.GetHtmlInjectionFeature().Replacements.Add(new ReplacementEntry(
                placeholder,
                async (stream, ct) => await replacementStream.CopyToAsync(stream, ct),
                scope));
        }

        public void Replace(string placeholder, Func<CancellationToken, Task<string>> replacementFactory, ReplacementScope scope = ReplacementScope.First)
        {
            context.GetHtmlInjectionFeature().Replacements.Add(new ReplacementEntry(
                placeholder,
                async (stream, ct) =>
                {
                    var html = await replacementFactory(ct);
                    if (!string.IsNullOrEmpty(html))
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(html), ct);
                    }
                },
                scope));
        }
    }
}