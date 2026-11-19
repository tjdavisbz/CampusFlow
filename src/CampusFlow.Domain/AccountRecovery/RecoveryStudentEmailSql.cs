namespace CampusFlow.AccountRecovery;

/// <summary>
/// Shared read-only SIS mapping for recovery and its import tool. Matches the
/// portal's active Local Email1 source and fails closed on ambiguous ownership.
/// Keep every Student row when building an import index, even without an email,
/// so duplicate institutional StudentIDs cannot disappear from ambiguity checks.
/// </summary>
public static class RecoveryStudentEmailSql
{
    public const string ActiveAddresses = """
        SELECT address.StudentUID, LTRIM(RTRIM(address.Email1))
        FROM dbo.CAMS_StudentAddressList_View address
        WHERE address.ActiveFlag = 'Yes' AND address.AddressType = 'Local'
        """;

    public const string From = """
        FROM dbo.Student s
        OUTER APPLY (
            SELECT MIN(LTRIM(RTRIM(address.Email1))) AS UserPrincipalName
            FROM dbo.CAMS_StudentAddressList_View address
            WHERE address.StudentUID = s.StudentUID
              AND address.ActiveFlag = 'Yes' AND address.AddressType = 'Local'
            HAVING COUNT_BIG(*) = 1
        ) recoveryEmail
        """;

    public const string UniqueOwner = """
        recoveryEmail.UserPrincipalName IS NOT NULL
        AND NOT EXISTS (
            SELECT 1 FROM dbo.CAMS_StudentAddressList_View other
            WHERE other.StudentUID <> s.StudentUID
              AND other.ActiveFlag = 'Yes' AND other.AddressType = 'Local'
              AND LOWER(LTRIM(RTRIM(other.Email1))) = LOWER(recoveryEmail.UserPrincipalName)
        )
        """;
}
