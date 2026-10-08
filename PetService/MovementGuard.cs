using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace PetService;

/// <summary>
/// Rejects gameplay movement queries independently of chat's raw keyboard input.
/// Does not modify character coordinates or suppress server-driven movement.
/// </summary>
internal sealed unsafe class MovementGuard : IDisposable
{
    private readonly Func<bool> reminderActive;
    private Hook<InputManager.Delegates.GetInputStatus>? hook;
    public bool Available => hook is { IsEnabled: true, IsDisposed: false };

    public MovementGuard(IGameInteropProvider interop, Func<bool> reminderActive, IPluginLog log)
    {
        this.reminderActive = reminderActive;
        try
        {
            hook = interop.HookFromAddress<InputManager.Delegates.GetInputStatus>(
                (nint)InputManager.MemberFunctionPointers.GetInputStatus, GetInputStatus);
            hook.Enable();
        }
        catch (Exception exception)
        {
            hook?.Dispose();
            hook = null;
            log.Error(exception, "Could not install movement guard; gameplay guarding is unavailable.");
        }
    }

    private bool GetInputStatus(InputManager* manager, InputCode code)
    {
        if (reminderActive() && (code is InputCode.MOVE_DESCENT or InputCode.MOVE_RETENTION
            or InputCode.MOVE_ANGLE_RISING or InputCode.MOVE_ANGLE_DESCENT
            or InputCode.MOVE_FORE or InputCode.MOVE_BACK or InputCode.MOVE_LEFT
            or InputCode.MOVE_STRIFE_L or InputCode.MOVE_RIGHT or InputCode.MOVE_STRIFE_R))
            return false;
        return hook!.Original(manager, code);
    }

    public void Dispose() => hook?.Dispose();
}
