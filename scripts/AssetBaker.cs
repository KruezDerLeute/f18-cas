using Godot;

/// Runs every generator once and writes the results into res://assets:
///   terrain.glb, models/*.glb, textures/*.png and sounds/*.wav.
/// After baking, the game only loads those files - edit them in Blender, an
/// image editor or Audacity and your changes show up in game.
///
/// Run:  godot --headless --path . -- --bake-assets
/// (then open the editor, or run with --import, so Godot imports the new files)
public static class AssetBaker
{
	public static bool BakeAll()
	{
		bool ok = Report(Terrain.AssetPath, TerrainBaker.Bake(Terrain.AssetPath));
		foreach (var name in ModelBuilder.Names)
			ok &= Report(Assets.ModelPath(name), ExportGlb(ModelBuilder.Build(name), Assets.ModelPath(name)));
		foreach (var name in TextureBaker.Names)
		{
			string path = Assets.TexturePath(name);
			MakeDir(path);
			ok &= Report(path, TextureBaker.Make(name).SavePng(ProjectSettings.GlobalizePath(path)));
		}
		foreach (var (name, _) in SoundBaker.All)
		{
			string path = Assets.SoundPath(name);
			MakeDir(path);
			ok &= Report(path, SoundBaker.Make(name).SaveToWav(ProjectSettings.GlobalizePath(path)));
		}
		return ok;
	}

	/// Saves a node tree as a binary glTF file, then frees it.
	public static Error ExportGlb(Node3D root, string path)
	{
		// glTF stores materials on the mesh, so move MaterialOverride onto the primitives.
		foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)node;
			if (mi.MaterialOverride != null && mi.Mesh is PrimitiveMesh pm)
			{
				pm.Material = mi.MaterialOverride;
				mi.MaterialOverride = null;
			}
		}
		MakeDir(path);
		var doc = new GltfDocument();
		var state = new GltfState();
		var err = doc.AppendFromScene(root, state);
		if (err == Error.Ok)
			err = doc.WriteToFilesystem(state, ProjectSettings.GlobalizePath(path));
		root.Free();
		return err;
	}

	static void MakeDir(string path) =>
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));

	static bool Report(string path, Error err)
	{
		GD.Print(err == Error.Ok ? $"  baked {path}" : $"  FAILED {path}: {err}");
		return err == Error.Ok;
	}
}
