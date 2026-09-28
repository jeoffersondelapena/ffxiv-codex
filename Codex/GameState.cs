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
