using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Injects Alt + left click into a window this program owns and reads what that window receives.
// It never targets another program: the cursor is moved onto its own window and every injection is
// skipped when that window is not the foreground one. Alt and Ctrl are always released at the end.
static class AltClickProbe
{
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x2, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
    const ushort VK_LMENU = 0xA4, VK_LCONTROL = 0xA2;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    static readonly int Size = Marshal.SizeOf(typeof(INPUT));
    static volatile string lastDown = "none";

    static bool Held(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static uint Key(ushort vk, bool down)
    {
        INPUT[] i = new INPUT[1];
        i[0].type = INPUT_KEYBOARD;
        i[0].u.ki.wVk = vk;
        i[0].u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
        return SendInput(1, i, Size);
    }

    static uint Click()
    {
        INPUT[] i = new INPUT[2];
        i[0].type = INPUT_MOUSE;
        i[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        i[1].type = INPUT_MOUSE;
        i[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
        return SendInput(2, i, Size);
    }

    static int Main()
    {
        bool keybank = Process.GetProcessesByName("KeyBank").Length > 0;
        Console.WriteLine("KeyBank running: " + keybank);
        if (!keybank)
            return 2;

        int exit = 1;
        Form form = new Form();
        form.Text = "KeyBank AltClickProbe (test window, do not touch)";
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Size = new Size(520, 260);
        form.TopMost = true;
        Label label = new Label();
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Text = "automated test in progress";
        form.Controls.Add(label);
        form.MouseDown += delegate { lastDown = "ctrl=" + Held(0x11) + " alt=" + Held(0x12); };
        label.MouseDown += delegate { lastDown = "ctrl=" + Held(0x11) + " alt=" + Held(0x12); };

        Thread worker = new Thread(delegate()
        {
            Point saved = new Point();
            GetCursorPos(out saved);
            try
            {
                Thread.Sleep(800);
                bool front = false;
                form.Invoke((MethodInvoker)delegate { form.Activate(); SetForegroundWindow(form.Handle); });
                Thread.Sleep(300);
                form.Invoke((MethodInvoker)delegate { front = GetForegroundWindow() == form.Handle; });
                Console.WriteLine("own window is foreground: " + front);
                if (!front)
                {
                    Console.WriteLine("ABORT: nothing injected");
                    return;
                }
                Point center = new Point();
                form.Invoke((MethodInvoker)delegate { center = form.PointToScreen(new Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2)); });
                SetCursorPos(center.X, center.Y);
                Thread.Sleep(200);

                lastDown = "none";
                uint c1 = Click();
                Thread.Sleep(400);
                string plain = lastDown;
                Console.WriteLine("plain click    : " + plain + " (injected " + c1 + "/2)");

                lastDown = "none";
                uint a = Key(VK_LMENU, true);
                Thread.Sleep(250);
                uint c2 = Click();
                Thread.Sleep(500);
                string withAlt = lastDown;
                Key(VK_LMENU, false);
                Thread.Sleep(400);
                Console.WriteLine("Alt + click    : " + withAlt + " (injected alt " + a + "/1, click " + c2 + "/2)");
                Console.WriteLine("after: ctrl held=" + Held(0x11) + " alt held=" + Held(0x12));

                bool okPlain = plain == "ctrl=False alt=False";
                bool okAlt = withAlt == "ctrl=True alt=False";
                bool clean = !Held(0x11) && !Held(0x12);
                Console.WriteLine("plain click received without modifiers : " + (okPlain ? "PASS" : "FAIL"));
                Console.WriteLine("Alt + click arrives as Ctrl + click     : " + (okAlt ? "PASS" : "FAIL"));
                Console.WriteLine("no modifier left held                   : " + (clean ? "PASS" : "FAIL"));
                exit = okPlain && okAlt && clean ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
            finally
            {
                Key(VK_LMENU, false);
                Key(VK_LCONTROL, false);
                SetCursorPos(saved.X, saved.Y);
                try { form.Invoke((MethodInvoker)delegate { form.Close(); }); } catch (Exception) { }
            }
        });
        worker.IsBackground = true;

        Thread guard = new Thread(delegate()
        {
            Thread.Sleep(15000);
            Key(VK_LMENU, false);
            Key(VK_LCONTROL, false);
            Environment.Exit(3);
        });
        guard.IsBackground = true;

        form.Shown += delegate { worker.Start(); guard.Start(); };
        Application.Run(form);
        return exit;
    }
}
