using Godot;

/// Generates the particle and cloud textures. AssetBaker saves them as
/// assets/textures/*.png; you can repaint those in any image editor.
public static class TextureBaker
{
	public static readonly string[] Names = { "particle_hard", "particle_soft", "cloud" };

	public static Image Make(string name) => name switch
	{
		"particle_hard" => Radial(0f),     // fire, sparks, flares
		"particle_soft" => Radial(0.45f),  // smoke, dust, vapour
		"cloud" => Radial(0.35f),
		_ => throw new System.ArgumentException($"Unknown texture '{name}'"),
	};

	/// White dot fading to transparent at the edge. `soft` > 0 holds the alpha up
	/// at 0.55 until that radius for a fluffier puff.
	static Image Radial(float soft)
	{
		const int size = 64;
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float u = (x + 0.5f) / size - 0.5f;
				float v = (y + 0.5f) / size - 0.5f;
				float t = Mathf.Clamp(Mathf.Sqrt(u * u + v * v) / 0.5f, 0f, 1f);
				float a;
				if (soft <= 0f)
					a = 1f - t;
				else if (t < soft)
					a = Mathf.Lerp(1f, 0.55f, t / soft);
				else
					a = Mathf.Lerp(0.55f, 0f, (t - soft) / (1f - soft));
				img.SetPixel(x, y, new Color(1, 1, 1, a));
			}
		}
		return img;
	}
}
