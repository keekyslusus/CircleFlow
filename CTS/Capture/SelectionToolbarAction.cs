using System.Text.Json.Serialization;

namespace CircleToSearch.Capture;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<SelectionToolbarAction>))]
public enum SelectionToolbarAction
{
    None = 0,
    Ask = 1,
    Copy = 2,
    Save = 4,
    Translate = 8,
    All = Ask | Copy | Save | Translate,
}
