using CircleToSearch.Ui.Emoji;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class EmojiSequencesTests
{
    private static readonly EmojiSequences Sequences = new(
        ["1f60d", "2764", "1f525", "2764_200d_1f525", "1f469", "1f468", "1f469_200d_2764_200d_1f468", "1f44d",
            "1f44d_1f3fd", "1f3fd", "1f1f7", "1f1fa", "1f1f7_1f1fa", "31_20e3", "a9", "1fa8a"],
        [0x2764, 0xA9]);

    [Fact]
    public void Plain_text_stays_one_part()
    {
        Assert.Equal([new EmojiTextPart("Cozy room ideas", null)], Split("Cozy room ideas"));
        Assert.Empty(Split(""));
    }

    [Fact]
    public void Emoji_between_text_become_their_own_parts()
    {
        Assert.Equal(
            [new("Room ", null), new("\U0001F60D", "1f60d"), new("\U0001FA8A", "1fa8a"), new(" decor", null)],
            Split("Room \U0001F60D\U0001FA8A decor"));
    }

    [Theory]
    [InlineData("❤️‍\U0001F525", "2764_200d_1f525")]
    [InlineData("❤‍\U0001F525", "2764_200d_1f525")]
    [InlineData("\U0001F469‍❤️‍\U0001F468", "1f469_200d_2764_200d_1f468")]
    [InlineData("\U0001F44D\U0001F3FD", "1f44d_1f3fd")]
    [InlineData("\U0001F1F7\U0001F1FA", "1f1f7_1f1fa")]
    [InlineData("1️⃣", "31_20e3")]
    [InlineData("1⃣", "31_20e3")]
    public void Longest_sequence_wins_with_or_without_variation_selectors(string text, string key) =>
        Assert.Equal([new EmojiTextPart(text, key)], Split(text));

    [Fact]
    public void Unknown_sequences_fall_back_to_their_known_components()
    {
        Assert.Equal(
            [new("\U0001F60D", "1f60d"), new("‍", null), new("\U0001F525", "1f525")],
            Split("\U0001F60D‍\U0001F525"));
        Assert.Equal([new("\U0001F1F7", "1f1f7"), new("x", null)], Split("\U0001F1F7x"));
    }

    [Fact]
    public void Keycap_bases_without_a_keycap_stay_text() =>
        Assert.Equal([new EmojiTextPart("#1 2", null)], Split("#1 2"));

    [Fact]
    public void Text_default_symbols_follow_the_text_font_unless_a_selector_decides()
    {
        Assert.Equal([new EmojiTextPart("❤", "2764")], Split("❤", hasTextGlyph: false));
        Assert.Equal([new EmojiTextPart("© 2026", null)], Split("© 2026", hasTextGlyph: true));
        Assert.Equal([new EmojiTextPart("❤", null)], Split("❤", hasTextGlyph: true));
        Assert.Equal([new EmojiTextPart("❤️", "2764")], Split("❤️", hasTextGlyph: true));
        Assert.Equal([new EmojiTextPart("❤︎", null)], Split("❤︎", hasTextGlyph: false));
    }

    [Fact]
    public void Parts_always_join_back_to_the_original_text()
    {
        const string text = "a️\U0001F469‍❤️‍\U0001F468‍ b\uD83D \U0001F44D\U0001F3FD\U0001F3FD❤";
        Assert.Equal(text, string.Concat(Split(text).Select(part => part.Text)));
    }

    [Fact]
    public void Empty_set_leaves_text_unchanged() =>
        Assert.Equal([new EmojiTextPart("\U0001F60D", null)], EmojiSequences.Empty.Split("\U0001F60D", _ => false));

    private static IReadOnlyList<EmojiTextPart> Split(string text, bool hasTextGlyph = false) =>
        Sequences.Split(text, _ => hasTextGlyph);
}
