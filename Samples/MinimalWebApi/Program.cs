using HtmlTransformer;
using MinimalWebApi;

var builder = WebApplication.CreateSlimBuilder(args);

var app = builder.Build();

app.UseHtmlTransformer();

app.Use((context, next) =>
{
    // We can replace the first occurence of a placeholder:
    context.Replace("[REPLACE_FIRST]", "(replaced first)");

    // Or every occurence:
    context.Replace("[REPLACE_ALL]", "(replaced all)", ReplacementScope.Global);

    // We can inject some HTML at the start of the <head> element:
    context.InjectHtml("<!-- added at start of head -->", HtmlInjectionLocation.Head);

    // Or at the end of the <head> element:
    context.InjectHtml("<!-- added at end of head -->", HtmlInjectionLocation.HeadEnd);

    // Or at the end of the <body> element:
    context.InjectHtml("<!-- added at end of body -->", HtmlInjectionLocation.BodyEnd);

    return next();
});

app.MapGet("/", async Task (HttpContext context, IWebHostEnvironment environment) =>
{
    context.Replace("[RANDOM_COMMENT]", async ct =>
    {
        var comment = await CommentsService.FetchRandomComment(ct);
        return comment?.Body ?? "A random comment was supposed to go here.";
    });
    
    var file = Path.Combine(environment.ContentRootPath, "example.html");
    await context.Response.SendFileAsync(file);
});

app.Run();