using System;
using System.Collections.Generic;

namespace Delta.Native;

// Retain consumption until release even if the user releases modifiers first.
internal sealed class ShortcutKeyState
{
    private readonly HashSet<uint> pressed = new();
    private readonly HashSet<uint> swallowed = new();

    internal bool KeyDown(uint key, uint modifiers, Func<uint, uint, bool> dispatch)
    {
        bool first = pressed.Add(key);
        if (swallowed.Contains(key)) return true;
        if (!first || !dispatch(key, modifiers)) return false;
        swallowed.Add(key);
        return true;
    }

    internal bool KeyUp(uint key)
    {
        pressed.Remove(key);
        return swallowed.Remove(key);
    }
}
