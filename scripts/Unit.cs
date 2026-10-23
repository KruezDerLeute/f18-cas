using System.Collections.Generic;
using Godot;

/// Ground vehicle: enemy tanks, trucks, ZSU-style AAA, SA-8 style SAMs and
/// friendly MBTs. Attached to the root of each vehicle scene (scenes/units/);
/// the stats are set in the Inspector, and the model sits under a "Body" node.
/// Hit testing uses the 2D lane position.
public partial class Unit : Node3D, ITarget
{
    [Export] public UnitKind Kind = UnitKind.Tank;
    [Export] public string DisplayName = "T-72";
    /// 0 = friendly, 1 = enemy.
    [Export] public int Team = 1;
    [Export] public float Hp = 100f;
    /// Hit circle radius, metres.
    [Export] public float Radius = 4.5f;
    /// Height of the hull centre above the ground, metres.
    [Export] public float CenterHeight = 1.6f;
    /// Driving speed, m/s (enemies only).
    [Export] public float Speed = 7f;
    /// Multiplier on cannon damage: low = armoured.
    [Export] public float GunArmor = 0.3f;
    /// Points for destroying it.
    [Export] public int Value = 100;

    public Game Main;
    public float MaxHp = 100f;
    public float X;
    public float LaneZ;
    public float StopX;
    public bool Alive = true;
    float _cooldown = 3f;
    int _ammo;
    bool _moving;
    public Vector2 CPos;             // centre of the hull in the 2D plane
    public Vector2 CVel;
    float _facing = -1f;
    Node3D _body;
    Node3D _turret;
    Node3D _gunPivot;
    readonly List<MeshInstance3D> _parts = new();
    readonly List<MeshInstance3D> _heat = new();
    readonly List<Node3D> _samRounds = new();
    float _burstT;
    bool _burstOn;
    bool _burstHeard;
    float _shotAcc;
    float _lockT;
    public float WreckT;

    /// Called by Game: for units placed in the editor and for units spawned by waves.
    public void Init(Game main, float x, float laneZ)
    {
        Main = main;
        X = x;
        LaneZ = laneZ;
        _facing = Team == 1 ? -1f : 1f;
        _body = GetNode<Node3D>("Body");
        _body.Rotation = new Vector3(0, _facing > 0f ? 0f : Mathf.Pi, 0);
        _cooldown = Fx.Rand(2f, 5f);
        MaxHp = Hp;
        FindParts();
        Place();
    }

    /// Finds the parts the AI animates, by name, inside the model:
    /// "Turret", "GunPivot" and the SA-8's "Missile0".."Missile3".
    void FindParts()
    {
        _turret = _body.FindChild("Turret", true, false) as Node3D;
        _gunPivot = _body.FindChild("GunPivot", true, false) as Node3D;
        foreach (var n in _body.FindChildren("*", "MeshInstance3D", true, false))
            _parts.Add((MeshInstance3D)n);
        for (int i = 0; _gunPivot != null && i < 4; i++)
            if (_gunPivot.FindChild($"Missile{i}", false, false) is Node3D r)
                _samRounds.Add(r);
        _ammo = _samRounds.Count;
        AddHeatCopies();
    }

    /// Gives every part a hot twin that only the targeting pod sees (see Flir).
    /// It's a child of the part, so it follows turret turns and tossed turrets.
    void AddHeatCopies()
    {
        foreach (var p in _parts)
        {
            p.Layers = 1u << (Flir.ColdLayer - 1);
            var h = new MeshInstance3D
            {
                Mesh = p.Mesh,
                MaterialOverride = Main.HotMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = 1u << (Flir.HotLayer - 1),
            };
            p.AddChild(h);
            _heat.Add(h);
        }
    }

    /// Sits the vehicle on the ground and tilts it to the slope.
    void Place()
    {
        var t = Main.Terrain;
        float h = t.Height(X);
        float slope = (t.Height(X + 3f) - t.Height(X - 3f)) / 6f;
        Position = new Vector3(X, h, LaneZ);
        Rotation = new Vector3(0, 0, Mathf.Atan(slope));
        CPos = new Vector2(X, h + CenterHeight);
    }

    Vector2 MuzzlePos() => CPos + new Vector2(_facing * 5.5f, 0.9f);

    // ---------------------------------------------------------------- AI

    public void Sim(float dt)
    {
        if (!Alive)
        {
            WreckT += dt;
            return;
        }
        _cooldown -= dt;
        float old = X;
        _moving = false;
        if (Team == 1 && X > StopX)
        {
            X -= Speed * dt;
            _moving = true;
        }
        CVel = new Vector2((X - old) / dt, 0f);
        Place();
        switch (Kind)
        {
            case UnitKind.Tank:
            case UnitKind.FriendlyTank:
                AiTank(dt);
                break;
            case UnitKind.Aaa:
                AiAaa(dt);
                break;
            case UnitKind.Sam:
                AiSam(dt);
                break;
        }
    }

