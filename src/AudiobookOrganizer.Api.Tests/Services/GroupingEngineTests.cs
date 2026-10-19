using System.Text.Json;
using AudiobookOrganizer.Api.Services.Grouping;

namespace AudiobookOrganizer.Api.Tests.Services;

public class GroupingEngineTests
{
    private static long _nextId;

    private static GroupingInput Mp3(string path, string? album = null, string? artist = null,
        string? albumArtist = null, int? track = null, int? disc = null, long? durationMs = null) =>
        new(Interlocked.Increment(ref _nextId), path, "mp3", null, album, artist, albumArtist, track, disc, durationMs);

    private static long[] Ids(ProposedGroup group) => group.Files.Select(f => f.FileId).ToArray();

    [Fact]
    public void Scenario_A_single_m4b_at_root_is_one_group()
    {
        var file = new GroupingInput(1, "ready-player-one.m4b", "m4b", DurationMs: 1000);

        var result = GroupingEngine.Group([file]);

        var group = Assert.Single(result.Groups);
        Assert.Equal([1L], Ids(group));
        Assert.True(group.Confidence >= GroupConfidence.Medium);
        Assert.Contains(group.Evidence, e => e.Contains("M4B") && e.Contains("ready-player-one.m4b"));
        Assert.Equal("ready-player-one", group.EstimatedTitle);
        Assert.Equal(1000, group.TotalDurationMs);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Scenario_B_untagged_tracks_in_a_book_folder_are_one_medium_group()
    {
        var t3 = Mp3("ready player one - ernest cline/track3.mp3", durationMs: 30);
        var t1 = Mp3("ready player one - ernest cline/track1.mp3", durationMs: 10);
        var t2 = Mp3("ready player one - ernest cline/track2.mp3", durationMs: 20);

        var result = GroupingEngine.Group([t3, t1, t2]);

        var group = Assert.Single(result.Groups);
        Assert.Equal([t1.FileId, t2.FileId, t3.FileId], Ids(group));
        Assert.Equal([1, 2, 3], group.Files.Select(f => f.Track!.Value));
        Assert.Equal("ready player one", group.EstimatedTitle);
        Assert.Equal("ernest cline", group.EstimatedAuthor);
        Assert.Equal(GroupConfidence.Medium, group.Confidence);
        Assert.Contains("Same folder \"ready player one - ernest cline\"", group.Evidence);
        Assert.Equal(60, group.TotalDurationMs);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Scenario_C_disc_folders_make_one_group_ordered_by_disc_then_track()
    {
        var d2t2 = Mp3("rpo - ernest cline/disc 2/track2.mp3");
        var d1t1 = Mp3("rpo - ernest cline/disc 1/track1.mp3");
        var d2t1 = Mp3("rpo - ernest cline/disc 2/track1.mp3");
        var d1t2 = Mp3("rpo - ernest cline/disc 1/track2.mp3");

        var result = GroupingEngine.Group([d2t2, d1t1, d2t1, d1t2]);

        var group = Assert.Single(result.Groups);
        Assert.Equal([d1t1.FileId, d1t2.FileId, d2t1.FileId, d2t2.FileId], Ids(group));
        Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], group.Files.Select(f => (f.Disc!.Value, f.Track!.Value)));
        Assert.Contains("Disc folders \"disc 1\", \"disc 2\"", group.Evidence);
        Assert.Equal("rpo", group.EstimatedTitle);
        Assert.Equal("ernest cline", group.EstimatedAuthor);
        Assert.NotEqual(GroupConfidence.Low, group.Confidence);
    }

    [Fact]
    public void Scenario_D_two_filename_series_at_root_are_two_groups()
    {
        var files = new[]
        {
            Mp3("03-rpo2-ernestcline.mp3"), Mp3("01-rpo1-ernestcline.mp3"), Mp3("02-rpo2-ernestcline.mp3"),
            Mp3("03-rpo1-ernestcline.mp3"), Mp3("01-rpo2-ernestcline.mp3"), Mp3("02-rpo1-ernestcline.mp3"),
        };

        var result = GroupingEngine.Group(files);

        Assert.Equal(2, result.Groups.Count);
        Assert.Empty(result.Unresolved);
        foreach (var stem in new[] { "rpo1-ernestcline", "rpo2-ernestcline" })
        {
            var group = Assert.Single(result.Groups, g => g.EstimatedTitle == stem);
            Assert.Equal([1, 2, 3], group.Files.Select(f => f.Track!.Value));
            var paths = group.Files.Select(f => files.Single(x => x.FileId == f.FileId).RelativePath);
            Assert.All(paths, p => Assert.Contains(stem, p));
            Assert.Contains($"Filename pattern \"{stem}\"", group.Evidence);
        }
    }

    [Fact]
    public void Scenario_E_generic_names_with_a_shared_album_tag_are_one_high_group()
    {
        var c = Mp3("some folder/c.mp3", album: "Heartwood", artist: "Some Author", track: 3);
        var a = Mp3("some folder/a.mp3", album: "Heartwood", artist: "Some Author", track: 1);
        var b = Mp3("some folder/b.mp3", album: "Heartwood", artist: "Some Author", track: 2);

        var result = GroupingEngine.Group([c, a, b]);

        var group = Assert.Single(result.Groups);
        Assert.Equal([a.FileId, b.FileId, c.FileId], Ids(group));
        Assert.Equal(GroupConfidence.High, group.Confidence);
        Assert.Contains("Album tag \"Heartwood\" on all 3 files", group.Evidence);
        Assert.Equal("Heartwood", group.EstimatedTitle);
        Assert.Equal("Some Author", group.EstimatedAuthor);
    }

