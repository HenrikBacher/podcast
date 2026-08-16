namespace DrPodcast.Tests;

/// <summary>
/// The asset-hash check streams the feed in 64 KB chunks rather than decoding it whole.
/// The carry-over between chunks is the part that can silently go wrong, so it is pinned here.
/// </summary>
public class FileContainsAsciiTests : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private readonly List<string> _files = [];

    private string WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"contains-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _files) if (File.Exists(f)) File.Delete(f);
        GC.SuppressFinalize(this);
    }

    private static Task<bool> Contains(string path, string needle) =>
        FeedGenerationService.FileContainsAsciiAsync(path, needle, CancellationToken.None);

    [Fact]
    public async Task FindsNeedleAtStart()
    {
        var path = WriteFile("abcdef" + new string('.', 1000));
        (await Contains(path, "abcdef")).Should().BeTrue();
    }

    [Fact]
    public async Task FindsNeedleAtEnd()
    {
        var path = WriteFile(new string('.', 1000) + "abcdef");
        (await Contains(path, "abcdef")).Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsFalseWhenAbsent()
    {
        var path = WriteFile(new string('.', 5000));
        (await Contains(path, "abcdef")).Should().BeFalse();
    }

    [Fact]
    public async Task ReturnsFalseForEmptyFile()
    {
        var path = WriteFile("");
        (await Contains(path, "abcdef")).Should().BeFalse();
    }

    [Fact]
    public async Task IsCaseSensitiveLikeTheOriginalOrdinalSearch()
    {
        var path = WriteFile("ABCDEF");
        (await Contains(path, "abcdef")).Should().BeFalse();
    }

    // The whole point of the carry-over: a needle split across a chunk boundary.
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(63)]
    public async Task FindsNeedleStraddlingAChunkBoundary(int bytesBeforeBoundary)
    {
        var needle = new string('a', 64);
        var prefix = new string('.', BufferSize - bytesBeforeBoundary);
        var path = WriteFile(prefix + needle + new string('.', 100));

        (await Contains(path, needle)).Should().BeTrue();
    }

    [Fact]
    public async Task FindsNeedleExactlyAtChunkBoundary()
    {
        var needle = new string('a', 64);
        var path = WriteFile(new string('.', BufferSize) + needle);

        (await Contains(path, needle)).Should().BeTrue();
    }

    [Fact]
    public async Task FindsNeedleSpanningThirdChunk()
    {
        var needle = new string('a', 64);
        var path = WriteFile(new string('.', (BufferSize * 2) - 10) + needle);

        (await Contains(path, needle)).Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsFalseAcrossManyChunksWhenAbsent()
    {
        var path = WriteFile(new string('.', BufferSize * 3 + 77));
        (await Contains(path, new string('a', 64))).Should().BeFalse();
    }

    // A partial needle straddling the boundary must not produce a false positive.
    [Fact]
    public async Task DoesNotFalselyMatchPartialNeedleAtBoundary()
    {
        var half = new string('a', 32);
        var path = WriteFile(new string('.', BufferSize - 32) + half + new string('.', 100) + half);

        (await Contains(path, new string('a', 64))).Should().BeFalse();
    }

    [Fact]
    public async Task HandlesFileShorterThanTheNeedle()
    {
        var path = WriteFile("abc");
        (await Contains(path, new string('a', 64))).Should().BeFalse();
    }

    [Fact]
    public async Task RealisticFeed_FindsRotatedAssetHash()
    {
        var hash = "3f2a" + new string('c', 60);
        var items = string.Concat(Enumerable.Range(0, 5000).Select(i =>
            $"<item><guid>{i}</guid><enclosure url=\"https://api.dr.dk/x/{new string('b', 64)}\"/></item>"));
        var path = WriteFile($"<rss><channel>{items}<item><enclosure url=\"https://api.dr.dk/x/{hash}\"/></item></channel></rss>");

        (await Contains(path, hash)).Should().BeTrue();
        (await Contains(path, "9" + new string('f', 63))).Should().BeFalse();
    }
}
