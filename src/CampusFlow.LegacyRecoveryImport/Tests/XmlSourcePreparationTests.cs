using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CampusFlow.AccountRecovery;
using Xunit;

namespace CampusFlow.LegacyRecoveryImport.Tests;

public sealed class XmlSourcePreparationTests
{
    private static XElement Student(int uid = 999, string id = "00001234", int questionCount = 1, bool allowed = false) => new("Student",
        new XElement("LegacyStudentUid", uid), new XElement("StudentId", id), new XElement("StoredQuestionCount", questionCount),
        new XElement("LegacyNoQuestionAllowed", allowed ? "1" : "0"), new XElement("Questions", Enumerable.Range(0, Math.Min(questionCount, 2)).Select(i => new XElement("Question",
            new XElement("QuestionId", 7+i), new XElement("Text", "Synthetic question <&>?"), new XElement("Answer", "Synthetic private answer")))));
    private static XElement Root(params XElement[] students) => new("CampusFlowRecoveryExport", new XAttribute("FormatVersion", "1"), new XAttribute("Kind", "CampusFlow.LegacyRecoverySourceXml"),
        new XElement("LegacyServer", "windb3"), new XElement("LegacyDatabase", "CAMs_Enterprise"), new XElement("ExportedAtUtc", "2026-11-10T12:00:00.000"),
        new XElement("DatabaseCollation", "SQL_Latin1_General_CP1_CI_AS"), new XElement("AnswerCollation", "SQL_Latin1_General_CP1_CI_AS"),
        new XElement("FerpaFunctionAvailable", "1"), new XElement("ExpectedStudentCount", students.Length), new XElement("Selection", "all"), new XElement("Students", students));
    private static XmlLegacySnapshot Read(XElement? xml = null, string prefix = "")
    { var bytes = Encoding.UTF8.GetBytes(prefix+(xml ?? Root(Student())).ToString()); return XmlSourcePreparation.Read(bytes, ImportModel.Sha(bytes), "windb3", "CAMS_Enterprise"); }
    private static RecoveryManifest Prepare(XmlLegacySnapshot source, string environment = "production", int? selected = null, RecoveryManifest? previous = null) =>
        XmlSourcePreparation.Prepare(source, environment, [new(42,"00001234"),new(43,"00005678")], [42,43], selected, previous);
    private static string[] Args() => ["prepare-xml", "--environment", "nonprod", "--expected-legacy-server", "windb3", "--expected-legacy-database", "CAMS_Enterprise", "--input", "/private/tmp/source.xml", "--expected-sha256", new string('a',64), "--output", "/private/tmp/proof.json", "--student-upn", "synthetic@my.nelson.edu"];

    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("not-an-email")] [InlineData("person@example.edu")] [InlineData("Display <synthetic@my.nelson.edu>")]
    public void Missing_or_invalid_replica_institutional_email_is_ineligible_instead_of_aborting_the_batch(string email)
    { Assert.Null(SqlSources.SchoolUpn(email)); }
    [Fact] public void A_valid_student_UPN_is_normalized_for_unique_matching()
    { Assert.Equal("synthetic@my.nelson.edu",SqlSources.SchoolUpn(" SYNTHETIC@my.nelson.edu ")); }
    [Fact] public void Bulk_email_mapping_rejects_shared_owners_and_multiple_active_addresses()
    {
        var eligible=SqlSources.EligibleEmailOwners([(1,"ONE@my.nelson.edu"),(2,"shared@my.nelson.edu"),(3," SHARED@my.nelson.edu "),
            (4,"four@my.nelson.edu"),(4,"four@my.nelson.edu"),(5,"five@my.nelson.edu"),(5,null),(6,""),(7,"other@example.edu"),(8,"eight@my.nelson.edu")]);
        Assert.Equal(new[]{1,8},eligible.Order().ToArray());
    }
    [Fact] public void Complete_typed_XML_and_the_pasted_grid_header_are_supported()
    { Assert.Single(Read().Students); Assert.Single(Read(prefix:"(No column name)\r\n").Students); Assert.Equal("00001234",Read().Students[0].Student.Id); }
    [Fact] public void A_truncated_export_or_declared_count_mismatch_is_rejected()
    {
        var text=Root(Student()).ToString()[..^12];var bytes=Encoding.UTF8.GetBytes(text);
        Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Read(bytes,ImportModel.Sha(bytes),"windb3","CAMS_Enterprise"));
        var root=Root(Student());root.Element("ExpectedStudentCount")!.Value="2";Assert.Throws<ImportFailure>(()=>Read(root));
    }
    [Fact] public void Content_hash_and_source_identity_are_pinned()
    { var bytes=Encoding.UTF8.GetBytes(Root(Student()).ToString());Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Read(bytes,new string('0',64),"windb3","CAMS_Enterprise"));Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Read(bytes,ImportModel.Sha(bytes),"other","CAMS_Enterprise")); }
    [Fact] public void External_entities_and_DTDs_are_never_processed()
    { var bytes=Encoding.UTF8.GetBytes("<!DOCTYPE CampusFlowRecoveryExport [<!ENTITY secret SYSTEM 'file:///synthetic-private'>]>"+Root(Student()).ToString());Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Read(bytes,ImportModel.Sha(bytes),"windb3","CAMS_Enterprise")); }
    [Fact] public void Unknown_elements_duplicate_metadata_and_nested_answer_markup_are_rejected()
    {
        var root=Root(Student());root.Add(new XElement("Unexpected","value"));Assert.Throws<ImportFailure>(()=>Read(root));
        root=Root(Student());root.Add(new XElement("LegacyServer","windb3"));Assert.Throws<ImportFailure>(()=>Read(root));
        root=Root(Student());root.Descendants("Answer").Single().Add(new XElement("Injected","value"));Assert.Throws<ImportFailure>(()=>Read(root));
    }
    [Fact] public void Invalid_comparison_rules_or_eligibility_flags_fail_closed()
    {
        var root=Root(Student());root.Element("AnswerCollation")!.Value="Turkish_CI_AS";Assert.Throws<ImportFailure>(()=>Read(root));
        Assert.Throws<ImportFailure>(()=>Read(Root(Student(allowed:true))));
        root=Root(Student(questionCount:0,allowed:true));root.Element("FerpaFunctionAvailable")!.Value="0";Assert.Throws<ImportFailure>(()=>Read(root));
    }
    [Fact] public void Duplicate_legacy_UID_or_inconsistent_question_counts_are_rejected()
    {
        Assert.Throws<ImportFailure>(()=>Read(Root(Student(),Student())));
        var root=Root(Student());root.Descendants("Questions").Single().RemoveNodes();Assert.Throws<ImportFailure>(()=>Read(root));
    }
    [Fact] public void Preparation_preserves_independent_UIDs_and_hashes_answers_without_plaintext_output()
    {
        var manifest=Prepare(Read());var row=Assert.Single(manifest.Records);
        Assert.Equal(42,row.StudentUid);Assert.Equal(999,row.LegacyStudentUid);Assert.Equal(ImportModel.StudentIdHash("00001234"),row.StudentIdHash);
        Assert.True(RecoveryAnswerHash.Verify("Synthetic private answer",row.AnswerHash));Assert.Equal("Synthetic question <&>?",row.Question);
        Assert.DoesNotContain("Synthetic private answer",JsonSerializer.Serialize(manifest,ImportModel.Json));
    }
    [Fact] public void Nonprod_requires_one_student_and_does_not_include_other_mapped_students()
    {
        var source=Read(Root(Student(),Student(1000,"00005678")));
        Assert.Throws<ImportFailure>(()=>Prepare(source,"nonprod"));
        var single=Prepare(source,"nonprod",42);Assert.Equal(42,Assert.Single(single.Records).StudentUid);
        Assert.Equal(2,Prepare(source).Records.Count);
    }
    [Fact] public void SQL_omission_of_an_empty_Questions_element_is_valid_only_for_zero_stored_questions()
    {
        var root=Root(Student(questionCount:0,allowed:true));root.Descendants("Questions").Single().Remove();
        Assert.True(Assert.Single(Prepare(Read(root)).Records).LegacyNoQuestionAllowed);
        root=Root(Student());root.Descendants("Questions").Single().Remove();Assert.Throws<ImportFailure>(()=>Read(root));
    }
    [Fact] public void No_question_proof_requires_the_exact_positive_exported_predicate()
    { Assert.True(Assert.Single(Prepare(Read(Root(Student(questionCount:0,allowed:true)))).Records).LegacyNoQuestionAllowed);Assert.True(Assert.Single(Prepare(Read(Root(Student(questionCount:0)))).Records).IsBlocked); }
    [Fact] public void Multiple_or_invalid_questions_become_blocked_proof_instead_of_bypass()
    {
        Assert.True(Assert.Single(Prepare(Read(Root(Student(questionCount:3)))).Records).IsBlocked);
        var root=Root(Student());root.Descendants("Answer").Single().Remove();Assert.True(Assert.Single(Prepare(Read(root)).Records).IsBlocked);
    }
    [Fact] public void Ambiguous_or_missing_student_mappings_never_create_usable_proof()
    {
        var source=Read(Root(Student(),Student(1000,"00001234")));Assert.Throws<ImportFailure>(()=>Prepare(source));
        Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Prepare(Read(),"production",[new(42,"00001234"),new(43,"00001234")],[42,43],null));
        Assert.Throws<ImportFailure>(()=>XmlSourcePreparation.Prepare(Read(),"production",[new(42,"1234")],[42],null));
    }
    [Fact] public void A_repeated_source_conversion_reuses_verified_prior_hashes()
    { var source=Read();var first=Prepare(source);var second=Prepare(source,previous:first);Assert.Equal(first.Records[0].AnswerHash,second.Records[0].AnswerHash);Assert.Equal(first.Records[0].SourceFingerprint,second.Records[0].SourceFingerprint); }
    [Fact] public void Changed_answer_in_a_previous_proof_stops_conversion()
    { var first=Prepare(Read());var root=Root(Student());root.Descendants("Answer").Single().Value="Changed synthetic answer";Assert.ThrowsAny<Exception>(()=>Prepare(Read(root),previous:first)); }
    [Fact] public void Xml_preparation_is_offline_output_only_and_rejects_write_or_credential_flags()
    {
        Assert.NotNull(XmlPreparationOptions.Parse(Args()));
        Assert.Throws<ImportFailure>(()=>XmlPreparationOptions.Parse([..Args(),"--apply"]));
        Assert.Throws<ImportFailure>(()=>XmlPreparationOptions.Parse([..Args(),"--password","do-not-print"]));
        Assert.Throws<ImportFailure>(()=>XmlPreparationOptions.Parse([..Args(),"--all"]));
        var args=Args()[..^2];Assert.Throws<ImportFailure>(()=>XmlPreparationOptions.Parse([..args,"--all"]));
    }
}
