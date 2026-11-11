using System;
using System.Net.Http;

namespace CampusFlow.Housing;

// Only fixed categories cross the integration boundary; never persist vendor response
// bodies or arbitrary exception messages in a student-facing/admin draft.
public sealed class HousingRequestException(string failureCode, int statusCode)
    : InvalidOperationException(HousingApplyFailure.Description(failureCode))
{
    public string FailureCode { get; } = failureCode;
    public int StatusCode { get; } = statusCode;
}

public static class HousingApplyFailure
{
    public static string ResponseCode(int status, string body) =>
        body.Contains("No rent data available for this room", StringComparison.OrdinalIgnoreCase) ? "rent-setup" :
        body.Contains("room is full", StringComparison.OrdinalIgnoreCase) ? "capacity" :
        body.Contains("already assigned", StringComparison.OrdinalIgnoreCase) ? "conflict" :
        status switch { 409 => "conflict", >= 500 => "unavailable", _ => "rejected" };

    public static string Code(Exception error) => error switch
    {
        HousingRequestException request => request.FailureCode,
        OperationCanceledException => "interrupted",
        TimeoutException => "timeout",
        HttpRequestException => "connection",
        _ => "unconfirmed"
    };

    public static string Description(string? code) => code switch
    {
        "rent-setup" => "Elements requires rent setup for the selected room. Review the housing period configuration.",
        "capacity" => "Elements rejected the room capacity. Review the live room and its occupants.",
        "conflict" => "Elements reported a housing conflict. Review its room assignment screen before resuming.",
        "rejected" => "Elements rejected the housing request. Review the housing period and assignment configuration.",
        "unavailable" => "The Elements housing service was unavailable. Verify the live result before resuming.",
        "connection" => "The connection to Elements failed. The last update may be unconfirmed; verify its live result.",
        "timeout" or "interrupted" => "The housing request was interrupted. Verify the live result before resuming.",
        _ => "The last housing operation could not be confirmed. Review the live assignments and housing batch."
    };
}
