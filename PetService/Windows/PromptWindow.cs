using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;

internal sealed class PromptWindow : Window
{
    private readonly Plugin plugin;
    private string? lastPromptId;
    private bool wasVisible;
    private long appearedAt;
    private string reply = "";
    private bool pushedStyle;
    private bool editingSnooze;
    private bool mouseOverChat;
    private bool drawFailed;
    private NativeChatState chatState;
    private const ImGuiWindowFlags ShieldFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoBackground;

    public PromptWindow(Plugin plugin) : base("Medication Reminder###PetServiceShield")
    {
        this.plugin = plugin;
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        AllowClickthrough = false;
        AllowPinning = false;
        ForceMainWindow = true;
        InhibitAtkCollision = true;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
        Flags = ShieldFlags;
    }

    public override bool DrawConditions()
    {
        if (plugin.HasVisibleReminder)
            return true;
        wasVisible = false;
        editingSnooze = false;
        return false;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        chatState = plugin.ChatState;
        mouseOverChat = chatState.Available
            && chatState.InputArea.Contains(ImGui.GetIO().MousePos - viewport.Pos);
        if (mouseOverChat && editingSnooze)
        {
            ImGuiP.ClearActiveID();
            editingSnooze = false;
        }
        // Only the native chat input rectangle can receive mouse input.
        Flags = ShieldFlags | (mouseOverChat || drawFailed ? ImGuiWindowFlags.NoInputs : 0);
        InhibitAtkCollision = !mouseOverChat && !drawFailed;
        Position = viewport.Pos;
        PositionCondition = ImGuiCond.Always;
        Size = viewport.Size;
        SizeCondition = ImGuiCond.Always;
        var occurrence = plugin.CurrentPrompt;
        if (!wasVisible || occurrence?.Id != lastPromptId)
        {
            appearedAt = Environment.TickCount64;
            reply = "";
            lastPromptId = occurrence?.Id;
            editingSnooze = false;
            // Focus ImGui for controller navigation. Native chat keeps keyboard
            // input because InputGuard leaves WantTextInput off outside our field.
            ImGui.SetNextWindowFocus();
        }
        wasVisible = true;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.025f, 0.03f, 0.045f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        pushedStyle = true;
        if (!drawFailed)
            plugin.CaptureInput(editingSnooze, mouseOverChat);
    }

    public override void Draw()
    {
        try
        {
            DrawPrompt();
        }
        catch
        {
            // WindowSystem catches Draw errors internally. Release here before
            // it replaces this window with its error UI on subsequent frames.
            drawFailed = true;
            plugin.ReleaseInput();
            throw;
        }
    }

    private void DrawPrompt()
    {
        var viewport = ImGui.GetMainViewport();
        DrawBackdrop(viewport.Pos, viewport.Size);
        var prompt = plugin.CurrentPrompt;
        if (prompt is null) return;
        var scale = ImGuiHelpers.GlobalScale;
        var available = ImGui.GetContentRegionAvail();
        var panelSize = new Vector2(MathF.Min(580f * scale, available.X), MathF.Min(600f * scale, available.Y));
        var panelPosition = PlacePanel(viewport.Pos + ImGui.GetCursorPos(), available, ref panelSize);
        ImGui.SetCursorPos(panelPosition - viewport.Pos);
        if (ImGui.BeginChild("PetServicePanel", panelSize, true,
            mouseOverChat ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None))
        {
            try
            {
                plugin.MarkDisplayed();
                ImGui.TextUnformatted(prompt.Kind == "reminder" ? "Daily medication reminder" : "Message from master");
                ImGui.Separator();
                ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(prompt.Label);
                if (!string.IsNullOrEmpty(prompt.Text)) ImGui.TextUnformatted(prompt.Text);
                if (prompt.LocalDay is not null) ImGui.TextUnformatted("Scheduled day: " + prompt.LocalDay);
                ImGui.TextUnformatted(chatState.Available ? "Chat remains available. Press Enter or click the chat input." : "Native chat detection is unavailable; full input capture is used.");
                ImGui.TextUnformatted("FFXIV continues running while this prompt is open.");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                var armed = Environment.TickCount64 - appearedAt >= 500;
                editingSnooze = false;
                if (prompt.Kind == "message")
                {
                    ImGui.InputTextMultiline("Reply", ref reply, 1000, new Vector2(-1, 90 * scale));
                    editingSnooze = ImGui.IsItemActive();
                    ImGui.BeginDisabled(!armed || string.IsNullOrWhiteSpace(reply));
                    if (ImGui.Button("Send reply", new Vector2(-1, 36 * scale))) plugin.Choose("reply", reply);
                    ImGui.EndDisabled();
                }
                ImGui.BeginDisabled(!armed);
                var snoozed = ImGui.Button("Snooze 10 minutes", new Vector2(-1, 36 * scale));
                ImGui.SetItemDefaultFocus();
                var acknowledged = prompt.Kind == "reminder" && ImGui.Button("I took them", new Vector2(-1, 36 * scale));
                ImGui.EndDisabled();
                if (snoozed) plugin.Choose("snooze");
                else if (acknowledged) plugin.Choose("taken");
                ImGui.Separator();
                if (ImGui.Button("Kill switch — stop master input", new Vector2(-1, 32 * scale))) plugin.SetEnabled(false);
                ImGui.TextWrapped(plugin.ServiceStatus);
                if (!string.IsNullOrEmpty(plugin.SaveError)) ImGui.TextWrapped(plugin.SaveError);
                if (plugin.InputCaptureUnavailable) ImGui.TextWrapped("Input capture failed; the prompt remains visible.");
                if (plugin.MovementGuardUnavailable) ImGui.TextWrapped("Movement hook unavailable. Native chat is disabled during the prompt.");
                if (plugin.AutorunStopFailed) ImGui.TextWrapped("Could not cancel an existing autorun.");
            }
            finally { ImGui.EndChild(); }
        }
        else ImGui.EndChild();
        if (plugin.HasVisibleReminder) plugin.CaptureInput(editingSnooze, mouseOverChat);
    }

