using HtmlTransformer;

var builder = WebApplication.CreateSlimBuilder(args);

var app = builder.Build();

app.UseHtmlTransformer();

app.MapGet("/", async context =>
{
    context.Replace("[OS_USER]", Environment.UserName, ReplacementScope.Global);
    context.Replace("[TIME]", ct => Task.FromResult(DateTime.Now.ToLongTimeString()));

    context.InjectHtml("<title>Transformed!</title>", HtmlInjectionLocation.Head);
    context.InjectHtml("""<button onclick="document.location.reload()">Reload</button>""", HtmlInjectionLocation.BodyEnd);

    // stream example.html to the response
    // the transformations defined above will be applied to the file
    var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    var filePath = Path.Combine(environment.ContentRootPath, "example.html");
    await context.Response.SendFileAsync(filePath);
});

app.Run();