using System.Collections.Generic;
namespace CampusFlow.Housing;

public sealed record MealPlanRemovalTarget(int StudentUid, int TermId, int AssignmentId, int MealPlanId, string Name, decimal Amount);
public sealed record MealPlanRemovalEntry(int Id, int BatchId, int DocumentId, int ArTypeId, decimal Amount, string Reference, string Description, string Kind);
public sealed record MealPlanRemovalReview(MealPlanRemovalTarget Target, List<MealPlanRemovalEntry> Entries, string Revision);