    void AiTank(float dt)
    {
        var tgt = Main.FindEnemyUnit(this, 2400f);
        if (tgt == null)
        {
            _gunPivot.SetRotZ(Mathf.MoveToward(_gunPivot.Rotation.Z, 0f, dt * 0.3f));
            return;
        }
        var muzzle = MuzzlePos();
        var rel = tgt.CPos - muzzle;
        const float v = 950f;
        // Ballistic elevation: aim angle plus half the angle the shell drops over the range.
        float ang = Mathf.Atan2(rel.Y, Mathf.Abs(rel.X)) + 0.5f * Mathf.Asin(Mathf.Clamp(9.81f * Mathf.Abs(rel.X) / (v * v), -1f, 1f));
        _gunPivot.SetRotZ(Mathf.MoveToward(_gunPivot.Rotation.Z, ang - Rotation.Z * _facing, dt * 0.5f));
        if (_cooldown <= 0f)
        {
            _cooldown = Fx.Rand(4.5f, 7.5f);
            float spread = _moving ? 0.006f : 0.003f;
            float a = ang + Fx.Rand(-spread, spread);
            float dirx = Mathf.Sign(rel.X);
            Main.SpawnRound(muzzle, new Vector2(Mathf.Cos(a) * dirx, Mathf.Sin(a)) * v, Team, false, true, ShellKind.Shell, 38f, LaneZ);
            var mp = new Vector3(muzzle.X, muzzle.Y, LaneZ);
            Main.MuzzleFlash(mp, 1f);
            Main.Audio.PlayAt("cannon", mp, -2f, Fx.Rand(0.9f, 1.1f), Main.Audio.SoundDelay(mp));
        }
    }

    void AiAaa(float dt)
    {
        var jet = Main.Jet;
        _burstT -= dt;
        var muzzle = CPos + new Vector2(0, 1.8f);
        if (!jet.Alive || jet.OnGround)
            return;
        var rel = jet.Pos - muzzle;
        float d = rel.Length();
        if (d > 2200f || rel.Y < 25f)
        {
            _burstOn = false;
            return;
        }
        // Lead the target assuming it keeps flying straight.
        const float v = 970f;
        var aim = jet.Pos;
        for (int i = 0; i < 3; i++)
        {
            float t = (aim - muzzle).Length() / v;
            aim = jet.Pos + jet.Vel * t + new Vector2(0, 0.5f * 9.81f * t * t);
        }
        var ar = aim - muzzle;
        float wantYaw = ar.X * _facing >= 0f ? 0f : Mathf.Pi;
        _turret.SetRotY(Mathf.MoveToward(_turret.Rotation.Y, wantYaw, dt * 1.2f));
        float el = Mathf.Atan2(ar.Y, Mathf.Abs(ar.X));
        // Turret slew is rate limited: fast crossing targets outrun it.
        _gunPivot.SetRotZ(Mathf.MoveToward(_gunPivot.Rotation.Z, Mathf.Clamp(el, 0f, 1.45f), dt * 0.9f));
        if (_burstT <= 0f)
        {
            _burstOn = !_burstOn;
            _burstT = _burstOn ? Fx.Rand(1.0f, 1.8f) : Fx.Rand(2.5f, 4.5f);
            _burstHeard = false;
        }
        bool aligned = Mathf.Abs(_turret.Rotation.Y - wantYaw) < 0.05f && Mathf.Abs(_gunPivot.Rotation.Z - el) < 0.08f;
        if (_burstOn && aligned)
        {
            if (!_burstHeard)
            {
                _burstHeard = true;
                var mp = new Vector3(muzzle.X, muzzle.Y, LaneZ);
                Main.Audio.PlayAt("aaa", mp, -1f, Fx.Rand(0.9f, 1.1f), Main.Audio.SoundDelay(mp));
            }
            _shotAcc += dt * 12f;
            float xdir = _facing * (_turret.Rotation.Y < Mathf.Pi * 0.5f ? 1f : -1f);
            while (_shotAcc >= 1f)
            {
                _shotAcc -= 1f;
                float a = _gunPivot.Rotation.Z + Fx.Rand(-0.02f, 0.02f);
                var dv = new Vector2(Mathf.Cos(a) * xdir, Mathf.Sin(a));
                Main.SpawnRound(muzzle + dv * 3f, dv * v, 1, false, true, ShellKind.Aaa, 3f, LaneZ);
            }
        }
    }

