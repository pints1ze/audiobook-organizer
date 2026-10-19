namespace AudiobookOrganizer.Api.Models.Local;

/// <summary>A book the user is working on; one per FileGroup, deleted with it.</summary>
public class Audiobook
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
}

/// <summary>
/// The provider candidate the user picked for an Audiobook. ProviderId is the ASIN, else the ISBN; CandidateJson is the
/// candidate as received (the raw values).
/// </summary>
public class MetadataMatch
{
    public long Id { get; set; }
    public long AudiobookId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderId { get; set; }
    public string CandidateJson { get; set; } = "";
    public string PickedAtUtc { get; set; } = "";
}

/// <summary>
/// One metadata value of an Audiobook. ValueJson is the value as JSON ("text", ["a","b"] or null). Origin is "file",
/// "provider" or "user"; Provider (e.g. "audible") is set only when Origin is "provider".
/// </summary>
public class AudiobookField
{
    public long AudiobookId { get; set; }
    public string Field { get; set; } = "";
    public string ValueJson { get; set; } = "null";
    public string Origin { get; set; } = "";
    public string? Provider { get; set; }
}
