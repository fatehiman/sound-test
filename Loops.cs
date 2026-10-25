using NAudio.Wave;

namespace SoundTest;

// An audio file that plays forever.
interface ILoop : ISampleProvider, IDisposable
{
    TimeSpan CurrentTime { get; set; }
}

static class Loops
{
    // Short files are loaded into memory and their silent edges are cut off.
    // MP3 encoders add silence at the start and end; without the cut you hear
    // a small gap (click) every time the file repeats.
    public static ILoop Open(string path)
    {
        var reader = new AudioFileReader(path);
        if (reader.TotalTime > TimeSpan.FromMinutes(1)) return new FileLoop(reader);
        using (reader) return MemoryLoop.Load(reader);
    }
}

sealed class MemoryLoop : ILoop
{
    const float Silence = 0.001f; // -60 dBFS

    readonly float[] samples;
    long pos;

    MemoryLoop(float[] samples, WaveFormat format)
    {
        this.samples = samples;
        WaveFormat = format;
    }

    public WaveFormat WaveFormat { get; }

    public static MemoryLoop Load(ISampleProvider source)
    {
        int ch = source.WaveFormat.Channels;
        var all = new List<float>();
        var buf = new float[source.WaveFormat.SampleRate * ch];
        int n;
        while ((n = source.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, n).ToArray());

        int first = all.FindIndex(x => Math.Abs(x) > Silence);
        int last = all.FindLastIndex(x => Math.Abs(x) > Silence);
        if (first < 0) return new MemoryLoop(all.ToArray(), source.WaveFormat); // all silent: keep as is
        first -= first % ch;                 // start of that frame
        last += ch - 1 - last % ch;          // end of that frame
        return new MemoryLoop(all.GetRange(first, last - first + 1).ToArray(), source.WaveFormat);
    }

    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds((double)pos / WaveFormat.Channels / WaveFormat.SampleRate);
        set
        {
            long frame = (long)(value.TotalSeconds * WaveFormat.SampleRate);
            pos = samples.Length == 0 ? 0 : frame * WaveFormat.Channels % samples.Length;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (samples.Length == 0) return 0;
        // Plain loop on purpose: NAudio passes a byte[] disguised as float[]
        // (WaveBuffer), and Array.Copy / Span copies throw on it.
        for (int i = 0; i < count; i++)
        {
            buffer[offset + i] = samples[pos];
            if (++pos == samples.Length) pos = 0;
        }
        return count;
    }

    public void Dispose() { }
}

// Long files: read from disk and jump back to the start at the end.
sealed class FileLoop : ILoop
{
    readonly AudioFileReader reader;

    public FileLoop(AudioFileReader reader) => this.reader = reader;

    public WaveFormat WaveFormat => reader.WaveFormat;

    public TimeSpan CurrentTime
    {
        get => reader.CurrentTime;
        set => reader.CurrentTime = value < reader.TotalTime ? value : TimeSpan.Zero;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = reader.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (reader.Position == 0) break; // empty file
                reader.Position = 0;
                continue;
            }
            total += read;
        }
        return total;
    }

    public void Dispose() => reader.Dispose();
}
