using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// Game root, attached to the "Game" node of game.tscn. It connects the scenes
/// you built in the editor (world, jet, units, camera, HUD), spawns waves,
/// projectiles and effects, checks hits and moves the camera.
/// Gameplay is 2D (x right, y up, everything on z = 0); rendering is full 3D.
public partial class Game : Node3D
{
    public const float BulletK = 2.0e-4f;   // drag constants: a = -k * v * |v| * density
    public const float ShellK = 3.0e-5f;
    public const float BombK = 1.1e-4f;
    public const float FlareK = 2.5e-3f;
    const int MaxDebris = 140;
    const int MaxScorch = 60;

    [ExportGroup("Units")]
    [Export] public PackedScene TankScene;
    [Export] public PackedScene TruckScene;
    [Export] public PackedScene AaaScene;
    [Export] public PackedScene SamScene;
    [Export] public PackedScene FriendlyScene;
    [ExportGroup("Weapons")]
    [Export] public PackedScene MaverickScene;
    [Export] public PackedScene SamMissileScene;
    [Export] public PackedScene BombModel;
    [ExportGroup("Effects")]
    [Export] public Texture2D ParticleHard;
    [Export] public Texture2D ParticleSoft;

    /// X of the friendly front line; read from the "FriendlyLine" marker in world.tscn.
    public float FriendlyLine = 3800f;
    public Terrain Terrain;
    public Jet Jet;
    public Camera3D Camera;
    public Audio Audio;
    Hud _hud;
    Node3D _unitsNode;
    public readonly List<Unit> Units = new();
    readonly List<Shell> _shells = new();
    public readonly List<Missile> Missiles = new();
    readonly List<RigidBody3D> _debris = new();
    readonly List<MeshInstance3D> _scorch = new();
    public readonly List<Message> Messages = new();
    public int Wave;
    public float WaveTimer = 8f;
    public bool WaveActive;
    public int Score;
    public int Kills;
    public int FriendlyKills;
    public float Shake;
    float _zoom = 1f;
    public float CamDist = 220f;
    Vector2 _camOffset;
    public bool Paused;
    public GameState State = GameState.Play;
    public float StateTime;
    public bool ShowHelp = true;
    int _impactCount;
    DirectionalLight3D _sun;

    BoxMesh _tracerMesh;
    StandardMaterial3D _tracerMat;
    StandardMaterial3D _enemyTracerMat;
    StandardMaterial3D _shellTracerMat;
    public QuadMesh FireQuad;
    public QuadMesh SmokeQuad;
    QuadMesh _sparkQuad;
    public StandardMaterial3D CharredMat;
    BoxMesh _debrisMesh;
    BoxShape3D _debrisShape;
    StandardMaterial3D _debrisMat;
    CylinderMesh _scorchMesh;
    StandardMaterial3D _flareMat;
    public Gradient FireRamp;
    public Gradient SmokeRamp;
    Gradient _dustRamp;
    Gradient _sparkRamp;
    public Gradient TrailRamp;
    Gradient _flakRamp;
    Curve _growCurve;

    public override void _Ready()
    {
        // "1.5" not "1,5" in HUD text, whatever the system locale.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GD.Randomize();
        SetupInput();
        BuildResources();

        // Nodes you placed in the editor. FindChild(..., owned: false) also
        // searches inside instanced scenes such as world.tscn.
        Audio = GetNode<Audio>("Audio");
        Audio.Main = this;
        Terrain = (Terrain)FindChild("Terrain", true, false);
        Terrain.Build();
        _sun = FindChild("Sun", true, false) as DirectionalLight3D;
        if (FindChild("FriendlyLine", true, false) is Node3D line)
            FriendlyLine = line.GlobalPosition.X;
        PlaceGroundProps();
        AddFriendlySmoke();

        _unitsNode = GetNode<Node3D>("Units");
        foreach (var u in _unitsNode.GetChildren().OfType<Unit>())
        {
            u.Init(this, u.Position.X, u.Position.Z);
            Units.Add(u);
        }
        Jet = GetNode<Jet>("Jet");
        Jet.Init(this);
        Camera = GetNode<Camera3D>("Camera");
        _hud = GetNode<Hud>("HudLayer/Hud");
        _hud.Main = this;

        Msg("TASKING: CLOSE AIR SUPPORT", new Color(0.6f, 1.0f, 0.6f), 6f);
        Msg("Friendly armor holding at the green smoke. Enemy column inbound from the east.", Colors.White, 7f);
        UpdateCamera(1f);
    }

    // ---------------------------------------------------------------- setup

