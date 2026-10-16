using Godot;

/// Synthesizes every sound effect from noise and sine waves. AssetBaker saves
/// them as assets/sounds/*.wav; replace any file with a recording of your own
/// (keep the name) and the game plays that instead.
public static class SoundBaker
{
	const int SR = 22050;

	/// Every sound and whether it loops. Audio loads them by these names.
	public static readonly (string name, bool loop)[] All =
	{
		("boom", false), ("boom_small", false), ("cannon", false), ("gun", true),
		("aaa", false), ("launch", false), ("flare", false), ("release", false),
		("thump", false), ("hit", false), ("engine", true), ("afterburner", true),
		("wind", true), ("rwr", true), ("missile_warn", true), ("pull_up", true),
		("stall", true), ("lock_tone", true),
	};

	static readonly System.Random Rng = new();

	public static AudioStreamWav Make(string name) => Wav(name switch
	{
		"boom" => Boom(3.2f, 1.6f, 0.9f),
		"boom_small" => Boom(1.4f, 3.5f, 0.7f),
		"cannon" => Boom(1.3f, 4.0f, 1.4f),
		"gun" => Gun(),
		"aaa" => AaaBurst(1.8f, 18f),
		"launch" => Launch(2.6f),
		"flare" => Flare(),
		"release" => Clunk(70f, 0.3f),
		"thump" => Clunk(50f, 0.5f),
		"hit" => Hit(),
		"engine" => Engine(),
		"afterburner" => Afterburner(),
		"wind" => Wind(),
		"rwr" => Tones(0.5f, (0f, 0.06f, 1200f), (0.1f, 0.16f, 1200f)),
		"missile_warn" => Tones(0.16f, (0f, 0.08f, 1600f)),
		"pull_up" => Tones(0.6f, (0f, 0.2f, 700f), (0.25f, 0.45f, 1000f)),
		"stall" => Tones(0.2f, (0f, 0.1f, 420f)),
		"lock_tone" => SineLoop(0.2f, 900f),
		_ => throw new System.ArgumentException($"Unknown sound '{name}'"),
	});

