using Godot;

/// Builds the game's 3D models from boxes and cylinders. AssetBaker saves them
/// as assets/models/*.glb; after that the game loads the files (Assets.Model)
/// and this code only runs again when you re-bake.
///
/// Conventions: noses and vehicle fronts point along +X, vehicles sit on y = 0,
/// and nodes the game animates are named: "Turret" (turns around Y),
/// "GunPivot" (elevates around Z) and "Missile0".."Missile3" on the SA-8.
public static class ModelBuilder
{
	public static readonly string[] Names =
	{
		"mk82", "agm65", "sa8_missile",
		"t72", "m1a2", "truck", "zsu23", "sa8",
		"airbase", "sandbag",
	};

	public static Node3D Build(string name)
	{
		Node3D n = name switch
		{
			"mk82" => Mk82(),
			"agm65" => Agm65(),
			"sa8_missile" => Sa8Missile(),
			"t72" => Tank(new Color(0.30f, 0.33f, 0.23f), false),
			"m1a2" => Tank(new Color(0.58f, 0.52f, 0.36f), true),
			"truck" => Truck(),
			"zsu23" => Zsu23(),
			"sa8" => Sa8(),
			"airbase" => Airbase(),
			"sandbag" => Sandbag(),
			_ => throw new System.ArgumentException($"Unknown model '{name}'"),
		};
		n.Name = name;
		return n;
	}

	/// Adds a child with a readable unique name ("Wheel", "Wheel2", ...) for Blender.
	static T Add<T>(Node parent, string name, T child) where T : Node
	{
		child.Name = name;
		parent.AddChild(child, true);
		return child;
	}

	static readonly Vector3 Fin45 = new(Mathf.Pi / 4, 0, 0);
	static readonly Vector3 FinNeg45 = new(-Mathf.Pi / 4, 0, 0);
	static readonly Vector3 Axle = new(Mathf.Pi / 2, 0, 0);

	// ---------------------------------------------------------------- weapons

	/// Mk-82 500 lb low-drag bomb, ~2.2 m long.
	static Node3D Mk82()
	{
		var n = new Node3D();
		var body = Fx.Mat(new Color(0.33f, 0.36f, 0.25f), 0.6f);
		var dark = Fx.Mat(new Color(0.15f, 0.15f, 0.13f));
		Add(n, "Body", Fx.Cyl(0.14f, 0.14f, 1.5f, body, new Vector3(0.1f, 0, 0), Fx.AlongX));
		Add(n, "Nose", Fx.Cyl(0.02f, 0.14f, 0.55f, body, new Vector3(1.12f, 0, 0), Fx.AlongX));
		Add(n, "Tail", Fx.Cyl(0.14f, 0.07f, 0.4f, body, new Vector3(-0.85f, 0, 0), Fx.AlongX));
		Add(n, "Fin", Fx.Box(new Vector3(0.35f, 0.55f, 0.02f), dark, new Vector3(-0.95f, 0, 0), Fin45));
		Add(n, "Fin", Fx.Box(new Vector3(0.35f, 0.55f, 0.02f), dark, new Vector3(-0.95f, 0, 0), FinNeg45));
		Add(n, "Band", Fx.Box(new Vector3(0.08f, 0.05f, 0.3f), Fx.Mat(new Color(0.8f, 0.7f, 0.1f)), new Vector3(0.75f, 0, 0)));
		return n;
	}

	/// AGM-65 Maverick, ~2.5 m long.
	static Node3D Agm65()
	{
		var n = new Node3D();
		var body = Fx.Mat(new Color(0.75f, 0.76f, 0.74f), 0.5f);
		var dark = Fx.Mat(new Color(0.25f, 0.25f, 0.25f));
		Add(n, "Body", Fx.Cyl(0.15f, 0.15f, 2.2f, body, Vector3.Zero, Fx.AlongX));
		Add(n, "Seeker", Fx.Sphere(0.15f, Fx.Mat(new Color(0.1f, 0.1f, 0.15f), 0.1f, 0.5f), new Vector3(1.1f, 0, 0)));
		Add(n, "Wing", Fx.Box(new Vector3(0.9f, 0.7f, 0.02f), dark, new Vector3(-0.2f, 0, 0), Fin45));
		Add(n, "Wing", Fx.Box(new Vector3(0.9f, 0.7f, 0.02f), dark, new Vector3(-0.2f, 0, 0), FinNeg45));
		Add(n, "Fin", Fx.Box(new Vector3(0.25f, 0.55f, 0.02f), dark, new Vector3(-1.0f, 0, 0), Fin45));
		Add(n, "Fin", Fx.Box(new Vector3(0.25f, 0.55f, 0.02f), dark, new Vector3(-1.0f, 0, 0), FinNeg45));
		return n;
	}

