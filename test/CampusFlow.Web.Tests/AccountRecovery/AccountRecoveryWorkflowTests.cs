using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AccountRecovery;
using CampusFlow.Web.AccountRecovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.AccountRecoveryTests;

public sealed class AccountRecoveryWorkflowTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static AccountRecoveryIdentity Identity() => new() { FirstName="Synthetic",LastName="Test",Ssn="000000000",BirthDate=new(2000,1,1) };
    private sealed class Fixture
    {
        public readonly IAccountRecoveryIdentitySource IdentitySource=Substitute.For<IAccountRecoveryIdentitySource>();
        public readonly IAccountRecoveryDirectory Directory=Substitute.For<IAccountRecoveryDirectory>();
        public readonly MemoryStore Store=new();
        public readonly Clock Time=new();
        public readonly RecoveryPerson Person=new(10,20,"synthetic@my.nelson.edu");
        public readonly RecoveryDirectoryAccount Account=new(Guid.NewGuid(),"synthetic@my.nelson.edu",true,true,false,false);
        public readonly AccountRecoveryWorkflow Flow;
        public Fixture(bool enabled=true,bool scopeMatches=true)
        {
            IdentitySource.IsConfigured.Returns(true);Directory.IsConfigured.Returns(true);
            IdentitySource.VerifyIdentityAsync(Arg.Any<AccountRecoveryIdentity>(),Arg.Any<CancellationToken>()).Returns(Person);
            IdentitySource.GetQuestionAsync(Person,Arg.Any<CancellationToken>()).Returns(new RecoveryQuestion(true,7,"Synthetic question?",false,new string('a',64)));
            IdentitySource.VerifyAnswerAsync(Person,7,Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(true);
            IdentitySource.RevalidatePersonAsync(Person,Arg.Any<CancellationToken>()).Returns(true);
            Directory.FindAsync(Person.UserPrincipalName,Arg.Any<CancellationToken>()).Returns(Account);
            Directory.ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(RecoveryPasswordWriteResult.Accepted);
            var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Owner);tenant.Name.Returns("nelson");
            var tenants=Substitute.For<ITenantStore>();tenants.FindAsync("NELSON").Returns(new TenantConfiguration(scopeMatches?Owner:Guid.NewGuid(),"nelson"));
            var normalizer=Substitute.For<ITenantNormalizer>();normalizer.NormalizeName("nelson").Returns("NELSON");
            var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["PasswordRecovery:Enabled"]=enabled.ToString(),["PasswordRecovery:VerificationImportReady"]="true",["PasswordRecovery:CampusFlowTenantId"]=Owner.ToString() }).Build();
            Flow=new(cfg,IdentitySource,Directory,Store,tenant,tenants,normalizer,NullLogger<AccountRecoveryWorkflow>.Instance,Time);
        }
        public async Task<string> Verified()
        {var result=await Flow.StartAsync(Identity(),"192.0.2.1");Assert.Equal(RecoveryStep.Question,result.Step);var next=await Flow.AnswerAsync(result.Ticket,"synthetic answer","192.0.2.1");Assert.Equal(RecoveryStep.Password,next.Step);return next.Ticket!;}
    }
    [Theory] [InlineData(false,true)] [InlineData(true,false)]
    public async Task Disabled_or_wrong_university_never_reaches_identity_or_directory(bool enabled,bool scope)
    {var f=new Fixture(enabled,scope);Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.StartAsync(Identity(),"192.0.2.1")).Step);await f.IdentitySource.DidNotReceive().VerifyIdentityAsync(Arg.Any<AccountRecoveryIdentity>(),Arg.Any<CancellationToken>());Assert.Empty(f.Store.Rows);}
    [Fact]public async Task Missing_identity_is_generic_and_never_reaches_Graph()
    {var f=new Fixture();f.IdentitySource.VerifyIdentityAsync(Arg.Any<AccountRecoveryIdentity>(),Arg.Any<CancellationToken>()).Returns((RecoveryPerson?)null);var r=await f.Flow.StartAsync(Identity(),"192.0.2.1");Assert.Equal(RecoveryNotice.NotVerified,r.Notice);await f.Directory.DidNotReceive().FindAsync(Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Theory] [InlineData(false,false)] [InlineData(true,false)]
    public async Task Missing_or_disallowed_legacy_proof_blocks_directory_lookup(bool source,bool allowed)
    {var f=new Fixture();f.IdentitySource.GetQuestionAsync(f.Person,Arg.Any<CancellationToken>()).Returns(new RecoveryQuestion(source,null,null,allowed));Assert.Equal(RecoveryStep.Identity,(await f.Flow.StartAsync(Identity(),"192.0.2.1")).Step);await f.Directory.DidNotReceive().FindAsync(Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task An_explicit_imported_no_question_eligibility_allows_password_step()
    {var f=new Fixture();f.IdentitySource.GetQuestionAsync(f.Person,Arg.Any<CancellationToken>()).Returns(new RecoveryQuestion(true,null,null,true,new string('a',64)));Assert.Equal(RecoveryStep.Password,(await f.Flow.StartAsync(Identity(),"192.0.2.1")).Step);}
    [Theory] [InlineData("disabled")] [InlineData("guest")] [InlineData("privileged")] [InlineData("sync")] [InlineData("differentUpn")]
    public async Task Ineligible_directory_accounts_are_never_bound(string kind)
    {var f=new Fixture();var a=kind switch {"disabled"=>f.Account with{Enabled=false},"guest"=>f.Account with{IsMember=false},"privileged"=>f.Account with{IsPrivileged=true},"sync"=>f.Account with{IsSynchronized=true},_=>f.Account with{UserPrincipalName="other@my.nelson.edu"}};f.Directory.FindAsync(f.Person.UserPrincipalName,Arg.Any<CancellationToken>()).Returns(a);Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.StartAsync(Identity(),"192.0.2.1")).Step);await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Three_wrong_answers_lock_the_ticket()
    {var f=new Fixture();f.IdentitySource.VerifyAnswerAsync(f.Person,7,Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(false);var start=await f.Flow.StartAsync(Identity(),"192.0.2.1");for(int n=0;n<3;n++)await f.Flow.AnswerAsync(start.Ticket,"wrong","192.0.2.1");Assert.Equal("Blocked",f.Store.Rows.Values.Single().State);Assert.Equal(RecoveryStep.Identity,(await f.Flow.ResetAsync(start.Ticket,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]
    public async Task Synced_account_can_complete_only_through_a_configured_writeback_backend()
    {
        var f=new Fixture();var account=f.Account with{IsSynchronized=true};f.Directory.SupportsSynchronizedAccounts.Returns(true);
        f.Directory.FindAsync(f.Person.UserPrincipalName,Arg.Any<CancellationToken>()).Returns(account);
        var ticket=await f.Verified();Assert.Equal(RecoveryStep.Complete,(await f.Flow.ResetAsync(ticket,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);
        await f.Directory.Received(1).ResetAsync(account,"Synthetic-password-42!",Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Losing_writeback_support_after_verification_prevents_a_reset()
    {
        var f=new Fixture();var account=f.Account with{IsSynchronized=true};f.Directory.SupportsSynchronizedAccounts.Returns(true);
        f.Directory.FindAsync(f.Person.UserPrincipalName,Arg.Any<CancellationToken>()).Returns(account);
        var ticket=await f.Verified();f.Directory.SupportsSynchronizedAccounts.Returns(false);
        Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResetAsync(ticket,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);
        await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Expiry_address_mismatch_and_tampered_ticket_never_write()
    {var f=new Fixture();var t=await f.Verified();Assert.Equal(RecoveryStep.Identity,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.2")).Step);Assert.Equal(RecoveryStep.Identity,(await f.Flow.ResetAsync("bad", "Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);f.Time.Utc=f.Time.Utc.AddMinutes(11);Assert.Equal(RecoveryNotice.Expired,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Notice);await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Password_mismatch_never_claims_or_writes()
    {var f=new Fixture();var t=await f.Verified();Assert.Equal(RecoveryNotice.PasswordMismatch,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","different","192.0.2.1")).Notice);Assert.Equal("Verified",f.Store.Rows.Values.Single().State);}
    [Fact]public async Task Changed_directory_object_after_verification_is_not_updated()
    {var f=new Fixture();var t=await f.Verified();f.Directory.FindAsync(f.Person.UserPrincipalName,Arg.Any<CancellationToken>()).Returns(f.Account with{ObjectId=Guid.NewGuid()});Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Revoked_or_replaced_verification_proof_invalidates_a_verified_request(bool replaced)
    {var f=new Fixture();var t=await f.Verified();f.IdentitySource.GetQuestionAsync(f.Person,Arg.Any<CancellationToken>()).Returns(replaced
        ? new RecoveryQuestion(true,7,"Synthetic question?",false,new string('b',64)) : new RecoveryQuestion(false,null,null,false));
        Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);
        await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Revoked_proof_on_resume_never_redisplays_the_password_form()
    {var f=new Fixture();var t=await f.Verified();f.IdentitySource.GetQuestionAsync(f.Person,Arg.Any<CancellationToken>()).Returns(new RecoveryQuestion(false,null,null,false));Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResumeAsync(t,"192.0.2.1")).Step);Assert.Equal("Blocked",f.Store.Rows.Values.Single().State);}
    [Fact]public async Task Changed_student_UPN_mapping_after_verification_prevents_password_write()
    {var f=new Fixture();var t=await f.Verified();f.IdentitySource.RevalidatePersonAsync(f.Person,Arg.Any<CancellationToken>()).Returns(false);Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);await f.Directory.DidNotReceive().ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Successful_write_is_claimed_first_and_never_replayed()
    {var f=new Fixture();var t=await f.Verified();f.Directory.ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(_=>{Assert.Equal("Applying",f.Store.Rows.Values.Single().State);return RecoveryPasswordWriteResult.Accepted;});Assert.Equal(RecoveryStep.Complete,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1");await f.Directory.Received(1).ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Lost_reply_remains_unknown_and_does_not_resend()
    {var f=new Fixture();var t=await f.Verified();f.Directory.ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(RecoveryPasswordWriteResult.Unknown);Assert.Equal(RecoveryStep.Unknown,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);Assert.Equal(RecoveryStep.Unknown,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);await f.Directory.Received(1).ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Microsoft_policy_rejections_allow_bounded_explicit_new_submissions()
    {var f=new Fixture();var t=await f.Verified();f.Directory.ResetAsync(Arg.Any<RecoveryDirectoryAccount>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(RecoveryPasswordWriteResult.PolicyRejected);for(int i=0;i<2;i++)Assert.Equal(RecoveryNotice.PasswordPolicy,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Notice);Assert.Equal(RecoveryStep.Unavailable,(await f.Flow.ResetAsync(t,"Synthetic-password-42!","Synthetic-password-42!","192.0.2.1")).Step);Assert.Equal("Blocked",f.Store.Rows.Values.Single().State);}
    [Fact]public async Task Rate_limit_prevents_identity_lookup()
    {var f=new Fixture();f.Store.Full=true;Assert.Equal(RecoveryNotice.RateLimited,(await f.Flow.StartAsync(Identity(),"192.0.2.1")).Notice);await f.IdentitySource.DidNotReceive().VerifyIdentityAsync(Arg.Any<AccountRecoveryIdentity>(),Arg.Any<CancellationToken>());}
    [Fact]public async Task Import_and_transient_failures_do_not_echo_sensitive_details()
    {var f=new Fixture();f.IdentitySource.VerifyIdentityAsync(Arg.Any<AccountRecoveryIdentity>(),Arg.Any<CancellationToken>()).Returns<RecoveryPerson?>(_=>throw new InvalidOperationException("synthetic sensitive value"));var r=await f.Flow.StartAsync(Identity(),"192.0.2.1");Assert.Equal(RecoveryStep.Unavailable,r.Step);Assert.Null(r.Message);Assert.DoesNotContain("000000000",Identity().ToString());}
    private sealed class Clock:TimeProvider {public DateTimeOffset Utc=new(2026,11,10,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Utc;}
    private sealed class MemoryStore:IAccountRecoveryRequestStore
    {
        public readonly Dictionary<Guid,RecoveryRequest> Rows=[];public bool Full;
        public Task<bool> AddAsync(RecoveryRequest row,DateTime now,CancellationToken ct=default){if(Full)return Task.FromResult(false);Rows.Add(row.Id,row);return Task.FromResult(true);}
        public Task<RecoveryRequest?> FindAsync(string hash,CancellationToken ct=default)=>Task.FromResult(Rows.Values.SingleOrDefault(x=>x.TicketHash==hash));
        public Task<bool> BindAsync(Guid id,RecoveryPerson p,RecoveryDirectoryAccount a,RecoveryQuestion q,DateTime now,CancellationToken ct=default)=>Task.FromResult(Rows[id].Bind(p,a,q,now));
        public Task<bool> BeginAnswerAsync(Guid id,DateTime now,CancellationToken ct=default)=>Task.FromResult(Rows[id].BeginAnswer(now));
        public Task FinishAnswerAsync(Guid id,bool v,CancellationToken ct=default){Rows[id].FinishAnswer(v);return Task.CompletedTask;}
        public Task<bool> ClaimResetAsync(Guid id,DateTime now,CancellationToken ct=default)=>Task.FromResult(Rows[id].ClaimReset(now));
        public Task FinishAsync(Guid id,RecoveryPasswordWriteResult o,CancellationToken ct=default){Rows[id].Finish(o);return Task.CompletedTask;}
        public Task RejectAsync(Guid id,CancellationToken ct=default){Rows[id].Reject();return Task.CompletedTask;}
    }
}
