using HtmlTransformer.Internal;
using Microsoft.AspNetCore.Builder;

namespace HtmlTransformer;

public static class AppBuilderExtensions
{
    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UseHtmlTransformer()
        {
            return app.UseMiddleware<HtmlTransformerMiddleware>();
        }
    }
}