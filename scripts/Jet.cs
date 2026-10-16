using System.Collections.Generic;
using System.Linq;
using Godot;

/// F/A-18 flight model in the vertical (x, y) plane. Attached to the root of
/// jet.tscn; the visual parts (model, flames, lights, stores) are placed in
/// that scene in the editor and looked up here by name.
///
/// Forces: lift (CL(alpha) with stall), drag (parasite + induced + post-stall +
/// wave drag + stores/gear/speedbrake), thrust (2x F414 with spool-up, altitude
/// lapse and afterburner) and gravity. Mass changes with fuel burn and stores
/// released. Pitch is a fly-by-wire style loop that commands angle of attack,
/// auto-trims to 1 g and limits to +7.5 / -3 g. Roll is a 180 degree flip that
/// changes which way "pull" goes.
public partial class Jet : Node3D, ITarget
{
    const float S = 46.5f;              // wing area m^2
    const float Cla = 3.6f;             // lift slope per rad
    const float AlphaStall = 0.45f;     // rad (~26 deg)
    const float AlphaLimit = 0.50f;
    const float KInd = 0.11f;
    const float Cd0 = 0.021f;
    const float G = 9.81f;
    const float EmptyMass = 14550f;     // kg incl. pilot, gun, pylons
    public const float FuelMax = 4900f; // kg internal
    const float MilThrust = 124000f;    // N, both engines
    const float AbThrust = 196000f;
    const float GLimit = 7.5f;
    const float GNeg = -3f;
    const float Mk82Mass = 227f;
    const float Agm65Mass = 300f;
    const float GearH = 3.4f;           // jet centre above the wheels, gear down
    const int MaxBombs = 4;
    const int MaxMavs = 2;
    const int MaxAmmo = 578;
    const int MaxFlares = 60;
    const float Length = 18.3f;

    /// Speed at the start, along the direction the jet is rotated to in the editor.
    [Export] public float StartSpeed = 190f;

    public Game Main;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Theta;          // pitch attitude (nose angle), rad
    float _thetaDot;
    float _phi;                  // roll angle: 0 = upright, PI = inverted
    float _phiTarget;
    public float Throttle = 0.85f;
    public float EngineSpool = 0.85f;
    public float Fuel = FuelMax;
    public float Hp = 100f;
    public bool Alive = true;
    public bool GearDown;
    public bool OnGround;
    public bool Airbrake;
    float _stick;
    public float GLoad = 1f;
    public float Alpha;
    public float Mach;
    public float Agl = 1000f;
    public bool Stalled;
    float _gammaPrev;

    public int Ammo = MaxAmmo;
    public int Bombs = MaxBombs;
    public int Mavs = MaxMavs;
    public int Flares = MaxFlares;
    float _gunAcc;
    float _bombCd;
    float _flareCd;
    float _mavCd;
    int _shots;
    public bool Firing;
    public Unit LockTarget;
    public float LockTime;
    public float RearmTimer;
    public float RwrLock;
    public Vector2? Ccip;
    public float CcipTime;
    public Vector2? GunCcip;

    Node3D _rollNode;
    readonly List<Node3D> _flames = new();
    OmniLight3D _abLight;
    OmniLight3D _muzzleLight;
    readonly List<CpuParticles3D> _vapor = new();
    CpuParticles3D _smoke;
    readonly List<Node3D> _gearOn = new();
    readonly List<Node3D> _gearOff = new();
    readonly List<Node3D> _bombVis = new();
    readonly List<Node3D> _mavVis = new();

    /// Called by Game once its shared resources exist. The start position is
    /// wherever you placed the jet in the editor.
    public void Init(Game main)
    {
        Main = main;
        Pos = new Vector2(Position.X, Position.Y);
        Vel = Vector2.FromAngle(Rotation.Z) * StartSpeed;
        Theta = Vel.Angle() + 0.06f;
        _gammaPrev = Vel.Angle();
        FindParts();
        UpdateVisual();
    }

    public float Mass() => EmptyMass + Fuel + Bombs * Mk82Mass + Mavs * Agm65Mass + Ammo * 0.26f;

    public float UpSign() => Mathf.Cos(_phi) >= 0f ? 1f : -1f;

    public Vector2 Fwd() => Vector2.FromAngle(Theta);

