using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Microsoft.AspNetCore.Hosting;
using PdfSharp.Fonts;

namespace CampusFlow.Web.BillApprovals;

public sealed class MigraDocBillApprovalPdfGenerator : IBillApprovalPdfGenerator
{
    private static readonly object FontLock = new();
    private readonly string _logoPath;

    public MigraDocBillApprovalPdfGenerator(IWebHostEnvironment environment)
    {
        _logoPath = Path.Combine(environment.WebRootPath, "images", "tenants", "nelson", "logo-reverse.png");
    }

    public byte[] Generate(BillApproval approval)
    {
        EnsureFonts();
        var snapshot = DeserializeSnapshot(approval.ReviewSnapshotJson);
        var document = new Document { Info = { Title = $"Approved Bill - {approval.TermName}", Author = "CampusFlow" } };
        ConfigureStyles(document);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(.55);
        section.PageSetup.BottomMargin = Unit.FromInch(.55);
        section.PageSetup.LeftMargin = Unit.FromInch(.62);
        section.PageSetup.RightMargin = Unit.FromInch(.62);

        AddHeader(section, approval, snapshot, _logoPath);
        AddSummary(section, approval);
        AddCourses(section, snapshot);
        AddTransactions(section, snapshot);
        AddAid(section, snapshot);
        AddPaymentPlan(section, approval);
        AddAgreement(section, approval);
        AddFooter(section, approval);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static void ConfigureStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = CampusFlowFontResolver.FamilyName;
        normal.Font.Size = 8.5;
        normal.Font.Color = Color.FromRgb(35, 35, 51);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4.5);
        var heading1 = document.Styles[StyleNames.Heading1]!;
        heading1.Font.Name = CampusFlowFontResolver.FamilyName;
        heading1.Font.Size = 14;
        heading1.Font.Bold = true;
        heading1.Font.Color = Color.FromRgb(80, 45, 127);
        heading1.ParagraphFormat.SpaceBefore = Unit.FromPoint(15);
        heading1.ParagraphFormat.SpaceAfter = Unit.FromPoint(7);
        heading1.ParagraphFormat.Borders.Bottom.Width = Unit.FromPoint(.7);
        heading1.ParagraphFormat.Borders.Bottom.Color = Color.FromRgb(221, 215, 233);
        var heading2 = document.Styles[StyleNames.Heading2]!;
        heading2.Font.Name = CampusFlowFontResolver.FamilyName;
        heading2.Font.Size = 11;
        heading2.Font.Bold = true;
        heading2.Font.Color = Color.FromRgb(80, 45, 127);
        heading2.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
        heading2.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
    }

    private static void AddHeader(Section section, BillApproval approval, BillApprovalReviewSnapshot snapshot, string logoPath)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromInch(4.65));
        table.AddColumn(Unit.FromInch(2.55));
        var row = table.AddRow();
        row.Shading.Color = Color.FromRgb(67, 35, 105);
        row.TopPadding = Unit.FromPoint(13);
        row.BottomPadding = Unit.FromPoint(13);
        var brand = row.Cells[0].AddParagraph();
        brand.Format.LeftIndent = Unit.FromPoint(11);
        if (File.Exists(logoPath))
        {
            var logo = brand.AddImage(logoPath);
            logo.Width = Unit.FromInch(2.75);
            logo.LockAspectRatio = true;
        }
        else
        {
            brand.AddFormattedText("NELSON UNIVERSITY", TextFormat.Bold);
            brand.Format.Font.Size = 17;
            brand.Format.Font.Color = Colors.White;
        }
        brand.AddLineBreak();
        var subtitle = brand.AddFormattedText("APPROVED BILL & REGISTRATION SCHEDULE", TextFormat.NotBold);
        subtitle.Font.Size = 7.5;
        subtitle.Font.Color = Color.FromRgb(221, 211, 235);
        var identity = row.Cells[1].AddParagraph();
        identity.Format.Alignment = ParagraphAlignment.Right;
        identity.Format.RightIndent = Unit.FromPoint(11);
        identity.Format.Font.Color = Colors.White;
        identity.Format.Font.Size = 8;
        var student = identity.AddFormattedText(snapshot.StudentName.Length == 0 ? approval.StudentId : snapshot.StudentName, TextFormat.Bold);
        student.Font.Size = 11;
        identity.AddLineBreak();
        identity.AddText($"Student ID {approval.StudentId}");
        identity.AddLineBreak();
        identity.AddText(approval.TermName);
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void AddSummary(Section section, BillApproval approval)
    {
        section.AddParagraph("Account summary", StyleNames.Heading1);
        var table = section.AddTable();
        foreach (var width in new[] { 1.65, .18, 1.65, .18, 1.65, .18, 1.65 })
            table.AddColumn(Unit.FromInch(width));
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(8);
        row.BottomPadding = Unit.FromPoint(8);
        AddSummaryCard(row.Cells[0], "CHARGES", approval.ChargesTotal);
        AddSummaryCard(row.Cells[2], "CREDITS", -approval.CreditsTotal);
        AddSummaryCard(row.Cells[4], "ANTICIPATED AID", -approval.AnticipatedAidTotal);
        AddSummaryCard(row.Cells[6], "REMAINING", approval.RemainingBalance, true);
    }

    private static void AddCourses(Section section, BillApprovalReviewSnapshot snapshot)
    {
        section.AddParagraph($"Class schedule ({snapshot.TotalCredits:0.##} credits)", StyleNames.Heading1);
        if (snapshot.Courses.Count == 0) { AddUnavailable(section); return; }
        var table = CreateTable(section, 1.4, 3.6, .7, 1.5);
        AddHeaderRow(table, "Course", "Title", "Credits", "Instructor");
        foreach (var item in snapshot.Courses)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(item.Code);
            row.Cells[1].AddParagraph(item.Name);
            row.Cells[2].AddParagraph(item.Credits.ToString("0.##"));
            row.Cells[3].AddParagraph(item.Instructor);
        }
    }

    private static void AddTransactions(Section section, BillApprovalReviewSnapshot snapshot)
    {
        section.AddParagraph("Charges and credits", StyleNames.Heading1);
        if (snapshot.Transactions.Count == 0) { AddUnavailable(section); return; }
        var table = CreateTable(section, 1.1, 4.8, 1.3);
        AddHeaderRow(table, "Date", "Description", "Amount");
        foreach (var item in snapshot.Transactions)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(item.Date.ToString("MMM d, yyyy"));
            row.Cells[1].AddParagraph(item.Description + (item.IsPending ? " (Pending)" : ""));
            AddMoney(row.Cells[2], item.Amount);
        }
    }

    private static void AddAid(Section section, BillApprovalReviewSnapshot snapshot)
    {
        section.AddParagraph("Expected financial aid", StyleNames.Heading1);
        if (snapshot.Aid.Count == 0) { section.AddParagraph("No anticipated aid was included in this approval."); return; }
        var table = CreateTable(section, 5.5, 1.7);
        AddHeaderRow(table, "Award", "Amount");
        foreach (var item in snapshot.Aid)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph($"{item.Description} - {item.Status}");
            AddMoney(row.Cells[1], -item.Amount);
        }
    }

    private static void AddPaymentPlan(Section section, BillApproval approval)
    {
        if (approval.PaymentChoice != BillPaymentChoice.DeferredPaymentPlan) return;
        section.AddParagraph("Deferred payment plan", StyleNames.Heading1);
        var installments = JsonSerializer.Deserialize<List<StudentPaymentInstallment>>(approval.PaymentScheduleJson) ?? [];
        if (installments.Count == 0) { AddUnavailable(section); return; }
        var table = CreateTable(section, .8, 2.5, 2.2, 1.7);
        AddHeaderRow(table, "#", "Payment", "Due", "Amount");
        foreach (var item in installments)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(item.Number.ToString());
            row.Cells[1].AddParagraph(item.Number == 1 ? "Due today" : $"Payment {item.Number}");
            row.Cells[2].AddParagraph(item.DueDescription);
            AddMoney(row.Cells[3], item.Amount);
        }
        section.AddParagraph($"Payment-plan enrollment fee included: {Money(approval.PaymentPlanFee)}").Format.Font.Italic = true;
    }

    private static void AddAgreement(Section section, BillApproval approval)
    {
        section.AddPageBreak();
        section.AddParagraph("Terms and conditions", StyleNames.Heading1);
        var accepted = section.AddParagraph();
        accepted.AddFormattedText("ACCEPTED ELECTRONICALLY", TextFormat.Bold).Font.Color = Color.FromRgb(80, 45, 127);
        accepted.AddLineBreak();
        accepted.AddText($" by Student ID {approval.StudentId} on {approval.AcceptedAt:MMMM d, yyyy 'at' h:mm tt} Central Time.");
        accepted.Format.Shading.Color = Color.FromRgb(247, 244, 251);
        accepted.Format.Borders.Color = Color.FromRgb(197, 192, 224);
        accepted.Format.Borders.Width = Unit.FromPoint(.8);
        accepted.Format.LeftIndent = Unit.FromPoint(12);
        accepted.Format.RightIndent = Unit.FromPoint(12);
        accepted.Format.SpaceBefore = Unit.FromPoint(7);
        accepted.Format.SpaceAfter = Unit.FromPoint(13);
        foreach (var block in HtmlToBlocks(approval.RenderedAgreementSnapshot ?? ""))
            section.AddParagraph(block.Text, block.IsHeading ? StyleNames.Heading2 : StyleNames.Normal);
        var fingerprint = section.AddParagraph();
        fingerprint.Format.SpaceBefore = Unit.FromPoint(12);
        fingerprint.Format.Font.Size = 7;
        fingerprint.Format.Font.Color = Colors.Gray;
        fingerprint.AddText($"Agreement template version {approval.AgreementTemplateVersion} | Approval {approval.Id}");
    }

    private static void AddFooter(Section section, BillApproval approval)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Colors.Gray;
        footer.AddText($"Nelson University | Approved Bill | {approval.TermName} | Page ");
        footer.AddPageField();
    }

    private static Table CreateTable(Section section, params double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(225, 222, 231);
        table.Borders.Width = Unit.FromPoint(.4);
        table.Rows.LeftIndent = Unit.Zero;
        foreach (var width in widths) table.AddColumn(Unit.FromInch(width));
        return table;
    }

    private static void AddHeaderRow(Table table, params string[] labels)
    {
        var row = table.AddRow();
        row.Shading.Color = Color.FromRgb(241, 237, 247);
        row.TopPadding = Unit.FromPoint(3.5);
        row.BottomPadding = Unit.FromPoint(3.5);
        for (var index = 0; index < labels.Length; index++)
        {
            var paragraph = row.Cells[index].AddParagraph(labels[index]);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.Font.Size = 7.5;
            paragraph.Format.Font.Color = Color.FromRgb(80, 45, 127);
        }
    }

    private static void AddSummaryCard(Cell cell, string label, decimal amount, bool emphasized = false)
    {
        cell.Shading.Color = emphasized ? Color.FromRgb(80, 45, 127) : Color.FromRgb(247, 245, 250);
        cell.Borders.Color = emphasized ? Color.FromRgb(80, 45, 127) : Color.FromRgb(226, 221, 234);
        cell.Borders.Width = Unit.FromPoint(.6);
        var paragraph = cell.AddParagraph();
        paragraph.Format.LeftIndent = Unit.FromPoint(6);
        paragraph.Format.RightIndent = Unit.FromPoint(6);
        var labelText = paragraph.AddFormattedText(label, TextFormat.Bold);
        labelText.Font.Size = 6.5;
        labelText.Font.Color = emphasized ? Color.FromRgb(222, 211, 236) : Color.FromRgb(105, 94, 122);
        paragraph.AddLineBreak();
        var value = paragraph.AddFormattedText(Money(amount), TextFormat.Bold);
        value.Font.Size = emphasized ? 13 : 11;
        value.Font.Color = emphasized ? Colors.White : Color.FromRgb(37, 30, 48);
    }

    private static void AddMoney(Cell cell, decimal amount, bool bold = false)
    {
        var paragraph = cell.AddParagraph(Money(amount));
        paragraph.Format.Alignment = ParagraphAlignment.Right;
        paragraph.Format.Font.Bold = bold;
    }

    private static string Money(decimal amount) => amount.ToString("$#,##0.00;($#,##0.00);$0.00");
    private static void AddUnavailable(Section section) => section.AddParagraph("Detailed snapshot unavailable for this approval.").Format.Font.Italic = true;

    private static BillApprovalReviewSnapshot DeserializeSnapshot(string json)
    {
        try { return JsonSerializer.Deserialize<BillApprovalReviewSnapshot>(json) ?? EmptySnapshot(); }
        catch (JsonException) { return EmptySnapshot(); }
    }

    private static BillApprovalReviewSnapshot EmptySnapshot() => new("", 0, [], [], []);

    private static IEnumerable<(string Text, bool IsHeading)> HtmlToBlocks(string html)
    {
        var normalized = Regex.Replace(html, "</?(h[1-6]|p|div|li|br)[^>]*>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(normalized).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => (Regex.Replace(x, "\\s+", " "), x.Length < 70 && !x.EndsWith('.')));
    }

    private static void EnsureFonts()
    {
        if (GlobalFontSettings.FontResolver is not null) return;
        lock (FontLock)
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new CampusFlowFontResolver();
    }

    private sealed class CampusFlowFontResolver : IFontResolver
    {
        public const string FamilyName = "CampusFlowSans";
        private readonly byte[] _regular = LoadFont(false);
        private readonly byte[] _bold = LoadFont(true);
        public byte[]? GetFont(string faceName) => faceName == "bold" ? _bold : _regular;
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "bold" : "regular");
        private static byte[] LoadFont(bool bold)
        {
            var candidates = bold
                ? new[] { "/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" }
                : new[] { "/System/Library/Fonts/Supplemental/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf" };
            var path = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("A supported PDF font was not found.");
            return File.ReadAllBytes(path);
        }
    }
}
