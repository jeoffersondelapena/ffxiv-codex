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

    // what a character chose in the window: two game windows would otherwise overwrite each other's choices
    public sealed class Settings
    {
        public string List { get; set; } = "blu";
        public Dictionary<string, List<string>> EnabledKinds { get; set; } = new();
        public Dictionary<string, bool> UnobtainedOnly { get; set; } = new();
        public string Sort { get; set; } = "lv";
        public bool AutoTravel { get; set; } = true;
        public uint MountId { get; set; }
        public float MountDistance { get; set; } = 15f;
        public bool Sprint { get; set; } = true;
        public bool GroupByBand { get; set; } = true;
    }

    public static string SerializeSettings(Settings settings)
        => JsonSerializer.Serialize(settings);

    public static Settings DeserializeSettings(string json)
        => JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
}
