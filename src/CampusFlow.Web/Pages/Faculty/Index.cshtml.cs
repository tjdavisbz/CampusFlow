using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Announcements;
using CampusFlow.Web.Portals;
using CampusFlow.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Web.Pages.Faculty;

[Authorize]
public class IndexModel(
    FacultyPortalReader facultyReader,
    ICurrentStudentView currentStudentView,
    IRepository<PortalAnnouncement, Guid> announcements) : CampusFlowPageModel
{
    public FacultyIdentity Faculty { get; private set; } = null!;
    public IReadOnlyList<FacultyTerm> Terms { get; private set; } = [];
    public FacultyTerm? SelectedTerm { get; private set; }
    public IReadOnlyList<FacultyCourse> Courses { get; private set; } = [];
    public IReadOnlyList<FacultyMeeting> Meetings { get; private set; } = [];
    public IReadOnlyList<FacultyStudent> Students { get; private set; } = [];
    public IReadOnlyList<PortalAnnouncement> VisibleAnnouncements { get; private set; } = [];
    public string ViewName { get; private set; } = "home";

    public async Task<IActionResult> OnGetAsync(string? view, int? term)
    {
        if (currentStudentView.IsImpersonating) return Forbid();
        var viewedFacultyId = FacultyViewSession.GetFacultyId(HttpContext);
        var faculty = viewedFacultyId.HasValue
            ? await facultyReader.FindByIdAsync(viewedFacultyId.Value, HttpContext.RequestAborted)
            : await facultyReader.FindFacultyAsync(CurrentUser.Email, HttpContext.RequestAborted);
        if (faculty is null) return Forbid();
        Faculty = faculty;

        ViewName = view?.ToLowerInvariant() switch
        {
            "students" => "students",
            "schedule" => "schedule",
            "roster" => "roster",
            "courses" => "courses",
            null or "" or "home" => "home",
            _ => "unknown"
        };
        if (ViewName == "unknown") return NotFound();

        Terms = await facultyReader.GetTermsAsync(faculty.FacultyId, HttpContext.RequestAborted);
        var today = DateTime.Today;
        SelectedTerm = Terms.FirstOrDefault(x => x.Id == term)
            ?? Terms.FirstOrDefault(x => x.StartsAt <= today && x.EndsAt >= today)
            ?? Terms.FirstOrDefault();
        if (SelectedTerm is null) return Page();

        if (ViewName is "home" or "courses" or "roster")
            Courses = await facultyReader.GetCoursesAsync(faculty.FacultyId, SelectedTerm.Id,
                HttpContext.RequestAborted);
        if (ViewName is "home" or "schedule")
            Meetings = await facultyReader.GetMeetingsAsync(faculty.FacultyId, SelectedTerm.Id,
                HttpContext.RequestAborted);
        if (ViewName is "students" or "roster")
            Students = await facultyReader.GetStudentsAsync(faculty.FacultyId, SelectedTerm.Id,
                HttpContext.RequestAborted);
        if (ViewName == "home")
        {
            var now = DateTime.UtcNow;
            VisibleAnnouncements = (await announcements.GetListAsync())
                .Where(x => x.IsVisible(now, AnnouncementAudience.Faculty, SelectedTerm.Name))
                .OrderByDescending(x => x.StartsAtUtc).ToArray();
        }
        return Page();
    }
}
