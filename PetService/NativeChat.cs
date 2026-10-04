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
    private bool focusFailed;

    public void FocusInput()
    {
        if (focusFailed || gameGui.GameUiHidden)
            return;
        try
        {
            var chat = gameGui.GetAddonByName<AddonChatLog>("ChatLog");
            if (chat == null || !chat->IsVisible || chat->TextInput == null)
                return;
            var component = (AtkComponentBase*)chat->TextInput;
            var node = component->GetFocusNode();
            if (node != null)
            {
                chat->Focus();
                chat->SetFocusNode(node);
                chat->SetComponentFocusNode(component);
            }
        }
        catch (Exception exception)
        {
            focusFailed = true;
            log.Error(exception, "Could not focus native chat; chat access disabled for this session.");
        }
    }

    public NativeChatState Read()
    {
        if (gameGui.GameUiHidden)
            return default;
        bool available=false,focused=false;
        try {
            // Use the game's text-input state directly. Requiring an exact
            // focused collision node rejects valid chat layouts and IME focus.
            var module=RaptureAtkModule.Instance();
            if(module!=null){available=true;focused=module->IsTextInputActive();}
        } catch { }
        var fallback=new NativeChatState(available,focused,default,default);
        if(failed)return fallback;
        try
        {
            // Fetch pointers afresh: addons can be destroyed on logout or HUD changes.
            var chat = gameGui.GetAddonByName<AddonChatLog>("ChatLog");
            if (chat == null || !chat->IsVisible || chat->TextInput == null)
                return fallback;

            var inputBase = (AtkComponentInputBase*)chat->TextInput;
            available=true;
            var component = (AtkComponentBase*)chat->TextInput;
            var node = (AtkResNode*)inputBase->CollisionNode;
            if (node == null)
                node = (AtkResNode*)component->OwnerNode;
            Bounds bounds = default;
            if(node != null)node->GetBounds(&bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                node = (AtkResNode*)component->OwnerNode;
                if(node != null)node->GetBounds(&bounds);
            }

            focused |= inputBase->IsActive;
            Bounds logBounds = default;
            chat->GetWindowBounds(&logBounds);
            if((logBounds.Width<=0 || logBounds.Height<=0) && chat->RootNode!=null)
                chat->RootNode->GetBounds(&logBounds);
            // An inactive input may not have drawable bounds yet. Keep Enter
            // available so the game can open it; never invent a clickable hole.
            var inputArea=bounds.Width>0 && bounds.Height>0 ? new ChatArea(
                new Vector2(bounds.Pos1.X,bounds.Pos1.Y),new Vector2(bounds.Pos2.X,bounds.Pos2.Y)) : default;
            return new NativeChatState(available, focused, inputArea, new ChatArea(
                new Vector2(logBounds.Pos1.X, logBounds.Pos1.Y),
                new Vector2(logBounds.Pos2.X, logBounds.Pos2.Y)));
        }
        catch (Exception exception)
        {
            // Geometry failure must not disable native text entry. Gameplay
            // remains filtered independently; no mouse hit area is invented.
            failed = true;
            log.Error(exception, "Native chat bounds unavailable; keyboard text entry remains independent.");
            return fallback;
        }
    }
}