    [Fact]
    public void Loose_untagged_root_files_with_no_shared_stem_are_unresolved()
    {
        var random = Mp3("random.mp3");
        var other = Mp3("other thing.mp3");

        var result = GroupingEngine.Group([random, other]);

        Assert.Empty(result.Groups);
        Assert.Equal(new[] { random.FileId, other.FileId }.Order(), result.Unresolved);
    }

    [Fact]
    public void Repeated_track_numbers_without_disc_info_give_low_confidence()
    {
        var files = new[]
        {
            Mp3("mystery book/part a.mp3", track: 1), Mp3("mystery book/part b.mp3", track: 1),
            Mp3("mystery book/part c.mp3", track: 2), Mp3("mystery book/part d.mp3", track: 2),
        };

        var result = GroupingEngine.Group(files);

        var group = Assert.Single(result.Groups);
        Assert.Equal(4, group.Files.Count);
        Assert.Equal(GroupConfidence.Low, group.Confidence);
    }

    [Fact]
    public void Album_tags_that_differ_only_slightly_give_low_confidence()
    {
        var result = GroupingEngine.Group(
        [
            Mp3("book/a.mp3", album: "Heartwood", artist: "X", track: 1),
            Mp3("book/b.mp3", album: "Heartwood.", artist: "X", track: 2),
        ]);

        var group = Assert.Single(result.Groups);
        Assert.Equal(GroupConfidence.Low, group.Confidence);
    }

    [Fact]
    public void Shuffled_input_gives_an_identical_result()
    {
        var files = new List<GroupingInput>
        {
            new(900, "ready-player-one.m4b", "m4b"),
            Mp3("rpo - ernest cline/disc 1/track1.mp3"), Mp3("rpo - ernest cline/disc 2/track1.mp3"),
            Mp3("rpo - ernest cline/disc 1/track2.mp3"), Mp3("rpo - ernest cline/disc 2/track2.mp3"),
            Mp3("01-rpo1-ernestcline.mp3"), Mp3("02-rpo1-ernestcline.mp3"),
            Mp3("01-rpo2-ernestcline.mp3"), Mp3("02-rpo2-ernestcline.mp3"),
            Mp3("random.mp3"), Mp3("other thing.mp3"),
            Mp3("e/a.mp3", album: "Heartwood", artist: "Some Author", track: 1),
            Mp3("e/b.mp3", album: "Heartwood", artist: "Some Author", track: 2),
        };
        var expected = JsonSerializer.Serialize(GroupingEngine.Group(files));

        var rng = new Random(42);
        for (var i = 0; i < 10; i++)
        {
            var shuffled = files.OrderBy(_ => rng.Next()).ToList();
            Assert.Equal(expected, JsonSerializer.Serialize(GroupingEngine.Group(shuffled)));
        }
    }

    [Theory]
    [InlineData("disk2", 2)]
    [InlineData("CD 01", 1)]
    [InlineData("cd1", 1)]
    [InlineData("Part 2", 2)]
    public void Disc_folder_names_supply_the_disc_and_container_is_the_grandparent(string discFolder, int disc)
    {
        var result = GroupingEngine.Group(
        [
            Mp3($"book - author/{discFolder}/01.mp3"),
            Mp3("book - author/other/01.mp3"),
        ]);

        var group = Assert.Single(result.Groups, g => g.Files.Any(f => f.Disc == disc));
        Assert.Contains("Same folder \"book - author\"", group.Evidence);
    }

    [Theory]
    [InlineData("01-rpo1-ernestcline.mp3", "02-rpo1-ernestcline.mp3", "rpo1-ernestcline", 1)]
    [InlineData("01 Some Book.mp3", "02 Some Book.mp3", "some book", 1)]
    [InlineData("Some  Book - 003.mp3", "Some Book - 004.mp3", "some book", 3)]
    [InlineData("Some Book (12).mp3", "Some Book (13).mp3", "some book", 12)]
    [InlineData("track7 Some Book.mp3", "track8 Some Book.mp3", "some book", 7)]
    public void Filename_stem_and_track_are_parsed(string first, string second, string stem, int track)
    {
        // Two root files sharing a stem form one group, titled by the stem.
        var group = Assert.Single(GroupingEngine.Group([Mp3(second), Mp3(first)]).Groups);

        Assert.Equal(stem, group.EstimatedTitle);
        Assert.Equal([track, track + 1], group.Files.Select(f => f.Track!.Value));
    }

    [Fact]
    public void Root_files_with_no_usable_stem_are_unresolved()
    {
        var result = GroupingEngine.Group([Mp3("track1.mp3"), Mp3("track2.mp3")]);

        Assert.Empty(result.Groups);
        Assert.Equal(2, result.Unresolved.Count);
    }

    [Fact]
    public void Natural_filename_sort_breaks_ties_when_there_are_no_tracks()
    {
        var p10 = Mp3("book/part10x.mp3");
        var p2 = Mp3("book/part2x.mp3");
        var p1 = Mp3("book/part1x.mp3");

        var group = Assert.Single(GroupingEngine.Group([p10, p2, p1]).Groups);

        Assert.Equal([p1.FileId, p2.FileId, p10.FileId], Ids(group));
        Assert.All(group.Files, f => Assert.Null(f.Track));
    }
}