    void AiSam(float dt)
    {
        var jet = Main.Jet;
        if (_ammo <= 0 || !jet.Alive)
        {
            _lockT = 0f;
            return;
        }
        var rel = jet.Pos - CPos;
        float d = rel.Length();
        // Radar can't hold a target in ground clutter: fly low to stay unlocked.
        bool masked = jet.Agl < 45f || d > 8000f || rel.Y < 60f;
        if (masked)
        {
            _lockT = Mathf.Max(_lockT - dt * 2f, 0f);
            return;
        }
        _lockT += dt;
        jet.RwrLock = 0.3f;
        float wantYaw = rel.X * _facing >= 0f ? 0f : Mathf.Pi;
        _turret.SetRotY(Mathf.MoveToward(_turret.Rotation.Y, wantYaw, dt * 1.5f));
        float el = Mathf.Clamp(Mathf.Atan2(rel.Y, Mathf.Abs(rel.X)), 0.25f, 1.2f);
        _gunPivot.SetRotZ(Mathf.MoveToward(_gunPivot.Rotation.Z, el, dt * 0.8f));
        if (_lockT > 4f && _cooldown <= 0f && Mathf.Abs(_turret.Rotation.Y - wantYaw) < 0.05f)
        {
            _cooldown = 15f;
            _ammo--;
            _samRounds[_ammo].Visible = false;
            float xdir = Mathf.Sign(rel.X);
            var dv = new Vector2(Mathf.Cos(_gunPivot.Rotation.Z) * xdir, Mathf.Sin(_gunPivot.Rotation.Z));
            Main.SpawnSam(CPos + new Vector2(0, 4f), dv * 45f, jet, LaneZ);
        }
    }

    // ---------------------------------------------------------------- damage

    public Vector2 TargetPos => CPos;
    public Vector2 TargetVel => CVel;
    public bool IsTargetValid() => Alive && IsInsideTree();

    public void TakeDamage(float dmg, bool byPlayer, Vector2 dir)
    {
        if (!Alive)
            return;
        Hp -= dmg;
        if (Hp <= 0f)
            Die(byPlayer, dir);
    }

    void Die(bool byPlayer, Vector2 dir)
    {
        Alive = false;
        // The pod stays on the wreck instead of jumping to the next target.
        if (Main.Jet.LockTarget == this)
            Main.Flir.Manual = true;
        foreach (var p in _parts)
            p.MaterialOverride = Main.CharredMat;
        foreach (var h in _heat)
            h.MaterialOverride = Main.WreckHotMat;   // burning wrecks stay warm
        Main.OnUnitKilled(this, byPlayer);
        var p3 = new Vector3(CPos.X, CPos.Y, LaneZ);
        Main.SpawnExplosion(p3, Kind == UnitKind.Truck ? 0.8f : 1f);
        Main.SpawnDebris(p3, 8, 16f);
        var fire = Main.MakeWreckFire();
        AddChild(fire);
        fire.Position = new Vector3(0, 2f, 0);
        float dx = Mathf.Abs(dir.X) > 0.1f ? Mathf.Sign(dir.X) : Fx.Rand(-1f, 1f);
        switch (Kind)
        {
            case UnitKind.Tank:
            case UnitKind.FriendlyTank:
                if (GD.Randf() < 0.5f)
                    TossTurret(dx);
                else  // ammunition cook-off a moment later
                    After(Fx.Rand(0.8f, 2.2f), () =>
                    {
                        Main.SpawnExplosion(GlobalPosition + new Vector3(0, 2.5f, 0), 1.3f);
                        TossTurret(dx);
                    });
                break;
            case UnitKind.Aaa:
                TossTurret(dx);
                break;
            case UnitKind.Sam:
                for (int i = 0; i < _ammo; i++)
                    After(Fx.Rand(0.3f, 1.8f), () =>
                        Main.SpawnExplosion(GlobalPosition + new Vector3(Fx.Rand(-2f, 2f), 3f, 0), 0.6f));
                foreach (var r in _samRounds)
                    r.Visible = false;
                break;
        }
    }

    /// Runs `action` after `delay` seconds, unless this unit has been freed by then.
    void After(float delay, System.Action action)
    {
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (IsInstanceValid(this) && IsInsideTree())
                action();
        };
    }

    void TossTurret(float dx)
    {
        if (_turret == null || !_turret.IsInsideTree())
            return;
        var t = _turret;
        _turret = null;
        Main.TossPart(t, new Vector3(dx * Fx.Rand(3, 8), Fx.Rand(14, 24), Fx.Rand(-4, 4)), new Vector3(3.4f, 1f, 2.8f), 900f);
    }
}
