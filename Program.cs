using System.Runtime.InteropServices;

namespace SoundTest;

static class Program
{
    const uint MB_ICONERROR = 0x10;
    const uint MB_TOPMOST = 0x40000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    static int Main()
    {
        // Single instance: a second copy shows an error and exits non-zero.
        using var mutex = new Mutex(true, @"Local\SoundTestSingleInstance", out bool created);
        if (!created)
        {
            MessageBoxW(IntPtr.Zero, "Sound Test is already running.", "Sound Test", MB_ICONERROR | MB_TOPMOST);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
