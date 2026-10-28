using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Types into a text box this program owns, using held-key repeats, and counts what arrives.
// Nothing is injected unless that text box is the foreground window's focus. Shift is always released at the end.
static class SeparatorProbe
{
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x2;
    const ushort VK_LSHIFT = 0xA0, VK_MINUS = 0xBD, VK_EQUALS = 0xBB, VK_8 = 0x38;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr h);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr h, IntPtr ctx);
    [DllImport("imm32.dll")] static extern bool ImmSetOpenStatus(IntPtr ctx, bool open);
    [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr ctx, int conversion, int sentence);

    const int IME_CMODE_NATIVE = 0x1, IME_CMODE_FULLSHAPE = 0x8;

    static readonly int Size = Marshal.SizeOf(typeof(INPUT));

    // Puts the probe's own text box into an open, full-width IME state so the hook sees what a Chinese-mode user has.
    static bool SetFullWidth(TextBox box, bool on)
    {
        bool ok = false;
        box.Invoke((MethodInvoker)delegate
        {
            IntPtr ctx = ImmGetContext(box.Handle);
            if (ctx == IntPtr.Zero)
                return;
            ok = ImmSetOpenStatus(ctx, on) && ImmSetConversionStatus(ctx, on ? IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE : 0, 0);
            ImmReleaseContext(box.Handle, ctx);
        });
        return ok;
    }

    static void Key(ushort vk, bool down)
    {
        INPUT[] i = new INPUT[1];
        i[0].type = INPUT_KEYBOARD;
        i[0].u.ki.wVk = vk;
        i[0].u.ki.wScan = 0;
        i[0].u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
        SendInput(1, i, Size);
    }

    // Down events without an up in between are what the keyboard's auto-repeat produces.
    static void Hold(ushort vk, int downs)
    {
        for (int n = 0; n < downs; n++)
        {
            Key(vk, true);
            Thread.Sleep(30);
        }
        Key(vk, false);
        Thread.Sleep(900);
    }

    static string Text(TextBox box)
    {
        string t = "";
        box.Invoke((MethodInvoker)delegate { t = box.Text; box.Clear(); });
        return t;
    }

    static string Summary(string t)
    {
        if (t.Length == 0)
            return "empty";
        char c = t[0];
        foreach (char x in t)
            if (x != c)
                return "mixed(" + t.Length + ")";
        return t.Length + " x '" + c + "'";
    }

    static int Main()
    {
        if (Process.GetProcessesByName("KeyBank").Length == 0)
        {
            Console.WriteLine("KeyBank is not running");
            return 2;
        }

        int exit = 1;
        Form form = new Form();
        form.Text = "KeyBank SeparatorProbe (test window, do not touch)";
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Size = new Size(900, 200);
        form.TopMost = true;
        TextBox box = new TextBox();
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.MaxLength = 0;
        box.ImeMode = ImeMode.Disable;
        form.Controls.Add(box);

        Thread worker = new Thread(delegate()
        {
            try
            {
                Thread.Sleep(800);
                bool ready = false;
                form.Invoke((MethodInvoker)delegate { form.Activate(); SetForegroundWindow(form.Handle); box.Focus(); });
                Thread.Sleep(300);
                form.Invoke((MethodInvoker)delegate { ready = GetForegroundWindow() == form.Handle && box.Focused; });
                Console.WriteLine("own text box has focus: " + ready);
                if (!ready)
                {
                    Console.WriteLine("ABORT: nothing injected");
                    return;
                }
                Text(box);

                Hold(VK_MINUS, 12);
                string dash = Summary(Text(box));
                Hold(VK_EQUALS, 12);
                string equals = Summary(Text(box));
                Key(VK_LSHIFT, true);
                Thread.Sleep(100);
                Hold(VK_8, 12);
                Key(VK_LSHIFT, false);
                Thread.Sleep(200);
                string star = Summary(Text(box));
                for (int i = 0; i < 10; i++)
                {
                    Key(VK_MINUS, true);
                    Key(VK_MINUS, false);
                    Thread.Sleep(60);
                }
                Thread.Sleep(300);
                string taps = Summary(Text(box));
                Hold(VK_MINUS, 4);
                string shortHold = Summary(Text(box));

                Console.WriteLine("hold -   (12 repeats): " + dash);
                Console.WriteLine("hold =   (12 repeats): " + equals);
                Console.WriteLine("hold Shift+8 (12)    : " + star);
                Console.WriteLine("tap - ten times      : " + taps);
                Console.WriteLine("hold - (4 repeats)   : " + shortHold);
                bool ok = dash == "88 x '-'" && equals == "88 x '='" && star == "88 x '*'" && taps == "10 x '-'" && shortHold == "4 x '-'";

                string wide = "skipped";
                if (Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs()[1] == "wide")
                {
                    form.Invoke((MethodInvoker)delegate { box.ImeMode = ImeMode.NoControl; box.Focus(); });
                    Thread.Sleep(300);
                    bool set = SetFullWidth(box, true);
                    Thread.Sleep(300);
                    Hold(VK_MINUS, 12);
                    wide = "imm set=" + set + ", typed " + Text(box).Length + " chars";
                    SetFullWidth(box, false);
                }
                Console.WriteLine("full-width pass      : " + wide);
                Console.WriteLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                exit = ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
            finally
            {
                Key(VK_LSHIFT, false);
                Key(VK_MINUS, false);
                Key(VK_EQUALS, false);
                Key(VK_8, false);
                try { form.Invoke((MethodInvoker)delegate { form.Close(); }); } catch (Exception) { }
            }
        });
        worker.IsBackground = true;

        Thread guard = new Thread(delegate()
        {
            Thread.Sleep(30000);
            Key(VK_LSHIFT, false);
            Key(VK_MINUS, false);
            Key(VK_EQUALS, false);
            Key(VK_8, false);
            Environment.Exit(3);
        });
        guard.IsBackground = true;

        form.Shown += delegate { worker.Start(); guard.Start(); };
        Application.Run(form);
        return exit;
    }
}
