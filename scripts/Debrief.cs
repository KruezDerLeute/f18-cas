using System;
using Godot;

/// End-of-mission debrief: outcome, stats and a pilot rank, with buttons to
/// fly again or go back to the title screen. Shown after the ending cutscene.
public partial class Debrief : Control
{
	static readonly Color CHud = new(0.35f, 1.0f, 0.45f);
	static readonly Color CDim = new(0.35f, 1.0f, 0.45f, 0.6f);
	static readonly Color CWarn = new(1.0f, 0.3f, 0.2f);
	static readonly Color CAmber = new(1.0f, 0.75f, 0.2f);

	public Game Main;
	public bool Won;
	public Action OnFlyAgain;
	public Action OnMenu;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var shade = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
		AddChild(shade);
		shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var center = new CenterContainer();
		AddChild(center);
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var panel = new PanelContainer();
		var box = new StyleBoxFlat { BgColor = new Color(0.02f, 0.06f, 0.03f, 0.9f), BorderColor = CDim };
		box.SetBorderWidthAll(2);
		box.SetCornerRadiusAll(6);
		box.SetContentMarginAll(28);
		panel.AddThemeStyleboxOverride("panel", box);
		center.AddChild(panel);

		var v = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		v.AddThemeConstantOverride("separation", 8);
		panel.AddChild(v);

		v.AddChild(MakeLabel("ДҮГНЭЛТ  -  \"ТӨМӨР БАМБАЙ\" АЖИЛЛАГАА", 16, CAmber));
		v.AddChild(MakeLabel(Won ? "ДААЛГАВАР БИЕЛЛЭЭ" : "ДААЛГАВАР БҮТЭЛГҮЙТЛЭЭ", 36, Won ? CHud : CWarn));
		v.AddChild(new HSeparator());

		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 40);
		grid.AddThemeConstantOverride("v_separation", 6);
		v.AddChild(grid);
		int cleared = Won ? Game.MissionWaves : Mathf.Max(Main.Wave - (Main.WaveActive ? 1 : 0), 0);
		Row(grid, "Түвшин", Settings.DifficultyName);
		Row(grid, "Няцаасан довтолгоо", $"{cleared} / {Game.MissionWaves}");
		Row(grid, "Таны устгасан дайсан", $"{Main.PlayerKills} / {Main.EnemiesSpawned}");
		Row(grid, "Аврагдсан танк", $"{Main.CountTeam(0)} / {Main.FriendlyStart}");
		if (Main.FriendlyKills > 0)
			Row(grid, "Өөрсдийгөө буудсан", Main.FriendlyKills.ToString(), CWarn);
		Row(grid, "Оноо", Main.Score.ToString());

		v.AddChild(new HSeparator());
		var (rank, note) = Rank();
		v.AddChild(MakeLabel("НИСГЭГЧИЙН ЦОЛ", 14, CDim));
		v.AddChild(MakeLabel(rank, 40, CAmber));
		v.AddChild(MakeLabel(note, 15, Colors.White));
		v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		v.AddChild(row);
		var again = MakeButton("ДАХИН НИСЭХ  (R)");
		again.Pressed += () => OnFlyAgain?.Invoke();
		row.AddChild(again);
		var menu = MakeButton("ҮНДСЭН ЦЭС");
		menu.Pressed += () => OnMenu?.Invoke();
		row.AddChild(menu);
		again.CallDeferred(Control.MethodName.GrabFocus);
	}

	public override void _Input(InputEvent e)
	{
		if (e.IsActionPressed("restart"))
		{
			GetViewport().SetInputAsHandled();
			OnFlyAgain?.Invoke();
		}
	}

	/// 0-100 rating from kills, tanks saved, winning and difficulty, mapped to a rank.
	(string, string) Rank()
	{
		float kills = Main.EnemiesSpawned > 0 ? (float)Main.PlayerKills / Main.EnemiesSpawned : 0f;
		float saved = Main.FriendlyStart > 0 ? (float)Main.CountTeam(0) / Main.FriendlyStart : 0f;
		float r = kills * 45f + saved * 25f + (Won ? 20f : 0f) - Main.FriendlyKills * 10f
			+ Settings.Difficulty switch { Difficulty.Normal => 5f, Difficulty.Realistic => 10f, _ => 0f };
		if (r >= 85f)
			return ("АС НИСГЭГЧ", "Гайхалтай нислэг, Вайпер 1. Бүх фронт таны тухай ярьж байна.");
		if (r >= 70f)
			return ("ТУРШЛАГАТАН", "Сайн ажиллалаа. Командлал таныг дараагийн довтолгоог удирдаасай гэж байна.");
		if (r >= 50f)
			return ("ХОСЛОЛЫН АХЛАГЧ", "Даалгавраа биелүүллээ. Арай нарийн ажиллавал Ас болно.");
		if (r >= 30f)
			return ("ХАМТРАГЧ НИСГЭГЧ", "Хэцүү нислэг байлаа. Ногоон утаанаас холдохгүй, хуурамч байгаа ашигла.");
		return ("ШИНЭХЭН", "Нислэгийн сургууль руугаа буц. ХЯЛБАР түвшинг туршиж, нэг нэгээр нь бай устга.");
	}

	static void Row(GridContainer g, string label, string value, Color? col = null)
	{
		g.AddChild(MakeLabel(label, 17, CDim));
		var l = MakeLabel(value, 17, col ?? CHud);
		l.HorizontalAlignment = HorizontalAlignment.Right;
		l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		g.AddChild(l);
	}

	static Label MakeLabel(string text, int size, Color col)
	{
		var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", col);
		l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		return l;
	}

	static Button MakeButton(string text)
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		b.AddThemeFontSizeOverride("font_size", 18);
		b.AddThemeColorOverride("font_color", CHud);
		b.AddThemeColorOverride("font_hover_color", Colors.Black);
		b.AddThemeColorOverride("font_focus_color", Colors.Black);
		b.AddThemeColorOverride("font_pressed_color", Colors.Black);
		foreach (var (state, c) in new[] { ("normal", new Color(0, 0, 0, 0.4f)), ("hover", CHud), ("focus", CHud), ("pressed", CAmber) })
		{
			var s = new StyleBoxFlat { BgColor = c, BorderColor = state == "normal" ? CDim : c };
			s.SetBorderWidthAll(2);
			s.SetCornerRadiusAll(6);
			s.SetContentMarginAll(8);
			b.AddThemeStyleboxOverride(state, s);
		}
		return b;
	}
}
