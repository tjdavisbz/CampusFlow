using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
namespace CampusFlow.Bibliu;

public sealed class ThesisElementsBibliuExportReader(IConfiguration configuration) : IBibliuExportReader, ITransientDependency
{
    // Fail instead of silently publishing a partial file. Raise deliberately after measuring volumes.
    public const int MaximumRows = 100000;
    public async Task<IReadOnlyList<Dictionary<string, string?>>> ReadAsync(string kind, BibliuExportRequest request, CancellationToken cancellationToken)
    {
        var sql = kind switch {
            "users" => BibliuExportSql.Users, "courses" => BibliuExportSql.Courses,
            "schedule" => BibliuExportSql.Schedule, "enrollments" => BibliuExportSql.Enrollments,
            _ => throw new ArgumentException("Unknown BibliU export.")
        };
        var cs = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(cs)) throw new UserFriendlyException("The Elements reporting connection is not configured.");
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.Add("@Billing", SqlDbType.Int).Value = request.BillingTermId;
        command.Parameters.Add("@Bibliu", SqlDbType.Int).Value = request.BibliuTermId;
        command.Parameters.Add("@Store", SqlDbType.VarChar, 3).Value = request.StoreNumber;
        command.Parameters.Add("@Expanded", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(request.ExpandedTermNames);
        command.Parameters.Add("@Approved", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(request.ApprovedStudentIds);
        var rows = new List<Dictionary<string,string?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string,string?>();
            for (var i=0; i<reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            if (kind == "users") AddUserRows(rows, row, request.StoreNumber);
            else { if (kind == "courses") FormatCourse(row); rows.Add(row); }
            if (rows.Count > MaximumRows) throw new UserFriendlyException("BibliU export exceeds the supported row limit. No partial export was returned.");
        }
        return rows;
    }
    private static void AddUserRows(List<Dictionary<string,string?>> rows, Dictionary<string,string?> source, string store)
    {
        var record = BibliuRules.StudentRecordNumber(store, source["StudentID"]!);
        void Add(string code, string field, string eventNumber, string decimals, string numeric, string alpha) => rows.Add(new() {
            ["RecordCode"]=code, ["RecordNumber"]=record, ["EventNumber"]=eventNumber,
            ["FieldCode"]=field, ["NumberOfDecimals"]=decimals, ["Blank"]=" ",
            ["NumericDataField"]=numeric, ["AlphaDataField"]=alpha, ["NotUsed"]=new string(' ',7)
        });
        if (source["IdentityRecord"] == "1")
        {
            Add("AM","1020","000000","00",new string('0',18),BibliuRules.Pad(source["LastName"],30));
            Add("AM","1030","000000","00",new string('0',18),BibliuRules.Pad(source["FirstName"],30));
        }
        if (source["VoucherRecord"] != "1") return;
        var balance=decimal.Parse(source["Balance"]!,CultureInfo.InvariantCulture);
        var used=decimal.Parse(source["Used"]!,CultureInfo.InvariantCulture);
        var evt=BibliuRules.EventNumber(store,source["RehabHold"]=="1",balance);
        Add("AE","2010",evt,"  ",BibliuRules.Pad(evt,18,'0',true),new string(' ',30));
        Add("AE","2050",evt,"02",BibliuRules.Cents(BibliuRules.VoucherAmount(balance,used)),new string(' ',30));
    }
    private static void FormatCourse(Dictionary<string,string?> row)
    {
        foreach (var name in new[]{"Department","Course","Section"}) row[name]=BibliuRules.Pad(row[name],10);
        row["Instructor"]=BibliuRules.Pad(row["Instructor"],20);
        foreach (var name in new[]{"EstimatedEnrollment","ActualEnrollment"})
        {
            if (row[name]?.Length > 5) throw new UserFriendlyException("BibliU enrollment count exceeds the POS field width.");
            row[name]=row[name]?.PadLeft(5,'0');
        }
        row["CourseTitle"]=BibliuRules.Pad(row["CourseTitle"],25);
        if(row["CourseID"]?.Length>15) throw new UserFriendlyException("BibliU course identifier exceeds the POS field width.");
        row["CourseID"]=row["CourseID"]?.PadLeft(15,'0');
    }
}
