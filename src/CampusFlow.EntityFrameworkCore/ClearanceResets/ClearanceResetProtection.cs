using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.ClearanceResets;

public sealed class ClearanceResetProtection(IConfiguration configuration,ICurrentTenant tenant):IClearanceResetProtection,ITransientDependency
{
    private Guid Owner()=>tenant.Id??throw new InvalidOperationException("A university context is required for clearance protection.");
    private NpgsqlConnection Connection()
    {
        var b=new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default")??throw new InvalidOperationException("Portal approval database is not configured."));
        var np=string.Equals(configuration["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase);
        var label=configuration["App:EnvironmentLabel"];var prod=(string.IsNullOrWhiteSpace(label)||string.Equals(label,"PRODUCTION",StringComparison.OrdinalIgnoreCase))&&string.Equals(configuration["ASPNETCORE_ENVIRONMENT"],"Production",StringComparison.OrdinalIgnoreCase);
        if(!np&&!prod)throw new InvalidOperationException("The portal environment is not recognized.");
        if(b.Host!="psql-campusflow-prod-001.postgres.database.azure.com"||b.Database!=(np?"campusflow_nonprod":"postgres"))
            throw new InvalidOperationException("Portal approval database does not match this environment.");
        return new(b.ConnectionString);
    }
    internal static long LockKey(Guid owner,int student,int term)=>BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"CampusFlow:clearance:{owner:D}:{student}:{term}")));
    public async Task<IAsyncDisposable> AcquireAsync(int studentUid,int termId,CancellationToken ct=default)
    {
        if(studentUid<=0||termId<=0)throw new ArgumentException("A student and term are required.");
        var owner=Owner();var c=Connection();
        try
        {
            await c.OpenAsync(ct);
            var key=LockKey(owner,studentUid,termId);
            await using var q=new NpgsqlCommand("SELECT pg_try_advisory_lock(@Key)",c);q.Parameters.AddWithValue("Key",key);
            if(await q.ExecuteScalarAsync(ct) is not true)throw new ClearanceUpdateBusyException();
            return new Lease(c,key);
        }
        catch {await c.DisposeAsync();throw;}
    }
    private sealed class Lease(NpgsqlConnection connection,long key):IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try {await using var q=new NpgsqlCommand("SELECT pg_advisory_unlock(@Key)",connection);q.Parameters.AddWithValue("Key",key);await q.ExecuteScalarAsync();}
            catch {NpgsqlConnection.ClearPool(connection);}
            finally {await connection.DisposeAsync();}
        }
    }
    public async Task<bool> IsProtectedAsync(ClearanceResetCandidate candidate,CancellationToken ct=default)=>
        (await ProtectedAsync([candidate],ct)).Contains((candidate.StudentUid,candidate.TermId));
    public async Task<HashSet<(int StudentUid,int TermId)>> ProtectedAsync(IReadOnlyCollection<ClearanceResetCandidate> candidates,CancellationToken ct=default)
    {
        var owner=Owner();var result=new HashSet<(int,int)>();if(candidates.Count==0)return result;
        await using var c=Connection();await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",c,tx))await ro.ExecuteNonQueryAsync(ct);
        var students=candidates.Select(x=>x.StudentUid.ToString(System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        var terms=candidates.Select(x=>x.TermId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        var current=new Dictionary<(int,int),bool>();
        await using(var q=new NpgsqlCommand("""
            SELECT b."ExternalStudentId",b."ExternalTermId",b."AcceptedAt",b."Status",p."Provider",p."ExternalStudentId",p."TenantId"
            FROM "AppBillApprovals" b LEFT JOIN "AppStudentProfiles" p ON p."Id"=b."StudentProfileId" AND NOT p."IsDeleted"
            WHERE b."TenantId"=@Tenant AND NOT b."IsDeleted"
              AND ltrim(trim(b."ExternalStudentId"),'0')=ANY(@Students) AND ltrim(trim(b."ExternalTermId"),'0')=ANY(@Terms)
            """,c,tx))
        {
            q.Parameters.AddWithValue("Tenant",owner);q.Parameters.AddWithValue("Students",students);q.Parameters.AddWithValue("Terms",terms);
            await using var rows=await q.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct))
            {
                var key=(int.Parse(rows.GetString(0)),int.Parse(rows.GetString(1)));
                if(rows.IsDBNull(4)||rows.GetInt32(4)!=1||rows.IsDBNull(6)||rows.GetGuid(6)!=owner||int.Parse(rows.GetString(5))!=key.Item1)
                    throw new InvalidOperationException("An approval's student/provider identity could not be verified.");
                if(current.ContainsKey(key))throw new InvalidOperationException("Multiple current approvals require staff review before resetting clearance.");
                var status=(BillApprovalStatus)rows.GetInt32(3);
                if(!Enum.IsDefined(status))throw new InvalidOperationException("Approval status is unknown; clearance was not reset.");
                current[key]=BillApprovalAcceptanceRules.IsApproved(rows.IsDBNull(2)?null:rows.GetDateTime(2),status);
            }
        }
        foreach(var row in current.Where(x=>x.Value))result.Add(row.Key);
        await using(var q=new NpgsqlCommand("""
            SELECT "ExternalStudentId","ExternalTermId" FROM "AppLegacyBillApprovals"
            WHERE "TenantId"=@Tenant AND NOT "IsDeleted" AND "SourceApproved" AND "AcceptTime" IS NOT NULL
              AND ltrim(trim("ExternalStudentId"),'0')=ANY(@Students) AND ltrim(trim("ExternalTermId"),'0')=ANY(@Terms)
            """,c,tx))
        {
            q.Parameters.AddWithValue("Tenant",owner);q.Parameters.AddWithValue("Students",students);q.Parameters.AddWithValue("Terms",terms);
            await using var rows=await q.ExecuteReaderAsync(ct);
            while(await rows.ReadAsync(ct)){var key=(int.Parse(rows.GetString(0)),int.Parse(rows.GetString(1)));if(!current.ContainsKey(key))result.Add(key);}
        }
        await using(var q=new NpgsqlCommand(""" SELECT "ExtraProperties" FROM "AppClearanceResetConfigurations" WHERE "TenantId"=@Tenant AND NOT "IsDeleted" """,c,tx))
        {
            q.Parameters.AddWithValue("Tenant",owner);await using var rows=await q.ExecuteReaderAsync(ct);var count=0;
            while(await rows.ReadAsync(ct))
            {
                if(++count>1)throw new InvalidOperationException("Multiple clearance configurations require review.");
                var extra=System.Text.Json.Nodes.JsonNode.Parse(rows.GetString(0))!;
                var exceptions=JsonSerializer.Deserialize<List<ManualClearanceException>>(extra["ManualClearanceExceptionsV1"]?.GetValue<string>()??"[]")??throw new InvalidOperationException("Manual exceptions could not be read.");
                if(exceptions.Any(x=>x.StudentUid<=0||x.TermId<=0||x.CreatedBy==Guid.Empty||string.IsNullOrWhiteSpace(x.Reason)))throw new InvalidOperationException("A manual clearance exception is invalid.");
                foreach(var item in exceptions)result.Add((item.StudentUid,item.TermId));
            }
        }
        await tx.CommitAsync(ct);return result;
    }
}
