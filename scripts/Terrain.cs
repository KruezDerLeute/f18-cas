using System.Collections.Generic;
using Godot;

/// The terrain node in world.tscn, holding the terrain asset (assets/terrain.glb)
/// as its "TerrainMesh" child. Answers gameplay height queries. Gameplay
/// happens on the z = 0 slice, so the height profile is read straight from
/// where the mesh crosses z = 0: edit the asset in Blender and bombs, wheels
/// and vehicles follow your changes.
public partial class Terrain : Node3D
{
    public const string AssetPath = "res://assets/terrain.glb";
    public const float XMin = -5000f;
    public const float XMax = 17000f;
    public const float Step = 10f;
    public const float RunwayX0 = 300f;
    public const float RunwayX1 = 2700f;
    public const float BaseH = 40f;
    public static int Count => (int)((XMax - XMin) / Step) + 1;

    // Collision only for the band around the lane (debris, tossed turrets).
    const float CollisionZMin = -95f;
    const float CollisionZMax = 100f;

    readonly float[] _table = new float[Count];

    /// Ground height on the gameplay lane, linearly interpolated between 10 m samples.
    public float Height(float x)
    {
        float f = (Mathf.Clamp(x, XMin, XMax - Step) - XMin) / Step;
        int i = (int)f;
        return Mathf.Lerp(_table[i], _table[i + 1], f - i);
    }

    public bool OnRunway(float x) => x > RunwayX0 && x < RunwayX1;

    /// Reads the ground heights and builds collision from the "TerrainMesh"
    /// child (the terrain.glb you placed under this node in world.tscn).
    public void Build()
    {
        var scene = GetNodeOrNull<Node3D>("TerrainMesh");
        if (scene == null)
        {
            GD.PushError("Terrain: add assets/terrain.glb as a child named \"TerrainMesh\".");
            return;
        }

        System.Array.Fill(_table, float.NegativeInfinity);
        var collision = new List<Vector3>();
        foreach (var node in scene.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            // glTF import may drop the vertex-colour flag; the terrain is painted with vertex colours.
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.GetActiveMaterial(s) is BaseMaterial3D m)
                    m.VertexColorUseAsAlbedo = true;
            if (!mi.Name.ToString().Contains("Ground"))
                continue;   // trees don't count as ground
            var xf = mi.GlobalTransform;
            var faces = mi.Mesh.GetFaces();
            for (int t = 0; t + 2 < faces.Length; t += 3)
            {
                var a = xf * faces[t];
                var b = xf * faces[t + 1];
                var c = xf * faces[t + 2];
                SampleLane(a, b, c);
                if (InBand(a) && InBand(b) && InBand(c))
                    collision.AddRange(new[] { a, b, c });
            }
        }
        FillGaps();

        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(collision.ToArray());
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(body);
    }

    // Generous margin: imported meshes are vertex-compressed, so rows can shift a few cm.
    static bool InBand(Vector3 v) => v.Z >= CollisionZMin - 1f && v.Z <= CollisionZMax + 1f;

    /// Where triangle abc crosses the plane z = 0 it leaves a line segment; write
    /// that segment's height into every 10 m sample it covers (highest wins).
    void SampleLane(Vector3 a, Vector3 b, Vector3 c)
    {
        System.Span<Vector2> pts = stackalloc Vector2[6];
        int n = 0;
        Cross(a, b, pts, ref n);
        Cross(b, c, pts, ref n);
        Cross(c, a, pts, ref n);
        if (n == 0)
            return;
        var p0 = pts[0];
        var p1 = pts[0];
        for (int i = 1; i < n; i++)
        {
            if (pts[i].X < p0.X) p0 = pts[i];
            if (pts[i].X > p1.X) p1 = pts[i];
        }
        int i0 = Mathf.Max((int)Mathf.Ceil((p0.X - XMin) / Step - 1e-3f), 0);
        int i1 = Mathf.Min((int)Mathf.Floor((p1.X - XMin) / Step + 1e-3f), Count - 1);
        for (int i = i0; i <= i1; i++)
        {
            float x = XMin + i * Step;
            float y = p1.X - p0.X < 1e-4f ? Mathf.Max(p0.Y, p1.Y) : Mathf.Lerp(p0.Y, p1.Y, Mathf.Clamp((x - p0.X) / (p1.X - p0.X), 0f, 1f));
            _table[i] = Mathf.Max(_table[i], y);
        }
    }

    /// Adds edge a-b's crossing of z = 0 (or its end point a, if a lies on the plane).
    static void Cross(Vector3 a, Vector3 b, System.Span<Vector2> pts, ref int n)
    {
        if (Mathf.Abs(a.Z) < 1e-4f)
            pts[n++] = new Vector2(a.X, a.Y);
        else if ((a.Z < 0f && b.Z > 0f) || (a.Z > 0f && b.Z < 0f))
        {
            float t = a.Z / (a.Z - b.Z);
            pts[n++] = new Vector2(Mathf.Lerp(a.X, b.X, t), Mathf.Lerp(a.Y, b.Y, t));
        }
    }

    /// Samples the mesh didn't cover (e.g. you trimmed it in Blender) copy their neighbours.
    void FillGaps()
    {
        float last = BaseH;
        for (int i = 0; i < _table.Length; i++)
        {
            if (float.IsNegativeInfinity(_table[i]))
                _table[i] = last;
            else
                last = _table[i];
        }
    }
}
