using System.Collections.Generic;
using Godot;

/// Loads the game's asset files from res://assets. If a file is missing (not
/// baked yet), the matching generator builds it in memory instead, so the game
/// always runs. Bake everything with:  godot --headless --path . -- --bake-assets
public static class Assets
{
	public const string Root = "res://assets";

	public static string ModelPath(string name) => $"{Root}/models/{name}.glb";
	public static string TexturePath(string name) => $"{Root}/textures/{name}.png";
	public static string SoundPath(string name) => $"{Root}/sounds/{name}.wav";

	static readonly Dictionary<string, PackedScene> Scenes = new();
	static readonly HashSet<string> Warned = new();

	/// A new instance of models/{name}.glb.
	public static Node3D Model(string name)
	{
		if (!Scenes.TryGetValue(name, out var scene))
		{
			string path = ModelPath(name);
			scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
			if (scene == null)
				Missing(path);
			Scenes[name] = scene;
		}
		return scene != null ? scene.Instantiate<Node3D>() : ModelBuilder.Build(name);
	}

	public static Texture2D Texture(string name)
	{
		string path = TexturePath(name);
		if (ResourceLoader.Exists(path))
			return GD.Load<Texture2D>(path);
		Missing(path);
		return ImageTexture.CreateFromImage(TextureBaker.Make(name));
	}

	public static AudioStreamWav Sound(string name)
	{
		string path = SoundPath(name);
		if (ResourceLoader.Exists(path))
			return GD.Load<AudioStreamWav>(path);
		Missing(path);
		return SoundBaker.Make(name);
	}

	static void Missing(string path)
	{
		if (Warned.Add(path))
			GD.PushWarning($"{path} not found - generating it in memory. Bake assets with: godot --headless --path . -- --bake-assets");
	}
}
