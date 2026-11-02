using System;
using System.Linq;
using System.Net;
using System.Text;
using CampusFlow.AdvisorPortal;

namespace CampusFlow.Web.Portals;

public static class AdvisorReviewEmailTemplate
{
    public static string Subject(AdvisorReviewEmailDto review) =>
        $"Your {review.TermName} course selection review";

    public static string Render(AdvisorReviewEmailDto review)
    {
        static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        static string Lines(string? text) => Escape(text).Replace("\r\n", "\n").Replace("\n", "<br>");

        var html = new StringBuilder();
        html.Append("<div style='background:#f5f3f8;padding:32px 16px;font-family:Arial,sans-serif;color:#241b35'>")
            .Append("<div style='max-width:680px;margin:auto;background:#fff;border:1px solid #e8e2ef;border-radius:16px;overflow:hidden'>")
            .Append("<div style='background:#3f2466;padding:24px 30px;color:#fff'>")
            .Append("<div style='font-size:12px;letter-spacing:2px;text-transform:uppercase'>Nelson University</div>")
            .Append("<h1 style='font-size:24px;margin:10px 0 0;color:#fff'>Course selection review</h1></div>")
            .Append("<div style='padding:28px 30px'><p>Hello ").Append(Escape(review.StudentName))
            .Append(",</p><p>Here is the current result of your course selection for <strong>")
            .Append(Escape(review.TermName)).Append("</strong>.</p>");

        foreach (var (status, heading, color) in new[]
                 {
                     ("Approved", "Approved courses", "#19704b"),
                     ("Rejected", "Rejected courses", "#a52b3b"),
                     ("Still pending", "Still pending review", "#865500")
                 })
        {
            var courses = review.Courses.Where(x => x.Status == status).ToArray();
            if (courses.Length == 0) continue;
            html.Append("<h2 style='font-size:18px;margin:28px 0 10px;color:").Append(color).Append("'>")
                .Append(heading).Append("</h2>");
            foreach (var course in courses)
            {
                html.Append("<div style='border:1px solid #e8e2ef;border-radius:10px;padding:14px 16px;margin:8px 0'>")
                    .Append("<strong>").Append(Escape(course.CourseCode)).Append("-")
                    .Append(Escape(course.Section)).Append(" · ").Append(Escape(course.CourseName))
                    .Append("</strong>");
                if (!string.IsNullOrWhiteSpace(course.AdvisorComment))
                    html.Append("<p style='margin:8px 0 0;color:#50445f'>Advisor comment: ")
                        .Append(Lines(course.AdvisorComment)).Append("</p>");
                html.Append("</div>");
            }
        }

        if (!string.IsNullOrWhiteSpace(review.OverallComment))
            html.Append("<h2 style='font-size:18px;margin:28px 0 8px'>Advisor notes</h2><p>")
                .Append(Lines(review.OverallComment)).Append("</p>");
        html.Append("<p style='margin-top:28px;color:#685d76;font-size:13px'>Sign in to the Nelson student portal for your current schedule.</p>")
            .Append("</div></div></div>");
        return html.ToString();
    }
}
