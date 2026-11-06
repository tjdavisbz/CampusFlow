using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.StudentInformationSystems;

public sealed class ElementsPrimaryProgramLookup(IConfiguration configuration, ICurrentTenant tenant) : IStudentPrimaryProgramLookup, ITransientDependency
{
    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
    private SqlConnection Connection()
    {
        if (!tenant.Id.HasValue || !string.Equals(tenant.Name, "nelson", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Primary-program reporting is available only for Nelson.");
        var cs = configuration.GetConnectionString("ThesisElementsReadOnly") ?? throw new InvalidOperationException("The program reporting connection is missing.");
        var b = new SqlConnectionStringBuilder(cs);
        var np = string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Staging", StringComparison.OrdinalIgnoreCase);
        var host = np ? "sql-elm-nonprod-usc-replica.database.windows.net" : "sql-elm-prod-usc-replica.database.windows.net";
        if (!(string.Equals(b.DataSource, host, StringComparison.OrdinalIgnoreCase) || string.Equals(b.DataSource, $"tcp:{host},1433", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(b.InitialCatalog, np ? "EL-NP-NEL-DB" : "EL-NEL-DB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The program reporting connection must match the Elements environment.");
        return new SqlConnection(cs);
    }
    public async Task<IReadOnlyList<StudentPrimaryProgram>> GetAsync(IReadOnlyCollection<StudentTermKey> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return [];
        if (keys.Count > 10000 || keys.Any(x => !int.TryParse(x.ExternalStudentId, out var id) || id <= 0 || !int.TryParse(x.ExternalTermId, out var term) || term <= 0))
            throw new ArgumentException("Program lookup needs valid student and term identifiers.", nameof(keys));
        await using var connection = Connection(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            WITH Requested AS (
              SELECT DISTINCT StudentUID,TermCalendarID FROM OPENJSON(@Keys)
                WITH(StudentUID int '$.ExternalStudentId',TermCalendarID int '$.ExternalTermId')
            )
            SELECT p.StudentUID,p.TermCalendarID,p.MajorDegreeID,LTRIM(RTRIM(p.MajorDegree)),m.DegreeNameID,m.DegreeName,p.BlockProgramID,p.BlockProgram
            FROM dbo.CAMS_StudentProgram_View p
            LEFT JOIN dbo.CAMS_MajorMinorRef_View m ON m.MajorMinorID=p.MajorDegreeID
            JOIN Requested r ON r.StudentUID=p.StudentUID AND r.TermCalendarID=p.TermCalendarID
            WHERE p.PriProgID=1 AND p.MajorDegreeID>0;
            """, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@Keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(keys);
        var rows = new List<StudentPrimaryProgram>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(new(reader.GetInt32(0).ToString(), reader.GetInt32(1).ToString()), reader.GetInt32(2), reader.IsDBNull(3) ? "" : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5).Trim(), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7).Trim()));
        // Ambiguous primary programs never silently broaden a scheduler scope.
        return rows.GroupBy(x => x.Key).Where(x => x.Select(p => (p.MajorId,p.DegreeId,p.BlockProgramId)).Distinct().Count() == 1).Select(x => x.First()).ToArray();
    }
    public async Task<IReadOnlyList<StudentProgramChoice>> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connection(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT m.MajorMinorID,LTRIM(RTRIM(m.MajorMinorName)) Name,m.DegreeNameID,m.DegreeName,
                CAST(NULL AS int) BlockProgramID,CAST(NULL AS varchar(100)) BlockProgram
            FROM dbo.CAMS_MajorMinorRef_View m
            WHERE m.ActiveMajor=1 OR EXISTS(SELECT 1 FROM dbo.CAMS_StudentProgram_View p WHERE p.PriProgID=1 AND p.MajorDegreeID=m.MajorMinorID)
            UNION
            SELECT 0,'',NULL,NULL,p.ProgramsID,LTRIM(RTRIM(p.Programs)) FROM dbo.Programs p
            WHERE p.ActiveFlag=1 OR EXISTS(SELECT 1 FROM dbo.CAMS_StudentProgram_View sp WHERE sp.PriProgID=1 AND sp.BlockProgramID=p.ProgramsID)
            ORDER BY Name;
            """, connection) { CommandTimeout = 60 };
        var rows = new List<StudentProgramChoice>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3).Trim(), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5).Trim()));
        return rows;
    }
}
