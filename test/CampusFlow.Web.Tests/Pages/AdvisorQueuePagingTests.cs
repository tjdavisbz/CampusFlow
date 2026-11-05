using System;
using System.Linq;
using CampusFlow.AdvisorPortal;
using CampusFlow.Web.Pages.Advisor;
using Xunit;

namespace CampusFlow.Pages;

public sealed class AdvisorQueuePagingTests
{
    [Fact]
    public void Name_search_matches_partial_words_in_either_order()
    {
        var queue = new[] { Student("Chris Wallace", "1001"), Student("Wallace Jones", "1002") };

        var result = AdvisorQueuePaging.Apply(queue, "  WALL,  chris  ", 1, 25);

        Assert.Equal("WALL chris", result.Search);
        Assert.Equal("Chris Wallace", Assert.Single(result.Students).StudentName);
    }

    [Fact]
    public void Id_search_and_paging_keep_the_requested_slice()
    {
        var queue = Enumerable.Range(1, 52)
            .Select(i => Student($"Student {i}", $"{i:0000}"))
            .ToArray();

        var secondPage = AdvisorQueuePaging.Apply(queue, null, 2, 25);
        var lastPage = AdvisorQueuePaging.Apply(queue, null, 99, 25);
        var byId = AdvisorQueuePaging.Apply(queue, "0052", 1, 25);

        Assert.Equal(52, secondPage.MatchingStudents);
        Assert.Equal(25, secondPage.Students.Count);
        Assert.Equal("Student 26", secondPage.Students[0].StudentName);
        Assert.Equal(3, lastPage.PageNumber);
        Assert.Equal(2, lastPage.Students.Count);
        Assert.Equal("Student 52", Assert.Single(byId.Students).StudentName);
    }

    private static AdvisorQueueItemDto Student(string name, string id) => new()
    {
        StudentProfileId = Guid.NewGuid(),
        ExternalTermId = "269",
        StudentName = name,
        StudentId = id,
        AttendanceType = "Residential Undergraduate"
    };
}
