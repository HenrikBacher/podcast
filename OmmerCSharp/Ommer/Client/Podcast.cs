namespace Ommer.Client;

public record Podcast(
    string Urn,
    string Slug,
    string? TitleSuffix,
    string? DescriptionSuffix,
    string? FeedUrl,
    string? ImageUrl
);
