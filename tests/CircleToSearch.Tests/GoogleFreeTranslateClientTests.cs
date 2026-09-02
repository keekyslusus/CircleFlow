namespace CircleToSearch.Tests;

using System.Windows;
using CircleToSearch.Ocr;
using CircleToSearch.Translation;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;

public sealed class GoogleFreeTranslateClientTests
{
    [Fact]
    public void ParseTranslationJson_extracts_sentences_correctly()
    {
        var json = @"[[[""Привет мир"",""Hello world"",null,null,10]],null,""en"",null,null,null,null,[]]";
        var result = GoogleFreeTranslateClient.ParseTranslationJson(json);
        Assert.Equal("Привет мир", result);
    }

    [Fact]
    public void ParseTranslationJson_concatenates_multiple_sentences()
    {
        var json = @"[[[""Первое предложение. "",""First sentence. "",null,null,10],[""Второе предложение."",""Second sentence."",null,null,10]],null,""en""]";
        var result = GoogleFreeTranslateClient.ParseTranslationJson(json);
        Assert.Equal("Первое предложение. Второе предложение.", result);
    }

    [Fact]
    public void ParseTranslationJson_handles_empty_and_malformed_json()
    {
        Assert.Equal(string.Empty, GoogleFreeTranslateClient.ParseTranslationJson("[]"));
        Assert.Equal(string.Empty, GoogleFreeTranslateClient.ParseTranslationJson("[[]]"));
    }

    [Fact]
    public void GroupLinesIntoBlocks_groups_adjacent_lines_and_separates_distant_lines()
    {
        var word1 = new OcrWordSnapshot("Line1", new Rect(10, 10, 50, 20), new GdiRectangle(10, 10, 50, 20));
        var line1 = new OcrLineSnapshot("Line 1", new Rect(10, 10, 100, 20), new GdiRectangle(10, 10, 100, 20), [word1]);

        var word2 = new OcrWordSnapshot("Line2", new Rect(10, 34, 50, 20), new GdiRectangle(10, 34, 50, 20));
        var line2 = new OcrLineSnapshot("Line 2", new Rect(10, 34, 100, 20), new GdiRectangle(10, 34, 100, 20), [word2]);

        var word3 = new OcrWordSnapshot("Line3", new Rect(10, 200, 50, 20), new GdiRectangle(10, 200, 50, 20));
        var line3 = new OcrLineSnapshot("Line 3", new Rect(10, 200, 100, 20), new GdiRectangle(10, 200, 100, 20), [word3]);

        var groups = GoogleFreeTranslateClient.GroupLinesIntoBlocks([line1, line2, line3]);
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Count); // line1 and line2
        Assert.Single(groups[1]); // line3 in separate group
    }
}