    Vector2 BodyDown() => new Vector2(Mathf.Sin(Theta), -Mathf.Cos(Theta)) * UpSign();

    // Target interface for enemy missiles.
    public Vector2 TargetPos => Pos;
    public Vector2 TargetVel => Vel;
    public bool IsTargetValid() => Alive;

    // ---------------------------------------------------------------- scene parts

    /// Finds the parts placed in jet.tscn and adds the particle effects at the markers.
    void FindParts()
    {
        _rollNode = GetNode<Node3D>("Roll");
        foreach (var n in GetNode("Roll/F18").FindChildren("*Landing*", "", true, false))
        {
            if (n is not Node3D g)
                continue;
            if (g.Name.ToString().Contains("LandingOn"))
                _gearOn.Add(g);
            else if (g.Name.ToString().Contains("LandingOff"))
                _gearOff.Add(g);
        }
        _flames.Add(GetNode<Node3D>("Roll/FlameL"));
        _flames.Add(GetNode<Node3D>("Roll/FlameR"));
        _abLight = GetNode<OmniLight3D>("Roll/AfterburnerLight");
        _muzzleLight = GetNode<OmniLight3D>("Roll/MuzzleLight");
        for (int i = 0; i < MaxBombs; i++)
            _bombVis.Add(GetNode<Node3D>($"Roll/Bomb{i}"));
        for (int i = 0; i < MaxMavs; i++)
            _mavVis.Add(GetNode<Node3D>($"Roll/Maverick{i}"));

        // Wingtip vapour: white puffs left behind in the air while pulling hard.
        var vq = new QuadMesh { Size = new Vector2(1, 1), Material = Main.SmokeQuad.Material };
        foreach (var marker in new[] { "VaporL", "VaporR" })
        {
            var v = new CpuParticles3D
            {
                Mesh = vq,
                Amount = 60,
                Lifetime = 0.7,
                LocalCoords = false,
                Emitting = false,
                InitialVelocityMin = 0f,
                InitialVelocityMax = 0.5f,
                Gravity = Vector3.Zero,
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.2f,
                ColorRamp = Fx.Ramp(new[] { 0f, 0.2f, 1f },
                        new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0f) }),
                Position = GetNode<Node3D>($"Roll/{marker}").Position,
            };
            _rollNode.AddChild(v);
            _vapor.Add(v);
        }
        // Damage smoke from the engines.
        _smoke = Main.MakeParticles(Main.SmokeQuad, 50, 3f, new Vector2(1, 4), new Vector2(2.5f, 6f),
                Main.SmokeRamp, new Vector3(0, 2, 0), 1f, false, 30f);
        _smoke.Emitting = false;
        _smoke.Position = GetNode<Node3D>("Roll/SmokePoint").Position;
        _rollNode.AddChild(_smoke);
    }

    // ---------------------------------------------------------------- simulation

    public void Sim(float dt)
    {
        RwrLock = Mathf.Max(RwrLock - dt, 0f);
        if (!Alive)
            return;
        Controls(dt);
        const int sub = 3;
        for (int i = 0; i < sub; i++)
        {
            Integrate(dt / sub);
            if (!Alive)
                return;
        }
        GroundService(dt);
        Weapons(dt);
        UpdateVisual();
        ComputeCcip();
    }

    void Controls(float dt)
    {
        float targetStick = Input.GetAxis("pitch_down", "pitch_up");
        _stick = Mathf.MoveToward(_stick, targetStick, dt * 5f);
        if (Input.IsActionPressed("throttle_up"))
            Throttle = Mathf.Min(Throttle + dt * 0.5f, 1.25f);
        if (Input.IsActionPressed("throttle_down"))
            Throttle = Mathf.Max(Throttle - dt * 0.5f, 0f);
        Airbrake = Input.IsActionPressed("airbrake");
        if (Input.IsActionJustPressed("flip") && !OnGround)
            _phiTarget += Mathf.Pi;
        if (Input.IsActionJustPressed("gear") && !OnGround)
        {
            GearDown = !GearDown;
            Main.Msg("GEAR " + (GearDown ? "DOWN" : "UP"), new Color(0.6f, 0.9f, 1.0f), 2f);
        }
        _phi = Mathf.MoveToward(_phi, _phiTarget, dt * 3.8f);
        if (_phi == _phiTarget && _phi >= Mathf.Tau)
        {
            _phi -= Mathf.Tau;
            _phiTarget -= Mathf.Tau;
        }
    }

    /// Lift coefficient: linear up to the stall, then blends into flat-plate lift.
    static float Cl(float a)
    {
        float aa = Mathf.Abs(a);
        if (aa <= AlphaStall)
            return Cla * a;
        float t = Mathf.Clamp((aa - AlphaStall) / 0.2f, 0f, 1f);
        return Mathf.Lerp(Cla * AlphaStall * Mathf.Sign(a), 0.95f * Mathf.Sin(2f * a), t);
    }

    static float WaveDrag(float m) =>
        Mathf.SmoothStep(0.85f, 1.05f, m) * 0.045f - Mathf.SmoothStep(1.2f, 2.0f, m) * 0.02f;

    float StoresDrag() =>
        Bombs * 0.0025f + Mavs * 0.002f + (GearDown ? 0.025f : 0f) + (Airbrake ? 0.05f : 0f);

    void Integrate(float h)
    {
        float rho = 1.225f * Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);
        float dens = rho / 1.225f;
        float v = Vel.Length();
        var dir = v > 0.5f ? Vel / v : Fwd();
        float gamma = dir.Angle();                     // flight path angle
        Theta = Mathf.Wrap(Theta, -Mathf.Pi, Mathf.Pi);
        Alpha = Mathf.Wrap(Theta - gamma, -Mathf.Pi, Mathf.Pi);
        float qbar = 0.5f * rho * v * v;
        float qs = Mathf.Max(qbar * S, 1f);
        float m = Mass();
        float us = UpSign();
        float rollEff = Mathf.Abs(Mathf.Cos(_phi));
        var perp = new Vector2(-dir.Y, dir.X);
        float flaps = GearDown && v < 120f ? 0.45f : 0f;
        Mach = v / Mathf.Max(340.3f - 0.0041f * Pos.Y, 295f);

        // --- fly-by-wire: command an angle of attack, auto-trim to 1 g
        float claEff = Cla * Mathf.Max(rollEff, 0.05f);
        float aCmd;
        if (OnGround)
            aCmd = Mathf.Max(_stick, 0f) * 0.24f * us;
        else
        {
            float aTrim = ((m * G * perp.Y / qs) - flaps * us) / claEff;
            float aPos = Mathf.Min(AlphaLimit, (GLimit * m * G / qs - flaps) / claEff);
            float aNeg = Mathf.Min(0.3f, (-GNeg * m * G / qs + flaps) / claEff);
            float trimB = Mathf.Clamp(aTrim * us, -aNeg, aPos);
            float cmdB = trimB;
            if (_stick > 0f)
                cmdB = Mathf.Lerp(trimB, aPos, _stick);
            else if (_stick < 0f)
                cmdB = Mathf.Lerp(trimB, -aNeg, -_stick);
            aCmd = cmdB * us;
        }
        float qn = Mathf.Clamp(qbar / 22000f, 0.02f, 2.5f);
        float w = 5.5f * Mathf.Sqrt(qn);
        float gammaDot = Mathf.Clamp(Mathf.Wrap(gamma - _gammaPrev, -Mathf.Pi, Mathf.Pi) / h, -3f, 3f);
        _gammaPrev = gamma;
        float thetaAcc = w * w * Mathf.Wrap(aCmd - Alpha, -Mathf.Pi, Mathf.Pi) - 2f * 0.85f * w * (_thetaDot - gammaDot);
        _thetaDot = Mathf.Clamp(_thetaDot + thetaAcc * h, -1.6f, 1.6f);
        Theta += _thetaDot * h;

        // --- aerodynamic forces
        float cl = Cl(Alpha) * rollEff;
        if (Mathf.Abs(Alpha) < AlphaStall)
            cl += flaps * us;
        float post = Mathf.Clamp((Mathf.Abs(Alpha) - AlphaStall) / 0.2f, 0f, 1f);
        float cd = Cd0 + StoresDrag() + KInd * cl * cl + 1.4f * Mathf.Pow(Mathf.Sin(Alpha), 2) * post + WaveDrag(Mach);
        var lift = perp * (qs * cl);
        var drag = -dir * (qs * cd);

        // --- engines: spool lag, altitude lapse, ram effect, afterburner
        float rate = EngineSpool < Throttle ? 0.55f : 0.9f;
        if (EngineSpool >= 1f && Throttle > 1f)
            rate = 2f;
        EngineSpool = Mathf.MoveToward(EngineSpool, Throttle, h * rate);
        float thrustNow = 0f;
        if (Fuel > 0f)
        {
            if (EngineSpool <= 1f)
                thrustNow = Mathf.Max(EngineSpool, 0.05f) * MilThrust;
            else
                thrustNow = MilThrust + (EngineSpool - 1f) / 0.25f * (AbThrust - MilThrust);
            thrustNow *= Mathf.Pow(dens, 0.7f) * (1f + 0.2f * Mach);
            if (Hp < 30f)
                thrustNow *= 0.6f;
            Fuel = Mathf.Max(Fuel - thrustNow * (EngineSpool > 1f ? 5.2e-5f : 2.3e-5f) * h, 0f);
        }
        var thrust = Fwd() * thrustNow;

        var force = lift + drag + thrust;
        if (OnGround)
        {
            float normal = Mathf.Max(m * G - force.Y, 0f);
            float mu = 0.02f + (Airbrake ? 0.5f : 0f);   // rolling friction, wheel brakes
            float fr = mu * normal;
            if (Mathf.Abs(Vel.X) < 0.5f && Mathf.Abs(force.X) < fr)
            {
                force.X = 0f;
                Vel = new Vector2(0f, Vel.Y);
            }
            else
                force.X -= Mathf.Sign(Vel.X) * fr;
        }
        var bodyUp = new Vector2(-Mathf.Sin(Theta), Mathf.Cos(Theta)) * us;
        GLoad = Mathf.Lerp(GLoad, (force / m).Dot(bodyUp) / G, 0.15f);
        Vel += (force / m + new Vector2(0, -G)) * h;
        Pos += Vel * h;
        Stalled = Mathf.Abs(Alpha) > AlphaStall + 0.02f || (v < 65f && !OnGround);
        GroundContact();
    }

    void GroundContact()
    {
        float gh = Main.Terrain.Height(Pos.X);
        Agl = Pos.Y - gh - GearH;
        if (Agl > 0.05f)
        {
            OnGround = false;
            return;
        }
        var f = Fwd();
        float pitchRel = Mathf.Atan2(f.Y, Mathf.Abs(f.X));
        bool upright = new Vector2(-Mathf.Sin(Theta), Mathf.Cos(Theta)).Y * UpSign() > 0.9f;
        if (!OnGround)
        {
            float sink = -Vel.Y;
            bool runway = Main.Terrain.OnRunway(Pos.X);
            bool ok = GearDown && runway && sink < 6.5f && upright && pitchRel > -0.06f && pitchRel < 0.32f;
            if (!ok)
            {
                string why = "CRASHED";
                if (GearDown && runway && sink >= 6.5f)
                    why = "HARD LANDING - GEAR COLLAPSED";
                else if (runway && !GearDown)
                    why = "GEAR-UP LANDING";
                Crash(why);
                return;
            }
            OnGround = true;
            Main.Audio.Play("thump", -2f);
            Main.Msg($"TOUCHDOWN  {sink:F1} m/s sink", new Color(0.6f, 0.9f, 1.0f), 3f);
        }
        Pos = new Vector2(Pos.X, gh + GearH);
        Agl = 0f;
        Vel = new Vector2(Vel.X, Mathf.Max(Vel.Y, 0f));
        if (!Main.Terrain.OnRunway(Pos.X) && Mathf.Abs(Vel.X) > 25f)
        {
            Crash("RAN OFF THE RUNWAY");
            return;
        }
        float clamped = Mathf.Clamp(pitchRel, 0f, 0.26f);
        if (clamped != pitchRel)
        {
            Theta = f.X >= 0f ? clamped : Mathf.Pi - clamped;
            _thetaDot = 0f;
        }
    }

    void GroundService(float dt)
    {
        if (OnGround && Vel.Length() < 3f && Main.Terrain.OnRunway(Pos.X))
        {
            if (RearmTimer == 0f)
                Main.Msg("REARMING / REFUELING...", new Color(0.6f, 0.9f, 1.0f), 3f);
            RearmTimer += dt;
            if (RearmTimer > 4f)
            {
                RearmTimer = -999f;
                Rearm();
                Main.Msg("READY - TURN AND TAKE OFF", new Color(0.6f, 1.0f, 0.6f), 4f);
            }
        }
        else if (!OnGround)
            RearmTimer = 0f;
    }

    void Rearm()
    {
        Ammo = MaxAmmo;
        Bombs = MaxBombs;
        Mavs = MaxMavs;
        Flares = MaxFlares;
        Fuel = FuelMax;
        Hp = 100f;
    }

    public void TakeDamage(float d)
    {
        if (!Alive)
            return;
        Hp -= d;
        if (Hp <= 0f)
        {
            Hp = 0f;
            Crash("SHOT DOWN");
        }
    }

    void Crash(string reason)
    {
        if (!Alive)
            return;
        Alive = false;
        Visible = false;
        foreach (var v in _vapor)
            v.Emitting = false;
        Main.OnJetDestroyed(reason);
    }

    // ---------------------------------------------------------------- weapons

    void Weapons(float dt)
    {
        _bombCd -= dt;
        _flareCd -= dt;
        _mavCd -= dt;
        Firing = Input.IsActionPressed("fire_gun") && Ammo > 0;
        if (Firing)
        {
            _gunAcc += dt * 100f;  // M61A2: 6000 rounds/min
            while (_gunAcc >= 1f && Ammo > 0)
            {
                _gunAcc -= 1f;
                FireRound();
            }
        }
        else
            _gunAcc = 0f;
        _muzzleLight.LightEnergy = Firing ? Fx.Rand(2f, 5f) : 0f;

        if (Input.IsActionJustPressed("drop_bomb") && Bombs > 0 && _bombCd <= 0f && !OnGround)
        {
            _bombCd = 0.2f;
            Bombs--;
            _bombVis[Bombs].Visible = false;
            var d = BodyDown();
            Main.SpawnBomb(Pos + d * 1.2f, Vel + d * 2.5f);
        }
        UpdateLock(dt);
        if (Input.IsActionJustPressed("fire_missile") && !OnGround)
        {
            if (Mavs <= 0)
                Main.Msg("NO MAVERICKS", new Color(1, 0.75f, 0.2f), 1.5f);
            else if (LockTarget == null || LockTime < 0.8f)
                Main.Msg("NO LOCK", new Color(1, 0.75f, 0.2f), 1.5f);
            else if (_mavCd <= 0f)
            {
                _mavCd = 0.6f;
                Mavs--;
                _mavVis[Mavs].Visible = false;
                var d = BodyDown();
                Main.SpawnMaverick(Pos + d * 1.5f, Vel + d * 3f, LockTarget);
                Main.Msg("RIFLE", new Color(0.6f, 1.0f, 0.6f), 1.5f);
            }
        }
        if (Input.IsActionJustPressed("flares") && Flares > 0 && _flareCd <= 0f)
        {
            _flareCd = 0.25f;
            for (int i = 0; i < 2; i++)
            {
                Flares--;
                var side = Vector2.FromAngle(Theta + Mathf.Pi + (i == 0 ? 0.6f : -0.6f));
                Main.SpawnFlare(Pos - Fwd() * 6f, Vel * 0.85f + side * 25f + BodyDown() * 10f);
            }
        }
    }

    void FireRound()
    {
        _shots++;
        var f = Fwd();
        float spread = Fx.Rand(-0.0035f, 0.0035f);
        var muzzle = Pos + f * (Length * 0.45f) - BodyDown() * 0.6f;
        var bv = Vel + Vector2.FromAngle(Theta + spread) * 1050f;
        Main.SpawnRound(muzzle, bv, 0, true, _shots % 3 == 0, ShellKind.Bullet, 7f);
        // M61 recoil is ~18 kN: a small but real decel while firing.
        Vel -= f * (18000f / Mass()) * 0.01f;
    }

    bool InSeeker(Unit u, float cone)
    {
        if (u == null || !IsInstanceValid(u) || !u.Alive)
            return false;
        var rel = u.CPos - Pos;
        float d = rel.Length();
        if (d < 250f || d > 9000f)
            return false;
        return Fwd().Dot(rel / d) > Mathf.Cos(cone);
    }

    void UpdateLock(float dt)
    {
        if (Mavs <= 0 || OnGround)
        {
            LockTarget = null;
            return;
        }
        if (LockTarget != null && !InSeeker(LockTarget, 0.55f))
            LockTarget = null;
        bool cycle = Input.IsActionJustPressed("cycle_target");
        if (LockTarget == null || cycle)
        {
            var f = Fwd();
            // Candidates sorted by how close they are to the nose.
            var cands = Main.Units
                .Where(u => u.Team == 1 && InSeeker(u, 0.45f))
                .OrderByDescending(u => f.Dot((u.CPos - Pos).Normalized()))
                .ToList();
            Unit pick = null;
            if (cands.Count > 0)
            {
                pick = cands[0];
                if (cycle && LockTarget != null)
                {
                    int i = cands.IndexOf(LockTarget);
                    pick = cands[(i + 1) % cands.Count];
                }
            }
            if (pick != LockTarget)
            {
                LockTarget = pick;
                LockTime = 0f;
            }
        }
        LockTime += dt;
    }

    /// Predicts where a bomb released now, and the gun rounds, would hit the ground.
    void ComputeCcip()
    {
        Ccip = null;
        GunCcip = null;
        if (OnGround)
            return;
        if (Bombs > 0)
        {
            var p = Pos;
            var v = Vel;
            float t = 0f;
            const float step = 0.05f;
            while (t < 60f)
            {
                float rho = Mathf.Exp(-Mathf.Max(p.Y, 0f) / 8500f);
                v += (new Vector2(0, -G) - v * v.Length() * Game.BombK * rho) * step;
                p += v * step;
                t += step;
                if (p.Y <= Main.Terrain.Height(p.X))
                {
                    Ccip = p;
                    CcipTime = t;
                    break;
                }
            }
        }
        var gp = Pos;
        var gv = Vel + Fwd() * 1050f;
        float gt = 0f;
        while (gt < 2.6f)
        {
            float rho = Mathf.Exp(-Mathf.Max(gp.Y, 0f) / 8500f);
            gv += (new Vector2(0, -G) - gv * gv.Length() * Game.BulletK * rho) * 0.02f;
            gp += gv * 0.02f;
            gt += 0.02f;
            if (gp.Y <= Main.Terrain.Height(gp.X))
            {
                GunCcip = gp;
                break;
            }
        }
    }

    // ---------------------------------------------------------------- visuals

    void UpdateVisual()
    {
        Position = new Vector3(Pos.X, Pos.Y, 0f);
        Rotation = new Vector3(0, 0, Theta);
        _rollNode.Rotation = new Vector3(_phi, 0, 0);
        foreach (var g in _gearOn)
            g.Visible = GearDown;
        foreach (var g in _gearOff)
            g.Visible = !GearDown;
        float ab = Mathf.Clamp((EngineSpool - 1f) / 0.25f, 0f, 1f);
        bool lit = Fuel > 0f;
        foreach (var f in _flames)
        {
            float fl = 0.4f + ab * (3.2f + GD.Randf() * 0.6f) + Mathf.Clamp(EngineSpool, 0f, 1f) * 0.6f;
            f.Scale = new Vector3(fl, 0.6f + ab * 0.5f, 0.6f + ab * 0.5f);
            f.Visible = lit && (ab > 0.02f || EngineSpool > 0.7f);
        }
        _abLight.LightEnergy = lit ? ab * Fx.Rand(2.5f, 3.5f) : 0f;
        bool vap = (GLoad > 5f || Mathf.Abs(Alpha) > 0.32f) && Vel.Length() > 110f;
        foreach (var v in _vapor)
            v.Emitting = vap;
        _smoke.Emitting = Hp < 55f;
        _smoke.Color = Hp < 30f ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.7f, 0.7f, 0.7f);
        for (int i = 0; i < _bombVis.Count; i++)
            _bombVis[i].Visible = i < Bombs;
        for (int i = 0; i < _mavVis.Count; i++)
            _mavVis[i].Visible = i < Mavs;
    }
}
