using System.Linq;
using CampusFlow.Web.BillApprovals;
using Xunit;

namespace CampusFlow.BillApprovals;

public class AgreementPdfContentTests
{
    [Fact]
    public void Word_heading_wrapper_preserves_paragraphs_labels_and_individual_refund_deadlines()
    {
        var blocks = AgreementPdfContent.Read("""
            <h3><p class="MsoNoSpacing"><b><u><span>Federal Funds:<o:p></o:p></span></u></b></p>
            <p><span>I agree to these terms.</span></p>
            <p><b>Refund dates:</b></p><p><b>Main Session:</b></p>
            <ul><li><span>100% Tuition Refund Deadline = Friday, January 22, 2027</span></li>
            <li><span>75% Tuition Refund Deadline = Friday, January 29, 2027</span></li></ul>
            <p><b>Failure to Pay:</b></p><p>Following terms still apply.</p></h3>
            """);

        Assert.Equal(8, blocks.Count);
        Assert.Equal("Federal Funds:", blocks[0].Text);
        Assert.True(blocks[0].IsHeading);
        Assert.False(blocks[1].IsHeading);
        Assert.DoesNotContain(blocks[1].Runs!, x => x.Bold);
        Assert.True(blocks[4].IsListItem);
        Assert.Equal("- 100% Tuition Refund Deadline = Friday, January 22, 2027", blocks[4].Text);
        Assert.Equal("- 75% Tuition Refund Deadline = Friday, January 29, 2027", blocks[5].Text);
        Assert.True(blocks[6].IsHeading);
        Assert.Equal("Following terms still apply.", blocks[7].Text);
        Assert.All(blocks, block => Assert.Equal(block.Text, string.Concat(block.Runs!.Select(x => x.Text))));
    }

    [Fact]
    public void Inline_emphasis_and_punctuation_are_preserved_without_making_body_text_a_heading()
    {
        var block = Assert.Single(AgreementPdfContent.Read(
            "<p><b>Important:</b> keep <u>this</u> copy (<i>including</i> <strong>dates</strong>).</p>"));
        Assert.False(block.IsHeading);
        Assert.Equal("Important: keep this copy (including dates).", block.Text);
        Assert.Contains(block.Runs!, x => x.Text == "Important:" && x.Bold);
        Assert.Contains(block.Runs!, x => x.Text == "this" && x.Underline);
        Assert.Contains(block.Runs!, x => x.Text == "including" && x.Italic);
        Assert.Contains(block.Runs!, x => x.Text == "dates" && x.Bold);
    }

    [Fact]
    public void A_real_heading_remains_a_heading_while_nested_document_sections_remain_separate()
    {
        var blocks = AgreementPdfContent.Read("<h2>Terms</h2><h3><div><p>First paragraph.</p><p>Second paragraph.</p></div></h3>");
        Assert.Equal(new[] { "Terms", "First paragraph.", "Second paragraph." }, blocks.Select(x => x.Text));
        Assert.True(blocks[0].IsHeading);
        Assert.All(blocks.Skip(1), x => Assert.False(x.IsHeading));
    }

    [Fact]
    public void Refund_table_cells_keep_paragraphs_on_one_row_and_do_not_join_adjacent_words()
    {
        var table = Assert.Single(AgreementPdfContent.Read(
            "<table><tr><td>100%</td><td><p>Friday, January <b>22</b>, 2027</p><p>Last refund day</p></td></tr></table>"));
        Assert.Equal("100%", table.Rows![0][0]);
        Assert.Equal("Friday, January 22, 2027 Last refund day", table.Rows[0][1]);
    }
}
