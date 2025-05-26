namespace Ommer.Rss;

public record Feed(
    string Link,
    string Title,
    string Description,
    string Language,
    string Copyright,
    string Email,
    string LastBuildDate,
    bool Explicit,
    string Author,
    string OwnerName,
    string FeedUrl,
    string ImageUrl,
    string ImageLink,
    string Category,
    string MediaRestrictionCountry,
    List<FeedItem> Items
)
{
    public string Language { get; } = Language;
    public string Copyright { get; } = Copyright;
    public bool Explicit { get; } = Explicit;
    public string Author { get; } = Author;
    public string OwnerName { get; } = OwnerName;
    public string Category { get; } = Category;
    public string MediaRestrictionCountry { get; } = MediaRestrictionCountry;
}

public record FeedItem(
    string Guid,
    string Link,
    string Title,
    string Description,
    string PubDate,
    bool Explicit,
    string Author,
    string Duration,
    string MediaRestrictionCountry,
    string EnclosureUrl,
    long EnclosureByteLength
)
{
    public bool Explicit { get; } = Explicit;
    public string Author { get; } = Author;
    public string MediaRestrictionCountry { get; } = MediaRestrictionCountry;
}
