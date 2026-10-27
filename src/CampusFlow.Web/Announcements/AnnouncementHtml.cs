using System;
using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace CampusFlow.Web.Announcements;

public static class AnnouncementHtml
{
    public static string Sanitize(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var result = new StringBuilder();
        foreach (var child in document.DocumentNode.ChildNodes) Append(child, result);
        return result.ToString().Trim();
    }

    private static void Append(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            output.Append(WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText)));
            return;
        }
        if (node.NodeType != HtmlNodeType.Element) return;

        var name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "iframe" or "object" or "svg" or "form" or "input" or "button") return;
        if (name == "br") { output.Append("<br>"); return; }
        var tag = name switch
        {
            "div" => "p",
            "p" or "h3" or "h4" or "strong" or "b" or "em" or "i" or "ul" or "ol" or "li" => name,
            "a" => "a",
            _ => null
        };
        if (tag == "a")
        {
            var href = HtmlEntity.DeEntitize(node.GetAttributeValue("href", string.Empty)).Trim();
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                tag = null;
            else
                output.Append("<a href=\"").Append(WebUtility.HtmlEncode(uri.AbsoluteUri))
                    .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">");
        }
        else if (tag is not null) output.Append('<').Append(tag).Append('>');
        foreach (var child in node.ChildNodes) Append(child, output);
        if (tag is not null) output.Append("</").Append(tag).Append('>');
    }
}
