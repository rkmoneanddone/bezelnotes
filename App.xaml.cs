using System.Windows;
using StickyNotes.Storage;

namespace StickyNotes;

public partial class App : Application
{
    public static SQLiteNoteStore NoteStore { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await NoteStore.InitializeAsync();
    }
}
