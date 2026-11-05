using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.AdvisorPortal;

namespace CampusFlow.Web.Pages.Advisor;

internal static class AdvisorQueuePaging
{
    internal sealed record Result(string Search, int MatchingStudents, int PageNumber,
        List<AdvisorQueueItemDto> Students);

    internal static Result Apply(IReadOnlyList<AdvisorQueueItemDto> queue, string? rawSearch,
        int requestedPage, int pageSize)
    {
        var search = string.Join(' ', (rawSearch ?? string.Empty).Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (search.Length > 100) search = search[..100].TrimEnd();

        var matches = search.Length == 0
            ? queue.ToList()
            : queue.Where(student => MatchesSearch(student, search)).ToList();
        var totalPages = Math.Max(1, (matches.Count + pageSize - 1) / pageSize);
        var page = Math.Clamp(requestedPage, 1, totalPages);
        return new Result(search, matches.Count, page,
            matches.Skip((page - 1) * pageSize).Take(pageSize).ToList());
    }

    private static bool MatchesSearch(AdvisorQueueItemDto student, string search)
    {
        if (student.StudentId.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(part => student.StudentName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
