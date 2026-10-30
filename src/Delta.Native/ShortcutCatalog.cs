using System;
using System.Collections.Generic;

namespace Delta.Native;
public sealed record DeltaShortcut(int Id, uint Modifiers, uint Key, string Action, string Display, bool Global = false);
public static class ShortcutCatalog
{
    public static readonly IReadOnlyList<DeltaShortcut> All = new DeltaShortcut[] {
        new(1, 1, 0x4D, "toggle-window", "Alt+M", true),
        new(2, 1, 0x42, "screenshot", "Alt+B", true),
        new(3, 1, 0x58, "ocr", "Alt+X", true),
        new(4, 1, 0x4B, "keyboard-toggle", "Alt+K", true),
        new(5, 1, 0xBC, "typing-step", "Alt+,", true),
        new(6, 1, 0x25, "opacity-down", "Alt+Left", true),
        new(7, 1, 0x27, "opacity-up", "Alt+Right", true),
        new(8, 5, 0x42, "crop", "Alt+Shift+B", true),
        new(9, 1, 0x50, "passive-overlay", "Alt+P", true),
        new(100, 2, 0x54, "new-tab", "Ctrl+T"),
        new(101, 2, 0x57, "close-tab", "Ctrl+W"),
        new(102, 2, 0x4C, "address", "Ctrl+L"),
        new(103, 2, 0x52, "reload", "Ctrl+R"),
        new(104, 2, 0x09, "next-tab", "Ctrl+Tab"),
        new(105, 6, 0x09, "previous-tab", "Ctrl+Shift+Tab"),
        new(106, 6, 0xBB, "zoom-in", "Ctrl++"),
        new(107, 2, 0x6B, "zoom-in", "Ctrl+Numpad+"),
        new(108, 2, 0xBD, "zoom-out", "Ctrl+-"),
        new(109, 2, 0x6D, "zoom-out", "Ctrl+Numpad-"),
        new(110, 2, 0xBF, "shortcuts", "Ctrl+/"),
        new(111, 0, 0x2D, "toggle-window", "Insert"),
        new(112, 0, 0x7A, "fullscreen", "F11"),
        new(113, 2, 0xBC, "typing-insert", "Ctrl+,"),
        new(114, 2, 0x30, "zoom-reset", "Ctrl+0"),
        new(115, 2, 0xBB, "zoom-in", "Ctrl+="),
    };
    public static string? ActionFor(uint key, uint modifiers)
    {
        foreach (var shortcut in All) if (shortcut.Key == key && shortcut.Modifiers == modifiers) return shortcut.Action;
        return null;
    }
}

