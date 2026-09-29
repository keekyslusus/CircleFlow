using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicHistoryTests
{
    private static readonly ShazamRecognition Lights =
        new("Blinding Lights", "The Weeknd", "After Hours", "R&B/Soul", "1", "https://example.com/1.jpg", "https://www.shazam.com/track/1");
    private static readonly ShazamRecognition City =
        new("Midnight City", "M83", null, null, null, null, null);

    [Fact]
    public void Recorded_tracks_are_listed_newest_first_and_survive_a_restart()
    {
        var harness = new Harness();
        var changes = 0;
        harness.History.Changed += () => changes++;

        harness.History.Record(Lights);
        harness.Time.Now = harness.Time.Now.AddMinutes(5);
        harness.History.Record(City);

        Assert.Equal(2, changes);
        Assert.Equal([City, Lights], harness.History.Entries.Select(entry => entry.Track));
        var reloaded = harness.Reopen().Entries;
        Assert.Equal(harness.History.Entries, reloaded);
        Assert.Equal(harness.Time.Now, reloaded[0].RecognizedAt);
        Assert.Empty(Directory.GetFiles(harness.Directory, "*.tmp"));
    }

    [Fact]
    public void Nothing_is_recorded_while_saving_is_off()
    {
        var harness = new Harness { Saving = false };

        harness.History.Record(Lights);

        Assert.Empty(harness.History.Entries);
        Assert.False(File.Exists(harness.FilePath));
    }

    [Fact]
    public void Tracks_older_than_the_retention_are_hidden_and_removed_on_the_next_change()
    {
        var harness = new Harness { RetentionDays = 28 };
        harness.History.Record(Lights);
        harness.Time.Now = harness.Time.Now.AddDays(20);
        harness.History.Record(City);

        harness.RetentionDays = 7;
        Assert.Equal([City], harness.History.Entries.Select(entry => entry.Track));

        harness.RetentionDays = MusicHistory.KeepForever;
        Assert.Equal(2, harness.History.Entries.Count);
        Assert.Equal(2, harness.Reopen().Entries.Count);

        harness.RetentionDays = 7;
        Assert.True(harness.History.ApplyRetention());
        harness.RetentionDays = MusicHistory.KeepForever;
        Assert.Equal([City], harness.Reopen().Entries.Select(entry => entry.Track));
    }

    [Fact]
    public void Only_the_newest_entries_are_kept()
    {
        var harness = new Harness();
        for (var index = 0; index <= MusicHistory.MaxEntries; index++)
        {
            harness.Time.Now = harness.Time.Now.AddMinutes(1);
            harness.History.Record(City with { Title = "Track " + index });
        }

        var entries = harness.Reopen().Entries;
        Assert.Equal(MusicHistory.MaxEntries, entries.Count);
        Assert.Equal("Track " + MusicHistory.MaxEntries, entries[0].Track.Title);
        Assert.Equal("Track 1", entries[^1].Track.Title);
    }

    [Fact]
    public void Clear_removes_every_track()
    {
        var harness = new Harness();
        harness.History.Record(Lights);
        var changes = 0;
        harness.History.Changed += () => changes++;

        Assert.True(harness.History.Clear());

        Assert.Equal(1, changes);
        Assert.Empty(harness.History.Entries);
        Assert.Empty(harness.Reopen().Entries);
    }

    [Fact]
    public void A_damaged_file_starts_an_empty_history()
    {
        var harness = new Harness();
        File.WriteAllText(harness.FilePath, "[{\"Track\": broken");

        Assert.Empty(harness.History.Entries);
        harness.History.Record(Lights);
        Assert.Equal([Lights], harness.Reopen().Entries.Select(entry => entry.Track));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_overwritten()
    {
        var harness = new Harness();
        harness.History.Record(Lights);
        var history = harness.Reopen();
        var saved = File.ReadAllText(harness.FilePath);

        using (new FileStream(harness.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(history.Entries);
            history.Record(City);
            Assert.False(history.Clear());
        }

        Assert.Equal(saved, File.ReadAllText(harness.FilePath));
        Assert.Equal([Lights], history.Entries.Select(entry => entry.Track));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Directory = TestOutputPaths.NewTempDirectory("music-history-" + Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(Directory, "music-history.json");
            History = Reopen();
        }

        public string Directory { get; }
        public string FilePath { get; }
        public bool Saving { get; set; } = true;
        public int RetentionDays { get; set; } = MusicHistory.KeepForever;
        public TestTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 29, 16, 40, 0, TimeSpan.Zero));
        public MusicHistory History { get; }

        public MusicHistory Reopen() =>
            new(FilePath, () => Saving, () => RetentionDays, Time, new PluginLog(Directory));
    }
}
