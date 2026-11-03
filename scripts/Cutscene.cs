using System;
using Godot;

/// Letterboxed scene over a slow camera pan: the mission briefing and the
/// victory / defeat endings. Subtitles follow the voice line, then the scene
/// continues by itself; SPACE / ENTER / click skips it.
public partial class Cutscene : Control
{
    static readonly Color CHud = new(0.35f, 1.0f, 0.45f);
    static readonly Color CDim = new(0.35f, 1.0f, 0.45f, 0.6f);
    static readonly Color CAmber = new(1.0f, 0.75f, 0.2f);
    const float HoldAtEnd = 1.5f;
    const float FadeIn = 0.15f;

    public string Header = "";
    public string Title = "";
    public string Info = "";
    public Color TitleColor = CHud;
    /// Subtitle lines, in order.
    public string[] Lines = Array.Empty<string>();
    /// Seconds into Voice where each line starts. Without it (or if the count
    /// doesn't match Lines) each line stays up long enough to read.
    public float[] Times;
    public AudioStream Voice;
    /// Called when the scene ends or the player skips it.
    public Action OnDone;

    Label _subtitle;
    float[] _times;
    float _end;
    float _time;
    float _shownAt;
    int _line = -1;
    bool _done;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Letterbox bars, so it reads like a cutscene.
        var top = AddBar(LayoutPreset.TopWide);
        var bottom = AddBar(LayoutPreset.BottomWide);

        var heading = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeConstantOverride("separation", 0);
        heading.AddChild(MakeLabel(Header, 14, CAmber));
        heading.AddChild(MakeLabel(Title, 24, TitleColor));
        var topRow = AddBarRow(top);
        topRow.AddChild(heading);
        topRow.AddChild(MakeLabel(Info, 14, CDim));

        var bottomRow = AddBarRow(bottom);
        var skip = MakeLabel("SPACE / ENTER / ТОВШИХ  -  АЛГАСАХ", 14, CDim);
        skip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        skip.HorizontalAlignment = HorizontalAlignment.Right;
        bottomRow.AddChild(skip);

        // Subtitles sit just above the bottom bar.
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 160);
        margin.AddThemeConstantOverride("margin_right", 160);
        margin.AddThemeConstantOverride("margin_bottom", 50);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _subtitle = MakeLabel("", 28, Colors.White);
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitle.SizeFlagsVertical = SizeFlags.ShrinkEnd;   // Labels default to the vertical centre of a container
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _subtitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        _subtitle.AddThemeConstantOverride("outline_size", 8);
        margin.AddChild(_subtitle);

        SetupTimes();
        // The player is a child of the scene, so it stops when the scene ends.
        if (Voice != null)
        {
            var voice = new AudioStreamPlayer { Stream = Voice };
            AddChild(voice);
            voice.Play();
        }
    }

    void SetupTimes()
    {
        _times = Times;
        if (_times == null || _times.Length != Lines.Length)
        {
            _times = new float[Lines.Length];
            float t = 0.2f;
            for (int i = 0; i < Lines.Length; i++)
            {
                _times[i] = t;
                t += Mathf.Max(2f, 0.6f + Lines[i].Length * 0.07f);
            }
            _end = t;
        }
        if (Voice != null)
            _end = (float)Voice.GetLength();
        else if (_times == Times && _times.Length > 0)
            _end = _times[^1] + 3f;
        _end += HoldAtEnd;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        int line = _line;
        while (line + 1 < _times.Length && _time >= _times[line + 1])
            line++;
        if (line != _line)
        {
            _line = line;
            _shownAt = _time;
            _subtitle.Text = Lines[line];
        }
        _subtitle.Modulate = new Color(1, 1, 1, Mathf.Clamp((_time - _shownAt) / FadeIn, 0f, 1f));
        if (_time >= _end)
            Finish();
    }

    void Finish()
    {
        if (_done)
            return;
        _done = true;
        OnDone?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        bool pressed = (e is InputEventKey k && k.Pressed && !k.Echo
                && (k.PhysicalKeycode is Key.Space or Key.Enter or Key.KpEnter))
            || (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left);
        if (!pressed)
            return;
        GetViewport().SetInputAsHandled();
        Finish();
    }

    ColorRect AddBar(LayoutPreset where)
    {
        var bar = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(where);
        bar.CustomMinimumSize = new Vector2(0, 70);
        if (where == LayoutPreset.BottomWide)
            bar.OffsetTop = -70;
        else
            bar.OffsetBottom = 70;
        return bar;
    }

    static HBoxContainer AddBarRow(ColorRect bar)
    {
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        bar.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddChild(row);
        return row;
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
