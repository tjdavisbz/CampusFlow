using System;
using System.Collections.Generic;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.Bookstores;

public static class CampusStoreLinks
{
    public static string? ForCourses(IConfiguration configuration, string? tenantName,
        string termName, IReadOnlyCollection<StudentCourseScheduleItem> courses)
    {
        var section = configuration.GetSection($"CampusStores:{tenantName ?? "Default"}");
        var baseUrl = section["CourseSelectionUrl"];
        var storeId = section["StoreId"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(storeId)) return null;

        var offeringIds = courses.Select(x => x.ExternalCourseOfferingId)
            .Where(x => long.TryParse(x, out _))
            .Select(x => x.PadLeft(15, '0'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (offeringIds.Length == 0) return null;

        var displayParts = termName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bookstoreTerm = displayParts.Length > 1 && displayParts[^1].Length >= 2
            ? $"{string.Join(' ', displayParts[..^1])} {displayParts[^1][^2..]}"
            : termName;
        return $"{baseUrl}?src=2&type=2&stoid={Uri.EscapeDataString(storeId)}" +
               $"&trm={Uri.EscapeDataString(bookstoreTerm)}&cid={string.Join(',', offeringIds)}";
    }
}
