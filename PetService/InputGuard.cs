using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using PetService.Core;

namespace PetService;

/// <summary>
/// Captures input only while our reminder is being rendered. Uses Dalamud services,
/// never an OS keyboard hook. Global controller navigation is restored on exit.
/// </summary>
internal sealed unsafe class InputGuard(IKeyState keys, IGamepadState gamepad,
    NativeChat nativeChat, ChatCommandGuard commands, MovementGuard movement, IPluginLog log)
{
    // Some API 15 builds expose this service property; earlier builds use the ImGui flag.
    private readonly PropertyInfo? gamepadNavigation = typeof(IGamepadState)
        .GetProperty("EnableGamepadNav");
    private VirtualKey[]? validKeys;
    private bool active;
    private bool previousGamepadNavigation;
    private ImGuiConfigFlags previousNavigationFlags;
    private bool previousTextInput;
    private long lastRendered;
    private bool disabledForSession;
    private bool editingReminder;
    private long lastAutorunStopAttempt;
    public bool HasFailed => disabledForSession;
    public bool AutorunStopFailed { get; private set; }
    public bool IsCapturing => active && !disabledForSession
        && Environment.TickCount64 - lastRendered <= 1000;

    public void CaptureRenderedFrame(bool editingReminder, bool mouseOverChat)
    {
        if (disabledForSession)
        {
            Release();
            return;
        }
        try
        {
            this.editingReminder = editingReminder;
            CaptureFrame(mouseOverChat);
        }
        catch (Exception exception)
        {
            disabledForSession = true;
            Release();
            log.Error(exception, "Could not capture reminder input; capture stopped for this session.");
        }
    }

    private void CaptureFrame(bool mouseOverChat)
    {
        var io = ImGui.GetIO();
        if (!active)
        {
            previousNavigationFlags = io.ConfigFlags
                & (ImGuiConfigFlags.NavEnableGamepad | ImGuiConfigFlags.NavEnableKeyboard);
            previousTextInput = io.WantTextInput;
            previousGamepadNavigation = gamepadNavigation?.GetValue(gamepad) as bool? ?? false;
            lastAutorunStopAttempt = 0;
            AutorunStopFailed = false;
            active = true;
        }

        if (gamepadNavigation?.CanWrite == true
            && !(gamepadNavigation.GetValue(gamepad) as bool? ?? false))
            gamepadNavigation.SetValue(gamepad, true);

        io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
        // Enter belongs to native chat, rather than activating a reminder button.
        io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
        var chatAvailable = commands.Available && movement.Available && nativeChat.Read().Available;
        io.WantCaptureMouse = !chatAvailable || !mouseOverChat;
        io.WantCaptureKeyboard = editingReminder || !chatAvailable;
        // Dalamud's Win32 backend gates keyboard messages on WantTextInput.
        io.WantTextInput = editingReminder || !chatAvailable;
        lastRendered = Environment.TickCount64;
        SuppressGameplay();
    }

    public void SuppressKeyboardOnFrameworkUpdate()
    {
        if (!active || disabledForSession)
            return;
        if (Environment.TickCount64 - lastRendered > 1000)
        {
            Release();
            return;
        }
        try
        {
            SuppressGameplay();
        }
        catch (Exception exception)
        {
            disabledForSession = true;
            Release();
            log.Error(exception, "Reminder keyboard suppression failed; suppression stopped.");
        }
    }

    public void Release()
    {
        if (!active)
            return;
        active = false;
        editingReminder = false;
        try
        {
            if (gamepadNavigation?.CanWrite == true)
                gamepadNavigation.SetValue(gamepad, previousGamepadNavigation);
            if (!ImGuiHelpers.IsImGuiInitialized)
                return;
            var io = ImGui.GetIO();
            var navigationMask = ImGuiConfigFlags.NavEnableGamepad | ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags = (io.ConfigFlags & ~navigationMask) | previousNavigationFlags;
            io.WantTextInput = previousTextInput;
            // ImGui recalculates its capture flags when it starts the next frame.
            io.WantCaptureMouse = false;
            io.WantCaptureKeyboard = false;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Could not restore reminder input capture state.");
        }
    }

    private void SuppressGameplay()
    {
        StopExistingAutorun();
        var state = commands.Available && movement.Available ? nativeChat.Read() : default;
        validKeys ??= keys.GetValidVirtualKeys().ToArray();
        foreach (var key in validKeys)
            if (!ChatInputPolicy.PassKey((int)key, state.Available, state.Focused, editingReminder))
                keys[key] = false;

        // Leave the UI's raw cursor intact for the chat field. Filter the separate
        // gameplay copy, including extra mouse buttons, camera drag, and gamepad.
        var uiInput = UIInputData.Instance();
        if (uiInput == null)
            return;
        uiInput->FilterUICursorInputs(MouseButtonFlags.LBUTTON | MouseButtonFlags.MBUTTON
            | MouseButtonFlags.RBUTTON | MouseButtonFlags.XBUTTON1 | MouseButtonFlags.XBUTTON2);
        uiInput->FilterDragInputs();
        uiInput->FilterGamepadInputs();
    }

    private void StopExistingAutorun()
    {
        var now = Environment.TickCount64;
        if (lastAutorunStopAttempt == 0)
        {
            lastAutorunStopAttempt = now;
            AutorunStopFailed = !commands.StopAutorun();
            return;
        }
        if (now - lastAutorunStopAttempt < 1000)
            return;
        lastAutorunStopAttempt = now;
        if (!InputManager.IsAutoRunning())
        {
            AutorunStopFailed = false;
            return;
        }
        AutorunStopFailed = !commands.StopAutorun();
    }
}
