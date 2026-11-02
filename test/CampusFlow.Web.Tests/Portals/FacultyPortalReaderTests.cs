using CampusFlow.Web.Portals;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class FacultyPortalReaderTests
{
    private static readonly FacultyIdentity Darren = new(13589, "Darren Daugherty");
    private static readonly FacultyIdentity PracticalMinistries = new(296170, "* Practical Ministries");

    [Fact]
    public void Single_Email_Match_Does_Not_Require_Alias_Match()
    {
        FacultyPortalReader.SelectFacultyIdentity("darren@nelson.edu",
            [(Darren, "ddaugherty")]).ShouldBe(Darren);
    }

    [Fact]
    public void Duplicate_Email_Uses_Unique_Matching_Portal_Alias()
    {
        FacultyPortalReader.SelectFacultyIdentity("DDaugherty@nelson.edu",
            [(Darren, " ddaugherty "), (PracticalMinistries, "Practical")])
            .ShouldBe(Darren);
    }

    [Fact]
    public void Duplicate_Email_Without_Alias_Match_Is_Denied()
    {
        FacultyPortalReader.SelectFacultyIdentity("darren@nelson.edu",
            [(Darren, "ddaugherty"), (PracticalMinistries, "Practical")])
            .ShouldBeNull();
    }

    [Fact]
    public void Duplicate_Email_With_Multiple_Alias_Matches_Is_Denied()
    {
        FacultyPortalReader.SelectFacultyIdentity("ddaugherty@nelson.edu",
            [(Darren, "ddaugherty"), (PracticalMinistries, "DDAUGHERTY")])
            .ShouldBeNull();
    }

    [Fact]
    public void No_Email_Match_Is_Denied()
    {
        FacultyPortalReader.SelectFacultyIdentity("ddaugherty@nelson.edu", [])
            .ShouldBeNull();
    }
}
