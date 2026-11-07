using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
namespace CampusFlow.Web.Pages;

internal static class FinancialAidBatch
{
    internal sealed record Result(int Confirmed, int Total, bool Unconfirmed);
    internal static async Task<Result> AcceptAsync(string student, string term, string awardIds,
        IStudentInformationSystemFinancialAidLookup lookup, IStudentInformationSystemFinancialAidDecisionService service,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(term, out var termId) || termId <= 0 || string.IsNullOrWhiteSpace(awardIds) || awardIds.Length > 6000)
            throw new InvalidOperationException("Review the semester and awards before accepting.");
        var expected = awardIds.Split(',');
        if (expected.Length > 500 || expected.Any(x => !int.TryParse(x, out var id) || id <= 0) || expected.Distinct().Count() != expected.Length)
            throw new InvalidOperationException("The award selection is invalid. Refresh the page.");
        var awards = (await lookup.GetAwardsAsync(student, cancellationToken)).Where(x => x.ExternalTermId == term).ToArray();
        var decisions = await service.GetDecisionsAsync(student, cancellationToken);
        if (awards.Any(x => !decisions.ContainsKey(x.ExternalAwardId)))
            throw new InvalidOperationException("Current award decisions could not be verified. Refresh before trying again.");
        var eligible = awards.Select(x => x with { StudentAccepted = decisions[x.ExternalAwardId] })
            .Where(FinancialAidRules.CanDecide).ToArray();
        if (eligible.Length == 0 || !eligible.Select(x => x.ExternalAwardId).ToHashSet().SetEquals(expected))
            throw new InvalidOperationException("The awards have changed. Refresh and review this semester before accepting.");
        var confirmed = 0;
        foreach (var award in eligible.OrderBy(x => int.Parse(x.ExternalAwardId)))
        {
            try { await service.SubmitDecisionAsync(student, award.ExternalAwardId, StudentFinancialAidDecision.Accept, cancellationToken, term); confirmed++; }
            catch (Exception) { return new(confirmed, eligible.Length, true); }
        }
        return new(confirmed, eligible.Length, false);
    }
}
