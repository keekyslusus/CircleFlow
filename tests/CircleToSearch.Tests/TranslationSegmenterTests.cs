using System.Text;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TranslationSegmenterTests
{
    [Fact]
    public void Every_chunk_is_within_500_utf8_bytes_and_reconstructs_input()
    {
        var text = string.Concat(Enumerable.Repeat("Привет 世界 😀. ", 100));
        var chunks = new TranslationSegmenter().Segment(4, text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.InRange(Encoding.UTF8.GetByteCount(chunk.Text), 1, 500));
        Assert.Equal(text, string.Concat(chunks.Select(chunk => chunk.Text)));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Order));
    }

    [Fact]
    public void Exact_boundary_stays_in_one_chunk_and_surrogate_pair_is_not_split()
    {
        Assert.Single(new TranslationSegmenter().Segment(0, new string('a', 500)));
        var chunks = new TranslationSegmenter(5).Segment(0, "😀😀");
        Assert.Equal(["😀", "😀"], chunks.Select(chunk => chunk.Text));
    }
}
