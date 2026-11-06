using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class HousingCapacityWriterTests
{
    internal static JsonObject Property()=>JsonNode.Parse("""
{
  "housingPeriodPropertyID": 21,
  "housingPeriodID": 1,
  "propertyName": "Commuter",
  "propertyTypeID": 1,
  "isActive": true,
  "isParkingAvailable": false,
  "isTransfers": false,
  "isSmokingAllowed": false,
  "isWheelchairAccessible": false,
  "moveInDate": "test",
  "moveOutDate": "test",
  "genderID": 1,
  "amenityIDs": [],
  "programsIDs": [],
  "yearClassificationIDs": [],
  "glossarySportsIDs": [],
  "streetAddress": "test",
  "postalCode": "test",
  "city": "test",
  "ageClassification": "test",
  "age": 1,
  "ageLowerLimit": 1,
  "ageUpperLimit": 1,
  "gpaClassification": "test",
  "gpa": 0,
  "gpaLowerLimit": 0,
  "gpaUpperLimit": 0,
  "floors": [
    {
      "housingPeriodFloorID": 11,
      "housingPeriodPropertyID": 21,
      "housingPeriodID": 1,
      "originalFloorID": 1,
      "floorName": 1,
      "totalUnitsCount": 1,
      "totalRoomsCount": 1,
      "genderID": 1,
      "isTransfers": false,
      "isWheelchairAccessible": false,
      "ageClassification": "test",
      "age": 1,
      "ageLowerLimit": 1,
      "ageUpperLimit": 1,
      "gpaClassification": "test",
      "gpa": 0,
      "gpaLowerLimit": 0,
      "gpaUpperLimit": 0,
      "amenityIDs": [],
      "programsIDs": [],
      "yearClassificationIDs": [],
      "glossarySportsIDs": []
    }
  ],
  "units": [],
  "rooms": [
    {
      "housingPeriodRoomID": 31,
      "housingPeriodUnitID": 1,
      "housingPeriodFloorID": 11,
      "housingPeriodPropertyID": 21,
      "housingPeriodID": 1,
      "originalRoomID": 1,
      "roomName": "000",
      "roomTypeID": 1,
      "roomArea": "test",
      "occupancy": 10,
      "bedTypeID": 1,
      "genderID": 1,
      "studentTypeID": 1,
      "emotionalSupportAnimal": "test",
      "isActive": false,
      "totalKeysCountPerRoom": 1,
      "roomKeyCodes": "test",
      "isHousingStaff": false,
      "isWheelchairAccessible": false,
      "roomStatusID": 1,
      "isTransfers": false,
      "residentTypeID": 1,
      "ageClassification": "test",
      "age": 1,
      "ageLowerLimit": 1,
      "ageUpperLimit": 1,
      "gpaClassification": "test",
      "gpa": 0,
      "gpaLowerLimit": 0,
      "gpaUpperLimit": 0,
      "roomRate": 0,
      "perPersonRate": 0,
      "amenityIDs": [],
      "programsIDs": [],
      "yearClassificationIDs": [],
      "glossarySportsIDs": []
    },
    {
      "housingPeriodRoomID": 32,
      "housingPeriodUnitID": 1,
      "housingPeriodFloorID": 11,
      "housingPeriodPropertyID": 21,
      "housingPeriodID": 1,
      "originalRoomID": 1,
      "roomName": "001",
      "roomTypeID": 1,
      "roomArea": "test",
      "occupancy": 2,
      "bedTypeID": 1,
      "genderID": 1,
      "studentTypeID": 1,
      "emotionalSupportAnimal": "test",
      "isActive": false,
      "totalKeysCountPerRoom": 1,
      "roomKeyCodes": "test",
      "isHousingStaff": false,
      "isWheelchairAccessible": false,
      "roomStatusID": 1,
      "isTransfers": false,
      "residentTypeID": 1,
      "ageClassification": "test",
      "age": 1,
      "ageLowerLimit": 1,
      "ageUpperLimit": 1,
      "gpaClassification": "test",
      "gpa": 0,
      "gpaLowerLimit": 0,
      "gpaUpperLimit": 0,
      "roomRate": 0,
      "perPersonRate": 0,
      "amenityIDs": [],
      "programsIDs": [],
      "yearClassificationIDs": [],
      "glossarySportsIDs": []
    }
  ]
}
""")!.AsObject();
    internal static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    {
        ["Housing:EnableNonProductionWrites"]="true",["App:EnvironmentLabel"]="NON-PRODUCTION",
        ["MealPlans:EnableAdministrativeRemoval"]="true",
        ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
        ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",
        ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",
        ["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="test",
        ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB"
    }).Build();
    [Fact]public void Only_the_selected_room_capacity_changes_in_the_complete_property_payload()
    {
        var property=Property();var original=(JsonObject)property.DeepClone();var expected=ElementsHousingAssignmentWriter.PropertyPayload(property);expected["rooms"]![0]!["occupancy"]=9;
        var body=ElementsHousingAssignmentWriter.BuildCapacityUpdate(property,1,[new(31,10,9)]);
        Assert.True(JsonNode.DeepEquals(expected,body));Assert.True(JsonNode.DeepEquals(original,property));
        Assert.Equal(2,body["rooms"]![1]!["occupancy"]!.GetValue<int>());
        Assert.Equal(0,body["rooms"]![0]!["roomRate"]!.GetValue<int>());
    }
    [Theory][InlineData(2,10,9)][InlineData(1,9,8)][InlineData(1,10,1001)]
    public void Wrong_period_stale_capacity_or_invalid_capacity_is_rejected(int period,int previous,int desired)=>Assert.ThrowsAny<Exception>(()=>ElementsHousingAssignmentWriter.BuildCapacityUpdate(Property(),period,[new(31,previous,desired)]));
    [Fact]public void Missing_unrelated_fields_block_writes_instead_of_defaulting_them()
    {var property=Property();property["rooms"]![1]!.AsObject().Remove("roomRate");Assert.Throws<InvalidOperationException>(()=>ElementsHousingAssignmentWriter.BuildCapacityUpdate(property,1,[new(31,10,9)]));}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Capacity_save_uses_period_property_endpoint_and_verifies_every_field(bool corruptReadback)
    {
        var handler=new Handler(corruptReadback);var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:Housing:IntegrationToken","test");
        var writer=new ElementsHousingAssignmentWriter(Config(),cache,new HttpClient(handler));
        if(corruptReadback)await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.UpdateCapacitiesAsync(1,[new(31,10,9)]));
        else await writer.UpdateCapacitiesAsync(1,[new(31,10,9)]);
        Assert.Equal(1,handler.Writes);Assert.Equal(9,handler.Current["rooms"]![0]!["occupancy"]!.GetValue<int>());
    }
    [Fact]public async Task Stale_capacity_does_not_make_a_save_request()
    {
        var handler=new Handler(false);var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:Housing:IntegrationToken","test");var writer=new ElementsHousingAssignmentWriter(Config(),cache,new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.UpdateCapacitiesAsync(1,[new(31,9,8)]));Assert.Equal(0,handler.Writes);
    }
    private sealed class Handler(bool corrupt):HttpMessageHandler
    {
        public JsonObject Current=Property();public int Writes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("app-elm-nonprod-usc-integrationapi.azurewebsites.net",request.RequestUri!.Host);
            if(request.Method==HttpMethod.Post)
            {
                Assert.EndsWith("/housing-period-properties/save",request.RequestUri.AbsolutePath);Writes++;
                var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();Assert.Equal(21,body["housingPeriodPropertyID"]!.GetValue<int>());
                Current["rooms"]![0]!["occupancy"]=body["rooms"]![0]!["occupancy"]!.DeepClone();if(corrupt)Current["rooms"]![1]!["roomRate"]=123;
                return Reply(new JsonObject{["isSuccess"]=true});
            }
            JsonNode data=request.RequestUri.AbsolutePath.Contains("/search/")?new JsonArray(new JsonObject{["housingPeriodPropertyID"]=21,["housingPeriodID"]=1}):Current.DeepClone();
            return Reply(new JsonObject{["isSuccess"]=true,["data"]=data});
        }
        private static HttpResponseMessage Reply(JsonNode root)=>new(HttpStatusCode.OK){Content=new StringContent(root.ToJsonString(),Encoding.UTF8,"application/json")};
    }
}
