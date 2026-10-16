using System.Collections.Generic;
using Godot;

/// Generates the terrain once and saves it as a glTF asset (assets/terrain.glb).
/// The game itself only loads that file, so you can open it in Blender, sculpt
/// it, repaint it and re-export: gameplay heights are read back from the mesh.
///
/// Baked together with everything else by AssetBaker (--bake-assets).
public static class TerrainBaker
{
	// Z positions of the mesh rows: dense near the lane, sparse far away.
	static readonly float[] Rows =
	{
		-7000f, -4800f, -3300f, -2300f, -1600f, -1100f, -760f, -520f,
		-350f, -230f, -150f, -95f, -55f, -28f, -12f, 0f, 12f, 28f, 55f,
		100f, 180f, 320f, 600f, 1100f, 2000f, 3400f,
	};

	const float XMin = Terrain.XMin;
	const float XMax = Terrain.XMax;
	const float Step = Terrain.Step;
	const float RunwayX0 = Terrain.RunwayX0;
	const float RunwayX1 = Terrain.RunwayX1;
	const float BaseH = Terrain.BaseH;

	static readonly FastNoiseLite Big = new() { Seed = 1337, Frequency = 0.00045f, FractalOctaves = 3 };
	static readonly FastNoiseLite Small = new() { Seed = 4242, Frequency = 0.004f, FractalOctaves = 2 };
	static readonly FastNoiseLite Hills = new() { Seed = 777, Frequency = 0.0008f, FractalOctaves = 4 };
	static readonly FastNoiseLite ColorNoise = new() { Seed = 99, Frequency = 0.003f };
	static float[] _lane;

	/// Writes the terrain to `path` (a res:// path) as a .glb file.
	public static Error Bake(string path) => AssetBaker.ExportGlb(BuildScene(), path);

	/// The terrain as a node tree: "Ground" (lane + hills) and "Trees".
	public static Node3D BuildScene()
	{
		BuildLaneTable();
		var root = new Node3D { Name = "Terrain" };
		root.AddChild(new MeshInstance3D { Name = "Ground", Mesh = BuildGround() });
		root.AddChild(new MeshInstance3D { Name = "Trees", Mesh = BuildTrees() });
		return root;
	}

	// ---------------------------------------------------------------- height functions

