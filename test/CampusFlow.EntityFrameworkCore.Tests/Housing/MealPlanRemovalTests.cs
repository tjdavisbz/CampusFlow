using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class MealPlanRemovalTests
{
    private static ThesisElementsMealPlanRemovalService Service(Handler handler,IConfiguration? configuration=null)
    {
        var environment=Substitute.For<IElementsHousingAssignmentWriter>();
        return new(configuration??HousingCapacityWriterTests.Config(),environment,new HttpClient(handler),(_,_)=>Task.FromResult<IReadOnlyList<StudentMealPlanAssignment>>([new(10,20,269,"Spring plan",100,new DateTime(2027,1,1),new DateTime(2027,5,1)),new(11,21,268,"Fall plan",100,new DateTime(2026,8,1),new DateTime(2026,12,1))]));
    }
    [Fact]public async Task Delete_targets_one_students_assignment_and_preserves_the_other_term()
    {var h=new Handler();await Service(h).RemoveAsync("123",269,10,20);Assert.Equal(1,h.Deletes);Assert.Equal(11,Assert.Single(h.Rows).Value);}
    [Theory][InlineData(268,10,20)][InlineData(269,11,21)][InlineData(269,10,21)][InlineData(269,999,20)]
    public async Task Cross_term_or_changed_identity_is_rejected_before_delete(int term,int assignment,int plan)
    {var h=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).RemoveAsync("123",term,assignment,plan));Assert.Equal(0,h.Deletes);}
    [Fact]public async Task The_off_by_default_gate_prevents_removal()
    {var h=new Handler();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h,config).RemoveAsync("123",269,10,20));Assert.Equal(0,h.Requests);}
    [Fact]public async Task A_foreign_student_in_the_live_response_prevents_removal()
    {var h=new Handler{ForeignStudent=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).RemoveAsync("123",269,10,20));Assert.Equal(0,h.Deletes);}
    [Fact]public async Task Successful_HTTP_without_actual_removal_is_not_reported_as_success()
    {var h=new Handler{KeepAssignment=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).RemoveAsync("123",269,10,20));Assert.Equal(1,h.Deletes);}
    [Fact]public async Task Explicit_failure_does_not_get_automatically_retried()
    {var h=new Handler{RejectDelete=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).RemoveAsync("123",269,10,20));Assert.Equal(1,h.Deletes);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Real_response_without_plan_ID_is_matched_by_name_dates_and_assignment(bool wrongDates)
    {
        var h=new Handler{WithoutPlanId=true,WrongDates=wrongDates};
        if(wrongDates){await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).RemoveAsync("123",269,10,20));Assert.Equal(0,h.Deletes);}
        else{await Service(h).RemoveAsync("123",269,10,20);Assert.Equal(1,h.Deletes);}
    }
    [Fact]public async Task Catalog_delete_is_never_called()
    {var h=new Handler();await Service(h).RemoveAsync("123",269,10,20);Assert.DoesNotContain(h.Paths,x=>x=="/api/student-life/housing/meal-plan/20");}
    private sealed class Handler:HttpMessageHandler
    {
        public Dictionary<int,int> Rows=new(){{10,10},{11,11}};public int Deletes;public int Requests;public bool KeepAssignment,RejectDelete,ForeignStudent,WithoutPlanId,WrongDates;public List<string> Paths=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Requests++;var path=r.RequestUri!.AbsolutePath;Paths.Add(path);Assert.Equal("app-elm-nonprod-usc-integrationapi.azurewebsites.net",r.RequestUri.Host);
            JsonObject root;
            if(path.EndsWith("/Authenticate"))root=new(){["data"]=new JsonObject{["token"]="test"}};
            else if(r.Method==HttpMethod.Delete){Assert.EndsWith("/delete-student-assignment/123/10",path);Deletes++;if(!KeepAssignment&&!RejectDelete)Rows.Remove(10);root=new(){["isSuccess"]=!RejectDelete};}
            else{var rows=new JsonArray();foreach(var id in Rows.Keys)
            {
                var row=new JsonObject{["assignmentID"]=id,["mealPlanID"]=id==10?20:21,["studentUID"]=ForeignStudent?999:123};
                if(WithoutPlanId){row.Remove("mealPlanID");row["mealPlanName"]=id==10?"Spring plan":"Fall plan";row["startDate"]=WrongDates?"2030-01-01":id==10?"2027-01-01":"2026-08-01";row["endDate"]=id==10?"2027-05-01":"2026-12-01";}rows.Add(row);
            }root=new(){["isSuccess"]=true,["data"]=rows};}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(root.ToJsonString(),Encoding.UTF8,"application/json")});
        }
    }
}
