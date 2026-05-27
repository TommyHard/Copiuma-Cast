namespace Cast.API.Domain;

public sealed class NewsImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NewsPostId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
}