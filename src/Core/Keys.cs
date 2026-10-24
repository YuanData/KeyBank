using System;
using System.Collections.Generic;

namespace KeyBank.Core
{
    public static class Vk
    {
        public const int Tab = 0x09;
        public const int Enter = 0x0D;
        public const int Escape = 0x1B;
        public const int Space = 0x20;
        public const int PageUp = 0x21;
        public const int PageDown = 0x22;
        public const int End = 0x23;
        public const int Home = 0x24;
        public const int Left = 0x25;
        public const int Up = 0x26;
        public const int Right = 0x27;
        public const int Down = 0x28;
        public const int PrintScreen = 0x2C;
        public const int Insert = 0x2D;
        public const int Delete = 0x2E;
        public const int LWin = 0x5B;
        public const int RWin = 0x5C;
        public const int Multiply = 0x6A;
        public const int Add = 0x6B;
        public const int Subtract = 0x6D;
        public const int Divide = 0x6F;
        public const int LShift = 0xA0;
        public const int RShift = 0xA1;
        public const int LCtrl = 0xA2;
        public const int RCtrl = 0xA3;
        public const int LAlt = 0xA4;
        public const int RAlt = 0xA5;

        public static bool IsModifier(int vk)
        {
            return (vk >= LShift && vk <= RAlt) || vk == LWin || vk == RWin;
        }

        public static bool IsExtended(int vk)
        {
            return (vk >= PageUp && vk <= Down) || vk == Insert || vk == Delete
                || vk == RCtrl || vk == RAlt || vk == LWin || vk == RWin || vk == Divide;
        }
    }

    public static class KeyNames
    {
        static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, string> byVk = new Dictionary<int, string>();

        static KeyNames()
        {
            Add("Tab", Vk.Tab);
            Add("Enter", Vk.Enter);
            Add("Escape", Vk.Escape);
            Add("Space", Vk.Space);
            Add("PageUp", Vk.PageUp);
            Add("PageDown", Vk.PageDown);
            Add("End", Vk.End);
            Add("Home", Vk.Home);
            Add("Left", Vk.Left);
            Add("Up", Vk.Up);
            Add("Right", Vk.Right);
            Add("Down", Vk.Down);
            Add("PrintScreen", Vk.PrintScreen);
            Add("Insert", Vk.Insert);
            Add("Delete", Vk.Delete);
            Add("LeftWin", Vk.LWin);
            Add("Multiply", Vk.Multiply);
            Add("Add", Vk.Add);
            Add("Subtract", Vk.Subtract);
            Add("Divide", Vk.Divide);
            Add("LeftShift", Vk.LShift);
            Add("LeftCtrl", Vk.LCtrl);
            Add("LeftAlt", Vk.LAlt);
            Add("Equals", 0xBB);
            Add("Minus", 0xBD);
            for (char c = 'A'; c <= 'Z'; c++)
                Add(c.ToString(), c);
            for (char c = '0'; c <= '9'; c++)
                Add(c.ToString(), c);
            for (int i = 1; i <= 12; i++)
                Add("F" + i, 0x6F + i);
        }

        static void Add(string name, int vk)
        {
            byName[name] = vk;
            byVk[vk] = name;
        }

        public static bool TryParse(string name, out int vk)
        {
            vk = 0;
            return name != null && byName.TryGetValue(name.Trim(), out vk);
        }

        public static string NameOf(int vk)
        {
            string name;
            return byVk.TryGetValue(vk, out name) ? name : "0x" + vk.ToString("X2");
        }
    }
}
