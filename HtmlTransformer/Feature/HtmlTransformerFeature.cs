using HtmlTransformer.Feature.Replacement;

namespace HtmlTransformer.Feature;

public sealed class HtmlTransformerFeature : IHtmlTransformerFeature
{
    public IList<InjectionEntry> Injections { get; } = new List<InjectionEntry>();
    public IList<ReplacementEntry> Replacements { get; } = new List<ReplacementEntry>();
}
