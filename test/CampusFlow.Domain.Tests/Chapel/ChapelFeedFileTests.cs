using System;
using CampusFlow.Chapel;
using Shouldly;
using Xunit;

namespace CampusFlow.Chapel;

public class ChapelFeedFileTests
{
    [Fact]
    public void KeepsAnImmutableCopyOfTheDownloadedBytes()
    {
        var content = new byte[] { 1, 2, 3 };
        var file = new ChapelFeedFile(Guid.NewGuid(), Guid.NewGuid(),
            "ChapelAttendanceDaily.csv", new string('a', 64), content, 1, DateTime.UtcNow);

        content[0] = 9;
        file.Content[0].ShouldBe((byte)1);
        file.Sha256.ShouldBe(new string('A', 64));
        file.ScanCount.ShouldBe(1);
    }

    [Fact]
    public void RejectsEmptyOrUnidentifiedFiles()
    {
        Should.Throw<ArgumentException>(() => new ChapelFeedFile(Guid.NewGuid(), null,
            "file.csv", "bad", [1], 1, DateTime.UtcNow));
        Should.Throw<ArgumentException>(() => new ChapelFeedFile(Guid.NewGuid(), null,
            "file.csv", new string('A', 64), [], 0, DateTime.UtcNow));
    }
}
