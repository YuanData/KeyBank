using System;
using KeyBank.Core;

namespace KeyBank
{
    public interface IEngineHost
    {
        Settings Settings { get; }
        string ConfigPath { get; }
        string LastError { get; }
        string ActiveProfileName { get; }
        string LastKeyText { get; }
        event EventHandler Changed;

        void Mutate(Action<ConfigFile> change);
    }
}
