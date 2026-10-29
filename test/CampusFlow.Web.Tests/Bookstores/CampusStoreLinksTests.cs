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
    public void Bill_approval_and_landing_page_use_the_same_course_link()
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

        Assert.Equal("https://judahsoutfitters.bibliucampus.com/SelectCourses?src=2&type=2&stoid=241&trm=Spring%2027&cid=000000000123456", link);
    }
}
