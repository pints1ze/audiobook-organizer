using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

/// <summary>
/// A fake reader that returns fixed metadata without touching the file. Once <see cref="BlockAfter"/> reads have
/// returned, the next read signals <see cref="Blocked"/> and waits for <see cref="Release"/>, so a scan can be held
/// open deterministically.
/// </summary>
internal sealed class GatedAudioFileReader(int blockAfter = int.MaxValue) : AudioFileReader(null!, null!, null!)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ManualResetEventSlim _blocked = new();
    private readonly ManualResetEventSlim _released = new();
    private int _calls;

    public int BlockAfter { get; } = blockAfter;

    public override AudioFileMetadata Read(string path)
    {
        if (Interlocked.Increment(ref _calls) > BlockAfter)
        {
            _blocked.Set();
            if (!_released.Wait(Timeout)) throw new TimeoutException("The test never released the reader.");
        }
        return new AudioFileMetadata("Fake", null, null, null, null, null, null, null, null, null, 1000, false, 0);
    }

    public void WaitUntilBlocked()
    {
        if (!_blocked.Wait(Timeout)) throw new TimeoutException("The scan never reached the gated read.");
    }

    public void Release() => _released.Set();
}
