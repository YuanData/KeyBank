using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyBank.Ui
{
    public interface IAutoStart
    {
        bool Enabled { get; set; }
    }

    public sealed class RegistryAutoStart : IAutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "KeyBank";

        public bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(RunName) != null;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value)
                        key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue(RunName, false);
                }
            }
        }
    }
}