    static void SetupInput()
    {
        var binds = new Dictionary<string, Key[]>
        {
            ["pitch_up"] = new[] { Key.W, Key.Up },
            ["pitch_down"] = new[] { Key.S, Key.Down },
            ["throttle_up"] = new[] { Key.Shift, Key.E },
            ["throttle_down"] = new[] { Key.Ctrl, Key.Q },
            ["fire_gun"] = new[] { Key.Space },
            ["drop_bomb"] = new[] { Key.B },
            ["fire_missile"] = new[] { Key.M },
            ["cycle_target"] = new[] { Key.T },
            ["flip"] = new[] { Key.F },
            ["flares"] = new[] { Key.X },
            ["gear"] = new[] { Key.G },
            ["airbrake"] = new[] { Key.Z },
            ["restart"] = new[] { Key.R },
            ["help"] = new[] { Key.H },
            ["pause"] = new[] { Key.P, Key.Escape },
            ["zoom_in"] = new[] { Key.Equal, Key.KpAdd },
            ["zoom_out"] = new[] { Key.Minus, Key.KpSubtract },
        };
        foreach (var (action, keys) in binds)
        {
            if (InputMap.HasAction(action))
                continue;
            InputMap.AddAction(action);
            foreach (var k in keys)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
        }
    }

    static StandardMaterial3D ParticleMat(bool additive, Texture2D tex) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        AlbedoTexture = tex,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// Materials, meshes and colour ramps shared by every effect.
    void BuildResources()
    {
        FireQuad = new QuadMesh { Material = ParticleMat(true, ParticleHard) };
        SmokeQuad = new QuadMesh { Material = ParticleMat(false, ParticleSoft) };
        _sparkQuad = new QuadMesh { Size = new Vector2(0.4f, 0.4f), Material = ParticleMat(true, ParticleHard) };

        _tracerMesh = new BoxMesh { Size = new Vector3(7f, 0.22f, 0.22f) };
        _tracerMat = Fx.Glow(new Color(1.0f, 0.85f, 0.4f), 6f);
        _enemyTracerMat = Fx.Glow(new Color(1.0f, 0.35f, 0.2f), 6f);
        _shellTracerMat = Fx.Glow(new Color(1.0f, 0.6f, 0.3f), 4f);
        _flareMat = Fx.Glow(new Color(1.0f, 0.9f, 0.7f), 10f);

        CharredMat = Fx.Mat(new Color(0.07f, 0.065f, 0.06f), 1f);
        _debrisMesh = new BoxMesh { Size = new Vector3(0.7f, 0.4f, 0.6f) };
        _debrisShape = new BoxShape3D { Size = new Vector3(0.7f, 0.4f, 0.6f) };
        _debrisMat = Fx.Mat(new Color(0.12f, 0.11f, 0.1f), 1f);
        _scorchMesh = new CylinderMesh
        {
            TopRadius = 1f,
            BottomRadius = 1f,
            Height = 0.3f,
            RadialSegments = 14,
            Material = Fx.Mat(new Color(0.09f, 0.075f, 0.06f), 1f),
        };

        FireRamp = Fx.Ramp(new[] { 0f, 0.2f, 0.55f, 1f }, new[] {
            new Color(1, 0.95f, 0.75f, 1), new Color(1, 0.55f, 0.15f, 0.9f),
            new Color(0.5f, 0.12f, 0.04f, 0.45f), new Color(0.1f, 0.05f, 0.05f, 0f) });
        SmokeRamp = Fx.Ramp(new[] { 0f, 0.1f, 0.6f, 1f }, new[] {
            new Color(0.2f, 0.18f, 0.17f, 0f), new Color(0.2f, 0.19f, 0.18f, 0.75f),
            new Color(0.33f, 0.32f, 0.31f, 0.45f), new Color(0.5f, 0.5f, 0.5f, 0f) });
        _dustRamp = Fx.Ramp(new[] { 0f, 0.1f, 1f }, new[] {
            new Color(0.55f, 0.47f, 0.35f, 0f), new Color(0.55f, 0.47f, 0.35f, 0.7f), new Color(0.6f, 0.55f, 0.45f, 0f) });
        _sparkRamp = Fx.Ramp(new[] { 0f, 0.5f, 1f }, new[] {
            new Color(1, 1, 0.8f, 1), new Color(1, 0.6f, 0.2f, 1), new Color(0.6f, 0.2f, 0f, 0) });
        TrailRamp = Fx.Ramp(new[] { 0f, 0.08f, 1f }, new[] {
            new Color(1, 0.9f, 0.7f, 0.9f), new Color(0.85f, 0.85f, 0.85f, 0.6f), new Color(0.8f, 0.8f, 0.8f, 0f) });
        _flakRamp = Fx.Ramp(new[] { 0f, 0.15f, 1f }, new[] {
            new Color(0.1f, 0.1f, 0.1f, 0f), new Color(0.12f, 0.12f, 0.12f, 0.8f), new Color(0.3f, 0.3f, 0.3f, 0f) });
        _growCurve = new Curve();
        _growCurve.AddPoint(new Vector2(0f, 0.35f));
        _growCurve.AddPoint(new Vector2(0.3f, 0.8f));
        _growCurve.AddPoint(new Vector2(1f, 1f));
    }

