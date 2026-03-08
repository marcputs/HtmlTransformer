namespace MinimalWebApi;

public class Comment
{
    public int Id { get; set; }
    public string Body { get; set; }
}

public static class CommentsService
{
    public static async Task<Comment?> FetchRandomComment(CancellationToken ct = default)
    {
        using var httpClient = new HttpClient();

        var commentId = new Random().Next(1, 20);
        var comment = await httpClient.GetFromJsonAsync<Comment>($"https://api.mydummyapi.com/comments/{commentId}", ct);

        return comment;
    }
}