	/// Normalises float samples and packs them into a 16-bit mono WAV stream.
	static AudioStreamWav Wav(float[] samples)
	{
		float peak = 0.0001f;
		foreach (float s in samples)
			peak = Mathf.Max(peak, Mathf.Abs(s));
		float g = 0.9f / peak;
		var bytes = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			short v = (short)(Mathf.Clamp(samples[i] * g, -1f, 1f) * 32767f);
			bytes[i * 2] = (byte)(v & 0xff);
			bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
		}
		return new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = SR,
			Stereo = false,
			Data = bytes,
		};
	}

	static float[] Buf(float dur) => new float[(int)(dur * SR)];

	static float Noise() => (float)(Rng.NextDouble() * 2.0 - 1.0);

	/// Explosion: sharp crack, then layered low-passed noise rolling off.
	static float[] Boom(float dur, float decay, float crack)
	{
		var o = Buf(dur);
		float lp1 = 0f, lp2 = 0f, lp3 = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp1 += 0.12f * (w - lp1);
			lp2 += 0.03f * (w - lp2);
			lp3 += 0.008f * (w - lp3);
			float env = Mathf.Exp(-t * decay) * (1f - Mathf.Exp(-t * 400f));
			float roll = 0.6f + 0.4f * Mathf.Sin(t * 7f + Mathf.Sin(t * 3f) * 2f);
			o[i] = (lp1 * 0.8f + lp2 * 2.5f + lp3 * 9f * roll) * env + w * crack * Mathf.Exp(-t * 40f);
		}
		return o;
	}

	/// M61 Vulcan: 100 rounds/s blur into a tearing "brrrt" buzz.
	static float[] Gun()
	{
		const int period = 220;
		var o = new float[period * 50];
		float lp = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float tl = (float)(i % period) / SR;
			float w = Noise();
			lp += 0.25f * (w - lp);
			o[i] = lp * Mathf.Exp(-tl * 250f) * 1.5f + Mathf.Sin(Mathf.Tau * 90f * tl) * Mathf.Exp(-tl * 120f) * 0.6f + w * 0.05f;
		}
		return o;
	}

	/// ZSU-23 burst: individual pops at the given rate.
	static float[] AaaBurst(float dur, float rate)
	{
		var o = Buf(dur);
		int period = (int)(SR / rate);
		float lp = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float tl = (float)(i % period) / SR;
			float w = Noise();
			lp += 0.2f * (w - lp);
			float fade = Mathf.Clamp((dur - (float)i / SR) / 0.1f, 0f, 1f);
			o[i] = (lp * Mathf.Exp(-tl * 70f) + Mathf.Sin(Mathf.Tau * 70f * tl) * Mathf.Exp(-tl * 50f) * 0.5f) * fade;
		}
		return o;
	}

	/// Rocket motor ignition and roar.
	static float[] Launch(float dur)
	{
		var o = Buf(dur);
		float lp1 = 0f, lp2 = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp1 += 0.3f * (w - lp1);
			lp2 += 0.05f * (w - lp2);
			float env = Mathf.Clamp(t / 0.08f, 0f, 1f) * Mathf.Exp(-t * 0.9f);
			o[i] = ((w - lp1) * 0.4f + lp1 * 0.8f + lp2 * 2.2f) * env + w * Mathf.Exp(-t * 30f) * 0.8f;
		}
		return o;
	}

	static float[] Flare()
	{
		var o = Buf(0.6f);
		float lp = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp += 0.4f * (w - lp);
			o[i] = w * Mathf.Exp(-t * 50f) + (w - lp) * Mathf.Exp(-t * 5f) * 0.35f;
		}
		return o;
	}

	static float[] Clunk(float freq, float dur)
	{
		var o = Buf(dur);
		float lp = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp += 0.15f * (w - lp);
			o[i] = Mathf.Sin(Mathf.Tau * freq * t) * Mathf.Exp(-t * 18f) + lp * Mathf.Exp(-t * 40f) * 0.8f;
		}
		return o;
	}

	/// Shrapnel hitting the airframe: noise burst plus a metallic ring.
	static float[] Hit()
	{
		var o = Buf(0.45f);
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			float ring = Mathf.Sin(Mathf.Tau * 1300f * t) + Mathf.Sin(Mathf.Tau * 2110f * t) * 0.7f + Mathf.Sin(Mathf.Tau * 3370f * t) * 0.5f;
			o[i] = w * Mathf.Exp(-t * 30f) + ring * Mathf.Exp(-t * 12f) * 0.25f;
		}
		return o;
	}

	/// Turbine whine (whole cycles so the loop is seamless) over a broadband hiss.
	static float[] Engine()
	{
		var o = Buf(2f);
		float lp = 0f, lp2 = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp += 0.08f * (w - lp);
			lp2 += 0.01f * (w - lp2);
			o[i] = Mathf.Sin(Mathf.Tau * 440f * t) * 0.12f + Mathf.Sin(Mathf.Tau * 880f * t) * 0.05f + lp * 0.9f + lp2 * 3f;
		}
		return o;
	}

	/// Afterburner: deep rumble with random crackle.
	static float[] Afterburner()
	{
		var o = Buf(2f);
		float lp1 = 0f, lp2 = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float w = Noise();
			lp1 += 0.04f * (w - lp1);
			lp2 += 0.006f * (w - lp2);
			float crackle = Rng.NextDouble() < 0.004 ? w * 0.6f : 0f;
			o[i] = lp1 * 1.5f + lp2 * 9f + crackle;
		}
		return o;
	}

	static float[] Wind()
	{
		var o = Buf(2f);
		float lp1 = 0f, lp2 = 0f;
		for (int i = 0; i < o.Length; i++)
		{
			float t = (float)i / SR;
			float w = Noise();
			lp1 += 0.15f * (w - lp1);
			lp2 += 0.02f * (w - lp2);
			o[i] = (lp1 * 0.6f + lp2 * 2f) * (0.85f + 0.15f * Mathf.Sin(Mathf.Tau * 0.5f * t));
		}
		return o;
	}

	/// Beep pattern: each segment is (start s, end s, frequency Hz) within one loop period.
	static float[] Tones(float period, params (float start, float end, float freq)[] segs)
	{
		var o = Buf(period);
		foreach (var sg in segs)
		{
			int a = (int)(sg.start * SR);
			int b = Mathf.Min((int)(sg.end * SR), o.Length);
			for (int i = a; i < b; i++)
			{
				float tl = (float)(i - a) / SR;
				float env = Mathf.Min(1f, Mathf.Min(tl / 0.005f, (float)(b - i) / SR / 0.005f));  // de-click
				o[i] += (Mathf.Sin(Mathf.Tau * sg.freq * tl) + 0.3f * Mathf.Sin(Mathf.Tau * sg.freq * 2f * tl)) * env;
			}
		}
		return o;
	}

	static float[] SineLoop(float dur, float f)
	{
		var o = Buf(dur);
		for (int i = 0; i < o.Length; i++)
			o[i] = Mathf.Sin(Mathf.Tau * f * i / SR);
		return o;
	}
}
