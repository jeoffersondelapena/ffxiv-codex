using Codex;
using Xunit;

public class MapMathTests
{
    [Theory]
    [InlineData(100, 0, 0)]      // a plain zone
    [InlineData(200, 0, 0)]      // a city map
    [InlineData(95, -170, 200)]  // a zone with offsets
    public void Map_to_world_inverts_world_to_map(int size, int offX, int offY)
    {
        var (x, z) = MapMath.MapToWorld(24.3f, 16.7f, size, offX, offY);
        var (mx, my) = MapMath.WorldToMap(x, z, size, offX, offY);
        Assert.Equal(24.3f, mx, 2);
        Assert.Equal(16.7f, my, 2);
    }

    [Fact]
    public void The_map_centre_is_the_world_origin_on_an_unoffset_zone()
    {
        var (x, z) = MapMath.MapToWorld(21.5f, 21.5f, 100, 0, 0);
        Assert.Equal(0f, x, 0);
        Assert.Equal(0f, z, 0);
    }
}