    /// Drops every child of the "GroundProps" node onto the terrain, so you can
    /// place props in the editor by x/z alone.
    void PlaceGroundProps()
    {
        if (FindChild("GroundProps", true, false) is not Node3D props)
            return;
        foreach (var c in props.GetChildren().OfType<Node3D>())
        {
            var p = c.GlobalPosition;
            c.GlobalPosition = new Vector3(p.X, Terrain.Height(p.X), p.Z);
        }
    }

    /// Green smoke marks the friendly line so you know where your side is.
    void AddFriendlySmoke()
    {
        var g = Fx.Ramp(new[] { 0f, 0.1f, 1f }, new[] {
            new Color(0.3f, 0.9f, 0.35f, 0f), new Color(0.3f, 0.85f, 0.35f, 0.65f), new Color(0.6f, 0.9f, 0.6f, 0f) });
        var smoke = MakeParticles(SmokeQuad, 60, 9f, new Vector2(2, 5), new Vector2(6, 18), g, new Vector3(2.5f, 2.5f, 0), 0.3f, false, 15f);
        smoke.Position = new Vector3(FriendlyLine + 80f, Terrain.Height(FriendlyLine + 80f) + 1f, -8f);
        AddChild(smoke);
    }

    // ---------------------------------------------------------------- spawning

    Unit SpawnUnit(PackedScene scene, float x, float z)
    {
        var u = scene.Instantiate<Unit>();
        _unitsNode.AddChild(u);
        u.Init(this, x, z);
        Units.Add(u);
        return u;
    }

    void SpawnFriendly() =>
        SpawnUnit(FriendlyScene, FriendlyLine - Fx.Rand(0f, 260f), Fx.Rand(-14f, 6f));

