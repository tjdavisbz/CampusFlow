using System;
using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Bookstores;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.Bookstores;

public class CampusStoreLinksTests
{
    [Fact]
    public void Course_link_uses_registration_CRN_instead_of_POS_export_padding()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CampusStores:nelson:CourseSelectionUrl"] = "https://judahsoutfitters.bibliucampus.com/SelectCourses",
            ["CampusStores:nelson:StoreId"] = "241"
        }).Build();
        var courses = new[]
        {
            new StudentCourseScheduleItem(StudentInformationSystemProvider.ThesisElements,
                "1", "123456", "269", "2027SP", "Spring 2027", "ENG", "1301", "", "1",
                "Composition", 3, "Official", null, null, "Instructor", null, null, null, null,
                null, null, null)
        };

        var link = CampusStoreLinks.ForCourses(configuration, "nelson", "Spring 2027", courses);

        Assert.Equal("https://judahsoutfitters.bibliucampus.com/SelectCourses?src=2&type=2&stoid=241&trm=Spring%2027&cid=123456", link);
    }

    [Fact]
    public void Normalizes_and_deduplicates_CRNs_and_keeps_distinct_courses()
    {
        var link = CampusStoreLinks.ForCourses(Configuration(), "nelson", "Spring 2027",
            new[] { Course("73313"), Course("000000000073313"), Course("73332") });

        Assert.EndsWith("&cid=73313,73332", link);
    }

    [Theory]
    [InlineData("Fall 2026", "Fall%2026")]
    [InlineData("Spring 2027", "Spring%2027")]
    [InlineData("Summer 2027", "Summer%2027")]
    public void Keeps_the_selected_term_in_the_link(string term, string encodedTerm)
    {
        var link = CampusStoreLinks.ForCourses(Configuration(), "nelson", term, new[] { Course("73313") });

        Assert.Contains("&trm=" + encodedTerm + "&cid=73313", link);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-73313")]
    [InlineData("+73313")]
    [InlineData("73313&cid=999")]
    [InlineData("9223372036854775808")]
    public void Does_not_offer_a_course_link_without_a_valid_positive_CRN(string id)
    {
        Assert.Null(CampusStoreLinks.ForCourses(Configuration(), "nelson", "Spring 2027", new[] { Course(id) }));
    }

    [Fact]
    public void Does_not_offer_a_link_without_store_configuration()
    {
        Assert.Null(CampusStoreLinks.ForCourses(new ConfigurationBuilder().Build(), "nelson", "Spring 2027", new[] { Course("73313") }));
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CampusStores:nelson:CourseSelectionUrl"] = "https://judahsoutfitters.bibliucampus.com/SelectCourses",
            ["CampusStores:nelson:StoreId"] = "241"
        }).Build();

    private static StudentCourseScheduleItem Course(string id) => new(StudentInformationSystemProvider.ThesisElements,
        "1", id, "269", "B27C", "Spring 2027", "ACC", "2213", "", "100", "Accounting", 3,
        "Official", null, null, "Instructor", null, null, null, null, null, null, null);
}
