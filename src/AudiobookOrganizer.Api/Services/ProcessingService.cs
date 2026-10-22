using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Services;

public enum ApproveOutcome { Approved, NotFound, Conflict, Invalid }

/// <summary>The approval's outcome: the job when Approved, the reason when Invalid.</summary>
public record ApproveResult(ApproveOutcome Outcome, ProcessingJobDto? Job = null, string? Message = null);

/// <summary>
/// Approves books into the processing queue and runs queued jobs. Approval only records the job; <see cref="Run"/>
/// writes a tagged copy of each file, reads it back, and deletes the sources once the whole job verified (unless
/// "keep source files" is on).
/// </summary>
public class ProcessingService(
    NamingPreviewService previews,
    ProcessingRepository jobs,
    AudiobookService audiobooks,
    AudiobookRepository audiobookData,
    GroupRepository groups,
    TagWriter tagWriter,
    AudioFileReader reader,
    SettingsService settings,
    ILogger<ProcessingService> logger)
{
    public const string Interrupted = "Interrupted by an application restart.";
    public const string BookGone = "The book is no longer in the library.";
    public const string SourceChanged = "The source changed after it was copied, so it was kept.";
    public const string SourceNotDeleted = "The copy is in place, but the source couldn't be deleted: ";

    /// <summary>What a source was just before its copy was written: its tags, size and last write time.</summary>
    private record SourceFacts(AudioFileMetadata Read, long Size, DateTime LastWriteUtc);

    // Keeps two approvals of the same book from both passing the active-job check.
    private readonly Lock _lock = new();

    /// <summary>
    /// Queues a job for the group when its preview is clean, snapshotting each file's source and destination in book
    /// order. Refused (Invalid) for no files, an error, missing values, a duplicate target or an existing destination,
    /// in that order; Conflict when the book already has a Queued or Running job.
    /// </summary>
    public ApproveResult Approve(long groupId)
    {
        lock (_lock)
        {
            if (previews.Preview(groupId) is not { } preview) return new ApproveResult(ApproveOutcome.NotFound);
            if (jobs.HasActiveJob(groupId)) return new ApproveResult(ApproveOutcome.Conflict);
            if (Refusal(preview.Files) is { } message) return new ApproveResult(ApproveOutcome.Invalid, Message: message);

            var job = new ProcessingJob
            {
                GroupId = groupId,
                Title = preview.Title,
                Status = ProcessingJob.Queued,
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
            };
            var files = preview.Files.Select(f => new ProcessingJobFile
            {
                AudioFileId = f.FileId,
                SourcePath = f.SourcePath,
                DestinationPath = f.DestinationPath!,
                Status = ProcessingJobFile.Pending,
            }).ToList();
            var id = jobs.CreateJob(job, files);
            var (saved, savedFiles) = jobs.GetJob(id)!.Value;
            return new ApproveResult(ApproveOutcome.Approved, ProcessingJobDto.From(saved, savedFiles));
        }
    }

    /// <summary>Why the book can't be approved, or null when every file has a clean destination.</summary>
    private static string? Refusal(IReadOnlyList<FilePreviewDto> files)
    {
        if (files.Count == 0) return "The book has no files.";
        if (files.FirstOrDefault(f => f.Error is not null) is { } error) return error.Error;
        var missing = files.SelectMany(f => f.Missing).Distinct().ToList();
        if (missing.Count > 0) return $"Fill in the missing values: {string.Join(", ", missing)}.";
        if (files.FirstOrDefault(f => f.Duplicate) is { } duplicate)
            return $"Two files would go to the same place: {duplicate.RelativePath}.";
        if (files.FirstOrDefault(f => f.Exists) is { } existing) return $"A file is already at {existing.RelativePath}.";
        return null;
    }

    /// <summary>
    /// Runs a Queued job: writes a tagged copy of each file, in position order, to its snapshotted destination through
    /// a <c>.partial</c> file in the destination folder, never overwriting anything, and reads each copy back. The tags
    /// come from the book's current metadata; a book that's gone fails the job before any file is touched. The first
    /// problem fails that file and the job, and removes the copies this job made. Once every file verified, the
    /// sources are deleted unless "keep source files" is on. Does nothing when the job isn't Queued.
    /// </summary>
    public void Run(long jobId, CancellationToken cancellationToken)
    {
        if (jobs.GetJob(jobId) is not { } found || found.Job.Status != ProcessingJob.Queued) return;
        var (job, files) = found;

        job.Status = ProcessingJob.Running;
        job.StartedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);

        if (Tags(job, files) is not { } tags)
        {
            job.Status = ProcessingJob.Failed;
            job.Error = BookGone;
            job.FinishedAtUtc = DateTime.UtcNow.ToString("O");
            jobs.UpdateJob(job);
            return;
        }

        string? partial = null;
        string? error = null;
        var sources = new List<SourceFacts>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (cancellationToken.IsCancellationRequested)
            {
                error = Interrupted;
                break;
            }
            try
            {
                error = Copy(file, tags[i], sources, ref partial);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            if (error is not null)
            {
                file.Status = ProcessingJobFile.Failed;
                file.Error = error;
                jobs.UpdateFile(file);
                break;
            }
            file.Status = ProcessingJobFile.Copied;
            jobs.UpdateFile(file);
        }

        if (error is not null) RemoveCopies(job, files, partial);
        else if (!settings.Get().KeepSourceFiles) DeleteSources(job, files, sources);
        job.Status = error is null ? ProcessingJob.Succeeded : ProcessingJob.Failed;
        job.Error = error;
        job.FinishedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);
    }

    /// <summary>
    /// Each file's tags from the book's current metadata, in the job's file order; null when the book is gone. Authors,
    /// narrators and genres are written by their canonical names where linked; the track is the file's position of the
    /// count.
    /// </summary>
    private List<BookTags>? Tags(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files)
    {
        if (job.GroupId is not { } groupId || audiobooks.GetMetadata(groupId) is not { } metadata) return null;
        var people = audiobooks.GetPeople(metadata);
        var artists = people.Authors.Select(a => a.Entry?.Name ?? a.Name).ToList();
        var narrators = people.Narrators.Select(n => n.Entry?.Name ?? n.Name).ToList();
        var fields = metadata.Fields;
        var genres = audiobooks.GetGenres(groupId)?.Genres.Select(g => g.Entry?.Name ?? g.Name).ToList() ?? [];
        var year = BookTokens.For(fields, null, null, 1, null)["year"] is { } text
            ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture)
            : (int?)null;
        var cover = audiobookData.GetCoverImage(groupId) is { } chosen ? new TagCover(chosen.Image, chosen.MimeType) : null;
        var discs = groups.GetGroupFiles(groupId).ToDictionary(f => f.FileId, f => f.Disc);

        return files.Select((file, index) => new BookTags(
            fields.Title.Value, artists, genres, year, index + 1,
            discs.GetValueOrDefault(file.AudioFileId), cover)
        {
            Narrators = narrators,
            Subtitle = fields.Subtitle.Value,
            Series = fields.Series.Value,
            SeriesPart = fields.SeriesPart.Value,
            Publisher = fields.Publisher.Value,
            ReleaseDate = fields.ReleaseDate.Value,
            Language = fields.Language.Value,
            Format = fields.Format.Value,
            Isbn = fields.Isbn.Value,
            Asin = fields.Asin.Value,
        }).ToList();
    }

    /// <summary>
    /// Writes one file's tagged copy into place and reads it back, or returns why it can't; a copy that fails the
    /// read-back is deleted. The source's facts are added to <paramref name="sources"/> before the write.
    /// <paramref name="partial"/> is set to the temporary file once this job has created it, and cleared once it's
    /// moved into place.
    /// </summary>
    private string? Copy(ProcessingJobFile file, BookTags tags, List<SourceFacts> sources, ref string? partial)
    {
        var destination = file.DestinationPath;
        if (!File.Exists(file.SourcePath)) return $"The source file is gone: {file.SourcePath}.";
        if (File.Exists(destination) || Directory.Exists(destination)) return $"A file is already at {destination}.";
        var temp = destination + ".partial";
        if (File.Exists(temp) || Directory.Exists(temp)) return $"A file is already at {temp}.";

        var info = new FileInfo(file.SourcePath);
        var source = new SourceFacts(reader.Read(file.SourcePath), info.Length, info.LastWriteTimeUtc);
        sources.Add(source);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // The writer creates the temp file with CreateNew and throws TagTargetExistsException, having created
        // nothing, if it appeared since the check; so someone else's file is never replaced. Past that, the temp file
        // is this job's, and is removed if the write fails part-way.
        try
        {
            tagWriter.Write(file.SourcePath, temp, tags);
        }
        catch (TagTargetExistsException ex)
        {
            return ex.Message;
        }
        catch
        {
            partial = temp;
            throw;
        }
        partial = temp;
        // Same folder, so this is a rename; overwrite: false refuses a destination that appeared meanwhile.
        File.Move(temp, destination, overwrite: false);
        partial = null;

        // The copy has to be read at its audio name, so it's checked in place. It's this job's (the move refuses to
        // overwrite), so a copy that fails is deleted here; the failure path removes the earlier ones.
        if (Verify(destination, tags, source.Read) is not { } mismatch) return null;
        try
        {
            File.Delete(destination);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't remove {Path} after it failed its read-back", destination);
        }
        return mismatch;
    }

    /// <summary>Why the copy at <paramref name="destination"/> doesn't match its tags and source, or null when it does.</summary>
    private string? Verify(string destination, BookTags tags, AudioFileMetadata source)
    {
        var name = Path.GetFileName(destination);
        AudioFileMetadata copy;
        try
        {
            copy = reader.Read(destination);
        }
        catch (Exception ex)
        {
            return $"The copy of {name} couldn't be read back: {ex.Message}";
        }

        if (!string.IsNullOrWhiteSpace(tags.Title) && copy.Title != tags.Title.Trim())
            return $"The copy of {name} has the title \"{copy.Title}\"; it should be \"{tags.Title.Trim()}\".";
        if (tags.Artists.Count > 0 && !string.IsNullOrWhiteSpace(tags.Artists[0])
            && copy.Artist?.Contains(tags.Artists[0].Trim(), StringComparison.Ordinal) != true)
            return $"The copy of {name} has the artist \"{copy.Artist}\"; it should include \"{tags.Artists[0].Trim()}\".";
        if (copy.Track != tags.Track)
            return $"The copy of {name} has track {copy.Track?.ToString() ?? "none"}; it should be {tags.Track}.";
        if ((tags.Cover is not null || source.HasCover) && !copy.HasCover) return $"The copy of {name} has no cover.";
        // M4B copies carry no chapters (decision 2026-10-21), so only MP3 copies are held to the source's count.
        if (!string.Equals(Path.GetExtension(destination), ".m4b", StringComparison.OrdinalIgnoreCase)
            && copy.ChapterCount != source.ChapterCount)
            return $"The copy of {name} has {Chapters(copy.ChapterCount)}; the source has {source.ChapterCount}.";
        if (Math.Abs(copy.DurationMs - source.DurationMs) > 1000)
            return $"The copy of {name} runs {copy.DurationMs / 1000.0:0.0} s; the source runs {source.DurationMs / 1000.0:0.0} s.";
        return null;
    }

    private static string Chapters(int count) => count == 1 ? "1 chapter" : $"{count} chapters";

    /// <summary>
    /// Deletes each verified file's source and marks it SourceDeleted. A source whose size or last write time changed
    /// since its copy was made, or that can't be deleted, is kept: the file stays Copied with the reason as its error.
    /// </summary>
    private void DeleteSources(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files, IReadOnlyList<SourceFacts> sources)
    {
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            try
            {
                var info = new FileInfo(file.SourcePath);
                if (info.Length != sources[i].Size || info.LastWriteTimeUtc != sources[i].LastWriteUtc)
                {
                    logger.LogWarning("Kept {Path} for job {JobId}: it changed after it was copied", file.SourcePath, job.Id);
                    file.Error = SourceChanged;
                }
                else
                {
                    File.Delete(file.SourcePath);
                    file.Status = ProcessingJobFile.SourceDeleted;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't delete {Path} for job {JobId}", file.SourcePath, job.Id);
                file.Error = SourceNotDeleted + ex.Message;
            }
            jobs.UpdateFile(file);
        }
    }

    /// <summary>
    /// Deletes the destinations this job copied (resetting those files to Pending) and its own temporary file.
    /// shortcut: folders the job created stay, possibly empty — revisit in phase 9 cleanup.
    /// </summary>
    private void RemoveCopies(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files, string? partial)
    {
        foreach (var path in files.Where(f => f.Status == ProcessingJobFile.Copied).Select(f => f.DestinationPath).Append(partial))
        {
            if (path is null) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't remove {Path} after job {JobId} failed", path, job.Id);
            }
        }
        foreach (var file in files.Where(f => f.Status == ProcessingJobFile.Copied))
        {
            file.Status = ProcessingJobFile.Pending;
            jobs.UpdateFile(file);
        }
    }

    /// <summary>Marks a job Failed with the message, whatever state it was left in (the worker's last resort).</summary>
    public void Fail(long jobId, string message)
    {
        if (jobs.GetJob(jobId) is not { } found) return;
        var job = found.Job;
        job.Status = ProcessingJob.Failed;
        job.Error = message;
        job.FinishedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);
    }

    public IReadOnlyList<ProcessingJobDto> GetJobs() =>
        jobs.GetJobs().Select(j => ProcessingJobDto.From(j.Job, j.Files)).ToList();

    public ProcessingJobDto? GetJob(long id) => jobs.GetJob(id) is { } found ? ProcessingJobDto.From(found.Job, found.Files) : null;
}
