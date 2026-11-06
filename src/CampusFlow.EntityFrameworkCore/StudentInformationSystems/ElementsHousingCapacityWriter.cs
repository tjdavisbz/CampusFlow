using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;

namespace CampusFlow.StudentInformationSystems;

public sealed partial class ElementsHousingAssignmentWriter
{
    private static readonly string[] PropertyFields = ["housingPeriodPropertyID", "isActive", "isParkingAvailable", "isTransfers", "isSmokingAllowed", "isWheelchairAccessible", "genderID", "ageClassification", "age", "ageLowerLimit", "ageUpperLimit", "gpaClassification", "gpa", "gpaLowerLimit", "gpaUpperLimit", "amenityIDs", "programsIDs", "yearClassificationIDs", "glossarySportsIDs", "floors", "units", "rooms"];
    private static readonly string[] FloorFields = ["housingPeriodFloorID", "floorName", "genderID", "isTransfers", "isWheelchairAccessible", "gpaClassification", "gpa", "gpaLowerLimit", "gpaUpperLimit", "ageClassification", "age", "ageLowerLimit", "ageUpperLimit", "amenityIDs", "programsIDs", "yearClassificationIDs", "glossarySportsIDs"];
    private static readonly string[] UnitFields = ["housingPeriodUnitID", "unitName", "totalKeysCountPerUnit", "unitKeyCodes"];
    private static readonly string[] RoomFields = ["housingPeriodRoomID", "housingPeriodFloorID", "roomName", "roomTypeID", "roomArea", "occupancy", "bedTypeID", "roomStatusID", "isActive", "emotionalSupportAnimal", "totalKeysCountPerRoom", "roomKeyCodes", "isHousingStaff", "studentTypeID", "genderID", "isTransfers", "isWheelchairAccessible", "gpaClassification", "gpa", "gpaLowerLimit", "gpaUpperLimit", "ageClassification", "age", "ageLowerLimit", "ageUpperLimit", "roomRate", "perPersonRate", "amenityIDs", "programsIDs", "yearClassificationIDs", "glossarySportsIDs"];

