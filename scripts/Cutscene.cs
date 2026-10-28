using System;
using Godot;

/// Letterboxed text scene over a slow camera pan: the mission briefing and
/// the victory / defeat endings. The body types itself out; SPACE / ENTER /
/// click first finishes the text, then continues.
public partial class Cutscene : Control
{
	static readonly Color CHud = new(0.35f, 1.0f, 0.45f);
	static readonly Color CDim = new(0.35f, 1.0f, 0.45f, 0.6f);
	static readonly Color CAmber = new(1.0f, 0.75f, 0.2f);
	const float CharsPerSecond = 55f;

	public string Header = "";
	public string Title = "";
	public string Info = "";
	public string Body = "";
	public string ContinueText = "CONTINUE";
	public Color TitleColor = CHud;
	/// Called when the player continues past the scene.
	public Action OnDone;

	Label _body;
	Label _prompt;
	float _typed;
	float _blink;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Letterbox bars, so it reads like a cutscene.
		AddBar(LayoutPreset.TopWide);
		AddBar(LayoutPreset.BottomWide);

		var panel = new PanelContainer();
		var box = new StyleBoxFlat { BgColor = new Color(0.02f, 0.06f, 0.03f, 0.8f), BorderColor = CDim };
		box.SetBorderWidthAll(2);
		box.SetCornerRadiusAll(6);
		box.SetContentMarginAll(24);
		panel.AddThemeStyleboxOverride("panel", box);
		// Panel sits on the left, vertically centred, leaving the battlefield visible on the right.
		var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		margin.AddThemeConstantOverride("margin_left", 60);
		AddChild(margin);
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		margin.AddChild(column);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		column.AddChild(panel);

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 8);
		panel.AddChild(v);
		v.AddChild(MakeLabel(Header, 16, CAmber));
		v.AddChild(MakeLabel(Title, 30, TitleColor));
		v.AddChild(MakeLabel(Info, 14, CDim));
		v.AddChild(new HSeparator());

		_body = MakeLabel(Body, 18, Colors.White);
		_body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_body.CustomMinimumSize = new Vector2(620, 0);
		_body.VisibleCharacters = 0;
		v.AddChild(_body);

		_prompt = MakeLabel("", 15, CAmber);
		v.AddChild(_prompt);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_blink += dt;
		int total = _body.GetTotalCharacterCount();
		if (_body.VisibleCharacters >= 0 && _body.VisibleCharacters < total)
		{
			_typed += dt * CharsPerSecond;
			_body.VisibleCharacters = Mathf.Min((int)_typed, total);
		}
		bool done = Finished();
		_prompt.Text = "SPACE / ENTER / CLICK  -  " + (done ? ContinueText : "SKIP");
		_prompt.Modulate = new Color(1, 1, 1, done ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_blink * 3f)) : 0.6f);
	}

	bool Finished() => _body.VisibleCharacters < 0 || _body.VisibleCharacters >= _body.GetTotalCharacterCount();

	public override void _Input(InputEvent e)
	{
		bool pressed = (e is InputEventKey k && k.Pressed && !k.Echo
				&& (k.PhysicalKeycode is Key.Space or Key.Enter or Key.KpEnter))
			|| (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left);
		if (!pressed)
			return;
		GetViewport().SetInputAsHandled();
		if (!Finished())
			_body.VisibleCharacters = -1;   // show everything
		else
			OnDone?.Invoke();
	}

	void AddBar(LayoutPreset where)
	{
		var bar = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(bar);
		bar.SetAnchorsAndOffsetsPreset(where);
		bar.CustomMinimumSize = new Vector2(0, 70);
		if (where == LayoutPreset.BottomWide)
			bar.OffsetTop = -70;
		else
			bar.OffsetBottom = 70;
	}

	static Label MakeLabel(string text, int size, Color col)
	{
		var l = new Label { Text = text };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", col);
		l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		return l;
	}
}
