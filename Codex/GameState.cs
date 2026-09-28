using FFXIVClientStructs.FFXIV.Client.Game.UI;

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
}
