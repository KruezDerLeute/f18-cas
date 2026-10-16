using Godot;

/// Small helpers for building primitive meshes and materials in code.
public static class Fx
{
	/// Rotation that turns a Y-axis cylinder so it points along +X.
	public static readonly Vector3 AlongX = new(0, 0, -Mathf.Pi / 2);

	public static float Rand(float a, float b) => (float)GD.RandRange(a, b);

	/// Godot vectors are structs, so `node.Rotation.Z = x` doesn't compile.
	public static void SetRotZ(this Node3D n, float z)
	{
		var r = n.Rotation;
		r.Z = z;
		n.Rotation = r;
	}

	public static void SetRotY(this Node3D n, float y)
	{
		var r = n.Rotation;
		r.Y = y;
		n.Rotation = r;
	}

	public static StandardMaterial3D Mat(Color c, float rough = 0.85f, float metal = 0f) => new()
	{
		AlbedoColor = c,
		Roughness = rough,
		Metallic = metal,
	};

	public static StandardMaterial3D Glow(Color c, float energy = 3f) => new()
	{
		AlbedoColor = Colors.Black,
		EmissionEnabled = true,
		Emission = c,
		EmissionEnergyMultiplier = energy,
	};

	public static StandardMaterial3D Additive(Color c) => new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		BlendMode = BaseMaterial3D.BlendModeEnum.Add,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		AlbedoColor = c,
	};

	public static MeshInstance3D Box(Vector3 size, Material m, Vector3 p = default, Vector3 rot = default) => new()
	{
		Mesh = new BoxMesh { Size = size },
		MaterialOverride = m,
		Position = p,
		Rotation = rot,
	};

	/// Cylinder along local Y. Use rot = AlongX to point it along +X.
	public static MeshInstance3D Cyl(float rTop, float rBot, float h, Material m, Vector3 p = default,
			Vector3 rot = default, int segs = 12) => new()
	{
		Mesh = new CylinderMesh { TopRadius = rTop, BottomRadius = rBot, Height = h, RadialSegments = segs, Rings = 1 },
		MaterialOverride = m,
		Position = p,
		Rotation = rot,
	};

	public static MeshInstance3D Sphere(float r, Material m, Vector3 p = default) => new()
	{
		Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 12, Rings = 6 },
		MaterialOverride = m,
		Position = p,
	};

	public static Gradient Ramp(float[] offsets, Color[] colors) => new()
	{
		Offsets = offsets,
		Colors = colors,
	};
}
