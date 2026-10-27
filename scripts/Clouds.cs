using Godot;

/// Fills the sky behind the lane with soft billboard clouds when the game
/// starts. Put it on a Node3D in world.tscn and set the texture in the Inspector.
public partial class Clouds : Node3D
{
    [Export] public Texture2D Texture;
    /// Number of cloud clusters (each is 5 overlapping quads).
    [Export] public int Count = 70;
    /// Same seed = same sky every time.
    [Export] public int Seed = 5;
    /// Thin low wisps right around the lane, at the heights you fly: close
    /// enough to whip past (or punch through), which shows off your speed.
    [Export] public int WispCount = 90;
    [Export] public Vector2 WispHeight = new(250f, 1600f);

    public override void _Ready()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = Texture,
            AlbedoColor = new Color(1, 1, 1, 0.55f),
        };
        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        for (int i = 0; i < Count; i++)
        {
            float cx = rng.RandfRange(-4000f, 16000f);
            float cy = rng.RandfRange(1400f, 3200f);
            float cz = rng.RandfRange(-4500f, -600f);
            for (int k = 0; k < 5; k++)
            {
                float s = rng.RandfRange(250f, 600f);
                AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(s, s * 0.6f) },
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Position = new Vector3(cx + rng.RandfRange(-350, 350), cy + rng.RandfRange(-60, 90), cz + rng.RandfRange(-100, 100)),
                });
            }
        }

        var wispMat = (StandardMaterial3D)mat.Duplicate();
        wispMat.AlbedoColor = new Color(1, 1, 1, 0.3f);
        for (int i = 0; i < WispCount; i++)
        {
            float cx = rng.RandfRange(-4000f, 16000f);
            float cy = rng.RandfRange(WispHeight.X, WispHeight.Y);
            float cz = rng.RandfRange(-90f, 40f);
            for (int k = 0; k < 3; k++)
            {
                float s = rng.RandfRange(70f, 180f);
                AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(s, s * 0.45f) },
                    MaterialOverride = wispMat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Position = new Vector3(cx + rng.RandfRange(-120, 120), cy + rng.RandfRange(-25, 25), cz + rng.RandfRange(-20, 20)),
                });
            }
        }
    }
}
