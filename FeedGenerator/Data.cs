using System;
using System.Collections.Generic;

namespace FeedGenerator
{
    public class Group
    {
        public int Limit { get; }
        public int Offset { get; }
        public int TotalSize { get; }
        public Uri Next { get; }
        public string Title { get; }
        public string GroupId { get; }
        public List<Item> Items { get; }

        public Group(int limit, int offset, int totalSize, Uri next, string title, string groupId, List<Item> items)
        {
            Limit = limit;
            Offset = offset;
            TotalSize = totalSize;
            Next = next;
            Title = title;
            GroupId = groupId;
            Items = items;
        }
    }

    public class Show
    {
        public List<string> Categories { get; }
        public string Description { get; }
        public string Id { get; }
        public List<ImageAsset> ImageAssets { get; }
        public string IsAvailableOnDemand { get; }
        public string LatestEpisodeStartTime { get; }
        public string LearnId { get; }
        public string NumberOfEpisodes { get; }
        public string OcsUrn { get; }
        public string PodcastUrl { get; }
        public string PresentationType { get; }
        public string PresentationUrl { get; }
        public string ProductionNumber { get; }
        public string PsdbSlug { get; }
        public string PsdbUrn { get; }
        public string Punchline { get; }
        public string Slug { get; }
        public string SortLetter { get; }
        public string Title { get; }
        public string Type { get; }
        public VisualIdentity VisualIdentity { get; }

        // Constructor not shown for brevity
    }

    public class VisualIdentity
    {
        public Gradient Gradient { get; }

        // Constructor not shown for brevity
    }

    public class Gradient
    {
        public List<string> Colors { get; }

        // Constructor not shown for brevity
    }

    public class ImageAsset
    {
        public string Id { get; }
        public string Target { get; }
        public string Ratio { get; }
        public string Format { get; }

        // Constructor not shown for brevity
    }

    public class Episodes
    {
        public List<Item> Items { get; }
        public int Limit { get; }
        public int Offset { get; }
        public Uri Previous { get; }
        public Uri Next { get; }
        public Uri Self { get; }
        public int TotalSize { get; }

        // Constructor not shown for brevity
    }

    public class Item
    {
        public string Id { get; }
        public long DurationMilliseconds { get; }
        public string ProductionNumber { get; }
        public List<AudioAsset> AudioAssets { get; }
        public string Title { get; }
        public string Description { get; }
        public Uri PresentationUrl { get; }
        public string PublishTime { get; }

        // Constructor not shown for brevity
    }

    public class AudioAsset
    {
        public string Target { get; }
        public bool IsStreamLive { get; }
        public string Format { get; }
        public int Bitrate { get; }
        public long FileSize { get; }
        public Uri Url { get; }

        // Constructor not shown for brevity
    }
}