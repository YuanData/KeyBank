param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'

if (-not $CompileOnly) {
    if (Get-Process -Name 'PowerToys.KeyboardManagerEngine' -ErrorAction SilentlyContinue) {
        Write-Host '拒絕執行：PowerToys.KeyboardManagerEngine 還在跑，它會重新對應 Alt、PgUp、PgDn 等鍵，實測結果不可信。' -ForegroundColor Red
        Write-Host '請先在 PowerToys 關閉 Keyboard Manager（或結束 PowerToys），再重跑本腳本。' -ForegroundColor Red
        exit 2
    }
}

$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$harness = Join-Path $bin 'LiveSmoke.cs'
$exe = Join-Path $bin 'LiveSmoke.exe'

@'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyBank.Core;
using KeyBank.Win;

// Injects keys with extra info 0 (not the KeyBank marker) so the real hook processes them like typing.
static class LiveSmoke
{
    class Wheel : IMessageFilter
    {
        public int Delta;
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == 0x020A)
                Delta = (short)((long)m.WParam >> 16);
            return false;
        }
    }

    static Form form;
    static TextBox box;
    static readonly Wheel wheel = new Wheel();
    static int failures;
    static bool f12Seen;

    const string Config = "{\"enabled\":true,\"activeProfile\":\"t\",\"defaultProfile\":\"t\","
        + "\"halfPage\":{\"enabled\":true,\"fraction\":\"1/2\",\"pageTicks\":8},\"excludeApps\":[],\"devices\":[],"
        + "\"profiles\":{\"t\":{\"description\":\"smoke\",\"altChords\":[\"C\",\"V\"],\"remap\":[],"
        + "\"tapHold\":[{\"key\":\"Home\",\"holdMs\":500,\"tap\":\"Home\",\"hold\":\"F12\"}]}}}";

    static void Pump(int ms)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
        }
    }

    static bool Foreground()
    {
        return Native.GetForegroundWindow() == form.Handle;
    }

    static void Send(params Out[] outs)
    {
        if (!Foreground())
            throw new InvalidOperationException("測試視窗不在最前面，停止注入。");
        INPUT[] inputs = Injector.Build(new List<Out>(outs));
        for (int i = 0; i < inputs.Length; i++)
            inputs[i].u.ki.dwExtraInfo = IntPtr.Zero;
        System.Threading.Thread injector = new System.Threading.Thread(delegate()
        {
            Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        });
        injector.Start();
        while (!injector.Join(10))
            Application.DoEvents();
    }

    static int Inject(string[] tokens)
    {
        List<Out> outs = new List<Out>();
        for (int i = 1; i < tokens.Length; i++)
            outs.Add(Out.Key(int.Parse(tokens[i].Substring(1)), tokens[i][0] == 'd'));
        INPUT[] inputs = Injector.Build(outs);
        for (int i = 0; i < inputs.Length; i++)
            inputs[i].u.ki.dwExtraInfo = IntPtr.Zero;
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        return 0;
    }

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : "  " + detail));
        if (!ok)
            failures++;
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--inject")
            return Inject(args);

        string path = Path.Combine(Path.GetTempPath(), "KeyBankLiveSmoke.json");
        File.WriteAllText(path, Config);

        form = new Form();
        form.TopMost = true;
        form.Text = "KeyBank live smoke";
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Size = new Size(500, 300);
        box = new TextBox();
        box.Multiline = true;
        box.Dock = DockStyle.Fill;
        form.Controls.Add(box);
        box.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.F12) f12Seen = true; };
        Application.AddMessageFilter(wheel);
        form.Show();
        form.Activate();
        box.Focus();
        Pump(500);

        Engine engine = null;
        try
        {
            if (!Foreground())
                throw new InvalidOperationException("測試視窗拿不到前景，停止測試。");
            engine = new Engine(path, true);
            Pump(300);

            box.Text = "hello";
            box.SelectAll();
            Clipboard.Clear();
            Send(Out.Key(Vk.LAlt, true), Out.Key('C', true), Out.Key('C', false), Out.Key(Vk.LAlt, false));
            Pump(400);
            Check("Alt+C copies", Clipboard.GetText() == "hello", "clipboard=" + Clipboard.GetText());

            box.Text = "";
            box.Focus();
            Send(Out.Key(Vk.LAlt, true), Out.Key('V', true), Out.Key('V', false), Out.Key(Vk.LAlt, false));
            Pump(400);
            Check("Alt+V pastes", box.Text == "hello", "text=" + box.Text);

            Cursor.Position = form.PointToScreen(new Point(form.Width / 2, form.Height / 2));
            wheel.Delta = 0;
            Send(Out.Key(Vk.PageDown, true), Out.Key(Vk.PageDown, false));
            Pump(400);
            Check("PageDown scrolls half page down", wheel.Delta == -480, "delta=" + wheel.Delta);

            f12Seen = false;
            Send(Out.Key(Vk.Home, true));
            Pump(800);
            Send(Out.Key(Vk.Home, false));
            Pump(300);
            Check("Home held 500ms outputs F12", f12Seen, "F12 not seen");
        }
        catch (Exception ex)
        {
            Console.WriteLine("ABORT " + ex.Message);
            failures++;
        }
        finally
        {
            if (engine != null)
                engine.Dispose();
            form.Dispose();
            File.Delete(path);
        }
        Console.WriteLine(failures == 0 ? "ALL PASS" : "FAILURES: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
'@ | Set-Content -Path $harness -Encoding UTF8

$lib = @("$root\src\Core\*.cs", "$root\src\IEngineHost.cs", "$root\src\Win\*.cs")
& $csc /nologo /target:exe /warnaserror+ "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @lib $harness
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
if ($CompileOnly) { Write-Host "compiled: $exe (not run)"; exit 0 }

$log = Join-Path $bin 'LiveSmoke.out.txt'
Remove-Item $log -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $exe -WindowStyle Hidden -RedirectStandardOutput $log -PassThru
if (-not $p.WaitForExit(30000)) { $p.Kill(); Write-Host '逾時，已結束測試程式。' -ForegroundColor Red; exit 1 }
Get-Content $log
exit $p.ExitCode
