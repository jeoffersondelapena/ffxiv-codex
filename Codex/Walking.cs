using System.Numerics;

namespace Codex;

public static class Walking
{
    public const uint SprintAction = 4;
    public const uint SprintStatus = 50, PelotonStatus = 1199, JogStatus = 4209;
    public static readonly uint[] AlreadyFaster = [SprintStatus, PelotonStatus, JogStatus];

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

    public static bool SprintNow(bool walking, bool mounted, bool faster, bool ready, float left)
        => walking && !mounted && !faster && ready && left >= ShortestSprintWalk;
}
