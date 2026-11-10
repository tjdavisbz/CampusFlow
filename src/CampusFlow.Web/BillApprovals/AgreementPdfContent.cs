using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CampusFlow.Web.BillApprovals;

internal sealed record AgreementPdfBlock(string Text, bool IsHeading = false,
    IReadOnlyList<IReadOnlyList<string>>? Rows = null);

internal static class AgreementPdfContent
{
    public static IReadOnlyList<AgreementPdfBlock> Read(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return Blocks(document.DocumentNode).ToList();
    }

    private static IEnumerable<AgreementPdfBlock> Blocks(HtmlNode node)
    {
        if (node.Name is "script" or "style" or "head" || node.NodeType == HtmlNodeType.Comment) yield break;
        if (node.Name == "table")
        {
            var rows = node.Descendants("tr").Where(row => row.Ancestors("table").FirstOrDefault() == node)
                .Select(row => (IReadOnlyList<string>)row.ChildNodes.Where(cell => cell.Name is "td" or "th")
                    .Select(Text).ToList()).Where(row => row.Count > 0).ToList();
            if (rows.Count > 0) yield return new("", Rows: rows);
            yield break;
        }
        if (Regex.IsMatch(node.Name, "^h[1-6]$"))
        {
            var text = Text(node);
            if (text.Length > 0) yield return new(text, true);
            yield break;
        }
        if (node.Name is "p" or "li" || !node.Descendants().Any(child => child.Name is "p" or "div" or "table" or "li" || Regex.IsMatch(child.Name, "^h[1-6]$")))
        {
            var text = Text(node);
            if (text.Length > 0) yield return new((node.Name == "li" ? "- " : "") + text);
            yield break;
        }
        foreach (var child in node.ChildNodes)
            foreach (var block in Blocks(child)) yield return block;
    }

    private static string Text(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Comment || node.Name is "script" or "style") return "";
        if (node.NodeType == HtmlNodeType.Text) return HtmlEntity.DeEntitize(node.InnerText);
        // Cell paragraphs and line breaks stay in the same refund row, separated by spaces.
        return Regex.Replace(string.Join(" ", node.ChildNodes.Select(Text)), @"\s+", " ").Trim();
    }
}
