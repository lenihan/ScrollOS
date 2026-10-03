using System.Text.Json.Serialization;

namespace ScrollOS.Core.Timeline;

public enum EntryKind { Session, Prompt, App, Notification }

public enum EntryStatus { Done, Running, Live, Closed }

/// <summary>
/// One item in the timeline. Large content (command output, the app's last UI tree, app state)
/// lives in files in the entry's folder and is loaded only when the entry scrolls into view.
/// </summary>
public sealed class TimelineEntry
{
    public long Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public EntryKind Kind { get; set; }
    public EntryStatus Status { get; set; }
    /// <summary>Command text, app name, or notification text.</summary>
    public string Title { get; set; } = "";
    public string? Cwd { get; set; }
    public string? App { get; set; }
    public string? AppPath { get; set; }
    /// <summary>For resumed apps, the artifact this entry continues from.</summary>
    public long? ResumedFrom { get; set; }
    public bool Error { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TimelineEntry))]
public partial class TimelineJson : JsonSerializerContext;
