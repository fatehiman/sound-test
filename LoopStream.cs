using NAudio.Wave;

namespace SoundTest;

// Plays the source stream forever: when it ends, jump back to the start.
sealed class LoopStream : WaveStream
{
    readonly WaveStream source;

    public LoopStream(WaveStream source) => this.source = source;

    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;

    public override long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (source.Position == 0) break; // empty file
                source.Position = 0;
                continue;
            }
            total += read;
        }
        return total;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
