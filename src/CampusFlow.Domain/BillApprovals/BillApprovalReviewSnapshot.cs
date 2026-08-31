using System;
using System.Collections.Generic;

namespace CampusFlow.BillApprovals;

public sealed record BillApprovalReviewSnapshot(
    string StudentName,
    decimal TotalCredits,
    IReadOnlyList<BillApprovalCourseSnapshot> Courses,
    IReadOnlyList<BillApprovalTransactionSnapshot> Transactions,
    IReadOnlyList<BillApprovalAidSnapshot> Aid);

public sealed record BillApprovalCourseSnapshot(string Code, string Name, decimal Credits, string Instructor);
public sealed record BillApprovalTransactionSnapshot(DateTime Date, string Description, decimal Amount, bool IsPending);
public sealed record BillApprovalAidSnapshot(string Description, decimal Amount, string Status);
