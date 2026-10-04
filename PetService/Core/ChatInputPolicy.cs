namespace PetService.Core;

internal static class ChatInputPolicy
{
    // Windows virtual-key numbers keep this policy independent of Dalamud.
    public static bool PassKey(int key, bool chatAvailable, bool chatFocused, bool editingReminder)
    {
        if (!chatAvailable || editingReminder)
            return false;
        if (!chatFocused)
            return key == 0x0D; // Let the game open chat naturally on Enter.

        return key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A // numbers and letters
            or >= 0x60 and <= 0x6F      // numpad text entry
            or >= 0xA0 and <= 0xA5      // left/right Shift, Control, Alt
            or >= 0xBA and <= 0xC0      // punctuation
            or >= 0xDB and <= 0xDF      // punctuation
            or 0x08 or 0x09 or 0x0D    // Backspace, Tab, Enter
            or 0x10 or 0x11 or 0x12 or 0x14 // modifiers, Caps Lock
            or 0x15 or 0x17 or 0x18 or 0x19 or 0x1C or 0x1D or 0x1E or 0x1F // IME
            or 0x1B or 0x20           // Escape (leave chat), Space
            or >= 0x21 and <= 0x28    // Page Up/Down, Home/End, arrows
            or 0x2D or 0x2E           // Insert, Delete
            or 0x90 or 0x91           // Num Lock, Scroll Lock
            or 0xE2 or 0xE5 or 0xE7; // international keyboard, IME, Unicode packet
    }
}
