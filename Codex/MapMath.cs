namespace Codex;

// the game's map formula and its inverse; no Dalamud types, so the tests compile it directly
public static class MapMath
{
    public static (float X, float Z) MapToWorld(float mapX, float mapY, int sizeFactor, int offsetX, int offsetY)
    {
        var c = sizeFactor / 100f;
        var x = ((mapX - 1f) * 2048f / (41f / c) - 1024f) / c - offsetX;
        var z = ((mapY - 1f) * 2048f / (41f / c) - 1024f) / c - offsetY;
        return (x, z);
    }

    public static (float X, float Y) WorldToMap(float worldX, float worldZ, int sizeFactor, int offsetX, int offsetY)
    {
        var c = sizeFactor / 100f;
        var x = 41f / c * ((worldX + offsetX) * c + 1024f) / 2048f + 1f;
        var y = 41f / c * ((worldZ + offsetY) * c + 1024f) / 2048f + 1f;
        return (x, y);
    }
}
