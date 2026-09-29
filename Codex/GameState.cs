using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Codex;

// what the game itself knows about this character: learned spells, done quests, cleared stages, allowances
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

    public int LeveAllowances
    {
        get { var qm = QuestManager.Instance(); return qm == null ? -1 : qm->NumLeveAllowances; }
    }

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

    public sealed record QuestInfo(uint Id, string Name, bool Repeatable, int Level);

    private Dictionary<string, QuestInfo>? questsByName;
    private Dictionary<uint, QuestInfo>? leveUnlocksByNpc;

    private void LoadQuests(IDataManager data)
    {
        questsByName = new Dictionary<string, QuestInfo>();
        leveUnlocksByNpc = new Dictionary<uint, QuestInfo>();
        var sheet = data.GetExcelSheet<Quest>();
        if (sheet == null) return;
        foreach (var q in sheet)
        {
            var name = q.Name.ExtractText();
            var key = Kinds.LeveKey(name);
            if (key.Length == 0) continue;
            var info = new QuestInfo(q.RowId, name, q.IsRepeatable, q.ClassJobLevel[0]);
            questsByName.TryAdd(key, info);
            // a levemete offers nothing until that NPC's own "Leves of …" quest is done
            if (name.StartsWith("Leves of ", StringComparison.Ordinal)) leveUnlocksByNpc.TryAdd(q.IssuerStart.RowId, info);
        }
    }

    public QuestInfo? Quest(IDataManager data, string name)
    {
        if (questsByName == null) LoadQuests(data);
        return questsByName!.TryGetValue(Kinds.LeveKey(name), out var q) ? q : null;
    }

    // null while the quest is open; otherwise whether the game lets it be taken again
    public bool? QuestDone(IDataManager data, string name)
        => Quest(data, name) is { } q && QuestManager.IsQuestComplete(q.Id) ? q.Repeatable : null;

    // that levemete's still-open "Leves of …" quest, or null once it is done (or the NPC is no levemete)
    public QuestInfo? LeveLockedBy(IDataManager data, uint npc)
    {
        if (npc == 0) return null;
        if (leveUnlocksByNpc == null) LoadQuests(data);
        return leveUnlocksByNpc!.TryGetValue(npc, out var u) && !QuestManager.IsQuestComplete(u.Id) ? u : null;
    }

    // the quest still standing between the character and this source, or null when it is open
    public QuestInfo? LockedBy(IDataManager data, Source s)
        => s.Unlock != null && Quest(data, s.Unlock) is { } q && !QuestManager.IsQuestComplete(q.Id) ? q : null;
}
