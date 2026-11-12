using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CampusFlow.Web.BillApprovals;

internal sealed record AgreementPdfBlock(string Text, bool IsHeading = false,
    IReadOnlyList<IReadOnlyList<string>>? Rows = null,
    IReadOnlyList<AgreementPdfRun>? Runs = null, bool IsListItem = false);

internal sealed record AgreementPdfRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false);

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
        var isHeading = Regex.IsMatch(node.Name, "^h[1-6]$");
        // Word-pasted agreements can wrap the whole document in an h3. A heading
        // containing paragraphs or lists is a container, not one giant heading.
        var containsBlocks = node.Descendants().Any(IsBlock);
        if (isHeading && !containsBlocks)
        {
            var runs = Runs(node);
            var text = string.Concat(runs.Select(x => x.Text));
            if (text.Length > 0) yield return new(text, true, Runs: runs);
            yield break;
        }
        if (node.Name is "p" or "li" || !containsBlocks)
        {
            var runs = Runs(node).ToList();
            var text = string.Concat(runs.Select(x => x.Text));
            if (text.Length == 0) yield break;
            // Word exports these section labels as bold paragraphs, not heading tags.
            var label = node.Name == "p" && text.EndsWith(":", StringComparison.Ordinal) &&
                runs.Where(x => !string.IsNullOrWhiteSpace(x.Text)).All(x => x.Bold);
            var listItem = node.Name == "li";
            if (listItem) { text = "- " + text; runs.Insert(0, new("- ")); }
            yield return new(text, label, Runs: runs, IsListItem: listItem);
            yield break;
        }
        foreach (var child in node.ChildNodes)
            foreach (var block in Blocks(child)) yield return block;
    }

    private static bool IsBlock(HtmlNode node) =>
        node.Name is "p" or "div" or "table" or "li" or "ul" or "ol" or "section" or "article" or "blockquote" ||
        Regex.IsMatch(node.Name, "^h[1-6]$");

    private static string Text(HtmlNode node) => string.Concat(Runs(node).Select(x => x.Text));

    private static IReadOnlyList<AgreementPdfRun> Runs(HtmlNode node)
    {
        var runs = new List<AgreementPdfRun>();
        foreach (var run in InlineRuns(node))
        {
            var text = Regex.Replace(run.Text, @"\s+", " ");
            if (runs.Count == 0 || runs[^1].Text.EndsWith(' ')) text = text.TrimStart();
            if (text.Length > 0) runs.Add(run with { Text = text });
        }
        if (runs.Count > 0)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
            if (runs[^1].Text.Length == 0) runs.RemoveAt(runs.Count - 1);
        }
        return runs;
    }

    private static IEnumerable<AgreementPdfRun> InlineRuns(HtmlNode node,
        bool bold = false, bool italic = false, bool underline = false)
    {
        if (node.NodeType == HtmlNodeType.Comment || node.Name is "script" or "style" or "head") yield break;
        if (node.NodeType == HtmlNodeType.Text)
        {
            yield return new(HtmlEntity.DeEntitize(node.InnerText), bold, italic, underline);
            yield break;
        }
        if (node.Name == "br") { yield return new(" "); yield break; }
        foreach (var child in node.ChildNodes)
            foreach (var run in InlineRuns(child, bold || node.Name is "b" or "strong",
                         italic || node.Name is "i" or "em", underline || node.Name == "u"))
                yield return run;
        // Keep separate cell paragraphs together on one refund row, without joining words.
        if (IsBlock(node)) yield return new(" ");
    }
}
