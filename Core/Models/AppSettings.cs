namespace StickyNotes.Core.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public string Edge { get; set; } = "Right";
    public string DefaultColor { get; set; } = "Yellow";
}
