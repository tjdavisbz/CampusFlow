using System;
using System.Threading.Tasks;
using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.AccountRecovery;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public sealed class AccountRecoveryStoreTests:CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]public async Task Client_throttling_is_shared_by_new_store_instances()
    {
        using var owner=GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(),"nelson");var now=DateTime.UtcNow;
        for(int i=0;i<8;i++)Assert.True(await GetRequiredService<IAccountRecoveryRequestStore>().AddAsync(new(Guid.NewGuid(),GetRequiredService<ICurrentTenant>().Id!.Value,i.ToString().PadLeft(64,'0'),new('a',64),now),now));
        Assert.False(await GetRequiredService<IAccountRecoveryRequestStore>().AddAsync(new(Guid.NewGuid(),GetRequiredService<ICurrentTenant>().Id!.Value,new('b',64),new('a',64),now),now));
    }
    [Fact]public async Task Account_throttling_counts_verified_identity_attempts_across_clients()
    {
        var id=Guid.NewGuid();using var owner=GetRequiredService<ICurrentTenant>().Change(id,"nelson");var now=DateTime.UtcNow;var p=new RecoveryPerson(10,20,"synthetic@my.nelson.edu");var a=new RecoveryDirectoryAccount(Guid.NewGuid(),p.UserPrincipalName,true,true,false,false);
        for(int i=0;i<4;i++){var r=new RecoveryRequest(Guid.NewGuid(),id,i.ToString().PadLeft(64,'0'),i.ToString().PadLeft(64,'0'),now);Assert.True(await GetRequiredService<IAccountRecoveryRequestStore>().AddAsync(r,now));Assert.Equal(i<3 ? RecoveryBindResult.Bound : RecoveryBindResult.RateLimited,await GetRequiredService<IAccountRecoveryRequestStore>().BindAsync(r.Id,p,a,new(true,null,null,true,new('c',64)),now));}
    }
    [Fact]public async Task A_persisted_unknown_request_cannot_send_again_and_does_not_store_secrets()
    {
        var id=Guid.NewGuid();using var owner=GetRequiredService<ICurrentTenant>().Change(id,"nelson");var now=DateTime.UtcNow;var r=new RecoveryRequest(Guid.NewGuid(),id,new('a',64),new('b',64),now);var p=new RecoveryPerson(10,20,"synthetic@my.nelson.edu");
        Assert.True(await GetRequiredService<IAccountRecoveryRequestStore>().AddAsync(r,now));Assert.Equal(RecoveryBindResult.Bound, await GetRequiredService<IAccountRecoveryRequestStore>().BindAsync(r.Id,p,new(Guid.NewGuid(),p.UserPrincipalName,true,true,false,false),new(true,null,null,true,new('c',64)),now));
        Assert.True(await GetRequiredService<IAccountRecoveryRequestStore>().ClaimResetAsync(r.Id,now));Assert.False(await GetRequiredService<IAccountRecoveryRequestStore>().ClaimResetAsync(r.Id,now));await GetRequiredService<IAccountRecoveryRequestStore>().FinishAsync(r.Id,RecoveryPasswordWriteResult.Unknown);Assert.Equal("Unknown",(await GetRequiredService<IAccountRecoveryRequestStore>().FindAsync(new('a',64)))!.State);Assert.False(await GetRequiredService<IAccountRecoveryRequestStore>().ClaimResetAsync(r.Id,now));
        var type=typeof(RecoveryRequest);foreach(var name in new[]{"Password","Ssn","BirthDate","Answer","Ticket"})Assert.Null(type.GetProperty(name));
    }
    [Fact]public async Task Imported_proofs_are_unique_per_student_and_do_not_cross_tenants()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var tenant=GetRequiredService<ICurrentTenant>();
        using(tenant.Change(first,"nelson")){
            await WithUnitOfWorkAsync(async()=>await GetRequiredService<IRepository<RecoveryVerification,Guid>>().InsertAsync(new(Guid.NewGuid(),first,10,20,null,null,null,true,false,new('a',64)),true));Assert.NotNull(await GetRequiredService<IAccountRecoveryVerificationStore>().GetAsync(10));
            await Assert.ThrowsAsync<DbUpdateException>(()=>WithUnitOfWorkAsync(async()=>await GetRequiredService<IRepository<RecoveryVerification,Guid>>().InsertAsync(new(Guid.NewGuid(),first,10,20,null,null,null,true,false,new('b',64)),true)));
        }
        using(tenant.Change(second,"nelson"))Assert.Null(await GetRequiredService<IAccountRecoveryVerificationStore>().GetAsync(10));
        using(tenant.Change(second,"another-university"))await Assert.ThrowsAsync<InvalidOperationException>(()=>GetRequiredService<IAccountRecoveryVerificationStore>().GetAsync(10));
    }
    private IAccountRecoveryRequestStore Store => GetRequiredService<IAccountRecoveryRequestStore>();
    private static RecoveryQuestion Question => new(true, null, null, true, new('c', 64));
    private async Task<RecoveryRequest> Started(Guid tenantId, DateTime now)
    {
        var request = new RecoveryRequest(Guid.NewGuid(), tenantId,
            Guid.NewGuid().ToString("N").PadLeft(64, '0'), Guid.NewGuid().ToString("N").PadLeft(64, '0'), now);
        Assert.True(await Store.AddAsync(request, now));
        return request;
    }
    private async Task<RecoveryRequest> Verified(Guid tenantId, RecoveryPerson person, RecoveryDirectoryAccount account, DateTime now)
    {
        var request = await Started(tenantId, now);
        Assert.Equal(RecoveryBindResult.Bound, await Store.BindAsync(request.Id, person, account, Question, now));
        return request;
    }
    [Fact]
    public async Task Independent_verified_tickets_share_one_durable_account_claim_and_success_consumes_others()
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10, 20, "synthetic@my.nelson.edu");
        var account = new RecoveryDirectoryAccount(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false);
        var first = await Verified(tenantId, person, account, now);
        var second = await Verified(tenantId, person, account, now);
        var third = await Verified(tenantId, person, account, now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        Assert.False(await Store.ClaimResetAsync(second.Id, now));
        await Store.FinishAsync(first.Id, RecoveryPasswordWriteResult.Accepted);
        Assert.Equal("Blocked", (await Store.FindAsync(third.TicketHash))!.State);
        Assert.False(await Store.ClaimResetAsync(third.Id, now));
        var complete = (await Store.FindAsync(first.TicketHash))!;
        Assert.Equal("Complete", complete.State);
        Assert.NotNull(complete.PasswordWriteFinishedAtUtc);
        Assert.Equal(Question.SourceFingerprint, complete.VerificationFingerprint);
    }
    [Fact]
    public async Task Unknown_result_blocks_existing_and_new_account_tickets_even_after_original_ticket_expires()
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10, 20, "synthetic@my.nelson.edu");
        var account = new RecoveryDirectoryAccount(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false);
        var first = await Verified(tenantId, person, account, now);
        var second = await Verified(tenantId, person, account, now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        await Store.FinishAsync(first.Id, RecoveryPasswordWriteResult.Unknown);
        Assert.NotNull((await Store.FindAsync(first.TicketHash))!.PasswordWriteFinishedAtUtc);
        Assert.False(await Store.ClaimResetAsync(second.Id, now));
        var later = now.AddMinutes(31);
        var fresh = await Started(tenantId, later);
        Assert.Equal(RecoveryBindResult.PendingReset, await Store.BindAsync(fresh.Id, person, account, Question, later));
        Assert.False(await Store.ClaimResetAsync(fresh.Id, later));
        Assert.Equal("Unknown", (await Store.FindAsync(first.TicketHash))!.State);
    }
    [Fact]
    public async Task A_started_ticket_cannot_bind_after_a_successful_reset_but_a_new_ticket_can()
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10, 20, "synthetic@my.nelson.edu");
        var account = new RecoveryDirectoryAccount(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false);
        var first = await Verified(tenantId, person, account, now);
        var oldStarted = await Started(tenantId, now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        await Store.FinishAsync(first.Id, RecoveryPasswordWriteResult.Accepted);
        Assert.Equal(RecoveryBindResult.Expired, await Store.BindAsync(oldStarted.Id, person, account, Question, now));
        var completedAt = (await Store.FindAsync(first.TicketHash))!.PasswordWriteFinishedAtUtc!.Value;
        var fresh = await Verified(tenantId, person, account, completedAt.AddMinutes(1));
        Assert.True(await Store.ClaimResetAsync(fresh.Id, completedAt.AddMinutes(1)));
    }
    [Fact]
    public async Task A_confirmed_policy_rejection_allows_the_same_verified_request_to_retry()
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10, 20, "synthetic@my.nelson.edu");
        var account = new RecoveryDirectoryAccount(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false);
        var first = await Verified(tenantId, person, account, now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        await Store.FinishAsync(first.Id, RecoveryPasswordWriteResult.PolicyRejected);
        Assert.Null((await Store.FindAsync(first.TicketHash))!.PasswordWriteFinishedAtUtc);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
    }
    [Theory]
    [InlineData(RecoveryPasswordWriteResult.Accepted, "Complete")]
    [InlineData(RecoveryPasswordWriteResult.PolicyRejected, "Verified")]
    [InlineData(RecoveryPasswordWriteResult.Unknown, "Unknown")]
    public async Task A_finished_write_is_persisted_even_when_the_browser_request_was_cancelled(RecoveryPasswordWriteResult outcome, string expected)
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10,20,"synthetic@my.nelson.edu");
        var first = await Verified(tenantId, person, new(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false), now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        using (var disconnected = new System.Threading.CancellationTokenSource())
        {
            disconnected.Cancel();
            using var request = GetRequiredService<Volo.Abp.Threading.ICancellationTokenProvider>().Use(disconnected.Token);
            await Store.FinishAsync(first.Id, outcome, System.Threading.CancellationToken.None);
        }
        Assert.Equal(expected, (await Store.FindAsync(first.TicketHash))!.State);
    }
    [Fact]
    public async Task The_same_directory_account_cannot_escape_a_claim_through_a_different_student_mapping()
    {
        var tenantId = Guid.NewGuid();
        using var owner = GetRequiredService<ICurrentTenant>().Change(tenantId, "nelson");
        var now = DateTime.UtcNow;
        var person = new RecoveryPerson(10, 20, "synthetic@my.nelson.edu");
        var account = new RecoveryDirectoryAccount(Guid.NewGuid(), person.UserPrincipalName, true, true, false, false);
        var first = await Verified(tenantId, person, account, now);
        var alternatePerson = new RecoveryPerson(11, 21, person.UserPrincipalName);
        var second = await Verified(tenantId, alternatePerson, account, now);
        Assert.True(await Store.ClaimResetAsync(first.Id, now));
        Assert.False(await Store.ClaimResetAsync(second.Id, now));
        var unrelatedPerson = new RecoveryPerson(12, 22, "other-synthetic@my.nelson.edu");
        var unrelated = await Verified(tenantId, unrelatedPerson,
            account with { ObjectId = Guid.NewGuid(), UserPrincipalName = unrelatedPerson.UserPrincipalName }, now);
        Assert.True(await Store.ClaimResetAsync(unrelated.Id, now));
    }

}
