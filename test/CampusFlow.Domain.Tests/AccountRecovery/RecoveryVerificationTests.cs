using System;
using System.Collections.Generic;
using Xunit;
namespace CampusFlow.AccountRecovery;
public sealed class RecoveryVerificationTests
{
    [Fact]public void Imported_answers_use_salted_slow_hashes_and_case_trim_compatible_matching()
    {var a=RecoveryAnswerHash.Create(" Synthetic answer ");var b=RecoveryAnswerHash.Create("synthetic answer");Assert.NotEqual(a,b);Assert.DoesNotContain("Synthetic answer",a);Assert.True(RecoveryAnswerHash.Verify("SYNTHETIC ANSWER",a));Assert.False(RecoveryAnswerHash.Verify("wrong",a));}
    [Theory][InlineData(null)][InlineData("")][InlineData("plain-answer")][InlineData("PBKDF2-SHA256$1$AA==$AA==")][InlineData("PBKDF2-SHA256$999999999$AA==$AA==")]
    public void Unknown_plain_or_unbounded_hash_formats_are_never_accepted(string? encoded)
    {Assert.False(RecoveryAnswerHash.IsValid(encoded));Assert.False(RecoveryAnswerHash.Verify("synthetic",encoded));}
    [Fact]public void Question_proofs_require_hash_and_deny_ambiguous_no_question_flags()
    {Assert.Throws<ArgumentException>(()=>new RecoveryVerification(Guid.NewGuid(),Guid.NewGuid(),1,2,7,"Question?",null,false,false,new('a',64)));Assert.Throws<ArgumentException>(()=>new RecoveryVerification(Guid.NewGuid(),Guid.NewGuid(),1,2,null,null,null,false,false,new('a',64)));}
    [Fact]public void An_explicitly_blocked_import_is_not_made_eligible_by_defaults()
    {var v=new RecoveryVerification(Guid.NewGuid(),Guid.NewGuid(),1,2,null,null,null,false,true,new('a',64));Assert.True(v.IsBlocked);Assert.False(v.LegacyNoQuestionAllowed);}
    [Fact]public void A_claim_is_one_use_and_unknown_outcomes_cannot_be_reclaimed()
    {var now=DateTime.UtcNow;var r=new RecoveryRequest(Guid.NewGuid(),Guid.NewGuid(),new('a',64),new('b',64),now);var p=new RecoveryPerson(1,2,"synthetic@my.nelson.edu");Assert.True(r.Bind(p,new(Guid.NewGuid(),p.UserPrincipalName,true,true,false,false),new(true,null,null,true,new('c',64)),now));Assert.True(r.ClaimReset(now));Assert.False(r.ClaimReset(now));r.Finish(RecoveryPasswordWriteResult.Unknown);Assert.False(r.ClaimReset(now));Assert.DoesNotContain("synthetic",r.ToString());}
}