	/// SA-8 style surface-to-air missile, ~3.2 m long.
	static Node3D Sa8Missile()
	{
		var n = new Node3D();
		var body = Fx.Mat(new Color(0.85f, 0.85f, 0.8f), 0.5f);
		var dark = Fx.Mat(new Color(0.3f, 0.32f, 0.28f));
		Add(n, "Body", Fx.Cyl(0.11f, 0.11f, 2.8f, body, Vector3.Zero, Fx.AlongX));
		Add(n, "Nose", Fx.Cyl(0.01f, 0.11f, 0.45f, body, new Vector3(1.62f, 0, 0), Fx.AlongX));
		Add(n, "Canard", Fx.Box(new Vector3(0.3f, 0.5f, 0.02f), dark, new Vector3(0.9f, 0, 0), Fin45));
		Add(n, "Canard", Fx.Box(new Vector3(0.3f, 0.5f, 0.02f), dark, new Vector3(0.9f, 0, 0), FinNeg45));
		Add(n, "Fin", Fx.Box(new Vector3(0.45f, 0.7f, 0.02f), dark, new Vector3(-1.2f, 0, 0), Fin45));
		Add(n, "Fin", Fx.Box(new Vector3(0.45f, 0.7f, 0.02f), dark, new Vector3(-1.2f, 0, 0), FinNeg45));
		return n;
	}

	// ---------------------------------------------------------------- vehicles

	/// T-72 (eastern) or M1A2 (western) main battle tank.
	static Node3D Tank(Color c, bool western)
	{
		var n = new Node3D();
		var hull = Fx.Mat(c);
		var dark = Fx.Mat(new Color(0.11f, 0.11f, 0.10f));
		float lng = western ? 7.8f : 6.9f;
		Add(n, "Hull", Fx.Box(new Vector3(lng, 1.2f, 3.5f), hull, new Vector3(0, 1.3f, 0)));
		Add(n, "Glacis", Fx.Box(new Vector3(1.4f, 0.8f, 3.4f), hull, new Vector3(lng * 0.5f, 1.2f, 0), new Vector3(0, 0, -0.55f)));
		foreach (float s in new[] { -1f, 1f })
		{
			Add(n, "Track", Fx.Box(new Vector3(lng + 0.4f, 1.05f, 0.75f), dark, new Vector3(0, 0.55f, 1.45f * s)));
			for (int i = 0; i < 5; i++)
				Add(n, "Wheel", Fx.Cyl(0.42f, 0.42f, 0.8f, dark, new Vector3(-lng * 0.4f + i * lng * 0.2f, 0.45f, 1.5f * s), Axle, 10));
		}
		var turret = Add(n, "Turret", new Node3D { Position = new Vector3(-0.4f, 2.2f, 0) });
		if (western)
		{
			Add(turret, "TurretBody", Fx.Box(new Vector3(3.8f, 0.9f, 3.0f), hull));
			Add(turret, "Bustle", Fx.Box(new Vector3(1.2f, 0.7f, 2.6f), hull, new Vector3(-2.3f, 0.05f, 0)));
			Add(turret, "Sight", Fx.Box(new Vector3(0.5f, 0.4f, 0.5f), dark, new Vector3(-0.5f, 0.65f, 0.8f)));
			// IR identification panel so friendlies are easy to spot.
			Add(turret, "IdPanel", Fx.Box(new Vector3(0.6f, 0.6f, 0.06f), Fx.Glow(new Color(0.2f, 1.0f, 0.3f), 1.5f), new Vector3(-1.2f, 0.2f, 1.53f)));
		}
		else
		{
			Add(turret, "TurretBody", Fx.Cyl(1.2f, 1.6f, 0.9f, hull, Vector3.Zero, Vector3.Zero, 14));
			Add(turret, "Marking", Fx.Box(new Vector3(0.7f, 0.12f, 0.7f), Fx.Mat(new Color(0.6f, 0.1f, 0.1f)), new Vector3(-0.3f, 0.5f, 0)));
		}
		var gun = Add(turret, "GunPivot", new Node3D { Position = new Vector3(1.4f, 0.1f, 0) });
		Add(gun, "Barrel", Fx.Cyl(0.11f, 0.15f, 5.0f, hull, new Vector3(2.5f, 0, 0), Fx.AlongX, 8));
		return n;
	}

