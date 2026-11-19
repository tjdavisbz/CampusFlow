using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusFlow.Housing;
using Npgsql;

if(args.Length is <3 or >4 || args[0] is not ("nonprod" or "production") || args[1] is not ("preview" or "apply"))
    throw new ArgumentException("Usage: HousingRateDefaultsRepair <nonprod|production> <preview|apply> <private-output-directory> [preview-sha256]");
var label=args[0];var apply=args[1]=="apply";var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var settings=JsonSerializer.Deserialize<Dictionary<string,string>>(Environment.GetEnvironmentVariable("CF_HOUSING_IMPORT_SETTINGS")??throw new InvalidOperationException("Runtime settings required."))!;
var pg=new NpgsqlConnectionStringBuilder(settings["ConnectionStrings__Default"]);
if(pg.Host!="psql-campusflow-prod-001.postgres.database.azure.com" || pg.Database!=(label=="production"?"postgres":"campusflow_nonprod")) throw new InvalidOperationException("Wrong environment database.");
await using var c=new NpgsqlConnection(pg.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
var changes=new List<(Guid Id,Guid Tenant,string Baseline,string Desired,string Extra,string Stamp,string NextBaseline,string NextDesired,int BaselineRates,int DesiredRates)>();
await using(var q=new NpgsqlCommand("""
 SELECT "Id","TenantId","BaselineJson","RoomsJson","ExtraProperties","ConcurrencyStamp"
 FROM "AppHousingAssignmentDrafts" WHERE NOT "IsDeleted" AND "TenantId"=(SELECT "Id" FROM "AbpTenants" WHERE LOWER("Name")='nelson' AND NOT "IsDeleted") ORDER BY "Id"
 """,c,tx))
await using(var row=await q.ExecuteReaderAsync())while(await row.ReadAsync())
{
 var baseline=row.GetString(2);var desired=row.GetString(3);var b=HousingRateDefaults.ReplaceUnreviewed(baseline);var d=HousingRateDefaults.ReplaceUnreviewed(desired);
 if(b.Count+d.Count==0)continue;
 var envelope=new HousingAssignmentDraft(row.GetGuid(0),row.GetGuid(1),1,baseline,DateTime.UtcNow);
 foreach(var prop in JsonNode.Parse(row.GetString(4))!.AsObject())envelope.ExtraProperties[prop.Key]=prop.Value is null?null!:JsonSerializer.Deserialize<JsonElement>(prop.Value.ToJsonString());
 HousingApplyProgress.EnsureEditable(envelope,DateTime.UtcNow);
 if(HousingRecoveryDispatch.Read(envelope)?.Status is "Queued" or "Running")throw new InvalidOperationException("A recovery is active. No rates changed.");
 changes.Add((row.GetGuid(0),row.GetGuid(1),baseline,desired,row.GetString(4),row.GetString(5),b.Json,d.Json,b.Count,d.Count));
}
string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
var hash=Hash(label+JsonSerializer.Serialize(changes.Select(x=>new{x.Id,x.Tenant,x.Baseline,x.Desired,x.Extra,x.Stamp})));
void Private(string name,string s){using var f=new FileStream(Path.Combine(output,name),new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite});using var writer=new StreamWriter(f);writer.Write(s);}
if(apply)
{
 if(args.Length!=4||args[3]!=hash)throw new InvalidOperationException("Workspace changed since reviewed preview. No repair applied.");
 Private(label+"-rate-backup-"+hash+".json",JsonSerializer.Serialize(changes.Select(x=>new{x.Id,x.Tenant,x.Baseline,x.Desired,x.Extra,x.Stamp})));
 foreach(var x in changes)
 {
  var extra=JsonNode.Parse(x.Extra)!.AsObject();extra["CF053RateDefaultsV1"]=JsonSerializer.Serialize(new{atUtc=DateTime.UtcNow,previewSha256=hash,baseline=x.BaselineRates,draft=x.DesiredRates,elementsWrites=false});
  await using var q=new NpgsqlCommand("""
   UPDATE "AppHousingAssignmentDrafts" SET "BaselineJson"=@B,"RoomsJson"=@D,"ExtraProperties"=@E,"ConcurrencyStamp"=@Next,"LastModificationTime"=@Now,"LastModifierId"=NULL
   WHERE "Id"=@Id AND "TenantId"=@Tenant AND "ConcurrencyStamp"=@Stamp AND "BaselineJson"=@OldB AND "RoomsJson"=@OldD AND "ExtraProperties"=@OldE AND NOT "IsDeleted"
   """,c,tx);
  foreach(var (name,value) in new (string,object)[]{("B",x.NextBaseline),("D",x.NextDesired),("E",extra.ToJsonString()),("Next",Guid.NewGuid().ToString("N")),("Now",DateTime.SpecifyKind(DateTime.UtcNow,DateTimeKind.Unspecified)),("Id",x.Id),("Tenant",x.Tenant),("Stamp",x.Stamp),("OldB",x.Baseline),("OldD",x.Desired),("OldE",x.Extra)})q.Parameters.AddWithValue(name,value);
  if(await q.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Concurrent edit detected. Repair rolled back.");
  await using var verify=new NpgsqlCommand("SELECT \"BaselineJson\",\"RoomsJson\" FROM \"AppHousingAssignmentDrafts\" WHERE \"Id\"=@Id",c,tx);verify.Parameters.AddWithValue("Id",x.Id);
  await using var readback=await verify.ExecuteReaderAsync();if(!await readback.ReadAsync()||readback.GetString(0)!=x.NextBaseline||readback.GetString(1)!=x.NextDesired)throw new InvalidOperationException("Repair readback failed.");
 }
 await tx.CommitAsync();
}
else await tx.RollbackAsync();
var result=JsonSerializer.Serialize(new{environment=label,mode=args[1],previewSha256=hash,workspaces=changes.Count,baselineRates=changes.Sum(x=>x.BaselineRates),draftRates=changes.Sum(x=>x.DesiredRates),assignmentsAndPendingEditsPreserved=true,elementsAssignmentsOrChargesWritten=false,checkedAtUtc=DateTime.UtcNow});
Console.WriteLine(result);
