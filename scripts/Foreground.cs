using Godot;

/// Scatters trees, bushes and rocks between the camera and the lane (positive
/// z), so there is something close by to rush past: they cross the screen much
/// faster than the ground behind them, which sells the jet's speed. Put it on a
/// Node3D named "Foreground" in world.tscn; Game calls Build once the terrain exists.
public partial class Foreground : Node3D
{
    /// Number of props.
    [Export] public int Count = 450;
    /// Same seed = same layout every time.
    [Export] public int Seed = 11;
    /// Props go between these distances in front of the lane (towards the camera).
    [Export] public float ZMin = 35f;
    [Export] public float ZMax = 95f;
    /// No props over this x range (the airbase and runway).
    [Export] public Vector2 KeepClear = new(0f, 3000f);

    public void Build(Terrain terrain)
    {
        var trunkMat = Fx.Mat(new Color(0.24f, 0.17f, 0.11f));
        var leafMat = Fx.Mat(new Color(0.16f, 0.27f, 0.13f));
        var bushMat = Fx.Mat(new Color(0.22f, 0.32f, 0.16f));
        var rockMat = Fx.Mat(new Color(0.42f, 0.40f, 0.37f));
        var trunk = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.4f, Height = 1f, RadialSegments = 6, Rings = 1 };
        var cone = new CylinderMesh { TopRadius = 0f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1 };
        var blob = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 4 };

        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        for (int i = 0; i < Count; i++)
        {
            float x = rng.RandfRange(Terrain.XMin + 200f, Terrain.XMax - 200f);
            if (x > KeepClear.X && x < KeepClear.Y)
                continue;
            float z = rng.RandfRange(ZMin, ZMax);
            var at = new Vector3(x, terrain.HeightAt(x, z), z);
            float roll = rng.Randf();
            if (roll < 0.5f)
            {
                // Conifer: trunk and two stacked cones.
                float h = rng.RandfRange(6f, 12f);
                Add(trunk, trunkMat, at + new Vector3(0, h * 0.15f, 0), new Vector3(1, h * 0.3f, 1));
                Add(cone, leafMat, at + new Vector3(0, h * 0.5f, 0), new Vector3(h * 0.28f, h * 0.55f, h * 0.28f));
                Add(cone, leafMat, at + new Vector3(0, h * 0.78f, 0), new Vector3(h * 0.2f, h * 0.4f, h * 0.2f));
            }
            else if (roll < 0.8f)
            {
                float r = rng.RandfRange(1f, 2.4f);
                Add(blob, bushMat, at + new Vector3(0, r * 0.4f, 0), new Vector3(r * 1.3f, r * 0.8f, r));
            }
            else
            {
                float r = rng.RandfRange(0.8f, 2.2f);
                var rock = Add(blob, rockMat, at + new Vector3(0, r * 0.2f, 0), new Vector3(r * 1.4f, r * 0.6f, r));
                rock.Rotation = new Vector3(0, rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(-0.2f, 0.2f));
            }
        }
    }

    MeshInstance3D Add(Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Scale = scale };
        AddChild(m);
        return m;
    }
}