	/// 1 inside [a, b], smoothly falling to 0 over `fade` metres outside it.
	static float Band(float x, float a, float b, float fade)
	{
		float t = Mathf.Clamp(Mathf.Min(x - (a - fade), (b + fade) - x) / fade, 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	static float RawHeight(float x)
	{
		float h = BaseH + 80f + Big.GetNoise1D(x) * 260f + Small.GetNoise1D(x) * 14f;
		float gentle = BaseH + 25f + Big.GetNoise1D(x) * 70f + Small.GetNoise1D(x) * 9f;
		h = Mathf.Lerp(h, gentle, Band(x, 2900f, 12500f, 900f));                     // flatter battlefield
		h = Mathf.Lerp(h, BaseH, Band(x, RunwayX0 - 200f, RunwayX1 + 200f, 600f));   // flat airbase
		return Mathf.Max(h, 3f);
	}

	static void BuildLaneTable()
	{
		_lane = new float[Terrain.Count];
		for (int i = 0; i < _lane.Length; i++)
			_lane[i] = RawHeight(XMin + i * Step);
	}

	static float LaneHeight(float x)
	{
		float f = (Mathf.Clamp(x, XMin, XMax - Step) - XMin) / Step;
		int i = (int)f;
		return Mathf.Lerp(_lane[i], _lane[i + 1], f - i);
	}

	/// Height anywhere on the strip: the lane profile plus hills behind it.
	static float Height3(float x, float z)
	{
		float h = LaneHeight(x);
		// Keep the airbase apron and hangar area flat.
		float rough = 1f - Band(x, RunwayX0 - 200f, RunwayX1 + 200f, 600f) * (1f - Mathf.Clamp((-z - 250f) / 300f, 0f, 1f));
		if (z < 0f)
		{
			float t = Mathf.Clamp((-z - 25f) / 1100f, 0f, 1f);
			float hills = Hills.GetNoise2D(x, z) * 0.5f + 0.5f;
			h += t * t * hills * 1100f * rough + Mathf.Min(-z / 60f, 1f) * Small.GetNoise2D(x, z) * 12f * rough;
		}
		else
		{
			float t = Mathf.Clamp((z - 25f) / 300f, 0f, 1f);
			h += -t * 28f + Mathf.Min(z / 60f, 1f) * Small.GetNoise2D(x, z) * 6f * rough;
		}
		return h;
	}

	static Color GroundColor(float x, float z, float h, float slope)
	{
		var grass = new Color(0.30f, 0.40f, 0.18f);
		var dry = new Color(0.50f, 0.46f, 0.28f);
		var dirt = new Color(0.42f, 0.34f, 0.24f);
		var rock = new Color(0.40f, 0.38f, 0.36f);
		var snow = new Color(0.92f, 0.93f, 0.96f);
		var c = grass.Lerp(dry, Mathf.Clamp(ColorNoise.GetNoise2D(x, z) * 0.8f + 0.4f, 0f, 1f));
		if (Mathf.Abs(z) < 14f && x > 2800f)
			c = c.Lerp(dirt, 0.65f);  // the road the enemy column drives along
		c = c.Lerp(rock, Mathf.Clamp((slope - 0.35f) * 2f, 0f, 1f));
		c = c.Lerp(rock, Mathf.Clamp((h - 500f) / 250f, 0f, 1f));
		c = c.Lerp(snow, Mathf.Clamp((h - 850f) / 120f, 0f, 1f));
		return c;
	}

	// ---------------------------------------------------------------- meshes

	static ArrayMesh BuildGround()
	{
		int cols = Terrain.Count;
		int rows = Rows.Length;
		var verts = new Vector3[cols * rows];
		var normals = new Vector3[cols * rows];
		var colors = new Color[cols * rows];
		var idx = new List<int>();
		for (int j = 0; j < rows; j++)
		{
			float z = Rows[j];
			for (int i = 0; i < cols; i++)
			{
				float x = XMin + i * Step;
				float h = Height3(x, z);
				const float e = 4f;
				float dx = (Height3(x + e, z) - Height3(x - e, z)) / (2f * e);
				float dz = (Height3(x, z + e) - Height3(x, z - e)) / (2f * e);
				var n = new Vector3(-dx, 1f, -dz).Normalized();
				int k = j * cols + i;
				verts[k] = new Vector3(x, h, z);
				normals[k] = n;
				colors[k] = GroundColor(x, z, h, 1f - n.Y);
			}
		}
		for (int j = 0; j < rows - 1; j++)
		{
			for (int i = 0; i < cols - 1; i++)
			{
				int a = j * cols + i;
				int b = a + 1;
				int c = a + cols;
				int d = c + 1;
				idx.AddRange(new[] { a, c, b, b, c, d });
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, new StandardMaterial3D
		{
			ResourceName = "Ground",
			VertexColorUseAsAlbedo = true,
			Roughness = 1f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		});
		return mesh;
	}

	/// About 3500 low-poly cone trees merged into one mesh (one draw call).
	static ArrayMesh BuildTrees()
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var rng = new RandomNumberGenerator { Seed = 2024 };
		int count = 0;
		int tries = 0;
		const int segs = 6;
		while (count < 3500 && tries < 40000)
		{
			tries++;
			float x = rng.RandfRange(XMin + 200f, XMax - 200f);
			float z = rng.RandfRange(-900f, 350f);
			if (Mathf.Abs(z) < 30f)
				continue;
			if (x > RunwayX0 - 300f && x < RunwayX1 + 300f && z > -220f && z < 80f)
				continue;
			if (ColorNoise.GetNoise2D(x * 0.7f, z * 0.7f) < -0.05f)
				continue;  // clearings
			float h = Height3(x, z);
			if (h > 650f)
				continue;  // tree line
			float s = rng.RandfRange(7f, 15f);
			var col = new Color(0.13f, 0.25f, 0.12f).Lerp(new Color(0.22f, 0.32f, 0.14f), rng.Randf());
			var apex = new Vector3(x, h + s * 0.95f, z);
			float r = 0.38f * s * 0.55f;
			float baseY = h - s * 0.05f;
			for (int k = 0; k < segs; k++)
			{
				float a0 = Mathf.Tau * k / segs;
				float a1 = Mathf.Tau * (k + 1) / segs;
				st.SetColor(col);
				st.AddVertex(apex);
				st.AddVertex(new Vector3(x + Mathf.Cos(a1) * r, baseY, z + Mathf.Sin(a1) * r));
				st.AddVertex(new Vector3(x + Mathf.Cos(a0) * r, baseY, z + Mathf.Sin(a0) * r));
			}
			count++;
		}
		st.GenerateNormals();
		st.SetMaterial(new StandardMaterial3D
		{
			ResourceName = "Trees",
			VertexColorUseAsAlbedo = true,
			Roughness = 1f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		});
		return st.Commit();
	}
}
