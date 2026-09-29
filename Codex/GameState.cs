using System.Globalization;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Codex;

public sealed unsafe class GameState
{
    public ulong ContentId
    {
        get { var ps = PlayerState.Instance(); return ps == null ? 0 : ps->ContentId; }
    }

    // every Blue Mage spell's action carries an unlock link; the game answers whether it is unlocked
    public bool IsUnlocked(int unlockLink)
    {
        if (unlockLink <= 0) return false;
        var ui = UIState.Instance();
        return ui != null && ui->IsUnlockLinkUnlockedOrQuestCompleted((uint)unlockLink, 0, true);
    }

    public bool HasLeve(ushort leveId)
    {
        var qm = QuestManager.Instance();
        if (qm == null || leveId == 0) return false;
        for (var i = 0; i < qm->LeveQuests.Length; i++)
            if (qm->LeveQuests[i].LeveId == leveId) return true;
        return false;
    }

    public bool HasAnyLeve()
    {
        var qm = QuestManager.Instance();
        if (qm == null) return false;
        for (var i = 0; i < qm->LeveQuests.Length; i++)
            if (qm->LeveQuests[i].LeveId != 0) return true;
        return false;
    }

    public int LeveAllowances
    {
        get { var qm = QuestManager.Instance(); return qm == null ? -1 : qm->NumLeveAllowances; }
    }

    private Dictionary<string, ushort>? leveIds;

    public ushort LeveId(IDataManager data, string name)
    {
        if (leveIds == null)
        {
            leveIds = new Dictionary<string, ushort>();
            var sheet = data.GetExcelSheet<Leve>();
            if (sheet != null)
                foreach (var row in sheet)
                {
                    var key = Kinds.LeveKey(row.Name.ExtractText());
                    if (key.Length > 0) leveIds.TryAdd(key, (ushort)row.RowId);
                }
        }
        return leveIds.TryGetValue(Kinds.LeveKey(name), out var id) ? id : (ushort)0;
    }

    private Dictionary<string, (uint Id, bool Repeatable)>? quests;

    private List<uint>? carnivale;

    // stage N is the Nth Carnivale duty by content id; the clear flag is the one the duty list shows
    public bool StageCleared(IDataManager data, int stage)
    {
        if (carnivale == null)
        {
            var sheet = data.GetExcelSheet<ContentFinderCondition>();
            carnivale = sheet == null ? new List<uint>() : sheet
                .Where(c => c.ContentLinkType == 1 && c.ContentType.ValueNullable?.Name.ExtractText().Contains("Masked Carnivale") == true)
                .Select(c => c.Content.RowId).OrderBy(id => id).ToList();
        }
        return stage >= 1 && stage <= carnivale.Count && UIState.IsInstanceContentCompleted(carnivale[stage - 1]);
    }

    // why a source can no longer be used, or null while it is still open
    public string? SpentLabel(IDataManager data, Source s)
    {
        if (Kinds.QuestOf(s) is string q) return QuestDone(data, q) is bool again ? (again ? "quest done, repeatable" : "quest done") : null;
        if (Kinds.StageOf(s) is int stage) return StageCleared(data, stage) ? "stage cleared" : null;
        return null;
    }

    private Dictionary<uint, (string Name, uint Id, int Level)>? leveUnlocks;

    // a levemete offers nothing until that NPC's own "Leves of …" quest is done
    public (string Name, int Level)? LeveLockedBy(IDataManager data, uint npc)
    {
        if (npc == 0) return null;
        if (leveUnlocks == null)
        {
            leveUnlocks = new Dictionary<uint, (string, uint, int)>();
            var sheet = data.GetExcelSheet<Quest>();
            if (sheet != null)
                foreach (var q in sheet)
                {
                    var name = q.Name.ExtractText();
                    if (name.StartsWith("Leves of ", StringComparison.Ordinal))
                        leveUnlocks.TryAdd(q.IssuerStart.RowId, (name, q.RowId, q.ClassJobLevel[0]));
                }
        }
        return leveUnlocks.TryGetValue(npc, out var u) && !QuestManager.IsQuestComplete(u.Id) ? (u.Name, u.Level) : null;
    }

    // null while the quest is open; otherwise whether the game lets it be taken again
    public bool? QuestDone(IDataManager data, string name)
    {
        if (quests == null)
        {
            quests = new Dictionary<string, (uint, bool)>();
            var sheet = data.GetExcelSheet<Quest>();
            if (sheet != null)
                foreach (var q in sheet)
                {
                    var key = Kinds.LeveKey(q.Name.ExtractText());
                    if (key.Length > 0) quests.TryAdd(key, (q.RowId, q.IsRepeatable));
                }
        }
        if (!quests.TryGetValue(Kinds.LeveKey(name), out var found) || !QuestManager.IsQuestComplete(found.Id)) return null;
        return found.Repeatable;
    }

    public bool IsMountUnlocked(uint mountId)
    {
        var ps = PlayerState.Instance();
        return mountId != 0 && ps != null && ps->IsMountUnlocked(mountId);
    }

    public bool FlyingUnlocked(uint aetherCurrentCompFlgSet)
    {
        var ps = PlayerState.Instance();
        return aetherCurrentCompFlgSet != 0 && ps != null && ps->IsAetherCurrentZoneComplete(aetherCurrentCompFlgSet);
    }

    public bool CanUse(ActionType type, uint id)
    {
        var am = ActionManager.Instance();
        return am != null && am->GetActionStatus(type, id) == 0;
    }

    public bool Use(ActionType type, uint id)
    {
        var am = ActionManager.Instance();
        return am != null && am->UseAction(type, id);
    }

    public List<(uint Id, string Name)> UnlockedMounts(IDataManager data)
    {
        var list = new List<(uint Id, string Name)>();
        var sheet = data.GetExcelSheet<Mount>();
        if (sheet == null) return list;
        foreach (var m in sheet)
        {
            var name = m.Singular.ExtractText();
            if (name.Length == 0 || !IsMountUnlocked(m.RowId)) continue;
            list.Add((m.RowId, TitleCase(name)));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static string TitleCase(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}
