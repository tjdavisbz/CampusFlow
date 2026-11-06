using System;
using System.Linq;
using CampusFlow.StudentInformationSystems;
using Xunit;
namespace CampusFlow.CourseSelections;
public class CourseScheduleNormalizationTests
{
    private static StudentCourseScheduleItem Row(string id="1",string instructor="First Instructor",string days="M",int hour=9) =>
        new(StudentInformationSystemProvider.ThesisElements,id,"10","269","B27C","Spring 2027","TEST","101","","100","Example",3,"Unofficial",null,null,instructor,days,TimeSpan.FromHours(hour),TimeSpan.FromHours(hour+1),"Room 1",null,null,null);
    [Fact] public void Multiple_instructors_do_not_duplicate_a_registration_or_credits_and_keep_all_meetings()
    {
        var result=CourseScheduleNormalization.Registrations([Row(),Row(instructor:"Second Instructor"),Row(days:"W",hour:10)]);
        var course=Assert.Single(result);Assert.Equal(3m,result.Sum(x=>x.Credits));Assert.Equal("First Instructor, Second Instructor",course.Instructor);Assert.Equal(2,course.Meetings.Count);
        Assert.Equal("M · 9:00 AM–10:00 AM",course.Meetings.Single(x=>x.Days=="M").DisplayText);
    }
    [Fact] public void Separate_registrations_for_the_same_course_are_not_merged() => Assert.Equal(2,CourseScheduleNormalization.Registrations([Row(),Row("2")]).Count);
    [Fact] public void Separate_terms_are_not_merged() => Assert.Equal(2,CourseScheduleNormalization.Registrations([Row(),Row() with {ExternalTermId="268"}]).Count);
    [Fact] public void Conflicting_registration_credits_fail_instead_of_selecting_an_arbitrary_total() =>
        Assert.Throws<InvalidOperationException>(()=>CourseScheduleNormalization.Registrations([Row(),Row() with {Credits=6}]));
    [Fact] public void Missing_registration_ids_do_not_merge_unrelated_rows() => Assert.Equal(2,CourseScheduleNormalization.Registrations([Row(""),Row("")]).Count);
    [Fact] public void Offerings_keep_distinct_times_and_one_credit_value()
    {
        var row=new CourseSelectionOffering(StudentInformationSystemProvider.ThesisElements,"10","20","269","Spring 2027","TEST","101","","100","Example",3,"Residential",20,5,0,"First","M",TimeSpan.FromHours(9),TimeSpan.FromHours(10));
        var course=Assert.Single(CourseScheduleNormalization.Offerings([row,row with {InstructorName="Second",MeetingDays="W",StartTime=TimeSpan.FromHours(11),EndTime=TimeSpan.FromHours(12)}]));
        Assert.Equal(2,course.Meetings.Count);Assert.Equal("First, Second",course.InstructorName);Assert.Equal(3m,course.Credits);
    }
    [Fact] public void Historical_snapshot_without_meeting_list_keeps_its_time() =>
        Assert.Equal("MWF · 1:00 PM–2:00 PM",Assert.Single(CourseMeeting.Resolve([],"MWF",TimeSpan.FromHours(13),TimeSpan.FromHours(14))).DisplayText);
    [Fact] public void A_missing_time_is_identified_rather_than_claimed_to_be_online() =>
        Assert.Equal("M · Time to be announced",new CourseMeeting("M",null,null).DisplayText);
    [Fact] public void No_schedule_remains_empty() => Assert.Empty(CourseMeeting.Resolve([],"N/A",null,null));
}
