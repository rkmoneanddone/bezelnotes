using StickyNotes.Core.Models;

namespace StickyNotes.Core.Interfaces;

public interface INoteStore
{
    Task InitializeAsync();
    Task<IReadOnlyList<Note>> GetNotesAsync();
    Task<Note?> GetNoteAsync(string id);
    Task SaveNoteAsync(Note note);
    Task DeleteNoteAsync(string id);
}
