using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SoundTest;

sealed class MainForm : Form
{
    const string DefaultName = "Default";
    const float FloorDb = -60f;

    static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundTest", "last-file.txt");

    sealed record DeviceItem(string Name, string? Id)
    {
        public override string ToString() => Name;
    }

    readonly MMDeviceEnumerator enumerator = new();
    readonly TextBox fileBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    readonly ComboBox speakerBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly ComboBox micBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly LevelMeter speakerMeter = new() { Dock = DockStyle.Fill };
    readonly LevelMeter micMeter = new() { Dock = DockStyle.Fill };
    readonly Label speakerDb = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label micDb = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 50 };

    MMDevice? speakerDevice;
    WasapiOut? output;
    AudioFileReader? reader;
    WasapiCapture? capture;
    volatile float micPeak;  // highest mic sample since the last timer tick (written by the capture thread)
    float speakerShown, micShown;
    bool loadingDevices;

    public MainForm()
    {
        Text = "Sound Test";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(560, 225);
        MinimumSize = new Size(420, 264);
        BackColor = Color.FromArgb(250, 251, 252);

        var browse = IconButton("\U0001F4C2", "Choose MP3 file");
        var refresh = IconButton("⟳", "Reload device lists");
        var play = IconButton("▶", "Play (repeat)");
        var stop = IconButton("■", "Stop");
        browse.Click += (_, _) => ChooseFile();
        refresh.Click += (_, _) => LoadDevices();
        play.Click += (_, _) => StartPlayback();
        stop.Click += (_, _) => StopPlayback("Stopped");

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

        AddRow(grid, Caption("MP3 file"), fileBox, browse);
        AddRow(grid, Caption("Speaker"), speakerBox, refresh);
        AddRow(grid, new Label(), speakerMeter, speakerDb, 24);
        AddRow(grid, Caption("Microphone"), micBox, new Label());
        AddRow(grid, new Label(), micMeter, micDb, 24);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        buttons.Controls.Add(play);
        buttons.Controls.Add(stop);
        buttons.Controls.Add(status);
        status.AutoSize = false;
        status.Width = 380;
        status.Height = play.Height;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 3);
        grid.RowCount++;
        Controls.Add(grid);

        speakerBox.SelectedIndexChanged += (_, _) => { if (!loadingDevices) OnSpeakerChanged(); };
        micBox.SelectedIndexChanged += (_, _) => { if (!loadingDevices) StartMic(); };
        meterTimer.Tick += (_, _) => UpdateMeters();

        try { fileBox.Text = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : ""; }
        catch (IOException) { }

        LoadDevices();
        meterTimer.Start();
    }

    static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    Button IconButton(string glyph, string tip)
    {
        var b = new Button
        {
            Text = glyph,
            Font = new Font("Segoe UI Symbol", 11f),
            Size = new Size(40, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(236, 241, 247),
            ForeColor = Color.FromArgb(70, 90, 115),
            Margin = new Padding(0, 0, 6, 0),
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        tips.SetToolTip(b, tip);
        return b;
    }

    static void AddRow(TableLayoutPanel grid, Control a, Control b, Control c, int height = 36)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        int row = grid.RowCount++;
        grid.Controls.Add(a, 0, row);
        grid.Controls.Add(b, 1, row);
        grid.Controls.Add(c, 2, row);
    }

    // ---- devices -------------------------------------------------------

    void LoadDevices()
    {
        loadingDevices = true;
        Fill(speakerBox, DataFlow.Render);
        Fill(micBox, DataFlow.Capture);
        loadingDevices = false;
        OnSpeakerChanged();
        StartMic();
    }

    void Fill(ComboBox box, DataFlow flow)
    {
        string? keep = (box.SelectedItem as DeviceItem)?.Id;
        box.Items.Clear();
        box.Items.Add(new DeviceItem(DefaultName, null));
        foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            box.Items.Add(new DeviceItem(d.FriendlyName, d.ID));
        int index = 0;
        for (int i = 1; i < box.Items.Count; i++)
            if ((box.Items[i] as DeviceItem)?.Id == keep) index = i;
        box.SelectedIndex = index;
    }

    MMDevice? Resolve(ComboBox box, DataFlow flow)
    {
        try
        {
            var item = box.SelectedItem as DeviceItem;
            return item?.Id is { } id
                ? enumerator.GetDevice(id)
                : enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }
        catch (Exception ex)
        {
            ShowStatus("Device error: " + ex.Message);
            return null;
        }
    }

    void OnSpeakerChanged()
    {
        speakerDevice = Resolve(speakerBox, DataFlow.Render);
        if (output != null) StartPlayback(); // move the running playback to the new speaker
    }

    // ---- playback ------------------------------------------------------

    void ChooseFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Audio files|*.mp3;*.wav;*.m4a;*.wma|All files|*.*" };
        if (File.Exists(fileBox.Text)) dlg.InitialDirectory = Path.GetDirectoryName(fileBox.Text);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        fileBox.Text = dlg.FileName;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, dlg.FileName);
        }
        catch (IOException) { }
        if (output != null) StartPlayback();
    }

    void StartPlayback()
    {
        if (!File.Exists(fileBox.Text))
        {
            ChooseFile();
            if (!File.Exists(fileBox.Text)) return;
        }
        var resume = reader != null && reader.FileName == fileBox.Text ? reader.CurrentTime : TimeSpan.Zero;
        StopPlayback(null);
        if (speakerDevice == null) return;
        try
        {
            reader = new AudioFileReader(fileBox.Text);
            if (resume < reader.TotalTime) reader.CurrentTime = resume;
            output = new WasapiOut(speakerDevice, AudioClientShareMode.Shared, true, 100);
            output.Init(new LoopStream(reader));
            output.Play();
            ShowStatus("Playing (repeat): " + Path.GetFileName(fileBox.Text));
        }
        catch (Exception ex)
        {
            StopPlayback(null);
            ShowStatus("Play error: " + ex.Message);
        }
    }

    void StopPlayback(string? message)
    {
        output?.Stop();
        output?.Dispose();
        output = null;
        reader?.Dispose();
        reader = null;
        if (message != null) ShowStatus(message);
    }

    // ---- microphone ----------------------------------------------------

    void StartMic()
    {
        StopMic();
        var device = Resolve(micBox, DataFlow.Capture);
        if (device == null) return;
        try
        {
            var c = new WasapiCapture(device);
            var format = c.WaveFormat;
            c.DataAvailable += (_, e) =>
            {
                float p = Peak(e.Buffer, e.BytesRecorded, format);
                if (p > micPeak) micPeak = p;
            };
            c.StartRecording();
            capture = c;
        }
        catch (Exception ex)
        {
            ShowStatus("Microphone error: " + ex.Message);
        }
    }

    void StopMic()
    {
        capture?.StopRecording();
        capture?.Dispose();
        capture = null;
        micPeak = 0;
    }

    static float Peak(byte[] buf, int count, WaveFormat f)
    {
        bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f is WaveFormatExtensible x && x.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        float peak = 0;
        if (isFloat && f.BitsPerSample == 32)
            for (int i = 0; i + 3 < count; i += 4) peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buf, i)));
        else if (f.BitsPerSample == 16)
            for (int i = 0; i + 1 < count; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(buf, i) / 32768f));
        else if (f.BitsPerSample == 24)
            for (int i = 0; i + 2 < count; i += 3) peak = Math.Max(peak, Math.Abs(((buf[i] << 8 | buf[i + 1] << 16 | buf[i + 2] << 24) >> 8) / 8388608f));
        else if (f.BitsPerSample == 32)
            for (int i = 0; i + 3 < count; i += 4) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt32(buf, i) / 2147483648f));
        return peak;
    }

    // ---- meters --------------------------------------------------------

    void UpdateMeters()
    {
        float speaker = 0;
        try { speaker = speakerDevice?.AudioMeterInformation.MasterPeakValue ?? 0; }
        catch (Exception) { }
        float mic = micPeak;
        micPeak = 0;

        // Fast rise, slow fall, like a normal VU meter.
        speakerShown = Math.Max(speaker, speakerShown * 0.8f);
        micShown = Math.Max(mic, micShown * 0.8f);
        ShowLevel(speakerMeter, speakerDb, speakerShown);
        ShowLevel(micMeter, micDb, micShown);
    }

    static void ShowLevel(LevelMeter meter, Label label, float peak)
    {
        float db = peak > 0 ? 20 * MathF.Log10(peak) : float.NegativeInfinity;
        meter.Level = db <= FloorDb ? 0 : 1 - db / FloorDb;
        label.Text = db <= FloorDb ? "silent" : $"{db:0} dB";
    }

    void ShowStatus(string text)
    {
        status.Text = text;
        tips.SetToolTip(status, text);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        meterTimer.Stop();
        StopPlayback(null);
        StopMic();
        base.OnFormClosing(e);
    }
}
