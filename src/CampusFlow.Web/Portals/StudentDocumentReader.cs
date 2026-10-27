using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Web.Portals;

public sealed record StudentDocumentImageLink(int Id, string FileName);

public sealed record StudentTrackedDocument(
    int Id, string Department, DateTime? Date, string Name, string Description,
    string Status, DateTime? CompletedAt, List<StudentDocumentImageLink> Images);

/// <summary>Reads a student's document list and explicitly portal-visible images from Elements.</summary>
public sealed class StudentDocumentReader(IConfiguration configuration) : ITransientDependency
{
    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Elements read-only connection is not configured.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<StudentTrackedDocument>> GetDocumentsAsync(
        int studentUid, bool includeCompleted, CancellationToken cancellationToken)
    {
        if (studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        const string sql = """
            SELECT dt.DocTrackID, location.DisplayText, dt.DocDate, name.DisplayText,
                dt.DocDescription, status.DisplayText, dt.CompDate, di.DocImageID, di.FileName
            FROM dbo.DocTrack dt
            INNER JOIN dbo.DoctrackNamesRef name ON name.UniqueID = dt.DocNameID
            INNER JOIN dbo.Glossary status ON status.UniqueID = dt.DocStatusID
            INNER JOIN dbo.Glossary location ON location.UniqueID = dt.LocationID
            LEFT JOIN dbo.DocImage di ON di.DocTrackID = dt.DocTrackID
                AND di.DisplayInPortal = 1
            WHERE dt.StudentUID = @StudentUID AND dt.Internal = 0 AND dt.IsProspectDoc = 0
            """;
        var query = sql + (includeCompleted ? "" : " AND dt.CompDate IS NULL") +
                    " ORDER BY location.DisplayText, status.DisplayText, dt.DocDate, dt.DocTrackID, di.DocImageID";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        var documents = new List<StudentTrackedDocument>();
        StudentTrackedDocument? current = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            if (current?.Id != id)
            {
                current = new StudentTrackedDocument(id,
                    reader.IsDBNull(1) ? "Other" : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    reader.GetString(3).Trim(), reader.GetString(4).Trim(),
                    reader.IsDBNull(5) ? "Pending" : reader.GetString(5).Trim(),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6), []);
                documents.Add(current);
            }
            if (!reader.IsDBNull(7))
                current.Images.Add(new StudentDocumentImageLink(reader.GetInt32(7), reader.GetString(8).Trim()));
        }
        return documents;
    }

    public async Task<StudentDocumentImageStream?> OpenVisibleImageAsync(
        int studentUid, int imageId, CancellationToken cancellationToken)
    {
        if (studentUid <= 0 || imageId <= 0) return null;
        const string sql = """
            SELECT di.ContentType, di.FileName, di.Image
            FROM dbo.DocImage di
            INNER JOIN dbo.DocTrack dt ON dt.DocTrackID = di.DocTrackID
            WHERE di.DocImageID = @ImageID AND di.DisplayInPortal = 1
                AND di.Image IS NOT NULL AND dt.StudentUID = @StudentUID
                AND dt.Internal = 0 AND dt.IsProspectDoc = 0
            """;
        var connection = await OpenAsync(cancellationToken);
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ImageID", SqlDbType.Int).Value = imageId;
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        SqlDataReader? reader = null;
        try
        {
            reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow,
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await command.DisposeAsync();
                await connection.DisposeAsync();
                return null;
            }
            var contentType = reader.GetString(0).Trim();
            var fileName = reader.GetString(1).Trim();
            return new StudentDocumentImageStream(connection, command, reader,
                reader.GetStream(2), contentType, fileName);
        }
        catch
        {
            if (reader is not null) await reader.DisposeAsync();
            await command.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class StudentDocumentImageStream(
    SqlConnection connection, SqlCommand command, SqlDataReader reader, Stream content,
    string contentType, string fileName) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string ContentType { get; } = contentType;
    public string FileName { get; } = fileName;

    public async ValueTask DisposeAsync()
    {
        await reader.DisposeAsync();
        await command.DisposeAsync();
        await connection.DisposeAsync();
    }
}
