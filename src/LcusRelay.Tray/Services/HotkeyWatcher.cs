
using System.Runtime.InteropServices;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Services;

public sealed class HotkeyWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly List<HotkeyConfig> _hotkeys;
    private readonly Func<string, Task> _onHotkey;
    private HotkeyWindow? _window;

    public HotkeyWatcher(ILogger log, List<HotkeyConfig> hotkeys, Func<string, Task> onHotkey)
    {
        _log = log;
        _hotkeys = hotkeys;
        _onHotkey = onHotkey;
    }

    public void Start()
    {
        _window = new HotkeyWindow(_log, _onHotkey);

        var id = 1;
        foreach (var hk in _hotkeys)
        {
            try
            {
                if (!hk.Actions.Any()) continue;

                var mods = ParseModifiers(hk.Modifiers);
                var key = ParseKey(hk.Key);

                _window.Register(id, mods, key, hk.Name);
                _log.LogInformation("Hotkey registered: {name} -> {mods}+{key}", hk.Name, mods, key);
                id++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Hotkey non valida: {name}", hk.Name);
            }
        }

        _log.LogInformation("HotkeyWatcher started.");
    }

    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
        _log.LogInformation("HotkeyWatcher disposed.");
    }

    private static Modifiers ParseModifiers(IEnumerable<string> mods)
    {
        Modifiers m = 0;
        foreach (var s in mods)
        {
            if (s.Equals("Alt", StringComparison.OrdinalIgnoreCase)) m |= Modifiers.Alt;
            else if (s.Equals("Control", StringComparison.OrdinalIgnoreCase) || s.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) m |= Modifiers.Control;
            else if (s.Equals("Shift", StringComparison.OrdinalIgnoreCase)) m |= Modifiers.Shift;
            else if (s.Equals("Win", StringComparison.OrdinalIgnoreCase) || s.Equals("Windows", StringComparison.OrdinalIgnoreCase)) m |= Modifiers.Win;
        }
        return m;
    }

    private static Keys ParseKey(string key)
    {
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var k))
            return k;

        // se è una lettera singola
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
                return (Keys)c;
        }

        throw new FormatException($"Key non valida: '{key}'.");
    }

    [Flags]
    private enum Modifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        private readonly ILogger _log;
        private readonly Func<string, Task> _onHotkey;
        private readonly Dictionary<int, string> _idToName = new();

        public HotkeyWindow(ILogger log, Func<string, Task> onHotkey)
        {
            _log = log;
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        public void Register(int id, Modifiers mods, Keys key, string name)
        {
            if (!RegisterHotKey(Handle, id, (uint)mods, (uint)key))
                throw new InvalidOperationException($"RegisterHotKey failed (id={id}).");

            _idToName[id] = name;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                var id = m.WParam.ToInt32();
                if (_idToName.TryGetValue(id, out var name))
                {
                    _ = SafeFire(name);
                }
            }

            base.WndProc(ref m);
        }

        private async Task SafeFire(string name)
        {
            try
            {
                await _onHotkey(name).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Errore hotkey {name}", name);
            }
        }

        public void Dispose()
        {
            foreach (var id in _idToName.Keys.ToArray())
            {
                try { UnregisterHotKey(Handle, id); } catch { }
            }
            _idToName.Clear();
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
