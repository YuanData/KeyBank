using System;
using KeyBank;
using KeyBank.Core;

class FakeHost : IEngineHost
{
    public Settings Settings { get; private set; }
    public string ConfigPath { get { return @"C:\Users\fake\AppData\Roaming\KeyBank\keybank.json"; } }
    public string LastError { get; set; }
    public string LastKeyText { get; set; }
    public event EventHandler Changed;

    public string ActiveProfileName { get { return Settings.ResolveProfile(null); } }

    public FakeHost()
    {
        Settings = Settings.Load(DefaultConfig.Json.Replace("\"altMouse\": false", "\"altMouse\": true"));
        LastKeyText = "PageUp（實體）→ Home";
    }

    public int MutateCount;
    public string RejectMutationsWith;

    public void Mutate(Action<ConfigFile> change)
    {
        MutateCount++;
        if (RejectMutationsWith != null)
            throw new ConfigException(RejectMutationsWith);
        ConfigFile copy = ConfigFile.Parse(Settings.File.ToJson());
        change(copy);
        Settings = Settings.Compile(copy);
        RaiseChanged();
    }

    public void RaiseChanged()
    {
        EventHandler h = Changed;
        if (h != null)
            h(this, EventArgs.Empty);
    }

    public int Subscribers
    {
        get { return Changed == null ? 0 : Changed.GetInvocationList().Length; }
    }
}
