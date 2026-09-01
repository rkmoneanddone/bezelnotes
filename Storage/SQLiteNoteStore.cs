using System.IO;
using Microsoft.Data.Sqlite;
using StickyNotes.Core.Interfaces;
using StickyNotes.Core.Models;

namespace StickyNotes.Storage;

public sealed class SQLiteNoteStore : INoteStore
{
    private readonly string _connectionString;

    public SQLiteNoteStore()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNotes"
        );

        Directory.CreateDirectory(appDataPath);

        var databasePath = Path.Combine(appDataPath, "stickynotes.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS notes
        (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            content TEXT NOT NULL,
            color TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0
        );
        """;

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Note>> GetNotesAsync()
    {
        var notes = new List<Note>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        SELECT id, title, content, color, sort_order, created_at, updated_at, is_deleted
        FROM notes
        WHERE is_deleted = 0
        ORDER BY sort_order, created_at;
        """;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            notes.Add(ReadNote(reader));
        }

        return notes;
    }

    public async Task<Note?> GetNoteAsync(string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        SELECT id, title, content, color, sort_order, created_at, updated_at, is_deleted
        FROM notes
        WHERE id = $id
        LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadNote(reader) : null;
    }

    public async Task SaveNoteAsync(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        INSERT INTO notes
        (
            id, title, content, color, sort_order, created_at, updated_at, is_deleted
        )
        VALUES
        (
            $id, $title, $content, $color, $sortOrder, $createdAt, $updatedAt, $isDeleted
        )
        ON CONFLICT(id) DO UPDATE SET
            title = excluded.title,
            content = excluded.content,
            color = excluded.color,
            sort_order = excluded.sort_order,
            updated_at = excluded.updated_at,
            is_deleted = excluded.is_deleted;
        """;

        command.Parameters.AddWithValue("$id", note.Id);
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$color", note.Color);
        command.Parameters.AddWithValue("$sortOrder", note.SortOrder);
        command.Parameters.AddWithValue("$createdAt", note.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", note.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isDeleted", note.IsDeleted ? 1 : 0);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteNoteAsync(string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        UPDATE notes
        SET is_deleted = 1,
            updated_at = $updatedAt
        WHERE id = $id;
        """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    private static Note ReadNote(SqliteDataReader reader)
    {
        return new Note
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Color = reader.GetString(3),
            SortOrder = reader.GetInt32(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UpdatedAt = DateTime.Parse(reader.GetString(6)),
            IsDeleted = reader.GetInt32(7) != 0
        };
    }
}

