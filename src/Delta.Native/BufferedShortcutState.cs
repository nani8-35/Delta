using System;
using System.Collections.Generic;
using System.Linq;

namespace Delta.Native;

internal readonly record struct ShortcutKeyEvent(uint Key, uint Scan, bool Extended, bool Up);

// Delay physical modifiers until the following key distinguishes a Delta chord
// from ordinary input. Never replay a modifier that was only used by Delta.
internal sealed class BufferedShortcutState
{
    private sealed class Modifier(ShortcutKeyEvent down, bool forwarded)
    {
        internal readonly ShortcutKeyEvent Down = down;
        internal bool Forwarded = forwarded;
        internal bool UsedByDelta;
    }
    private readonly Dictionary<uint, Modifier> modifiers = new();
    private readonly ShortcutKeyState triggers = new();

    internal static uint ModifierBit(uint key) => key switch {
        0x10 or 0xA0 or 0xA1 => 4u,
        0x11 or 0xA2 or 0xA3 => 2u,
        0x12 or 0xA4 or 0xA5 => 1u,
        0x5B or 0x5C => 8u,
        _ => 0u
    };

    internal void SeedForwardedModifier(uint key)
        => modifiers.TryAdd(key, new Modifier(new ShortcutKeyEvent(key, 0, key is 0xA3 or 0xA5 or 0x5B or 0x5C, false), true));

    internal void ObserveInjectedModifier(ShortcutKeyEvent input)
    {
        if (ModifierBit(input.Key) == 0) return;
        if (input.Up) modifiers.Remove(input.Key);
        else modifiers.TryAdd(input.Key, new Modifier(input, true));
    }

    private List<ShortcutKeyEvent> ForwardPending()
    {
        var result = new List<ShortcutKeyEvent>();
        foreach (var modifier in modifiers.Values) {
            if (modifier.Forwarded) continue;
            result.Add(modifier.Down);
            modifier.Forwarded = true;
        }
        return result;
    }

    internal bool Handle(ShortcutKeyEvent input, Func<uint, uint, bool> dispatch, Action<IReadOnlyList<ShortcutKeyEvent>> replay)
    {
        if (ModifierBit(input.Key) != 0) {
            if (!input.Up) {
                modifiers.TryAdd(input.Key, new Modifier(input, false));
                return true;
            }
            if (!modifiers.TryGetValue(input.Key, out var modifier)) return false;
            if (!modifier.Forwarded && !modifier.UsedByDelta) {
                // A standalone modifier tap or modifier-only chord is ordinary input.
                var pending = ForwardPending();
                pending.Add(input);
                replay(pending);
            } else if (modifier.Forwarded) {
                replay(new[] { input });
            }
            modifiers.Remove(input.Key);
            return true;
        }
        if (input.Up) return triggers.KeyUp(input.Key);

        uint mask = 0;
        foreach (uint key in modifiers.Keys) mask |= ModifierBit(key);
        if (triggers.KeyDown(input.Key, mask, dispatch)) {
            foreach (var modifier in modifiers.Values) modifier.UsedByDelta = true;
            return true;
        }
        // Replay the current key in the same batch as buffered modifiers so its
        // original event cannot overtake the modifier-downs in the input queue.
        var output = ForwardPending();
        if (output.Count == 0) return false;
        output.Add(input);
        replay(output);
        return true;
    }

    internal IReadOnlyList<ShortcutKeyEvent> ReleaseForwarded()
    {
        var result = modifiers.Values.Where(m => m.Forwarded)
            .Select(m => m.Down with { Up = true }).ToArray();
        modifiers.Clear();
        return result;
    }
}