    private void DrawBackdrop(Vector2 origin, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.025f, 0.03f, 0.045f, 1f));
        var end = origin + size;
        if (!chatState.Available)
        {
            draw.AddRectFilled(origin, end, color);
            return;
        }
        // Keep the chat log readable; only its input field accepts clicks.
        var minimum = Vector2.Clamp(origin + chatState.LogArea.Minimum, origin, end);
        var maximum = Vector2.Clamp(origin + chatState.LogArea.Maximum, minimum, end);
        draw.AddRectFilled(origin, new Vector2(end.X, minimum.Y), color);
        draw.AddRectFilled(new Vector2(origin.X, maximum.Y), end, color);
        draw.AddRectFilled(new Vector2(origin.X, minimum.Y), new Vector2(minimum.X, maximum.Y), color);
        draw.AddRectFilled(new Vector2(maximum.X, minimum.Y), new Vector2(end.X, maximum.Y), color);
    }

    private Vector2 PlacePanel(Vector2 origin, Vector2 available, ref Vector2 size)
    {
        var centered = origin + (available - size) / 2f;
        if (!chatState.Available)
            return centered;
        var viewportOrigin = ImGui.GetMainViewport().Pos;
        var input = new ChatArea(viewportOrigin + chatState.InputArea.Minimum,
            viewportOrigin + chatState.InputArea.Maximum);
        var chat = new ChatArea(viewportOrigin + chatState.LogArea.Minimum,
            viewportOrigin + chatState.LogArea.Maximum);
        Vector2[] candidates = [centered, origin + new Vector2(available.X - size.X, 0),
            origin, origin + new Vector2(0, available.Y - size.Y), origin + available - size];
        Vector2? best = null;
        var leastOverlap = float.MaxValue;
        foreach (var candidate in candidates)
        {
            if (Overlap(candidate, size, input) > 0)
                continue;
            var overlap = Overlap(candidate, size, chat);
            if (overlap >= leastOverlap)
                continue;
            leastOverlap = overlap;
            best = candidate;
        }
        if (best.HasValue)
            return best.Value;

        // A centered/custom HUD may leave no full-height position. Use the larger
        // strip above or below the chat field; the child can scroll when needed.
        var above = Math.Clamp(input.Minimum.Y - origin.Y, 0, available.Y);
        var below = Math.Clamp(origin.Y + available.Y - input.Maximum.Y, 0, available.Y);
        size.Y = MathF.Min(size.Y, MathF.Max(above, below));
        return above >= below ? origin : new Vector2(origin.X, origin.Y + available.Y - size.Y);
    }

    private static float Overlap(Vector2 position, Vector2 size, ChatArea area)
    {
        var extent = Vector2.Min(position + size, area.Maximum) - Vector2.Max(position, area.Minimum);
        return MathF.Max(0, extent.X) * MathF.Max(0, extent.Y);
    }

    public override void PostDraw()
    {
        if (!pushedStyle)
            return;
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        pushedStyle = false;
    }
}