    private async Task<JsonNode> HousingDataAsync(string route, CancellationToken cancellationToken, JsonObject? search = null)
    {
        EnsureWriteEnabled();var token=await GetTokenAsync(cancellationToken);
        using var request=Request(search is null?HttpMethod.Get:HttpMethod.Post,route,token);
        if(search is not null)request.Content=new StringContent(search.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await _client.SendAsync(request,cancellationToken);
        return (await ReadAsync(response,cancellationToken))["data"]??throw new InvalidOperationException("Elements returned no housing data.");
    }
    public async Task VerifyRoomsAsync(int periodId,IReadOnlyList<HousingWorkspaceRoom> expected,CancellationToken cancellationToken=default)
    {
        if(periodId<=0)throw new ArgumentOutOfRangeException(nameof(periodId));
        if(expected.Count==0)return;
        var choices=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/search/{periodId}",cancellationToken)).AsArray();
        var catalog=new List<JsonObject>();
        foreach(var choice in choices.OfType<JsonObject>())
        {
            if(ReadInt(choice,"housingPeriodID")!=periodId)throw new InvalidOperationException("Property period changed.");
            var property=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/{ReadInt(choice,"housingPeriodPropertyID")}",cancellationToken)).AsObject();
            if(ReadInt(property,"housingPeriodID")!=periodId)throw new InvalidOperationException("Property period changed.");
            catalog.AddRange(property["rooms"]!.AsArray().OfType<JsonObject>());
        }
        foreach(var room in expected)
        {
            var actual=catalog.Where(x=>ReadInt(x,"housingPeriodRoomID")==room.Id&&ReadInt(x,"housingPeriodID")==periodId).ToList();
            if(actual.Count!=1 || ReadInt(actual[0],"occupancy")!=room.Capacity || ReadString(actual[0],"roomName")!=room.Name.Trim())
                throw new InvalidOperationException("The live room identity or capacity changed. Refresh before applying.");
            // The vendor's aggregate room searches omit valid assignments in this tenant.
            // Verify every known occupant using the working student-scoped assignment API;
            // the complete replica snapshot remains the check for additional occupants.
            foreach(var occupant in room.Occupants)
            {
                var assigned=await GetAssignedRoomAsync(occupant.StudentUid,periodId,cancellationToken);
                if(assigned is null||!string.Equals(assigned.Value.Building.Trim(),room.Building.Trim(),StringComparison.OrdinalIgnoreCase)||
                    !string.Equals(assigned.Value.Room.Trim(),room.Name.Trim(),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A room occupant changed in Elements. Refresh before applying.");
            }
        }
    }
    public async Task UpdateCapacitiesAsync(int periodId,IReadOnlyList<HousingCapacityChange> changes,CancellationToken cancellationToken=default)
    {
        EnsureWriteEnabled();
        if(periodId<=0||changes.Any(x=>x.RoomId<=0||x.Capacity<1||x.Capacity>10)||changes.Select(x=>x.RoomId).Distinct().Count()!=changes.Count)
            throw new ArgumentException("Invalid room capacity changes.");
        if(changes.Count==0)return;
        var choices=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/search/{periodId}",cancellationToken)).AsArray();
        var prepared=new List<(int Id,JsonObject Body,JsonObject Snapshot)>();var found=new HashSet<int>();
        foreach(var choice in choices.OfType<JsonObject>())
        {
            var id=ReadInt(choice,"housingPeriodPropertyID");
            if(ReadInt(choice,"housingPeriodID")!=periodId||id<=0)throw new InvalidOperationException("A property belongs to a different period.");
            var current=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/{id}",cancellationToken)).AsObject();
            if(ReadInt(current,"housingPeriodPropertyID")!=id)throw new InvalidOperationException("Property identity changed.");
            var roomIds=current["rooms"]?.AsArray().OfType<JsonObject>().Select(x=>ReadInt(x,"housingPeriodRoomID")).ToHashSet()??[];
            var selected=changes.Where(x=>roomIds.Contains(x.RoomId)).ToList();if(selected.Count==0)continue;
            foreach(var change in selected)if(!found.Add(change.RoomId))throw new InvalidOperationException("Duplicate room identity in Elements.");
            prepared.Add((id,BuildCapacityUpdate(current,periodId,selected),(JsonObject)current.DeepClone()));
        }
        if(found.Count!=changes.Count)throw new InvalidOperationException("A selected room is not in this housing period.");
        foreach(var (id,body,snapshot) in prepared)
        {
            // Re-read before writing; retain the entire property/floor/unit/room payload.
            var current=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/{id}",cancellationToken)).AsObject();
            var selected=changes.Where(x=>current["rooms"]!.AsArray().Any(r=>r?["housingPeriodRoomID"]?.GetValue<int>()==x.RoomId)).ToList();
            if(!JsonNode.DeepEquals(snapshot,current)||!JsonNode.DeepEquals(body,BuildCapacityUpdate(current,periodId,selected)))throw new InvalidOperationException("Property settings changed before the capacity update.");
            using var request=Request(HttpMethod.Post,"/api/student-life/housing/housing-periods/housing-period-properties/save",await GetTokenAsync(cancellationToken));
            request.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");
            using var response=await _client.SendAsync(request,cancellationToken);await ReadAsync(response,cancellationToken);
            var after=(await HousingDataAsync($"/api/student-life/housing/housing-periods/housing-period-properties/{id}",cancellationToken)).AsObject();
            var expected=(JsonObject)snapshot.DeepClone();
            foreach(var change in selected)expected["rooms"]!.AsArray().OfType<JsonObject>().Single(x=>ReadInt(x,"housingPeriodRoomID")==change.RoomId)["occupancy"]=change.Capacity;
            if(ReadInt(after,"housingPeriodID")!=periodId || ReadInt(after,"housingPeriodPropertyID")!=id || !JsonNode.DeepEquals(expected,after))
                throw new InvalidOperationException("The updated capacity or other property settings could not be verified. Review Elements before retrying.");
        }
    }
    internal static JsonObject BuildCapacityUpdate(JsonObject current,int periodId,IReadOnlyList<HousingCapacityChange> changes)
    {
        if(ReadInt(current,"housingPeriodID")!=periodId||ReadInt(current,"housingPeriodPropertyID")<=0)throw new InvalidOperationException("Property period does not match.");
        var result=PropertyPayload(current);var rooms=result["rooms"]!.AsArray();
        foreach(var change in changes)
        {
            if(change.Capacity<1||change.Capacity>10)throw new ArgumentException("Elements room capacity must be between 1 and 10.");
            var matches=rooms.OfType<JsonObject>().Where(x=>ReadInt(x,"housingPeriodRoomID")==change.RoomId).ToList();
            if(matches.Count!=1||ReadInt(matches[0],"occupancy")!=change.PreviousCapacity)throw new InvalidOperationException("Room capacity changed since refresh.");
            matches[0]["occupancy"]=change.Capacity;
        }
        return result;
    }
    internal static JsonObject PropertyPayload(JsonObject current)
    {
        if(current["rooms"] is not JsonArray sourceRooms || sourceRooms.OfType<JsonObject>().Count()!=sourceRooms.Count ||
            sourceRooms.OfType<JsonObject>().Any(x=>ReadInt(x,"housingPeriodID")!=ReadInt(current,"housingPeriodID")||ReadInt(x,"housingPeriodPropertyID")!=ReadInt(current,"housingPeriodPropertyID")) ||
            sourceRooms.OfType<JsonObject>().Select(x=>ReadInt(x,"housingPeriodRoomID")).Distinct().Count()!=sourceRooms.Count)
            throw new InvalidOperationException("The property contains invalid, duplicate or foreign rooms.");
        var result=CopyFields(current,PropertyFields);
        foreach(var (name,fields) in new[]{("floors",FloorFields),("units",UnitFields),("rooms",RoomFields)})
        {
            if(current[name] is null){result[name]=null;continue;}
            var rows=new JsonArray();foreach(var row in current[name]!.AsArray())
            {if(row is not JsonObject value)throw new InvalidOperationException("A property list contains an invalid row.");rows.Add(CopyFields(value,fields));}result[name]=rows;
        }
        if(result["rooms"] is not JsonArray)throw new InvalidOperationException("The property room list is missing.");
        return result;
    }
    private static JsonObject CopyFields(JsonObject source,IReadOnlyList<string> fields)
    {
        var result=new JsonObject();foreach(var name in fields)
        {if(!source.ContainsKey(name))throw new InvalidOperationException("Elements omitted a property field; no capacity update was sent.");result[name]=source[name]?.DeepClone();}return result;
    }
}
