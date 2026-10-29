using System.Collections.Generic;
using Godot;

/// Heads-up display: flight data, weapons, CCIP pipper, target boxes,
/// RWR warnings, battlefield strip map and messages. Everything is drawn
/// with Control._Draw() every frame. Put it on a full-screen Control under a
/// CanvasLayer in the main scene.
public partial class Hud : Control
{
	static readonly Color CHud = new(0.35f, 1.0f, 0.45f, 0.95f);
	static readonly Color CDim = new(0.35f, 1.0f, 0.45f, 0.45f);
	static readonly Color CWarn = new(1.0f, 0.3f, 0.2f);
	static readonly Color CAmber = new(1.0f, 0.75f, 0.2f);
	static readonly Color CFriend = new(0.35f, 0.75f, 1.0f);
	const float MapX0 = -1000f;
	const float MapX1 = 13000f;

	public Game Main;
	Font _font;
	float _blink;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		_font = ThemeDB.FallbackFont;
	}

	public override void _Process(double delta)
	{
		_blink += (float)delta;
		// Always cover the whole viewport, even after a window resize.
		var vs = GetViewportRect().Size;
		if (Size != vs)
		{
			Position = Vector2.Zero;
			Size = vs;
		}
		QueueRedraw();
	}

	/// Text with a drop shadow so it reads over bright sky.
	void Txt(Vector2 p, string s, int fs = 16, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
	{
		var col = color ?? CHud;
		DrawString(_font, p + new Vector2(1.5f, 1.5f), s, align, width, fs, new Color(0, 0, 0, 0.65f * col.A));
		DrawString(_font, p, s, align, width, fs, col);
	}

	void CTxt(Vector2 p, string s, int fs = 16, Color? col = null) =>
		Txt(p - new Vector2(400, 0), s, fs, col, HorizontalAlignment.Center, 800f);

	/// World (lane) position to screen position, or null if behind the camera.
	Vector2? W2S(Vector2 p, float z = 0f)
	{
		var p3 = new Vector3(p.X, p.Y, z);
		if (Main.Camera.IsPositionBehind(p3))
			return null;
		return Main.Camera.UnprojectPosition(p3);
	}

	bool OnScreen(Vector2 s, float margin = 0f) =>
		s.X > margin && s.Y > margin && s.X < Size.X - margin && s.Y < Size.Y - margin;

	/// Where the line from the screen centre to an off-screen point crosses the
	/// screen rectangle inset by margin, and the direction towards the point.
	(Vector2 edge, Vector2 dir) EdgePoint(Vector2 s, float margin)
	{
		var center = Size * 0.5f;
		var d = s - center;
		float t = Mathf.Min((center.X - margin) / Mathf.Max(Mathf.Abs(d.X), 0.001f), (center.Y - margin) / Mathf.Max(Mathf.Abs(d.Y), 0.001f));
		return (center + d * t, d.Normalized());
	}

	public override void _Draw()
	{
		if (Main?.Jet == null)
			return;
		var j = Main.Jet;
		DrawHitFlash(j);
		DrawWorldMarkers(j);
		DrawOffscreenEnemies(j);
		DrawFlightData(j);
		DrawWeapons(j);
		DrawFlir(j);
		DrawScore();
		DrawMap(j);
		DrawWarnings(j);
		DrawMessages();
		if (Main.ShowHelp)
			DrawHelp();
		if (Main.Paused)
		{
			CTxt(new Vector2(Size.X * 0.5f, Size.Y * 0.42f), "ТҮР ЗОГСООСОН", 40, CHud);
			CTxt(new Vector2(Size.X * 0.5f, Size.Y * 0.42f + 40f), "P үргэлжлүүлэх    ENTER үндсэн цэс", 20, CAmber);
		}
		if (Main.State == GameState.Dead)
			DrawEnd();
	}

	/// Red glow around the screen edges after the jet takes a hit, with a faint
	/// red tint over the middle. Drawn first so the HUD stays readable on top.
	void DrawHitFlash(Jet j)
	{
		float a = j.HitFlash;
		if (a <= 0f)
			return;
		var edge = new Color(0.9f, 0.05f, 0.02f, 0.75f * a);
		var clear = new Color(0.9f, 0.05f, 0.02f, 0f);
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.9f, 0.05f, 0.02f, 0.12f * a));
		float b = Mathf.Min(Size.X, Size.Y) * 0.22f;
		Vector2 o0 = Vector2.Zero, o1 = new(Size.X, 0), o2 = Size, o3 = new(0, Size.Y);
		Vector2 i0 = new(b, b), i1 = new(Size.X - b, b), i2 = Size - new Vector2(b, b), i3 = new(b, Size.Y - b);
		var cols = new[] { edge, edge, clear, clear };
		DrawPolygon(new[] { o0, o1, i1, i0 }, cols);
		DrawPolygon(new[] { o1, o2, i2, i1 }, cols);
		DrawPolygon(new[] { o2, o3, i3, i2 }, cols);
		DrawPolygon(new[] { o3, o0, i0, i3 }, cols);
	}

	/// Arrows on the screen edge pointing at hostiles outside the view.
	/// SAMs and AAA get bigger amber arrows; the nearest one shows its range.
	void DrawOffscreenEnemies(Jet j)
	{
		if (Main.State != GameState.Play)
			return;
		var center = Size * 0.5f;
		const float margin = 48f;
		float nearestD = float.PositiveInfinity;
		var nearestE = Vector2.Zero;
		var nearestDir = Vector2.Zero;
		foreach (var u in Main.Units)
		{
			if (!u.Alive || u.Team != 1)
				continue;
			var s = W2S(u.CPos, u.LaneZ);
			if (s == null || OnScreen(s.Value))
				continue;
			if ((s.Value - center).Length() < 1f)
				continue;
			var (e, dir) = EdgePoint(s.Value, margin);
			var side = dir.Orthogonal();
			bool threat = u.Kind == UnitKind.Sam || u.Kind == UnitKind.Aaa;
			var col = threat ? CAmber : CWarn;
			float sz = threat ? 12f : 8f;
			DrawColoredPolygon(new[] { e + dir * sz, e - dir * sz * 0.6f + side * sz * 0.7f, e - dir * sz * 0.6f - side * sz * 0.7f },
					new Color(col, 0.9f));
			if (threat)
				CTxt(e - dir * 22f + new Vector2(0, 5), u.Kind == UnitKind.Sam ? "ЗУП" : "ЗЕНИТ", 11, col);
			float dist = u.CPos.DistanceTo(j.Pos);
			if (dist < nearestD)
			{
				nearestD = dist;
				nearestE = e;
				nearestDir = dir;
			}
		}
		if (nearestD < float.PositiveInfinity)
			CTxt(nearestE - nearestDir * 40f + new Vector2(0, 5), $"{nearestD / 1000f:F1} км", 14, CWarn);
	}

	void DrawWorldMarkers(Jet j)
	{
		bool viewFar = Main.CamDist > 450f;
		// Ground units.
		foreach (var u in Main.Units)
		{
			if (!u.Alive)
				continue;
			var sp = W2S(u.CPos + new Vector2(0, 7f), u.LaneZ);
			if (sp == null || !OnScreen(sp.Value))
				continue;
			var s = sp.Value;
			if (u.Team == 1)
			{
				float r = viewFar ? 7f : 5f;
				DrawColoredPolygon(new[] { s + new Vector2(-r, -r * 1.6f), s + new Vector2(r, -r * 1.6f), s }, new Color(CWarn, 0.85f));
				if (!viewFar || u.Kind == UnitKind.Sam || u.Kind == UnitKind.Aaa)
					Txt(s + new Vector2(-20, -r * 1.6f - 4), u.DisplayName, 11, new Color(CWarn, 0.9f));
				if (u.Hp < u.MaxHp)
				{
					const float w = 22f;
					DrawRect(new Rect2(s + new Vector2(-w * 0.5f, 4), new Vector2(w, 3)), new Color(0, 0, 0, 0.5f));
					DrawRect(new Rect2(s + new Vector2(-w * 0.5f, 4), new Vector2(w * u.Hp / u.MaxHp, 3)), CWarn);
				}
			}
			else
				DrawPolyline(new[] { s + new Vector2(-6, -10), s + new Vector2(0, -4), s + new Vector2(6, -10) }, CFriend, 2f);
		}
		// Jet marker and flight path.
		var jp = W2S(j.Pos);
		if (j.Alive && jp != null)
		{
			if (viewFar)
				DrawArc(jp.Value, 16f, 0, Mathf.Tau, 28, CHud, 1.5f);
			float vlen = Main.CamDist * 0.12f;
			var vp = W2S(j.Pos + j.Vel.Normalized() * vlen);
			if (vp != null)
			{
				var v = vp.Value;
				DrawLine(jp.Value, v, CDim, 1.5f);
				DrawArc(v, 5f, 0, Mathf.Tau, 14, CHud, 1.5f);
				DrawLine(v + new Vector2(5, 0), v + new Vector2(11, 0), CHud, 1.5f);
				DrawLine(v - new Vector2(5, 0), v - new Vector2(11, 0), CHud, 1.5f);
			}
			// Bomb CCIP pipper and fall line.
			if (j.Ccip != null && W2S(j.Ccip.Value) is Vector2 cp)
			{
				DrawDashedLine(jp.Value, cp, CDim, 1f, 8f);
				DrawArc(cp, 12f, 0, Mathf.Tau, 24, CHud, 2f);
				DrawCircle(cp, 2f, CHud);
				bool low = j.CcipTime < 1f;   // bomb wouldn't arm in time
				Txt(cp + new Vector2(16, 5), $"{j.CcipTime:F1}с", 13, low ? CWarn : CHud);
			}
			// Gun impact cross.
			if (j.GunCcip != null && W2S(j.GunCcip.Value) is Vector2 gp)
			{
				var c = j.Firing ? CHud : CDim;
				DrawLine(gp + new Vector2(-8, 0), gp + new Vector2(8, 0), c, 1.5f);
				DrawLine(gp + new Vector2(0, -8), gp + new Vector2(0, 8), c, 1.5f);
			}
		}
		// Targeting pod crosshair on the ground while it's slewed by hand.
		if (Main.Flir is { Visible: true, Manual: true } pod && W2S(new Vector2(pod.Aim.X, pod.Aim.Y), pod.Aim.Z) is Vector2 ps)
		{
			var pc = pod.Slewing ? CHud : CDim;
			if (OnScreen(ps, 10f))
			{
				DrawLine(ps + new Vector2(-12, 0), ps + new Vector2(-4, 0), pc, 1.5f);
				DrawLine(ps + new Vector2(4, 0), ps + new Vector2(12, 0), pc, 1.5f);
				DrawLine(ps + new Vector2(0, -12), ps + new Vector2(0, -4), pc, 1.5f);
				DrawLine(ps + new Vector2(0, 4), ps + new Vector2(0, 12), pc, 1.5f);
			}
			else
			{
				var (e, dir) = EdgePoint(ps, 30f);
				DrawPolyline(new[] { e - dir * 10f + dir.Orthogonal() * 6f, e, e - dir * 10f - dir.Orthogonal() * 6f }, pc, 2f);
				CTxt(e - dir * 26f + new Vector2(0, 5), "КАМЕР", 11, pc);
			}
		}
		// Maverick lock. Off-screen targets get the diamond pinned to the screen edge.
		var lt = j.LockTarget;
		if (j.Alive && lt != null && IsInstanceValid(lt) && W2S(lt.CPos, lt.LaneZ) is Vector2 ls)
		{
			bool tracking = j.LockTime > 0.8f;
			float r = tracking ? 14f : 22f - j.LockTime * 10f;   // diamond shrinks while acquiring
			var c = !lt.Alive ? CDim : tracking ? CHud : CAmber;
			if (jp != null)
				DrawDashedLine(jp.Value, ls, new Color(c, 0.4f), 1f, 6f);   // seeker line
			bool onScreen = OnScreen(ls, 40f);
			var dp = ls;
			if (!onScreen)
			{
				var (e, dir) = EdgePoint(ls, 70f);
				dp = e;
				DrawColoredPolygon(new[] { e + dir * (r + 14f), e + dir * r + dir.Orthogonal() * 6f, e + dir * r - dir.Orthogonal() * 6f }, c);
			}
			DrawPolyline(new[] { dp + new Vector2(0, -r), dp + new Vector2(r, 0), dp + new Vector2(0, r), dp + new Vector2(-r, 0), dp + new Vector2(0, -r) }, c, 2f);
			string label = !lt.Alive ? "УСТСАН" : tracking ? "ДАГАЛТ" : "ХАЙЛТ";
			if (!onScreen)
				label += $" {lt.DisplayName} {lt.CPos.DistanceTo(j.Pos) / 1000f:F1} км";
			// Keep the label inside the screen when the diamond sits on the right edge.
			bool left = dp.X > Size.X * 0.5f && !onScreen;
			Txt(dp + new Vector2(left ? -r - 4 - 150 : r + 4, -r), label, 12, c, left ? HorizontalAlignment.Right : HorizontalAlignment.Left, left ? 150f : -1f);
		}
		// Incoming missiles.
		foreach (var m in Main.Missiles)
		{
			if (m.IsAgm || m.Dead)
				continue;
			var msp = W2S(m.Pos, m.LaneZ);
			if (msp == null)
				continue;
			var ms = msp.Value;
			if (OnScreen(ms, 10f))
				DrawArc(ms, 9f, 0, Mathf.Tau, 16, CWarn, 2f);
			else
			{
				var c = Size * 0.5f;
				var d = (ms - c).Normalized();
				var e = c + d * Mathf.Min(Size.X, Size.Y) * 0.45f;
				DrawColoredPolygon(new[] { e + d * 14, e + d.Orthogonal() * 8, e - d.Orthogonal() * 8 }, CWarn);
			}
		}
	}

	void DrawFlightData(Jet j)
	{
		const float x = 24f;
		float y = 40f;
		float kmh = j.Vel.Length() * 3.6f;
		var lines = new (string label, string value)[]
		{
			("ХУРД", $"{(int)kmh,4} км/ц"),
			("МАХ", $"{j.Mach:F2}"),
			("ӨНДӨР", $"{(int)j.Pos.Y,5} м"),
			("ГАЗАР", $"{(int)Mathf.Max(j.Agl, 0f),5} м"),
			("G", j.GLoad.ToString("+0.0;-0.0")),
			("ӨНЦӨГ", $"{Mathf.RadToDeg(j.Alpha * j.UpSign()),4:F1}°"),
			("БОСОО", $"{((int)j.Vel.Y).ToString("+0;-0"),5} м/с"),
		};
		foreach (var (label, value) in lines)
		{
			Txt(new Vector2(x, y), label, 14, CDim);
			Txt(new Vector2(x + 62, y), value, 18, CHud);
			y += 24f;
		}
		y += 8f;
		float thr = j.EngineSpool;
		bool ab = thr > 1f;
		Txt(new Vector2(x, y), "ТҮЛХ", 14, CDim);
		Txt(new Vector2(x + 62, y), ab ? $"НШ {(int)((thr - 1f) / 0.25f * 100f + 0.5f)}" : $"{(int)(thr * 100f),3}%", 18, ab ? CAmber : CHud);
		DrawRect(new Rect2(x + 150, y - 12, 100, 10), CDim, false, 1f);
		DrawRect(new Rect2(x + 150, y - 12, 100f * Mathf.Clamp(j.Throttle / 1.25f, 0f, 1f), 10), j.Throttle > 1f ? CAmber : CHud);
		DrawLine(new Vector2(x + 150 + 80, y - 14), new Vector2(x + 150 + 80, y), CWarn, 1f);   // the AB detent
		y += 24f;
		Txt(new Vector2(x, y), "ТҮЛШ", 14, CDim);
		Txt(new Vector2(x + 62, y), $"{(int)j.Fuel,5} кг", 18, j.Fuel < 907f ? CWarn : CHud);
		y += 24f;
		Txt(new Vector2(x, y), "ЖИН", 14, CDim);
		Txt(new Vector2(x + 62, y), $"{(int)j.Mass(),5} кг", 18, CHud);
		y += 24f;
		Txt(new Vector2(x, y), "БҮТЭН", 14, CDim);
		var hc = j.Hp > 55f ? CHud : (j.Hp > 30f ? CAmber : CWarn);
		DrawRect(new Rect2(x + 62, y - 13, 150, 12), new Color(0, 0, 0, 0.4f));
		DrawRect(new Rect2(x + 62, y - 13, 1.5f * j.Hp, 12), hc);
		y += 24f;
		var status = new List<string>();
		if (j.GearDown)
			status.Add("ДУГУЙ");
		if (j.Airbrake)
			status.Add(j.OnGround ? "ТООРМОС" : "АГ.ТООРМОС");
		if (j.UpSign() < 0f)
			status.Add("УРВУУ");
		if (status.Count > 0)
			Txt(new Vector2(x, y), string.Join(" ", status), 14, CAmber);
	}

	void DrawWeapons(Jet j)
	{
		const float x = 24f;
		float y = Size.Y - 120f;
		DrawRect(new Rect2(x - 10, y - 24, 270, 112), new Color(0, 0, 0, 0.3f));
		var rows = new (string name, int count, string key)[]
		{
			("БУУ  M61A2", j.Ammo, "SPACE"),
			("GBU-12", j.Bombs, "B"),
			("AGM-65", j.Mavs, "M"),
			("ХУУРАМЧ БАЙ", j.Flares, "X"),
		};
		foreach (var (name, count, key) in rows)
		{
			bool empty = count == 0;
			Txt(new Vector2(x, y), name, 15, empty ? CDim : CHud);
			Txt(new Vector2(x + 140, y), count.ToString(), 17, empty ? CWarn : CHud);
			Txt(new Vector2(x + 195, y), key, 12, CDim);
			y += 24f;
		}
		// Maverick target readout above the panel.
		var lt = j.LockTarget;
		if (lt != null && IsInstanceValid(lt))
		{
			bool tracking = j.LockTime > 0.8f;
			string state = !lt.Alive ? "УСТСАН" : tracking ? "ДАГАЛТ" : "ХАЙЛТ";
			Txt(new Vector2(x - 10, Size.Y - 152f), $"БАЙ  {lt.DisplayName}  {lt.CPos.DistanceTo(j.Pos) / 1000f:F1} км  {state}",
					14, !lt.Alive ? CDim : tracking ? CHud : CAmber);
		}
		else if (j.Mavs > 0 || j.Bombs > 0)
			Txt(new Vector2(x - 10, Size.Y - 152f), "БАЙ  АЛГА", 14, CDim);
	}

	/// Frame, crosshair and readouts over the targeting pod picture.
	void DrawFlir(Jet j)
	{
		var f = Main.Flir;
		if (f == null || !f.Visible)
			return;
		var r = f.Rect;
		DrawRect(r, CHud, false, 1.5f);
		var c = r.GetCenter();
		const float gap = 8f, len = 22f;
		foreach (var d in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
			DrawLine(c + d * gap, c + d * len, CHud, 1.5f);
		Txt(r.Position + new Vector2(6, 16), "ДУЛААНЫ КАМЕР", 12, CHud);
		Txt(r.Position + new Vector2(r.Size.X - 60, 16), f.Manual ? "ГАРААР" : "АВТО", 12, CHud);
		string info;
		if (f.Slewing)
			info = $"ЧИГЛҮҮЛЭХ  {f.Range / 1000f:F1} км";
		else if (f.Target != null)
			info = $"{f.Target.DisplayName}  {f.Range / 1000f:F1} км  {(!f.Target.Alive ? "УСТСАН" : j.LockTime > 0.8f ? "ДАГАЛТ" : "ХАЙЛТ")}";
		else
			info = $"{(f.Manual ? "ЗААХ" : "ХАЙЛТ")}  {f.Range / 1000f:F1} км";
		Txt(r.Position + new Vector2(6, r.Size.Y - 8), info, 12, CHud);
		// Laser: steady while lasing, blinking with the time to impact while a bomb is guiding.
		float tti = Main.LgbTimeToImpact();
		if (j.Lasing)
		{
			string laser = tti >= 0f ? $"ЛАЗЕР  БОМБ {tti:F0}с" : "ЛАЗЕР БЭЛЭН";
			if (tti < 0f || (_blink % 0.6f) < 0.4f)
				Txt(r.Position + new Vector2(r.Size.X - 6 - 120, r.Size.Y - 8), laser, 12, CHud, HorizontalAlignment.Right, 120f);
		}
	}

	void DrawScore()
	{
		float x = Size.X - 230f;
		float y = 40f;
		Txt(new Vector2(x, y), $"ОНОО  {Main.Score}", 20, CHud);
		y += 26f;
		Txt(new Vector2(x, y), $"ДОВТОЛГОО  {Main.Wave} / {Game.MissionWaves}", 16, CHud);
		y += 22f;
		Txt(new Vector2(x, y), $"ДАЙСАН  {Main.CountTeam(1)}", 16, CWarn);
		y += 22f;
		Txt(new Vector2(x, y), $"УСТГАСАН  {Main.Kills}", 14, CDim);
		if (!Main.WaveActive && Main.State == GameState.Play)
		{
			y += 22f;
			Txt(new Vector2(x, y), $"ДАРААГИЙН ДОВТОЛГОО {(int)Mathf.Ceil(Main.WaveTimer)} с", 14, CAmber);
		}
	}

	/// Side-view strip map of the whole battlefield at the top of the screen.
	void DrawMap(Jet j)
	{
		float w = Mathf.Min(640f, Size.X - 560f);
		var r = new Rect2(Size.X * 0.5f - w * 0.5f, 10, w, 66);
		DrawRect(r, new Color(0, 0.05f, 0, 0.45f));
		DrawRect(r, CDim, false, 1f);
		var t = Main.Terrain;
		var pts = new Vector2[97];
		for (int i = 0; i < pts.Length; i++)
		{
			float wx = Mathf.Lerp(MapX0, MapX1, i / 96f);
			pts[i] = Map(r, new Vector2(wx, t.Height(wx)));
		}
		DrawPolyline(pts, CDim, 1f);
		DrawLine(Map(r, new Vector2(Terrain.RunwayX0, Terrain.BaseH)), Map(r, new Vector2(Terrain.RunwayX1, Terrain.BaseH)), CFriend, 3f);
		var fl = Map(r, new Vector2(Main.FriendlyLine, 0));
		DrawLine(new Vector2(fl.X, r.Position.Y), new Vector2(fl.X, r.End.Y), new Color(0.3f, 0.9f, 0.35f, 0.5f), 1f);
		foreach (var u in Main.Units)
			if (u.Alive)
				DrawCircle(Map(r, u.CPos), 2f, u.Team == 1 ? CWarn : CFriend);
		foreach (var m in Main.Missiles)
			DrawCircle(Map(r, m.Pos), 1.5f, CAmber);
		if (j.Alive)
		{
			var jp = Map(r, j.Pos);
			var f = j.Fwd() * new Vector2(1, -1);   // screen y points down
			DrawColoredPolygon(new[] { jp + f * 7, jp + f.Orthogonal() * 4 - f * 3, jp - f.Orthogonal() * 4 - f * 3 }, Colors.White);
		}
		Txt(new Vector2(r.Position.X + 4, r.End.Y - 4), "БААЗ", 10, CFriend);
	}

	/// Square-root altitude scale so low flying is still readable on the map.
	static Vector2 Map(Rect2 r, Vector2 p)
	{
		float u = Mathf.Clamp((p.X - MapX0) / (MapX1 - MapX0), 0f, 1f);
		float v = Mathf.Clamp(p.Y / 5000f, 0f, 1f);
		return new Vector2(r.Position.X + u * r.Size.X, r.End.Y - 3f - Mathf.Sqrt(v) * (r.Size.Y - 6f));
	}

	void DrawWarnings(Jet j)
	{
		if (!j.Alive)
			return;
		bool on = _blink % 0.5f < 0.3f;
		var c = new Vector2(Size.X * 0.5f, Size.Y * 0.2f);
		var w = new List<(string text, Color col)>();
		if (Main.SamInbound())
			w.Add(("ПУУЖИН! - ХУУРАМЧ БАЙ (X) / ОГЦОМ ЭРГЭ / НАМ ЯВ", CWarn));
		else if (j.RwrLock > 0f)
			w.Add(("ЗУП-ИЙН РАДАР ТАНЫГ БАРЬЛАА", CAmber));
		if (Main.PullUpWarning())
			w.Add(("ДЭЭШЭЭ ТАТ!", CWarn));
		if (!j.OnGround && j.Stalled)
			w.Add(("ДААЦ АЛДАГДЛАА", CWarn));
		if (!j.OnGround && j.GLoad > 12f)
			w.Add(("ХЭТ АЧААЛАЛ", CAmber));
		if (j.Fuel < 900f)
			w.Add(("ТҮЛШ ДУУСАЖ БАЙНА - БААЗДАА БУЦ", CAmber));
		if (j.Hp < 30f)
			w.Add(("ХӨДӨЛГҮҮР ГЭМТСЭН", CWarn));
		if (j.Pos.X > 14000f || j.Pos.X < -3500f)
			w.Add(("БҮСЭЭС ГАРЧ БАЙНА - БУЦ", CAmber));
		for (int i = 0; i < w.Count; i++)
			if (on || w[i].col != CWarn)   // red warnings blink
				CTxt(c + new Vector2(0, i * 30f), w[i].text, 24, w[i].col);
		if (j.OnGround && j.RearmTimer > 0f)
			CTxt(new Vector2(Size.X * 0.5f, Size.Y * 0.32f), $"ЦЭНЭГЛЭЖ БАЙНА  {(int)(Mathf.Min(j.RearmTimer / 4f, 1f) * 100f)}%", 20, CFriend);
	}

	void DrawMessages()
	{
		float y = 110f;
		foreach (var m in Main.Messages)
		{
			var col = m.Color;
			col.A = Mathf.Clamp(m.Time, 0f, 1f);   // fade out over the last second
			CTxt(new Vector2(Size.X * 0.5f, y), m.Text, 17, col);
			y += 24f;
		}
	}

	void DrawHelp()
	{
		string[] lines =
		{
			"W / S   татах / түлхэх  (бүхээгтэй харьцангуй)",
			"F       хагас эргэлт (бүхээгийн талыг солих)",
			"E / Q   түлхэлт нэмэх / хасах  (80%-иас дээш = нэмэлт шаталт)",
			"SPACE   M61 их буу",
			"B       GBU-12 бомб хаях  (дагаж буй бай руу чиглэнэ, үгүй бол онилуурыг ашигла)",
			"M       AGM-65 Maverick пуужин  (T бай солих / камер авто)",
			"X       хуурамч бай",
			"Z       агаарын / дугуйн тоормос (асаах/унтраах)",
			"V       онилох камер (FLIR) асаах / унтраах",
			"Хулганы баруун товч  дарж чирэх: камер чиглүүлэх, дугуй: томруулах, суллах: бай сонгох",
			"G       буух дугуй  (зэвсэглэхийн тулд зурвас дээр буу)",
			"Дугуй / +/-  томруулах    P түр зогсоох   R дахин эхлэх   H тусламж",
		};
		const float w = 640f;
		float x = Size.X - w - 16f;
		float y = Size.Y - 24f * lines.Length - 24f;
		DrawRect(new Rect2(x - 10, y - 22, w + 10, 24f * lines.Length + 14), new Color(0, 0, 0, 0.35f));
		foreach (var l in lines)
		{
			Txt(new Vector2(x, y), l, 14, CHud);
			y += 24f;
		}
	}

	void DrawEnd()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, Mathf.Clamp(Main.StateTime * 0.3f, 0f, 0.45f)));
		const string title = "ОНГОЦ УНАЛАА";
		var c = new Vector2(Size.X * 0.5f, Size.Y * 0.4f);
		CTxt(c, title, 44, CWarn);
		CTxt(c + new Vector2(0, 50), $"Оноо {Main.Score}    Даван туулсан довтолгоо {Mathf.Max(Main.Wave - 1, 0)}    Устгасан {Main.Kills}", 20, CHud);
		CTxt(c + new Vector2(0, 90), "R дахин нисэх    ENTER үндсэн цэс", 20, CAmber);
	}
}
