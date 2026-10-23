using Godot;

/// Targeting pod: a small thermal (FLIR) picture on the HUD. It looks at the
/// lock target, or scans the ground ahead of the jet when nothing is locked.
/// Hold the right mouse button and drag to slew it by hand (wheel zooms);
/// releasing designates the enemy nearest the crosshair. T hands it back to
/// automatic. Created by Game; V turns it on and off.
///
/// Heat works with render layers: every ground unit carries a magenta "hot"
/// copy of its meshes on HotLayer (see Unit.AddHeatCopies). The main camera
/// hides HotLayer and the pod camera hides ColdLayer (the normal meshes), and
/// the shader turns magenta into white-hot and everything else into dim grey.
public partial class Flir : Node
{
    /// Render layer (1-based) of the hot copies; only the pod sees it.
    public const int HotLayer = 11;
    /// Render layer of the normal unit meshes; the pod doesn't see it.
    public const int ColdLayer = 12;
    static readonly Vector2I PicSize = new(320, 240);

    const string ShaderCode = @"
shader_type canvas_item;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec3 c = texture(TEXTURE, UV).rgb;
    // Magenta (red and blue, no green) only comes from the hot copies.
    float heat = clamp((min(c.r, c.b) - c.g) * 1.4, 0.0, 1.0);
    float lum = dot(c, vec3(0.299, 0.587, 0.114));
    float v = mix(lum * 0.5 + 0.08, 1.0, heat);
    v += (hash(floor(UV * vec2(320.0, 240.0)) + fract(TIME) * 61.0) - 0.5) * 0.07;   // sensor noise
    v *= mix(0.7, 1.0, smoothstep(0.8, 0.3, length(UV - 0.5)));                        // vignette
    COLOR = vec4(vec3(v), 1.0);
}";

    public Game Main;
    public bool On = true;
    /// Where the picture sits on screen, for the HUD overlay.
    public Rect2 Rect;
    /// What the pod is looking at (possibly a wreck); null while scanning.
    public Unit Target;
    /// Slant range to the aim point, metres.
    public float Range;
    public bool Visible => _view.Visible;
    /// Where the pod is looking, in the world.
    public Vector3 Aim => _aim;
    /// Right mouse button held: the mouse moves the pod.
    public bool Slewing;
    /// Aimed by hand: no automatic scan or lock (T clears it).
    public bool Manual;

    SubViewport _vp;
    Camera3D _cam;
    TextureRect _view;
    Vector3 _aim;
    float _width = 400f;
    bool _aimSet;
    Vector3 _spot;              // hand-aimed ground point
    float _manualWidth = 400f;

    public override void _Ready()
    {
        // A SubViewport without its own world renders the game's world.
        _vp = new SubViewport { Size = PicSize, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_vp);
        _cam = new Camera3D
        {
            Current = true,
            Near = 10f,   // keeps the jet's own model out of the picture
            Far = 20000f,
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.12f, 0.12f),   // cold sky
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.5f),
                FogEnabled = true,
                FogLightColor = new Color(0.3f, 0.3f, 0.3f),
                FogDensity = 9e-5f,
            },
        };
        _cam.SetCullMaskValue(ColdLayer, false);
        _vp.AddChild(_cam);

        _view = new TextureRect
        {
            Texture = _vp.GetTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } },
        };
        // Under the Hud node, so the HUD draws its frame and crosshair on top.
        var layer = Main.GetNode<CanvasLayer>("HudLayer");
        layer.AddChild(_view);
        layer.MoveChild(_view, 0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_view.Visible)
            return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed)
                    StartSlew();
                else if (Slewing)
                    EndSlew();
            }
            else if (Slewing && mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
                _manualWidth = Mathf.Clamp(_manualWidth * (mb.ButtonIndex == MouseButton.WheelUp ? 0.8f : 1.25f), 30f, 1500f);
            else
                return;
            // Handled here, so Game doesn't also zoom the main camera.
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion mm && Slewing)
        {
            Slew(mm.Relative);
            GetViewport().SetInputAsHandled();
        }
    }

    void StartSlew()
    {
        Slewing = true;
        Manual = true;
        _spot = _aim;
        _manualWidth = _width;
        Main.Jet.Designate(null);
        Input.MouseMode = Input.MouseModeEnum.Captured;   // drag as far as you like
    }

    /// Designates the enemy nearest the crosshair: anything in the picture, or up to 150 m away.
    void EndSlew()
    {
        Slewing = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        float best = Mathf.Max(_width, 150f);
        Unit pick = null;
        foreach (var u in Main.Units)
        {
            if (!u.Alive || u.Team != 1)
                continue;
            float d = Mathf.Abs(u.CPos.X - _spot.X);   // along the lane only
            if (d < best)
            {
                best = d;
                pick = u;
            }
        }
        var warn = new Color(1, 0.75f, 0.2f);
        if (pick == null)
            Main.Msg("NO TARGET UNDER CROSSHAIR", warn, 1.5f);
        else if (!Main.Jet.Designate(pick))
            Main.Msg("POD CAN'T SEE " + pick.DisplayName + " FROM HERE", warn, 1.5f);
    }

    /// Moves the crosshair along the lane. Right/left matches the main view
    /// (east/west); up/down matches the pod picture (away from / towards the jet).
    /// Faster mouse moves go further, and zooming out moves faster too.
    void Slew(Vector2 mouse)
    {
        float mpp = _width / PicSize.X;   // metres per picture pixel at the aim point
        float away = _spot.X >= Main.Jet.Pos.X ? 1f : -1f;
        float px = mouse.X - mouse.Y * away;
        float dx = px * mpp * (1f + Mathf.Abs(px) * 0.1f);
        float x = Mathf.Clamp(_spot.X + dx, Main.Jet.Pos.X - 12000f, Main.Jet.Pos.X + 12000f);
        x = Mathf.Clamp(x, Terrain.XMin, Terrain.XMax);
        _spot = new Vector3(x, Main.Terrain.Height(x), 0f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Input.IsActionJustPressed("flir"))
            On = !On;
        var j = Main.Jet;
        bool show = On && j.Alive && Main.State == GameState.Play;
        _view.Visible = show;
        _vp.RenderTargetUpdateMode = show ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!show)
        {
            if (Slewing)
                EndSlew();
            return;
        }

        var screen = _view.GetViewportRect().Size;
        Rect = new Rect2(screen.X - PicSize.X - 24f, 170f, PicSize);
        _view.Position = Rect.Position;
        _view.Size = Rect.Size;

        var lt = j.LockTarget;
        Target = lt != null && IsInstanceValid(lt) ? lt : null;   // may be a wreck
        Vector3 want;
        if (Target != null)
        {
            want = new Vector3(Target.CPos.X, Target.CPos.Y, Target.LaneZ);
            _spot = want;   // if the lock drops in manual mode, stay where the target was
        }
        else
            want = Manual ? _spot : ScanPoint(j);
        // The pod slews and zooms smoothly instead of snapping, except under the mouse.
        float k = 1f - Mathf.Exp(-dt * 6f);
        _aim = _aimSet && !Slewing ? _aim.Lerp(want, k) : want;
        _aimSet = true;
        float wantWidth = Manual ? _manualWidth : Target != null ? 70f : 400f;   // metres of ground across the picture
        _width = Slewing ? wantWidth : Mathf.Lerp(_width, wantWidth, k);

        var eye = new Vector3(j.Pos.X, j.Pos.Y - 3f, 0f);
        var dir = _aim - eye;
        Range = dir.Length();
        if (Range < 1f)
            return;
        _cam.Position = eye;
        _cam.LookAt(_aim, Mathf.Abs(dir.Y / Range) > 0.99f ? Vector3.Right : Vector3.Up);
        _cam.Fov = Mathf.Clamp(Mathf.RadToDeg(2f * Mathf.Atan(_width * 0.5f / Range)), 0.3f, 40f);
    }

    /// Where a line 15 degrees below the horizon, in the direction of flight, meets the ground.
    Vector3 ScanPoint(Jet j)
    {
        float sx = j.Vel.X >= 0f ? 1f : -1f;
        var eye = new Vector3(j.Pos.X, j.Pos.Y, 0f);
        var dir = new Vector3(sx * Mathf.Cos(0.26f), -Mathf.Sin(0.26f), 0f);
        return GroundHit(eye, dir, 9000f) ?? eye + dir * 9000f;
    }

    /// First point along a ray that's below the ground (the ground height only depends on x).
    Vector3? GroundHit(Vector3 from, Vector3 dir, float maxDist = 12000f)
    {
        for (float d = 50f; d <= maxDist; d += 25f)
        {
            var p = from + dir * d;
            float h = Main.Terrain.Height(p.X);
            if (p.Y <= h)
                return new Vector3(p.X, h, p.Z);
        }
        return null;
    }
}
