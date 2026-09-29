using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

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
    private readonly IDataManager data;
    private readonly Dictionary<string, string> search = new();
    private (int Id, string Name, bool Value)? pendingBeast;

    public MainWindow(Plugin plugin, IGameGui gameGui, IChatGui chat, IPluginLog log, IDataManager data) : base("Codex##main")
    {
        this.plugin = plugin; this.gameGui = gameGui; this.chat = chat; this.log = log; this.data = data;
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
        var entries = plugin.Data.For(list);
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
        DrawMountPicker();
        var query = search.GetValueOrDefault(list, "");
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputTextWithHint("##search", "Search name, enemy or place", ref query, 128)) search[list] = query;
        ImGui.SameLine();
        ImGui.TextColored(Grey, "Sort:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##sort", Kinds.Sorts.FirstOrDefault(s => s.Key == cfg.Sort).Label ?? "by level"))
        {
            foreach (var (key, label) in Kinds.Sorts)
                if (ImGui.Selectable(label, cfg.Sort == key)) { cfg.Sort = key; plugin.SaveConfig(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var grouped = cfg.GroupByBand;
        if (ImGui.Checkbox("Group by band", ref grouped)) { cfg.GroupByBand = grouped; plugin.SaveConfig(); }
        ImGui.SameLine();
        var goOn = cfg.ContinueAfterLeve;
        if (ImGui.Checkbox("Continue after a leve is accepted", ref goOn)) { cfg.ContinueAfterLeve = goOn; plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("After a trip to a levemete, travel on to the enemy by itself once the leve shows as accepted. Off: it says so and waits for your tap.");
        ImGui.TextColored(Grey, "Include:");
        var present = Kinds.PresentOptIn(entries);
        if (present.Count == 0) { ImGui.SameLine(); ImGui.TextColored(Grey, "nothing optional in this list"); }
        foreach (var (key, label) in present)
        {
            ImGui.SameLine();
            var on = enabled.Contains(key);
            if (ImGui.Checkbox(label, ref on)) { cfg.SetKind(list, key, on); plugin.SaveConfig(); enabled = cfg.KindsFor(list); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Kinds.IncludeTip(key));
        }
        ImGui.Separator();
        if (!plugin.State.Ready) { ImGui.TextColored(Grey, "Log in to see this character's progress."); return; }

        var doneById = entries.ToDictionary(e => e.Id, e => IsDone(list, e));
        Func<Source, bool> usable = s => plugin.Game.SpentLabel(data, s) == null;
        var levelById = entries.ToDictionary(e => e.Id, e => Kinds.ShownLevel(e, enabled, usable));
        var hidden = levelById.Count(kv => kv.Value == null);
        var obtained = doneById.Count(kv => kv.Value);
        var summary = Progress.Summary(obtained, entries.Count, hidden);
        if (obtained == entries.Count && entries.Count > 0) ImGui.TextColored(Green, summary); else ImGui.Text(summary);
        ImGui.Spacing();
        var groups = cfg.GroupByBand
            ? Kinds.Bands.Select(b => (Title: $"Lv {b.Lo}-{b.Hi}", Key: $"band{b.Lo}", Band: ((int, int)?)b, All: false)).ToList()
            : new List<(string Title, string Key, (int, int)? Band, bool All)>();
        if (cfg.Sort == "lv-desc") groups.Reverse();
        groups.Add(cfg.GroupByBand ? ("Level unknown", "bandnone", null, false) : ("All levels", "all", null, true));
        foreach (var (title, key, band, all) in groups)
        {
            var rows = new List<(Entry Entry, List<Source> Shown, bool Done, int Lv)>();
            foreach (var e in entries)
            {
                if (levelById[e.Id] is not int lv) continue;
                var inBand = all || (band is (int lo, int hi) ? lv >= lo && lv <= hi : Kinds.BandOf(lv) == null);
                if (!inBand) continue;
                var shownSources = e.Sources.Where(s => Kinds.Visible(s, enabled)).ToList();
                if (!Kinds.Matches(e, shownSources, query)) continue;
                rows.Add((e, shownSources, doneById[e.Id], lv));
            }
            if (rows.Count == 0) continue;
            var done = rows.Count(r => r.Done);
            var complete = done == rows.Count;
            var header = $"{title}   {done}/{rows.Count}" + (complete ? "   Complete, look at the next band" : "") + $"##{key}";
            if (complete) ImGui.PushStyleColor(ImGuiCol.Text, Green);
            var open = ImGui.CollapsingHeader(header, complete ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen);
            if (complete) ImGui.PopStyleColor();
            if (!open) continue;
            ImGui.Indent();
            // fixed widths for the optional cells keep every band aligned; the source column takes the rest
            if (ImGui.BeginTable($"rows##{key}", 7, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
            {
                ImGui.TableSetupColumn("done", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("no", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("lv", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("min", ImGuiTableColumnFlags.WidthFixed, 52);
                ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, 220);
                ImGui.TableSetupColumn("rank", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("source", ImGuiTableColumnFlags.WidthStretch);
                foreach (var (e, shown, isDone, lv) in Kinds.Sorted(rows, r => r.Entry, r => r.Lv, cfg.Sort))
                {
                    if (unobtainedOnly && isDone) continue;
                    DrawEntry(list, e, shown, isDone, lv);
                }
                ImGui.EndTable();
            }
            ImGui.Unindent();
        }
        DrawTickConfirmation();
    }

    // A tamed beast is ticked by hand, so a click asks first; the box shows the saved value until then.
    private void DrawTickConfirmation()
    {
        if (pendingBeast == null) return;
        var (id, name, value) = pendingBeast.Value;
        if (!ImGui.IsPopupOpen("Codex##confirm")) ImGui.OpenPopup("Codex##confirm");
        if (!ImGui.BeginPopupModal("Codex##confirm", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.Text(value ? $"Mark {name} as tamed?" : $"Remove the tick on {name}?");
        ImGui.Spacing();
        if (ImGui.Button("Yes", new Vector2(120, 0))) { plugin.State.SetBeast(id, value); pendingBeast = null; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("No", new Vector2(120, 0))) { pendingBeast = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private void DrawMountPicker()
    {
        var cfg = plugin.Config;
        ImGui.TextColored(Grey, "Mount:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("##mount", MountName(cfg.MountId)))
        {
            if (ImGui.Selectable("Mount Roulette", cfg.MountId == 0)) { cfg.MountId = 0; plugin.SaveConfig(); }
            foreach (var (id, name) in plugin.Game.UnlockedMounts(data))
                if (ImGui.Selectable(name, cfg.MountId == id)) { cfg.MountId = id; plugin.SaveConfig(); }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Travel On Tap mounts up for the longer stretches and flies where the zone allows it.");
    }

    private string MountName(uint id)
    {
        if (id == 0) return "Mount Roulette";
        var row = data.GetExcelSheet<Mount>()?.GetRowOrDefault(id);
        return row == null ? "Mount Roulette" : GameState.TitleCase(row.Value.Singular.ExtractText());
    }

    private void DrawEntry(string list, Entry e, List<Source> shown, bool done, int lv)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (list == "bst")
        {
            var tick = done;
            if (ImGui.Checkbox($"##done{e.Id}", ref tick)) pendingBeast = (e.Id, e.Name, tick);
        }
        else
        {
            ImGui.TextColored(done ? Green : Grey, done ? "[x]" : "[ ]");
        }
        ImGui.TableNextColumn();
        ImGui.TextColored(Grey, $"#{e.Id,3}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(list == "bst" ? "Number in the Master's Bestiary" : "Number in the Blue Magic Spellbook");
        ImGui.TableNextColumn();
        ImGui.TextColored(Grey, $"Lv {lv,3}");
        ImGui.TableNextColumn();
        if (e.MinLv > 1)
        {
            ImGui.TextColored(Grey, $"min {e.MinLv}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Minimum level to obtain");
        }
        ImGui.TableNextColumn();
        Func<Source, bool> usable = s => plugin.Game.SpentLabel(data, s) == null;
        var best = Kinds.Driver(e, plugin.Config.KindsFor(list), usable) ?? shown[0];
        if (ImGui.Selectable($"{e.Name}##e{list}{e.Id}"))
            Tap(e, best);
        // read before the stars draw, or they take the hover and the name shows nothing
        var nameHovered = ImGui.IsItemHovered();
        ImGui.TableNextColumn();
        if (e.Rank is > 0 and <= 5)
        {
            ImGui.TextColored(Amber, new string('*', e.Rank.Value));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Rank {e.Rank}");
        }
        if (nameHovered)
        {
            var enabledKinds = plugin.Config.KindsFor(list);
            var lines = e.Sources.OrderBy(s => Kinds.Rank(s.K)).ThenBy(s => s.Lv).Select(s => (ReferenceEquals(s, best) ? "> " : "   ")
                + $"{Kinds.SourceLabel(s)}: {s.Name}" + (s.Loc != null ? $" — {s.Loc}" : "") + (s.Xy != null ? $" ({s.Xy[0]:0.0}, {s.Xy[1]:0.0})" : "")
                + $", {Kinds.LevelText(s)}" + (s.Note != null ? $"; {s.Note}" : "") + (Kinds.Visible(s, enabledKinds) ? "" : " (not included)")
                + (plugin.Game.SpentLabel(data, s) is string spent ? $" ({spent})" : "")
                + (s.Via != null ? $"\n       levemete {s.Via.Name} — {s.Via.Loc}" + (s.Via.Xy != null ? $" ({s.Via.Xy[0]:0.0}, {s.Via.Xy[1]:0.0})" : "") + ", first stop"
                    + (plugin.Game.LeveLockedBy(data, s.Via.Npc) is { } u ? $"; locked until '{u.Name}' (level {u.Level}) is done" : "") : ""));
            var hints = e.Sources.Select(s => Kinds.Hint(s.K)).Where(h => h.Length > 0).Distinct().ToList();
            if (shown.Any(s => s.K == "leve") && plugin.Game.LeveAllowances >= 0) hints.Add($"Leve allowances now: {plugin.Game.LeveAllowances}");
            ImGui.SetTooltip(string.Join("\n", lines) + "\n\n" + string.Join("\n", hints) + "\n\nTap: map flag" + (plugin.Config.Travel ? " and travel" : ""));
        }
        ImGui.TableNextColumn();
        ImGui.TextColored(Grey, $"{Kinds.SourceLabel(best)}: {best.Name}" + (best.Loc != null ? $" — {best.Loc}" : "") + (best.Xy != null ? $" ({best.Xy[0]:0.0}, {best.Xy[1]:0.0})" : ""));
    }

    private bool IsDone(string list, Entry e)
        => list == "bst" ? plugin.State.IsBeastDone(e.Id) : plugin.Game.IsUnlocked(e.UnlockLink);

    private void Tap(Entry e, Source tappedSource)
    {
        var s = plugin.Travel.NextStop(tappedSource);
        if (s.HasMapPosition)
        {
            var ok = gameGui.OpenMapWithMapLink(new MapLinkPayload(s.Terr, s.Map, s.Xy![0], s.Xy[1], 0f));
            log.Information($"[Codex] Flag for {e.Name}: {s.Name} in {s.Loc} ({s.Xy[0]:0.0}, {s.Xy[1]:0.0}), map {(ok ? "opened" : "did not open")}");
        }
        else
        {
            chat.Print($"[Codex] {e.Name}: {Kinds.SourceLabel(s)} — {s.Name}" + (s.Loc != null ? $" in {s.Loc}" : "") + " (no map position).");
            log.Information($"[Codex] Tap on {e.Name}: no map position for {s.Name} ({s.K})");
        }
        if (plugin.Config.Travel) plugin.Travel.Go(tappedSource, e.Name);
    }
}
