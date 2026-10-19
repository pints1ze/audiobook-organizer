using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>A book metadata source (Audible, Open Library). Throws <see cref="MetadataProviderException"/> on any provider failure.</summary>
public interface IMetadataProvider
{
    string Name { get; }

    Task<IReadOnlyList<MetadataCandidateDto>> SearchAsync(string keywords, CancellationToken ct);
}

/// <summary>A provider call failed (bad status, timeout, unreadable response). The message names the provider.</summary>
public class MetadataProviderException(string message, Exception? inner = null) : Exception(message, inner);
