using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Builds the dashboard: saved paths, the latest scan, and inventory, book and file-state counts.</summary>
public class DashboardService(SettingsService settings, ScanRepository scans, GroupRepository groups)
{
    public DashboardDto Get()
    {
        var saved = settings.Get();
        var latest = scans.GetLatestRun();
        var completed = scans.GetLatestCompletedRun();
        var (files, errors) = completed is null ? (0, 0) : scans.CountFilesSeenIn(completed.Id);
        var (unresolved, excluded) = completed is null ? (0, 0) : groups.CountUnresolvedAndExcluded(completed.Id);
        var books = groups.CountGroups();
        return new DashboardDto(saved.IncomingPath, saved.LibraryPath,
            latest is null ? null : ScanRunDto.From(latest), files, errors,
            books.Total, new BooksByConfidenceDto(books.Low, books.Medium, books.High), books.Corrected,
            unresolved, excluded);
    }
}
