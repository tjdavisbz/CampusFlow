using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.Web.Tests.Portals;

public class StudentDocumentsTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public void Safe_document_types_can_open_inline(string contentType)
    {
        Assert.True(DocumentsModel.IsSafeInlineType(contentType));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/javascript")]
    [InlineData("application/octet-stream")]
    public void Active_or_unknown_types_must_download(string contentType)
    {
        Assert.False(DocumentsModel.IsSafeInlineType(contentType));
    }
}
