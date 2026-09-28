using System.Security.Cryptography;
using System.Text.Json;

namespace Codex;

// per-character file contents and naming; no Dalamud types
public static class StateStore
{
    public sealed class StateFile { public List<int> Beasts { get; set; } = new(); }

    public static string Key(ulong contentId)
        => Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(contentId)))[..16].ToLowerInvariant();

    public static string Serialize(IEnumerable<int> beasts)
        => JsonSerializer.Serialize(new StateFile { Beasts = beasts.Distinct().OrderBy(x => x).ToList() });

    public static HashSet<int> Deserialize(string json)
        => new(JsonSerializer.Deserialize<StateFile>(json)?.Beasts ?? new());
}