    void StartWave()
    {
        Wave++;
        WaveActive = true;
        // Clear out old wrecks.
        foreach (var u in Units.Where(u => !u.Alive && u.WreckT > 15f).ToList())
        {
            Units.Remove(u);
            u.QueueFree();
        }
        var comp = new Dictionary<UnitKind, int>
        {
            [UnitKind.Tank] = 2 + Wave,
            [UnitKind.Truck] = 2 + Wave,
            [UnitKind.Aaa] = 1 + Wave / 2,
            [UnitKind.Sam] = Wave / 2,
        };
        var order = new List<UnitKind>();
        foreach (var (k, n) in comp)
            for (int i = 0; i < n; i++)
                order.Add(k);
        // Shuffle so each wave's column is in a different order.
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = GD.RandRange(0, i);
            (order[i], order[j]) = (order[j], order[i]);
        }
        float x = FriendlyLine + 4400f;
        foreach (var k in order)
        {
            var scene = k switch
            {
                UnitKind.Tank => TankScene,
                UnitKind.Truck => TruckScene,
                UnitKind.Aaa => AaaScene,
                _ => SamScene,
            };
            var u = SpawnUnit(scene, x, Fx.Rand(-14f, 10f));
            u.StopX = FriendlyLine + k switch
            {
                UnitKind.Tank => Fx.Rand(900f, 1500f),
                UnitKind.Truck => Fx.Rand(1800f, 2400f),
                UnitKind.Aaa => Fx.Rand(1500f, 2600f),
                _ => Fx.Rand(3000f, 3800f),   // SAMs stay far back
            };
            x += Fx.Rand(70f, 160f);
        }
        Msg($"WAVE {Wave}: {comp[UnitKind.Tank]} TANKS, {comp[UnitKind.Truck]} TRUCKS, {comp[UnitKind.Aaa]} AAA, {comp[UnitKind.Sam]} SAM",
                new Color(1, 0.75f, 0.3f), 6f);
    }

    public void SpawnRound(Vector2 p, Vector2 v, int team, bool byPlayer, bool tracer, ShellKind kind, float dmg, float z = 0f)
    {
        var s = new Shell
        {
            Kind = kind,
            Team = team,
            ByPlayer = byPlayer,
            Pos = p,
            Prev = p,
            Vel = v,
            Z = z,
            Damage = dmg,
            DragK = kind == ShellKind.Shell ? ShellK : BulletK,
            Life = kind == ShellKind.Aaa ? 3.2f : 4f,
        };
        if (tracer)
        {
            var mi = new MeshInstance3D
            {
                Mesh = _tracerMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = kind switch
                {
                    ShellKind.Aaa => _enemyTracerMat,
                    ShellKind.Shell => _shellTracerMat,
                    _ => _tracerMat,
                },
                Position = new Vector3(p.X, p.Y, z),
                Rotation = new Vector3(0, 0, v.Angle()),
            };
            AddChild(mi);
            s.Node = mi;
        }
        _shells.Add(s);
    }

    public void SpawnBomb(Vector2 p, Vector2 v)
    {
        var s = new Shell
        {
            Kind = ShellKind.Bomb,
            Team = 0,
            ByPlayer = true,
            Pos = p,
            Prev = p,
            Vel = v,
            DragK = BombK,
            Life = 90f,
            Damage = 520f,
            Blast = 22f,
            ArmTime = 1f,   // fuze arms after 1 s of fall
            Node = BombModel.Instantiate<Node3D>(),
        };
        Audio.Play("release", -4f, Fx.Rand(0.95f, 1.05f));
        AddChild(s.Node);
        s.Node.Position = new Vector3(p.X, p.Y, 0);
        _shells.Add(s);
    }

    public void SpawnFlare(Vector2 p, Vector2 v)
    {
        var s = new Shell { Kind = ShellKind.Flare, Pos = p, Prev = p, Vel = v, DragK = FlareK, Life = 3.5f };
        Audio.Play("flare", -10f, Fx.Rand(0.9f, 1.1f));
        var n = new Node3D();
        n.AddChild(Fx.Sphere(0.6f, _flareMat));
        n.AddChild(MakeParticles(FireQuad, 40, 0.6f, new Vector2(0, 1), new Vector2(1.5f, 3f), FireRamp, Vector3.Zero, 0f, false, 180f));
        n.Position = new Vector3(p.X, p.Y, 0);
        AddChild(n);
        s.Node = n;
        _shells.Add(s);
        foreach (var m in Missiles)
            m.ConsiderFlare(s);
    }

    public void SpawnMaverick(Vector2 p, Vector2 v, Unit target)
    {
        var m = MaverickScene.Instantiate<Missile>();
        AddChild(m);
        m.Init(this, p, v, target);
        Missiles.Add(m);
        Audio.Play("launch", -5f, 1.2f);
    }

    public void SpawnSam(Vector2 p, Vector2 v, ITarget target, float z)
    {
        var m = SamMissileScene.Instantiate<Missile>();
        AddChild(m);
        m.Init(this, p, v, target, z);
        Missiles.Add(m);
        Msg("SAM LAUNCH!", new Color(1, 0.3f, 0.2f), 3f);
        var p3 = new Vector3(p.X, p.Y, z);
        Audio.PlayAt("launch", p3, 2f, 1f, Audio.SoundDelay(p3));
        MuzzleFlash(p3, 1.6f);
    }

    // ---------------------------------------------------------------- simulation

    public override void _PhysicsProcess(double delta)
    {
        if (Paused)
            return;
        float dt = (float)delta;
        StateTime += dt;
        Jet.Sim(dt);
        foreach (var u in Units)
            u.Sim(dt);
        UpdateShells(dt);
        foreach (var m in Missiles)
            m.Sim(dt);
        Missiles.RemoveAll(m => m.Dead);
        UpdateWaves(dt);
        _debris.RemoveAll(d => !IsInstanceValid(d));
    }

    static bool CanHit(Shell s, Unit u)
    {
        if (s.ByPlayer)
            return true;   // the player's rounds hit everything: friendly fire is possible
        return s.Team == 1 ? u.Team == 0 : u.Team == 1;
    }

    /// Distance from point p to the segment a-b. Fast rounds move ~17 m per
    /// physics tick, so we test the whole path, not just the end point.
    static float SegDist(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float l2 = ab.LengthSquared();
        if (l2 < 0.0001f)
            return a.DistanceTo(p);
        float t = Mathf.Clamp((p - a).Dot(ab) / l2, 0f, 1f);
        return (a + ab * t).DistanceTo(p);
    }

    void UpdateShells(float dt)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            var s = _shells[i];
            if (s.Dead)
                continue;
            s.Step(dt);
            if (s.Kind == ShellKind.Flare)
            {
                if (s.Age > s.Life || s.Pos.Y < Terrain.Height(s.Pos.X))
                    s.Kill();
                continue;
            }
            float reach = Mathf.Abs(s.Vel.X) * dt + 12f;
            Unit hit = null;
            foreach (var u in Units)
            {
                if (!u.Alive || Mathf.Abs(u.CPos.X - s.Pos.X) > reach || !CanHit(s, u))
                    continue;
                if (SegDist(s.Prev, s.Pos, u.CPos) < u.Radius)
                {
                    hit = u;
                    break;
                }
            }
            if (hit != null)
            {
                ShellHitUnit(s, hit);
                continue;
            }
            if (s.Team == 1 && Jet.Alive && Mathf.Abs(Jet.Pos.X - s.Pos.X) < reach + 10f
                    && SegDist(s.Prev, s.Pos, Jet.Pos) < 7.5f)
            {
                Jet.TakeDamage(s.Damage);
                s.Kill();
                continue;
            }
            float gh = Terrain.Height(s.Pos.X);
            if (s.Pos.Y <= gh)
            {
                ShellGround(s, new Vector2(s.Pos.X, gh));
                continue;
            }
            if (s.Age > s.Life)
            {
                if (s.Kind == ShellKind.Aaa)   // self-destruct puff
                {
                    var f = MakeParticles(SmokeQuad, 6, 2f, new Vector2(1, 3), new Vector2(3, 6), _flakRamp, Vector3.Zero, 1f);
                    FireOnce(f, new Vector3(s.Pos.X, s.Pos.Y, s.Z), 3f);
                }
                s.Kill();
            }
        }
        _shells.RemoveAll(s => s.Dead);
    }

    void ShellHitUnit(Shell s, Unit u)
    {
        var p = s.Pos;
        switch (s.Kind)
        {
            case ShellKind.Bullet:
            case ShellKind.Aaa:
                u.TakeDamage(s.Damage * u.GunArmor, s.ByPlayer, s.Vel.Normalized());
                _impactCount++;
                if (_impactCount % 2 == 0)
                    Sparks(new Vector3(p.X, p.Y, u.LaneZ), 6);
                break;
            case ShellKind.Shell:
                u.TakeDamage(s.Damage, s.ByPlayer, s.Vel.Normalized());
                SpawnExplosion(new Vector3(p.X, p.Y, u.LaneZ), 0.35f);
                break;
            case ShellKind.Bomb:
                if (s.Age >= s.ArmTime)
                {
                    Explode(p, u.LaneZ, s.Blast, s.Damage, true, 1f);
                    Scorch(p.X, s.Blast * 0.5f);
                }
                else
                {
                    u.TakeDamage(60f, true, s.Vel.Normalized());
                    Msg("DUD - bomb not armed (release higher)", new Color(1, 0.75f, 0.2f), 2.5f);
                }
                break;
        }
        s.Kill();
    }

    void ShellGround(Shell s, Vector2 p)
    {
        switch (s.Kind)
        {
            case ShellKind.Bullet:
            case ShellKind.Aaa:
                _impactCount++;
                if (_impactCount % 3 == 0)
                {
                    var d = MakeParticles(SmokeQuad, 5, 0.9f, new Vector2(4, 12), new Vector2(1f, 2.6f), _dustRamp, new Vector3(0, -6, 0), 2f, true, 30f);
                    FireOnce(d, new Vector3(p.X, p.Y, s.Z), 1.5f);
                }
                break;
            case ShellKind.Shell:
                Explode(p, s.Z, 5f, 30f, s.ByPlayer, 0.35f, false);
                break;
            case ShellKind.Bomb:
                if (s.Age >= s.ArmTime)
                {
                    Explode(p, 0f, s.Blast, s.Damage, true, 1f);
                    Scorch(p.X, s.Blast * 0.5f);
                }
                else
                {
                    var d = MakeParticles(SmokeQuad, 20, 2f, new Vector2(4, 12), new Vector2(3, 6), _dustRamp, new Vector3(0, -4, 0), 1f);
                    FireOnce(d, new Vector3(p.X, p.Y, 0), 3f);
                    Msg("DUD - bomb not armed (release higher)", new Color(1, 0.75f, 0.2f), 2.5f);
                }
                break;
        }
        s.Kill();
    }

    /// Blast damage with falloff, frag damage to a jet that is too close, and a push on loose debris.
    public void Explode(Vector2 p, float z, float radius, float damage, bool byPlayer, float size, bool hurtJet = true)
    {
        SpawnExplosion(new Vector3(p.X, p.Y, z), size);
        foreach (var u in Units)
        {
            if (!u.Alive)
                continue;
            float d = u.CPos.DistanceTo(p);
            float dmg = 0f;
            if (d < radius)
                dmg = damage * (1f - 0.5f * d / radius);
            else if (d < radius * 2.5f)
                dmg = damage * 0.5f * Mathf.Pow(1f - (d - radius) / (radius * 1.5f), 2f);
            if (dmg > 0f)
                u.TakeDamage(dmg, byPlayer, (u.CPos - p).Normalized());
        }
        if (hurtJet && Jet.Alive)
        {
            float frag = radius * 7f * size;
            float dj = Jet.Pos.DistanceTo(p);
            if (dj < frag)
                Jet.TakeDamage(45f * size * Mathf.Pow(1f - dj / frag, 2f));
        }
        var p3 = new Vector3(p.X, p.Y, z);
        foreach (var rb in _debris)
        {
            if (!IsInstanceValid(rb))
                continue;
            var off = rb.GlobalPosition - p3;
            float d = off.Length();
            if (d < radius * 3f && d > 0.01f)
                rb.ApplyCentralImpulse(off / d * rb.Mass * 25f * size * (1f - d / (radius * 3f)));
        }
    }

    // ---------------------------------------------------------------- effects

    public CpuParticles3D MakeParticles(Mesh quad, int amount, float life, Vector2 vel, Vector2 scl, Gradient ramp,
            Vector3 grav, float damp = 0f, bool oneShot = true, float spread = 180f, Vector3? dir = null, float radius = 0f)
    {
        var p = new CpuParticles3D
        {
            Mesh = quad,
            Amount = amount,
            Lifetime = life,
            OneShot = oneShot,
            Explosiveness = oneShot ? 0.95f : 0f,
            Direction = dir ?? Vector3.Up,
            Spread = spread,
            InitialVelocityMin = vel.X,
            InitialVelocityMax = vel.Y,
            ScaleAmountMin = scl.X,
            ScaleAmountMax = scl.Y,
            ScaleAmountCurve = _growCurve,
            ColorRamp = ramp,
            Gravity = grav,
            DampingMin = damp,
            DampingMax = damp,
            AngleMin = 0f,
            AngleMax = 360f,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (radius > 0f)
        {
            p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
            p.EmissionSphereRadius = radius;
        }
        return p;
    }

    /// Adds a one-shot emitter at `at`, starts it and frees it later.
    void FireOnce(CpuParticles3D p, Vector3 at, float freeAfter)
    {
        p.Position = at;
        AddChild(p);
        p.Emitting = true;
        FreeLater(p, freeAfter);
    }

    void FreeLater(Node n, float seconds)
    {
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (IsInstanceValid(n))
                n.QueueFree();
        };
    }

    public void SpawnExplosion(Vector3 p, float size)
    {
        bool nearGround = p.Y - Terrain.Height(p.X) < 6f;
        string snd = size >= 0.9f ? "boom" : "boom_small";
        float pitch = Fx.Rand(0.85f, 1.1f) * (size < 0.5f ? 1.25f : 1f);
        Audio.PlayAt(snd, p, Mathf.LinearToDb(Mathf.Clamp(size, 0.3f, 2.5f)) + 2f, pitch, Audio.SoundDelay(p));
        FireOnce(MakeParticles(FireQuad, (int)(18 + 14 * size), 0.9f, new Vector2(6, 22) * size, new Vector2(5, 11) * size,
                FireRamp, new Vector3(0, 4, 0), 4f, true, 180f, Vector3.Up, 1.5f * size), p, 2f);
        FireOnce(MakeParticles(SmokeQuad, (int)(10 + 10 * size), 4.5f, new Vector2(3, 11) * size, new Vector2(9, 18) * size,
                SmokeRamp, new Vector3(1.5f, 3.5f, 0), 1.5f, true, 120f, Vector3.Up, 2f * size), p, 6f);
        FireOnce(MakeParticles(_sparkQuad, (int)(20 * size) + 6, 1.6f, new Vector2(20, 55) * size, new Vector2(0.6f, 1.4f),
                _sparkRamp, new Vector3(0, -9.8f, 0), 0.5f), p, 2.5f);
        if (nearGround)
            FireOnce(MakeParticles(SmokeQuad, (int)(14 * size) + 4, 2.5f, new Vector2(10, 30) * size, new Vector2(4, 9) * size,
                    _dustRamp, new Vector3(0, -2, 0), 2.5f, true, 80f, Vector3.Up), p, 3.5f);
        var light = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.65f, 0.3f),
            OmniRange = 40f * size,
            LightEnergy = 8f,
            Position = p + new Vector3(0, 3, 0),
        };
        AddChild(light);
        var tw = CreateTween();
        tw.TweenProperty(light, "light_energy", 0f, 0.5);
        tw.TweenCallback(Callable.From(light.QueueFree));
        if (size >= 0.6f)
            SpawnDebris(p, (int)(5 * size), 14f * size);
        float camD = Camera != null ? new Vector2(Camera.Position.X, Camera.Position.Y).DistanceTo(new Vector2(p.X, p.Y)) : 1e9f;
        Shake = Mathf.Max(Shake, size * Mathf.Clamp(1.2f - camD / 1800f, 0f, 1f));
    }

    void Sparks(Vector3 p, int n) =>
        FireOnce(MakeParticles(_sparkQuad, n, 0.4f, new Vector2(8, 25), new Vector2(0.6f, 1.2f), _sparkRamp, new Vector3(0, -9.8f, 0)), p, 1f);

    public void MuzzleFlash(Vector3 p, float size)
    {
        FireOnce(MakeParticles(FireQuad, 8, 0.25f, new Vector2(4, 10) * size, new Vector2(1.5f, 3f) * size, FireRamp,
                Vector3.Zero, 3f), p, 1f);
        FireOnce(MakeParticles(SmokeQuad, 6, 1.6f, new Vector2(2, 6) * size, new Vector2(2.5f, 5f) * size, _dustRamp,
                new Vector3(0, 1, 0), 2f), p, 2.5f);
    }

    /// Physics-driven chunks that bounce off the terrain collision band.
    public void SpawnDebris(Vector3 p, int count, float speed, Vector3 inherit = default)
    {
        for (int i = 0; i < count; i++)
        {
            var rb = new RigidBody3D
            {
                Mass = Fx.Rand(10f, 40f),
                Position = p + new Vector3(Fx.Rand(-1.5f, 1.5f), Fx.Rand(0.5f, 2f), Fx.Rand(-1.5f, 1.5f)),
            };
            rb.AddChild(new MeshInstance3D
            {
                Mesh = _debrisMesh,
                MaterialOverride = _debrisMat,
                Scale = new Vector3(Fx.Rand(0.6f, 2f), Fx.Rand(0.5f, 1.5f), Fx.Rand(0.6f, 1.8f)),
            });
            rb.AddChild(new CollisionShape3D { Shape = _debrisShape });
            AddChild(rb);
            var d = new Vector3(Fx.Rand(-1, 1), Fx.Rand(0.4f, 1.2f), Fx.Rand(-0.6f, 0.6f)).Normalized();
            rb.LinearVelocity = inherit + d * speed * Fx.Rand(0.5f, 1.3f);
            rb.AngularVelocity = new Vector3(Fx.Rand(-8, 8), Fx.Rand(-8, 8), Fx.Rand(-8, 8));
            if (i % 3 == 0)
            {
                var t = MakeParticles(SmokeQuad, 24, 1.4f, new Vector2(0, 1), new Vector2(1.5f, 3.5f), SmokeRamp, new Vector3(0, 2, 0), 1f, false);
                rb.AddChild(t);
                GetTree().CreateTimer(Fx.Rand(1.5f, 3f)).Timeout += () =>
                {
                    if (IsInstanceValid(t))
                        t.Emitting = false;
                };
            }
            TrackDebris(rb, Fx.Rand(6f, 10f));
        }
    }

    void TrackDebris(RigidBody3D rb, float life)
    {
        _debris.Add(rb);
        FreeLater(rb, life);
        while (_debris.Count > MaxDebris)
        {
            var old = _debris[0];
            _debris.RemoveAt(0);
            if (IsInstanceValid(old))
                old.QueueFree();
        }
    }

    /// Turns a node (e.g. a turret) into a burning rigid body flung into the air.
    public void TossPart(Node3D node, Vector3 v, Vector3 extents, float m)
    {
        var rb = new RigidBody3D();
        var gt = node.GlobalTransform;
        AddChild(rb);
        rb.GlobalTransform = gt;
        node.Reparent(rb);
        rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = extents } });
        rb.Mass = m;
        rb.LinearVelocity = v;
        rb.AngularVelocity = new Vector3(Fx.Rand(-4, 4), Fx.Rand(-4, 4), Fx.Rand(-4, 4));
        var fire = MakeWreckFire();
        fire.Scale = Vector3.One * 0.6f;
        rb.AddChild(fire);
        _debris.Add(rb);
        FreeLater(rb, 30f);
    }

    public Node3D MakeWreckFire()
    {
        var n = new Node3D();
        n.AddChild(MakeParticles(FireQuad, 16, 0.7f, new Vector2(1, 3), new Vector2(1.5f, 3.5f), FireRamp, new Vector3(0, 5, 0), 1f, false, 25f));
        n.AddChild(MakeParticles(SmokeQuad, 30, 6f, new Vector2(2, 5), new Vector2(4, 10), SmokeRamp, new Vector3(1.5f, 3f, 0), 0.6f, false, 15f));
        return n;
    }

    void Scorch(float x, float r)
    {
        float slope = (Terrain.Height(x + 2f) - Terrain.Height(x - 2f)) / 4f;
        var mi = new MeshInstance3D
        {
            Mesh = _scorchMesh,
            Scale = new Vector3(r, 1, r),
            Position = new Vector3(x, Terrain.Height(x), Fx.Rand(-3f, 3f)),
            Rotation = new Vector3(0, 0, Mathf.Atan(slope)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        _scorch.Add(mi);
        if (_scorch.Count > MaxScorch)
        {
            _scorch[0].QueueFree();
            _scorch.RemoveAt(0);
        }
    }

    // ---------------------------------------------------------------- game flow

    public Unit FindEnemyUnit(Unit from, float maxRange)
    {
        Unit best = null;
        float bd = maxRange;
        foreach (var u in Units)
        {
            if (u.Alive && u.Team != from.Team)
            {
                float d = Mathf.Abs(u.X - from.X);
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
        }
        return best;
    }

    public int CountTeam(int t) => Units.Count(u => u.Alive && u.Team == t);

    /// True while a guided SAM is chasing the jet (drives the HUD and audio warnings).
    public bool SamInbound() => Missiles.Any(m => !m.IsAgm && !m.Dead && m.Guided && m.Target == Jet);

    /// Ground-proximity warning: less than 4.5 s to impact at the current sink rate.
    public bool PullUpWarning()
    {
        if (Jet.OnGround)
            return false;
        float sink = -Jet.Vel.Y;
        return sink > 5f && Jet.Agl / sink < 4.5f && !(Jet.GearDown && Terrain.OnRunway(Jet.Pos.X));
    }

    public void OnUnitKilled(Unit u, bool byPlayer)
    {
        if (u.Team == 1)
        {
            Kills++;
            if (byPlayer)
            {
                Score += u.Value;
                Msg($"SPLASH {u.DisplayName}  +{u.Value}", new Color(0.6f, 1.0f, 0.6f), 3f);
            }
            else
                Msg($"Friendly armor killed a {u.DisplayName}", new Color(0.8f, 0.9f, 0.8f), 3f);
        }
        else if (byPlayer)
        {
            Score -= 400;
            FriendlyKills++;
            Msg("FRIENDLY FIRE! CEASE FIRE!  -400", new Color(1, 0.25f, 0.2f), 4f);
        }
        else
            Msg($"Friendly {u.DisplayName} destroyed", new Color(1, 0.55f, 0.3f), 3f);
    }

    /// Ends the game when the jet is lost. The jet just disappears.
    public void OnJetDestroyed(string reason)
    {
        State = GameState.Dead;
        StateTime = 0f;
        Msg(reason, new Color(1, 0.3f, 0.2f), 8f);
    }

    void UpdateWaves(float dt)
    {
        if (State != GameState.Play)
            return;
        if (!WaveActive)
        {
            WaveTimer -= dt;
            if (WaveTimer <= 0f)
                StartWave();
            return;
        }
        if (CountTeam(1) == 0)
        {
            WaveActive = false;
            WaveTimer = 25f;
            Score += 300 * Wave;
            Msg($"WAVE {Wave} CLEARED  +{300 * Wave}  -  land on the runway to rearm", new Color(0.6f, 1.0f, 0.6f), 7f);
            int alive = CountTeam(0);
            for (int i = 0; i < Mathf.Clamp(6 - alive, 0, 2); i++)
                SpawnFriendly();
            if (alive < 6)
                Msg("Friendly reinforcements arriving", new Color(0.8f, 0.9f, 0.8f), 4f);
        }
        else if (CountTeam(0) == 0)
        {
            State = GameState.Overrun;
            StateTime = 0f;
            Msg("FRIENDLY POSITION OVERRUN", new Color(1, 0.3f, 0.2f), 10f);
        }
    }

    public void Msg(string text, Color? col = null, float t = 4f)
    {
        Messages.Insert(0, new Message { Text = text, Time = t, Color = col ?? Colors.White });
        if (Messages.Count > 7)
            Messages.RemoveAt(Messages.Count - 1);
    }

    // ---------------------------------------------------------------- frame

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _zoom = Mathf.Clamp(_zoom * 0.9f, 0.3f, 3f);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                _zoom = Mathf.Clamp(_zoom * 1.1f, 0.3f, 3f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Input.IsActionJustPressed("restart"))
        {
            GetTree().ReloadCurrentScene();
            return;
        }
        if (Input.IsActionJustPressed("pause"))
            Paused = !Paused;
        if (Input.IsActionJustPressed("help"))
            ShowHelp = !ShowHelp;
        if (Input.IsActionPressed("zoom_in"))
            _zoom = Mathf.Clamp(_zoom * (1f - dt), 0.3f, 3f);
        if (Input.IsActionPressed("zoom_out"))
            _zoom = Mathf.Clamp(_zoom * (1f + dt), 0.3f, 3f);
        if (!Paused)
        {
            foreach (var m in Messages)
                m.Time -= dt;
            Messages.RemoveAll(m => m.Time <= 0f);
        }
        UpdateCamera(dt);
    }

    /// Side-on camera: pulls back with speed and height, leads the velocity and
    /// tries to keep the ground in frame.
    void UpdateCamera(float dt)
    {
        var p = Jet.Pos;
        var v = Jet.Alive ? Jet.Vel : Vector2.Zero;
        float gh = Terrain.Height(p.X);
        float agl = Mathf.Max(p.Y - gh, 0f);
        float want = Mathf.Max(150f + v.Length() * 0.32f, agl * 1.25f + 80f);
        want = Mathf.Clamp(want, 110f, 2400f) * _zoom;
        CamDist = Mathf.Lerp(CamDist, want, 1f - Mathf.Exp(-dt * 1.5f));
        float halfH = CamDist * Mathf.Tan(Mathf.DegToRad(Camera.Fov * 0.5f));
        var lead = v * 0.45f;
        if (lead.Length() > halfH * 0.9f)
            lead = lead.Normalized() * halfH * 0.9f;
        var wantOff = lead;
        // Keep the ground in view when it's reachable.
        float low = p.Y + wantOff.Y - halfH * 0.8f;
        if (low > gh)
            wantOff.Y -= Mathf.Min(low - gh, halfH * 0.6f);
        _camOffset = _camOffset.Lerp(wantOff, 1f - Mathf.Exp(-dt * 2.5f));
        var focus = p + _camOffset;
        Shake = Mathf.Max(Shake - dt * 1.8f, 0f);
        var sh = new Vector3(Fx.Rand(-1, 1), Fx.Rand(-1, 1), 0) * Shake * CamDist * 0.012f;
        Camera.Position = new Vector3(focus.X, focus.Y + CamDist * 0.07f, CamDist) + sh;
        Camera.LookAt(new Vector3(focus.X, focus.Y, 0) + sh * 0.5f, Vector3.Up);
        // Keep the sun's shadow map centred on the action.
        if (_sun != null)
            _sun.DirectionalShadowMaxDistance = Mathf.Clamp(CamDist * 2.5f, 600f, 4000f);
    }
}
