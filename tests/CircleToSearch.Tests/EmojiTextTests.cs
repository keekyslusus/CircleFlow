using System.IO;
using System.IO.Compression;
using System.Windows.Controls;
using System.Windows.Documents;
using CircleToSearch.Ui.Emoji;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class EmojiTextTests
{
    private const string Trombone = "\U0001FA8A";
    private const string Couple = "\U0001F469‍❤️‍\U0001F468";
    private const string England = "\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F";

    [Fact]
    public void Bundled_archive_draws_new_and_combined_emoji_as_images()
    {
        RunSta(() =>
        {
            var block = new TextBlock { FontFamily = new("Segoe UI Variable Text"), FontSize = 12 };
            new EmojiText(new AppPaths().EmojiArchivePath).SetText(block, $"Band {Trombone}{Couple} {England}!");

            var inlines = block.Inlines.ToList();
            Assert.Equal(["Band ", " ", "!"], inlines.OfType<Run>().Select(run => run.Text));
            var images = inlines.OfType<InlineUIContainer>().Select(inline => Assert.IsType<Image>(inline.Child)).ToList();
            Assert.Equal(3, images.Count);
            Assert.All(images, image =>
            {
                Assert.NotNull(image.Source);
                Assert.Equal(12 * 1.15, image.Height, 3);
            });
            Assert.True(block.ClipToBounds);
        });
    }

    [Fact]
    public void Every_alias_in_the_bundled_archive_points_at_an_image()
    {
        using var archive = ZipFile.OpenRead(new AppPaths().EmojiArchivePath);
        using var reader = new StreamReader(archive.GetEntry("aliases.txt")!.Open());
        var targets = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[1]);
        var images = archive.Entries.Select(entry => entry.Name).ToHashSet();
        Assert.All(targets, target => Assert.Contains(target + ".png", images));
    }

    [Fact]
    public void Missing_archive_keeps_the_text_as_is()
    {
        RunSta(() =>
        {
            var block = new TextBlock();
            var text = $"Band {Trombone}{Couple}";
            new EmojiText(Path.Combine(TestOutputPaths.TempDirectory, "missing-emoji.zip")).SetText(block, text);
            Assert.Equal(text, string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text)));
            Assert.DoesNotContain(block.Inlines, inline => inline is InlineUIContainer);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
