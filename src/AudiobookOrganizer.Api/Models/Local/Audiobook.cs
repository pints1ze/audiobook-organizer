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
