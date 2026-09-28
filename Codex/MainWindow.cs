using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace Codex;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Grey  = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Amber = new(0.95f, 0.75f, 0.35f, 1f);
    private static readonly (string Key, string Label)[] Lists = { ("blu", "Blue Magic"), ("bst", "Beasts") };

    private readonly Plugin plugin;
    private readonly IGameGui gameGui;
    private readonly IChatGui chat;
    private readonly IPluginLog log;

    public MainWindow(Plugin plugin, IGameGui gameGui, IChatGui chat, IPluginLog log) : base("Codex##main")
    {
        this.plugin = plugin; this.gameGui = gameGui; this.chat = chat; this.log = log;
        Size = new Vector2(560, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = plugin.Config;
        plugin.State.Refresh();
        if (ImGui.BeginTabBar("lists"))
        {
            foreach (var (key, label) in Lists)
            {
                if (!ImGui.BeginTabItem(label)) continue;
                if (cfg.List != key) { cfg.List = key; plugin.SaveConfig(); }
                DrawList(key);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawList(string list)
    {
        var cfg = plugin.Config;
        var enabled = cfg.KindsFor(list);
        var unobtainedOnly = cfg.UnobtainedOnly.GetValueOrDefault(list);
        if (ImGui.Checkbox("Unobtained Only", ref unobtainedOnly)) { cfg.UnobtainedOnly[list] = unobtainedOnly; plugin.SaveConfig(); }
        ImGui.SameLine();
        var travel = cfg.Travel;
        if (ImGui.Checkbox("Travel On Tap", ref travel)) { cfg.Travel = travel; plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tapping an entry also teleports there with Lifestream and walks with vnavmesh. Off: the map flag only.");
        if (plugin.Travel.Active)
        {
            ImGui.SameLine();
            ImGui.TextColored(Amber, plugin.Travel.Status);
            ImGui.SameLine();
            if (ImGui.SmallButton("Stop")) plugin.Travel.Cancel();
        }
        ImGui.TextColored(Grey, "Include:");
        foreach (var (key, label) in Kinds.OptIn)
        {
            ImGui.SameLine();
            var on = enabled.Contains(key);
            if (ImGui.Checkbox(label, ref on)) { cfg.SetKind(list, key, on); plugin.SaveConfig(); enabled = cfg.KindsFor(list); }
        }
        ImGui.Separator();
        if (!plugin.State.Ready) { ImGui.TextColored(Grey, "Log in to see this character's progress."); return; }

        var entries = plugin.Data.For(list);
        foreach (var (lo, hi) in Kinds.Bands)
        {
            var rows = new List<(Entry Entry, List<Source> Shown, bool Done)>();
            foreach (var e in entries)
            {
                if (e.MinLv < lo || e.MinLv > hi) continue;
                var shown = e.Sources.Where(s => Kinds.Visible(s, enabled)).ToList();
                if (shown.Count == 0) continue;
                rows.Add((e, shown, IsDone(list, e)));
            }
            if (rows.Count == 0) continue;
            var done = rows.Count(r => r.Done);
            var complete = done == rows.Count;
            var header = $"Lv {lo}-{hi}   {done}/{rows.Count}" + (complete ? "   Complete, look at the next band" : "") + $"##band{lo}";
            if (complete) ImGui.PushStyleColor(ImGuiCol.Text, Green);
            var open = ImGui.CollapsingHeader(header, complete ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen);
            if (complete) ImGui.PopStyleColor();
            if (!open) continue;
            ImGui.Indent();
            foreach (var (e, shown, isDone) in rows.OrderBy(r => r.Entry.MinLv).ThenBy(r => r.Entry.Id))
            {
                if (unobtainedOnly && isDone) continue;
                DrawEntry(list, e, shown, isDone);
            }
            ImGui.Unindent();
        }
    }

    private void DrawEntry(string list, Entry e, List<Source> shown, bool done)
    {
        if (list == "bst")
        {
            var tick = done;
            if (ImGui.Checkbox($"##done{e.Id}", ref tick)) plugin.State.SetBeast(e.Id, tick);
        }
        else
        {
            ImGui.TextColored(done ? Green : Grey, done ? "[x]" : "[ ]");
        }
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"Lv {e.MinLv,3}");
        ImGui.SameLine();
        var best = shown.FirstOrDefault(s => s.Rec) ?? shown[0];
        if (ImGui.Selectable($"{e.Name}##e{list}{e.Id}", false, ImGuiSelectableFlags.None, new Vector2(220, 0)))
            Tap(e, best);
        if (ImGui.IsItemHovered())
        {
            var lines = shown.Select(s => $"{Kinds.Label(s.K)}: {s.Name}" + (s.Loc != null ? $" — {s.Loc}" : "") + (s.Xy != null ? $" ({s.Xy[0]:0.0}, {s.Xy[1]:0.0})" : "") + (s.Note != null ? $"; {s.Note}" : ""));
            ImGui.SetTooltip(string.Join("\n", lines) + "\n\nTap: map flag" + (plugin.Config.Travel ? " and travel" : ""));
        }
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"{Kinds.Label(best.K)}: {best.Name}" + (best.Loc != null ? $" — {best.Loc}" : "") + (best.Xy != null ? $" ({best.Xy[0]:0.0}, {best.Xy[1]:0.0})" : ""));
    }

    private bool IsDone(string list, Entry e)
        => list == "bst" ? plugin.State.IsBeastDone(e.Id) : plugin.Game.IsUnlocked(e.UnlockLink);

    private void Tap(Entry e, Source s)
    {
        if (s.HasMapPosition)
        {
            var ok = gameGui.OpenMapWithMapLink(new MapLinkPayload(s.Terr, s.Map, s.Xy![0], s.Xy[1], 0f));
            log.Information($"[Codex] Flag for {e.Name}: {s.Name} in {s.Loc} ({s.Xy[0]:0.0}, {s.Xy[1]:0.0}), map {(ok ? "opened" : "did not open")}");
        }
        else
        {
            chat.Print($"[Codex] {e.Name}: {Kinds.Label(s.K)} — {s.Name}" + (s.Loc != null ? $" in {s.Loc}" : "") + " (no map position).");
            log.Information($"[Codex] Tap on {e.Name}: no map position for {s.Name} ({s.K})");
        }
        if (plugin.Config.Travel) plugin.Travel.Go(s, e.Name);
    }
}
