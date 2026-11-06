using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.Chapel;

public sealed record ChapelDormOption(string Key, string Name);
public sealed record ChapelResidence(string StudentId, string StudentName, string DormKey);
public interface IChapelDormLookup
{
    Task<IReadOnlyList<ChapelResidence>> GetResidencesAsync(int termCalendarId, CancellationToken cancellationToken = default);
}
public static class ChapelDorms
{
    public static IReadOnlyList<ChapelDormOption> Options { get; } =
    [new("off-campus","Off campus"),new("kendrick","Kendrick"),new("savell","Savell"),new("teeter","Teeter"),
     new("bridges","Bridges"),new("guynes","Guynes"),new("collins","Collins"),new("other","Other housing"),new("unknown","Housing not identified")];
    public static bool IsValid(string key) => Options.Any(x => x.Key == key);
    public static string Name(string key) => Options.Single(x => x.Key == key).Name;
    public static string Classify(IEnumerable<string?> buildings, IEnumerable<string?> statuses)
    {
        var keys = buildings.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => BuildingKey(x!)).Distinct().ToArray();
        if (keys.Length > 1) return "unknown";
        if (keys.Length == 1) return keys[0];
        return statuses.Any(x => x?.Trim().ToLowerInvariant() is "commuter" or "off campus" or "off-campus" or "offcampus") ? "off-campus" : "unknown";
    }
    private static string BuildingKey(string value)
    {
        var name = value.Trim().ToLowerInvariant();
        if (name is "commuter" or "commuter dorm" or "off campus" or "off-campus") return "off-campus";
        foreach (var key in Options.Where(x => x.Key is not ("off-campus" or "other" or "unknown")))
            if (name == key.Key || name == key.Key + " hall") return key.Key;
        return "other";
    }
}
