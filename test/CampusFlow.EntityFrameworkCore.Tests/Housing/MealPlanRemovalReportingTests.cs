using System.Threading.Tasks;
using CampusFlow.Housing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class MealPlanRemovalReportingTests
{
    private static async Task<SqliteConnection> Database()
    {
        var connection=new SqliteConnection("Data Source=:memory:;Pooling=False");await connection.OpenAsync();
        await using var setup=connection.CreateCommand();setup.CommandText="""
            ATTACH DATABASE ':memory:' AS dbo;
            CREATE TABLE dbo.Billing (OwnerUID INT,TermCalendarID INT,TransDocID INT,Debits NUMERIC,Credits NUMERIC,Voided TEXT,Reversing TEXT);
            CREATE TABLE dbo.BillingBatch (BillingBatchID INT,BatchMasterID INT,OwnerUID INT,TermCalendarID INT,TransDocID INT,
                ARTypeID INT,Debits NUMERIC,Credits NUMERIC,RefNo TEXT,Description TEXT);
            CREATE TABLE dbo.BatchMaster (BatchMasterID INT,Comment TEXT,Status TEXT,SourceModuleID INT,TermBased INT);
            INSERT INTO dbo.BatchMaster VALUES (100,'Meal Plan Charges','Open',15,1);
            """;await setup.ExecuteNonQueryAsync();return connection;
    }
    private static SqliteCommand Review(SqliteConnection connection)
    {
        var command=connection.CreateCommand();command.CommandText=ElementsMealPlanRemovalGateway.ReportedSql;
        foreach(var (name,value) in new[]{("Student",10),("Term",269),("BaseDoc",165),("TaxDoc",261),("Legacy",200)})
            command.Parameters.AddWithValue(name,value);
        return command;
    }
    [Fact]public async Task The_reporting_query_includes_real_zero_dollar_entries_but_keeps_student_term_and_document_scope()
    {
        await using var database=await Database();await using var setup=database.CreateCommand();setup.CommandText="""
            INSERT INTO dbo.BillingBatch VALUES (1,100,10,269,165,0,0,0,'','Meal Plan');
            INSERT INTO dbo.BillingBatch VALUES (2,100,11,269,165,0,0,0,'','Meal Plan');
            INSERT INTO dbo.BillingBatch VALUES (3,100,10,268,165,0,0,0,'','Meal Plan');
            INSERT INTO dbo.BillingBatch VALUES (4,100,10,269,999,0,0,0,'','Unrelated');
            """;await setup.ExecuteNonQueryAsync();
        await using var query=Review(database);await using var rows=await query.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());Assert.Equal(0,rows.GetInt32(0));
        Assert.True(await rows.NextResultAsync());Assert.True(await rows.ReadAsync());
        Assert.Equal(1,rows.GetInt32(0));Assert.Equal(0,rows.GetDecimal(4));Assert.False(await rows.ReadAsync());
    }
    [Theory][InlineData(0)][InlineData(715)]
    public async Task Posted_entries_are_protected_even_when_the_amount_is_zero(int amount)
    {
        await using var database=await Database();await using var setup=database.CreateCommand();
        setup.CommandText="INSERT INTO dbo.Billing VALUES (10,269,165,@Amount,0,'No','No')";
        setup.Parameters.AddWithValue("Amount",amount);await setup.ExecuteNonQueryAsync();
        await using var query=Review(database);await using var rows=await query.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());Assert.Equal(1,rows.GetInt32(0));
    }
    [Fact]public async Task Voided_reversed_other_term_and_other_student_postings_do_not_block_this_review()
    {
        await using var database=await Database();await using var setup=database.CreateCommand();setup.CommandText="""
            INSERT INTO dbo.Billing VALUES (10,269,165,0,0,'Yes','No'),(10,269,165,0,0,'No','Yes'),
                (10,268,165,0,0,'No','No'),(11,269,165,0,0,'No','No'),(10,269,999,0,0,'No','No');
            """;await setup.ExecuteNonQueryAsync();
        await using var query=Review(database);await using var rows=await query.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());Assert.Equal(0,rows.GetInt32(0));
    }
}
