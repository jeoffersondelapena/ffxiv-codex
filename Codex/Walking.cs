using System.Numerics;

namespace Codex;

public static class Walking
{
    public const uint SprintAction = 4;

    // a walk this short gains under a second, while the 60-second recast may leave the next long walk without it
    public const float ShortestSprintWalk = 30f;

    public static float Left(Vector3 from, IReadOnlyList<Vector3> waypoints)
    {
        var left = 0f;
        foreach (var point in waypoints)
        {
            left += Vector3.Distance(from, point);
            from = point;
        }
        return left;
    }

    // Sprint (+30%) replaces the slower Jog and Peloton (+20%), and Jog is what Sprint leaves behind, so neither is a reason to wait
    public static bool SprintNow(bool walking, bool mounted, bool ready, float left)
        => walking && !mounted && ready && left >= ShortestSprintWalk;
}