	static Node3D Truck()
	{
		var n = new Node3D();
		var c = Fx.Mat(new Color(0.34f, 0.36f, 0.26f));
		var dark = Fx.Mat(new Color(0.1f, 0.1f, 0.1f));
		var canvas = Fx.Mat(new Color(0.40f, 0.40f, 0.30f));
		Add(n, "Cab", Fx.Box(new Vector3(2.0f, 2.0f, 2.4f), c, new Vector3(2.4f, 1.9f, 0)));
		Add(n, "Windscreen", Fx.Box(new Vector3(0.1f, 0.8f, 2.0f), Fx.Mat(new Color(0.2f, 0.25f, 0.3f), 0.2f, 0.4f), new Vector3(3.42f, 2.3f, 0)));
		Add(n, "Chassis", Fx.Box(new Vector3(6.6f, 0.5f, 2.4f), c, new Vector3(0, 1.0f, 0)));
		Add(n, "Canvas", Fx.Box(new Vector3(4.2f, 1.9f, 2.4f), canvas, new Vector3(-1.1f, 2.15f, 0)));
		foreach (float wx in new[] { 2.4f, -0.6f, -2.4f })
			foreach (float s in new[] { -1f, 1f })
				Add(n, "Wheel", Fx.Cyl(0.55f, 0.55f, 0.4f, dark, new Vector3(wx, 0.55f, 1.1f * s), Axle, 10));
		return n;
	}

	/// ZSU-23-4 "Shilka" self-propelled AAA.
	static Node3D Zsu23()
	{
		var n = new Node3D();
		var c = Fx.Mat(new Color(0.33f, 0.35f, 0.26f));
		var dark = Fx.Mat(new Color(0.11f, 0.11f, 0.10f));
		Add(n, "Hull", Fx.Box(new Vector3(6.4f, 1.2f, 3.1f), c, new Vector3(0, 1.2f, 0)));
		foreach (float s in new[] { -1f, 1f })
			Add(n, "Track", Fx.Box(new Vector3(6.6f, 0.95f, 0.65f), dark, new Vector3(0, 0.5f, 1.3f * s)));
		var turret = Add(n, "Turret", new Node3D { Position = new Vector3(0.2f, 2.25f, 0) });
		Add(turret, "TurretBody", Fx.Box(new Vector3(3.6f, 1.2f, 2.9f), c));
		Add(turret, "RadarMast", Fx.Cyl(0.05f, 0.05f, 1.2f, dark, new Vector3(-1.4f, 1.1f, 0)));
		Add(turret, "RadarDish", Fx.Cyl(0.7f, 0.7f, 0.12f, dark, new Vector3(-1.4f, 1.75f, 0), new Vector3(0, 0, 1.1f), 14));
		var gun = Add(turret, "GunPivot", new Node3D { Position = new Vector3(1.3f, 0.2f, 0) });
		foreach (float yy in new[] { -0.18f, 0.18f })
			foreach (float zz in new[] { -0.9f, 0.9f })
				Add(gun, "Barrel", Fx.Cyl(0.05f, 0.06f, 2.6f, dark, new Vector3(1.3f, yy, zz), Fx.AlongX, 6));
		return n;
	}

