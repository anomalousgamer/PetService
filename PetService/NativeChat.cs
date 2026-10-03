using Vector2 = System.Numerics.Vector2;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PetService;

internal readonly record struct ChatArea(Vector2 Minimum, Vector2 Maximum)
{
    public bool Contains(Vector2 point) => point.X >= Minimum.X && point.X < Maximum.X
        && point.Y >= Minimum.Y && point.Y < Maximum.Y;
}

internal readonly record struct NativeChatState(bool Available, bool Focused, ChatArea InputArea, ChatArea LogArea);

internal sealed unsafe class NativeChat(IGameGui gameGui, IPluginLog log)
{
    private bool failed;

    public void FocusInput()
    {
        if (failed || gameGui.GameUiHidden)
            return;
        try
        {
            var chat = gameGui.GetAddonByName<AddonChatLog>("ChatLog");
            if (chat == null || !chat->IsVisible || chat->TextInput == null || !chat->TextInput->Enabled)
                return;
            var component = (AtkComponentBase*)chat->TextInput;
            var node = component->GetFocusNode();
            if (node != null)
            {
                chat->Focus();
                chat->SetFocusNode(node);
            }
        }
        catch (Exception exception)
        {
            failed = true;
            log.Error(exception, "Could not focus native chat; chat access disabled for this session.");
        }
    }

    public NativeChatState Read()
    {
        if (failed || gameGui.GameUiHidden)
            return default;
        try
        {
            // Fetch pointers afresh: addons can be destroyed on logout or HUD changes.
            var chat = gameGui.GetAddonByName<AddonChatLog>("ChatLog");
            if (chat == null || !chat->IsVisible || chat->TextInput == null || !chat->TextInput->Enabled)
                return default;

            var inputBase = (AtkComponentInputBase*)chat->TextInput;
            var component = (AtkComponentBase*)chat->TextInput;
            var node = (AtkResNode*)inputBase->CollisionNode;
            if (node == null)
                node = (AtkResNode*)component->OwnerNode;
            if (node == null || !node->IsVisible())
                return default;

            Bounds bounds = default;
            node->GetBounds(&bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return default;

            var stage = AtkStage.Instance();
            var manager = stage == null ? null : stage->AtkInputManager;
            var target = manager == null || manager->TextInput == null
                ? null : manager->TextInput->TargetTextInputEventInterface;
            var focused = manager != null && manager->IsTextInputActive && target != null
                && target->GetOwnerNode() == (AtkResNode*)component->OwnerNode;
            Bounds logBounds = default;
            chat->GetWindowBounds(&logBounds);
            return new NativeChatState(true, focused, new ChatArea(
                new Vector2(bounds.Pos1.X, bounds.Pos1.Y),
                new Vector2(bounds.Pos2.X, bounds.Pos2.Y)), new ChatArea(
                new Vector2(logBounds.Pos1.X, logBounds.Pos1.Y),
                new Vector2(logBounds.Pos2.X, logBounds.Pos2.Y)));
        }
        catch (Exception exception)
        {
            // Unknown layouts must not create a hole through to gameplay.
            failed = true;
            log.Error(exception, "Native chat detection failed; chat access disabled for this session.");
            return default;
        }
    }
}
