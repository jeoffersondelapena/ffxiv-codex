using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
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
        var auto = cfg.AutoTravel;
        if (ImGui.Checkbox("Auto travel", ref auto)) { cfg.AutoTravel = auto; plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("NPC and Enemy also switch to the list's job and travel there: the game's teleport, Lifestream for a city aethernet, a mount for long stretches, vnavmesh for the walk. Off: the map flag only.");
        if (plugin.Travel.Active)
        {
            ImGui.SameLine();
            ImGui.TextColored(Amber, plugin.Travel.Status);
            ImGui.SameLine();
            if (ImGui.SmallButton("Stop")) plugin.Travel.Cancel();
        }
        if (auto) DrawMountPicker();
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
        // the controls above stay put; only the list scrolls
        ImGui.BeginChild($"list##{list}", new Vector2(0, 0), false);
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
            if (ImGui.BeginTable($"rows##{key}", 11, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("done", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("no", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("lv", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("min", ImGuiTableColumnFlags.WidthFixed, 52);
                ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("rank", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("npc", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("enemy", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("wiki", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("copy", ImGuiTableColumnFlags.WidthFixed);
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
        ImGui.EndChild();
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

    private void DrawEntry(string list, Entry e, List<Source> shown, bool done, int lv)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        // these bindings have no hovered-row query; the row's own rectangle stands in, measured once the first cell is placed
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var left = ImGui.GetWindowPos().X;
        if (ImGui.IsMouseHoveringRect(new Vector2(left, rowTop), new Vector2(left + ImGui.GetWindowSize().X, rowTop + ImGui.GetFrameHeight())))
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderHovered));
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
        ImGui.TextUnformatted(e.Name);
        // read before the stars draw, or they take the hover and the name shows nothing
        var nameHovered = ImGui.IsItemHovered();
        ImGui.TableNextColumn();
        if (e.Rank is > 0 and <= 5)
        {
            ImGui.TextColored(Amber, new string('*', e.Rank.Value));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Rank {e.Rank} of 5: how hard the spell is to learn from a kill; one star is the easiest, five the hardest. Inside a level-synced duty the learn is guaranteed whatever the rank.");
        }
        // one button per place a row can point at, greyed with the reason when there is none
        var id = $"{list}{e.Id}";
        var npc = Kinds.NpcStop(best);
        var role = npc == null ? "" : ReferenceEquals(npc, best.Via) ? (best.Leve != null ? $", the levemete for '{best.Leve}'" : npc.Quest != null ? $", who gives '{npc.Quest}'" : "") : $", {Kinds.SourceLabel(best).ToLowerInvariant()}";
        var verb = plugin.Config.AutoTravel ? "Travel to" : "Flag";
        RowButton("NPC", id, npc != null, npc != null ? $"{verb} {npc.Name} — {npc.Loc} {Coords(npc)}{role}" : "No one to see first: go straight to the enemy", () => Visit(list, e, npc!));
        var enemy = Kinds.EnemyStop(best);
        RowButton("Enemy", id, enemy != null, enemy != null ? $"{verb} {enemy.Name} — {enemy.Loc} {Coords(enemy)}" + (best.Via != null ? " (after the NPC)" : "") : Kinds.NoEnemy(best), () => Visit(list, e, enemy!));
        RowButton("Wiki", id, e.Wiki != null, e.Wiki != null ? "Open the wiki page in the browser" : "No wiki page known", () => OpenWiki(e));
        RowButton("Copy", id, e.Wiki != null, e.Wiki != null ? "Copy the wiki link" : "No wiki page known", () => { ImGui.SetClipboardText(e.Wiki!); chat.Print($"[Codex] Link copied: {e.Wiki}"); });
        ImGui.TableNextColumn();
        ImGui.TextColored(Grey, $"{Kinds.SourceLabel(best)}: {best.Name}" + (best.Loc != null ? $" — {best.Loc}" : "") + (best.Xy != null ? $" ({best.Xy[0]:0.0}, {best.Xy[1]:0.0})" : ""));
        // the tooltip belongs to the name and to the source text alike
        if (nameHovered || ImGui.IsItemHovered())
        {
            var enabledKinds = plugin.Config.KindsFor(list);
            var lines = e.Sources.OrderBy(s => Kinds.Rank(s.K)).ThenBy(s => s.Lv).Select(s => (ReferenceEquals(s, best) ? "> " : "   ")
                + $"{Kinds.SourceLabel(s)}: {s.Name}" + (s.Loc != null ? $" — {s.Loc}" : "") + (s.Xy != null ? $" ({s.Xy[0]:0.0}, {s.Xy[1]:0.0})" : "")
                + $", {Kinds.LevelText(s)}" + (s.Note != null ? $"; {s.Note}" : "") + (Kinds.Visible(s, enabledKinds) ? "" : " (not included)")
                + (plugin.Game.SpentLabel(data, s) is string spent ? $" ({spent})" : "")
                + (plugin.Game.LockedBy(data, s) is { } lq ? $" (locked: '{lq.Name}', level {lq.Level}" + (s.UnlockVia != null ? $", from {s.UnlockVia.Name} — {s.UnlockVia.Loc} {Coords(s.UnlockVia)}" : "") + ")" : "")
                + (s.Via != null ? "\n       " + (s.Leve != null ? "levemete " : s.Via.Quest != null ? "quest giver " : "") + $"{s.Via.Name} — {s.Via.Loc} {Coords(s.Via)}, first"
                    + (plugin.Game.LeveLockedBy(data, s.Via.Npc) is { } u ? $"; locked until '{u.Name}' (level {u.Level}) is done" : "") : ""));
            var hints = e.Sources.Select(s => Kinds.Hint(s.K)).Where(h => h.Length > 0).Distinct().ToList();
            if (shown.Any(s => s.K == "leve") && plugin.Game.LeveAllowances >= 0) hints.Add($"Leve allowances now: {plugin.Game.LeveAllowances}");
            ImGui.SetTooltip(string.Join("\n", lines) + "\n\n" + string.Join("\n", hints) + "\n\nNPC: whom to see first. Enemy: where it appears" + (plugin.Config.AutoTravel ? " (both travel there)" : "") + ". Wiki and Copy: the page");
        }
    }

    private bool IsDone(string list, Entry e)
        => list == "bst" ? plugin.State.IsBeastDone(e.Id) : plugin.Game.IsUnlocked(e.UnlockLink);

    private static string Coords(Source s) => s.Xy is { Length: 2 } ? $"({s.Xy[0]:0.0}, {s.Xy[1]:0.0})" : "";

    // the flag always; the trip only when asked for, and on the right job since the learn counts only there
    private void Visit(string list, Entry e, Source stop)
    {
        Flag(e, stop);
        if (plugin.Config.AutoTravel) plugin.Travel.Go(stop, e.Name, list == "bst" ? (byte)43 : (byte)36);
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
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Used for the longer stretches; flies where the zone allows it.");
    }

    private string MountName(uint id)
    {
        if (id == 0) return "Mount Roulette";
        var row = data.GetExcelSheet<Mount>()?.GetRowOrDefault(id);
        return row == null ? "Mount Roulette" : GameState.TitleCase(row.Value.Singular.ExtractText());
    }

    private void RowButton(string label, string id, bool enabled, string tip, System.Action act)
    {
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!enabled);
        if (ImGui.SmallButton($"{label}##{label}{id}")) act();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip);
    }

    // the browser is the user's; a failed hand-off leaves the link in chat and on the clipboard
    private void OpenWiki(Entry e)
    {
        if (e.Wiki == null) return;
        try { Util.OpenLink(e.Wiki); }
        catch (Exception ex)
        {
            ImGui.SetClipboardText(e.Wiki);
            chat.Print($"[Codex] The browser did not open ({ex.Message}); link copied: {e.Wiki}");
        }
    }

    private void Flag(Entry e, Source s)
    {
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
    }

}