	/// SA-8 "Gecko" SAM launcher with four ready missiles.
	static Node3D Sa8()
	{
		var n = new Node3D();
		var c = Fx.Mat(new Color(0.36f, 0.38f, 0.28f));
		var dark = Fx.Mat(new Color(0.1f, 0.1f, 0.1f));
		Add(n, "Hull", Fx.Box(new Vector3(8.6f, 1.8f, 2.9f), c, new Vector3(0, 1.6f, 0)));
		Add(n, "Nose", Fx.Box(new Vector3(1.6f, 1.0f, 2.8f), c, new Vector3(4.0f, 1.4f, 0), new Vector3(0, 0, -0.6f)));
		foreach (float wx in new[] { -3f, 0f, 3f })
			foreach (float s in new[] { -1f, 1f })
				Add(n, "Wheel", Fx.Cyl(0.65f, 0.65f, 0.5f, dark, new Vector3(wx, 0.65f, 1.25f * s), Axle, 12));
		var turret = Add(n, "Turret", new Node3D { Position = new Vector3(-1.0f, 2.6f, 0) });
		Add(turret, "TurretBase", Fx.Box(new Vector3(1.6f, 0.8f, 1.6f), c, new Vector3(0, 0.4f, 0)));
		Add(turret, "Radar", Fx.Box(new Vector3(0.9f, 1.4f, 1.8f), dark, new Vector3(-0.3f, 1.5f, 0)));
		var gun = Add(turret, "GunPivot", new Node3D { Position = new Vector3(0, 1.0f, 0) });
		float[] rails = { -1.25f, -1.0f, 1.0f, 1.25f };
		for (int i = 0; i < rails.Length; i++)
		{
			var m = Sa8Missile();
			m.Position = new Vector3(0.6f, 0, rails[i]);
			Add(gun, $"Missile{i}", m);
		}
		return n;
	}

	// ---------------------------------------------------------------- scenery

	/// Runway, apron, hangars and tower, in world coordinates (place at the origin).
	static Node3D Airbase()
	{
		var n = new Node3D();
		const float y = Terrain.BaseH;
		const float x0 = Terrain.RunwayX0;
		const float x1 = Terrain.RunwayX1;
		var asphalt = Fx.Mat(new Color(0.16f, 0.16f, 0.17f), 0.95f);
		var white = Fx.Mat(new Color(0.9f, 0.9f, 0.9f));
		Add(n, "Runway", Fx.Box(new Vector3(x1 - x0, 0.3f, 46f), asphalt, new Vector3((x0 + x1) * 0.5f, y + 0.05f, 0)));
		for (float x = x0 + 60f; x < x1 - 60f; x += 60f)
			Add(n, "CenterLine", Fx.Box(new Vector3(30f, 0.32f, 0.9f), white, new Vector3(x, y + 0.06f, 0)));
		foreach (float s in new[] { -1f, 1f })
			Add(n, "EdgeLine", Fx.Box(new Vector3(x1 - x0, 0.32f, 0.8f), white, new Vector3((x0 + x1) * 0.5f, y + 0.06f, 21f * s)));
		Add(n, "Apron", Fx.Box(new Vector3(500f, 0.3f, 90f), asphalt, new Vector3(1300f, y + 0.04f, -80f)));
		var hangar = Fx.Mat(new Color(0.52f, 0.53f, 0.5f));
		for (int i = 0; i < 4; i++)
		{
			float hx = 900f + i * 140f;
			Add(n, "Hangar", Fx.Box(new Vector3(70f, 18f, 50f), hangar, new Vector3(hx, y + 9f, -150f)));
			Add(n, "HangarRoof", Fx.Cyl(25f, 25f, 70f, hangar, new Vector3(hx, y + 18f, -150f), new Vector3(0, 0, Mathf.Pi / 2), 16));
		}
		Add(n, "Tower", Fx.Box(new Vector3(8f, 30f, 8f), Fx.Mat(new Color(0.7f, 0.68f, 0.62f)), new Vector3(1700f, y + 15f, -95f)));
		Add(n, "TowerCab", Fx.Box(new Vector3(12f, 5f, 12f), Fx.Mat(new Color(0.2f, 0.3f, 0.35f), 0.2f, 0.5f), new Vector3(1700f, y + 32f, -95f)));
		return n;
	}

	/// One sandbag barrier section, sitting on y = 0.
	static Node3D Sandbag()
	{
		var n = new Node3D();
		Add(n, "Bags", Fx.Box(new Vector3(4f, 1.3f, 2f), Fx.Mat(new Color(0.55f, 0.5f, 0.36f)), new Vector3(0, 0.6f, 0)));
		return n;
	}
}
