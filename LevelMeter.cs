namespace SoundTest;

// Horizontal level bar. Level is 0..1 on a dB scale (-60 dB .. 0 dB).
sealed class LevelMeter : Control
{
    static readonly Color Back = Color.FromArgb(236, 239, 242);
    static readonly Color Low = Color.FromArgb(129, 199, 132);
    static readonly Color Mid = Color.FromArgb(240, 196, 110);
    static readonly Color High = Color.FromArgb(229, 128, 120);

    float level;

    public LevelMeter()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Height = 18;
    }

    public float Level
    {
        get => level;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(value - level) < 0.002f) return;
            level = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        int w = (int)(Width * level);
        if (w <= 0) return;
        var color = level > 0.9f ? High : level > 0.7f ? Mid : Low;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, w, Height);
    }
}
