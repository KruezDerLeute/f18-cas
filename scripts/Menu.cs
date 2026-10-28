using System;
using Godot;

/// Title screen: mission briefing, difficulty choice and the controls list.
/// Created by Game when the game first starts (Settings.SkipMenu is false).
/// Works with the mouse or the keyboard (arrows + Enter).
public partial class Menu : Control
{
	static readonly Color CHud = new(0.35f, 1.0f, 0.45f);
	static readonly Color CDim = new(0.35f, 1.0f, 0.45f, 0.6f);
	static readonly Color CAmber = new(1.0f, 0.75f, 0.2f);

	/// Called with the chosen difficulty.
	public Action<Difficulty> OnStart;

	Label _controls;
	VBoxContainer _main;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Darken the scenery behind the menu.
		var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
		AddChild(shade);
		shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var center = new CenterContainer();
		AddChild(center);
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", Box(new Color(0.02f, 0.06f, 0.03f, 0.85f), CDim, 2, 28));
		center.AddChild(panel);

		var root = new HBoxContainer();
		root.AddThemeConstantOverride("separation", 40);
		panel.AddChild(root);

		_main = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
		_main.AddThemeConstantOverride("separation", 10);
		root.AddChild(_main);

		_main.AddChild(MakeLabel("F/A-18", 64, CHud));
		_main.AddChild(MakeLabel("CLOSE AIR SUPPORT", 30, CHud));
		_main.AddChild(MakeLabel(
			"Enemy tanks, trucks and anti-air are rolling in from the RIGHT.\n" +
			"Protect the friendly tanks at the GREEN SMOKE.\n" +
			"Survive as many waves as you can.", 17, Colors.White));
		_main.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
		_main.AddChild(MakeLabel("CHOOSE DIFFICULTY", 18, CAmber));

		var easy = AddChoice("EASY", "Start in the air, can't stall, auto pull-up near the ground,\nunlimited fuel, free rearm after each wave, fewer SAMs.", Difficulty.Easy);
		AddChoice("NORMAL", "Start in the air, can't stall, takes less damage.\nLand on the runway to rearm.", Difficulty.Normal);
		AddChoice("REALISTIC", "Full flight model: take off from the runway,\nmanage stall, fuel and landing yourself.", Difficulty.Realistic);

		_main.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		_main.AddChild(row);
		var controlsBtn = MakeButton("CONTROLS", 16);
		controlsBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		controlsBtn.Pressed += () => _controls.Visible = !_controls.Visible;
		row.AddChild(controlsBtn);
		var quit = MakeButton("QUIT", 16);
		quit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quit.Pressed += () => GetTree().Quit();
		row.AddChild(quit);

		_controls = MakeLabel(
			"CONTROLS\n\n" +
			"W / S        pull up / push down\n" +
			"F            flip (turn around)\n" +
			"E / Q        throttle up / down\n" +
			"SPACE        cannon\n" +
			"B            drop bomb\n" +
			"M            fire Maverick missile\n" +
			"T            next target\n" +
			"X            flares (dodge SAMs!)\n" +
			"G            landing gear\n" +
			"Z            speedbrake / wheel brakes\n" +
			"V            targeting camera\n\n" +
			"P / ESC      pause\n" +
			"R            restart\n" +
			"H            show / hide help\n\n" +
			"TIP: when a missile warning sounds,\n" +
			"press X for flares and turn hard.", 16, CHud);
		_controls.HorizontalAlignment = HorizontalAlignment.Left;
		_controls.Visible = false;
		root.AddChild(_controls);

		easy.CallDeferred(Control.MethodName.GrabFocus);
	}

	Button AddChoice(string title, string desc, Difficulty d)
	{
		var b = MakeButton(title, 22);
		b.Pressed += () => OnStart?.Invoke(d);
		_main.AddChild(b);
		var l = MakeLabel(desc, 13, CDim);
		_main.AddChild(l);
		return b;
	}

	static Label MakeLabel(string text, int size, Color col)
	{
		var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", col);
		l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		return l;
	}

	static Button MakeButton(string text, int size)
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44) };
		b.AddThemeFontSizeOverride("font_size", size);
		b.AddThemeColorOverride("font_color", CHud);
		b.AddThemeColorOverride("font_hover_color", Colors.Black);
		b.AddThemeColorOverride("font_focus_color", Colors.Black);
		b.AddThemeColorOverride("font_pressed_color", Colors.Black);
		b.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0.4f), CDim, 2, 8));
		b.AddThemeStyleboxOverride("hover", Box(CHud, CHud, 2, 8));
		b.AddThemeStyleboxOverride("focus", Box(CHud, CHud, 2, 8));
		b.AddThemeStyleboxOverride("pressed", Box(CAmber, CAmber, 2, 8));
		return b;
	}

	static StyleBoxFlat Box(Color bg, Color border, int bw, int pad)
	{
		var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
		s.SetBorderWidthAll(bw);
		s.SetCornerRadiusAll(6);
		s.SetContentMarginAll(pad);
		return s;
	}
}
