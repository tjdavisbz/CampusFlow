using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using CampusFlow.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CampusFlow.Web.Pages.Faculty;

[Authorize]
public class AdvisingModel(
    FacultyPortalReader facultyReader,
    FacultyAdvisingReader advisingReader,
    ICurrentStudentView currentStudentView,
    IEnumerable<IStudentInformationSystemDegreeAuditLookup> auditLookups,
    ILogger<AdvisingModel> logger) : CampusFlowPageModel
{
    public FacultyIdentity Faculty { get; private set; } = null!;
    public IReadOnlyList<FacultyTerm> Terms { get; private set; } = [];
    public FacultyTerm? SelectedTerm { get; private set; }
    public IReadOnlyList<FacultyAdvisee> Advisees { get; private set; } = [];
    public FacultyAdvisee? SelectedStudent { get; private set; }
    public IReadOnlyList<StudentDegreeAuditSummary> Audits { get; private set; } = [];
    public StudentDegreeAuditDetail? SelectedAudit { get; private set; }
    public bool AuditUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? term, int? student,
        int? revisionTermId, int? auditDegreeId, int? auditProgramId)
    {
        if (currentStudentView.IsImpersonating) return Forbid();
        var viewedFacultyId = FacultyViewSession.GetFacultyId(HttpContext);
        var faculty = viewedFacultyId.HasValue
            ? await facultyReader.FindByIdAsync(viewedFacultyId.Value, HttpContext.RequestAborted)
            : await facultyReader.FindFacultyAsync(CurrentUser.Email, HttpContext.RequestAborted);
        if (faculty is null || !await advisingReader.HasAdvisorAccessAsync(
                faculty.FacultyId, HttpContext.RequestAborted)) return Forbid();
        Faculty = faculty;

        Terms = await advisingReader.GetTermsAsync(faculty.FacultyId, HttpContext.RequestAborted);
        var today = DateTime.Today;
        SelectedTerm = Terms.FirstOrDefault(x => x.Id == term)
            ?? Terms.FirstOrDefault(x => x.StartsAt <= today && x.EndsAt >= today)
            ?? Terms.FirstOrDefault();
        if (SelectedTerm is null) return Page();

        Advisees = await advisingReader.GetAdviseesAsync(
            faculty.FacultyId, SelectedTerm.Id, HttpContext.RequestAborted);
        if (student is null) return Page();

        // Never load an audit from a student ID supplied by the browser until the current
        // faculty-to-advisor and advisor-to-student relationship has been checked for this term.
        SelectedStudent = Advisees.FirstOrDefault(x => x.StudentUid == student.Value);
        if (SelectedStudent is null) return NotFound();

        var lookup = auditLookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (lookup is null)
        {
            AuditUnavailable = true;
            return Page();
        }
        try
        {
            var studentUid = student.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Audits = await lookup.GetAuditsAsync(studentUid, HttpContext.RequestAborted);
            var selected = Audits.FirstOrDefault(x => x.RevisionTermId == revisionTermId &&
                                                    x.AuditDegreeId == auditDegreeId &&
                                                    x.AuditProgramId == auditProgramId)
                           ?? Audits.FirstOrDefault();
            if (selected is not null)
                SelectedAudit = await lookup.GetAuditAsync(studentUid, selected.RevisionTermId,
                    selected.AuditDegreeId, selected.AuditProgramId, HttpContext.RequestAborted);
        }
        catch (Exception exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to load read-only degree audit for a faculty advisee.");
            AuditUnavailable = true;
        }
        return Page();
    }
}
