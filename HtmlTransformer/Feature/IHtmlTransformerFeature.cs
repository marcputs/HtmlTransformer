using HtmlTransformer.Feature.Replacement;

namespace HtmlTransformer.Feature;

public interface IHtmlTransformerFeature
{
    IList<InjectionEntry> Injections { get; }
    IList<ReplacementEntry> Replacements { get; }
}