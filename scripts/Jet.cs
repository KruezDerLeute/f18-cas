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
    const float GLimit = 12.5f;
    const float GNeg = -3f;
    const float Mk82Mass = 227f;
    const float Agm65Mass = 300f;
    const float GearH = 3.4f;           // jet centre above the wheels, gear down
    const float TailStrike = 0.40f;     // rad (~23 deg): a higher nose at touchdown drags the tail
    const int MaxBombs = 4;
    const int MaxMavs = 4;
    const int MaxAmmo = 578;
    const int MaxFlares = 130;
    const float Length = 18.3f;

    /// Speed at the start, along the direction the jet is rotated to in the editor.
    [Export] public float StartSpeed = 190f;

    /// Start parked on the runway instead of in the air.
    [Export] public bool StartOnRunway = true;

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
    public float HitFlash;   // 0..1, red screen flash after taking a hit; fades out
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
    /// True while the targeting pod's laser is on a tracked target (guides bombs).
    public bool Lasing => Alive && LockTarget != null && IsInstanceValid(LockTarget) && LockTarget.Alive
            && LockTime > 0.8f && Main.Flir.On;
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
    CpuParticles3D _shockCone;
    Gradient _boomRamp;
    bool _supersonic;
    float _coneFlash;   // seconds left on the vapour cone after crossing Mach 1
    const float ConeFlashTime = 3.6f;
    CpuParticles3D _smoke;
    CpuParticles3D _streaks;
    CpuParticles3D _spray;
    readonly List<Node3D> _gearOn = new();
    readonly List<Node3D> _gearOff = new();
    readonly List<Node3D> _bombVis = new();
    readonly List<Node3D> _mavVis = new();

    /// Called by Game once its shared resources exist. The jet starts on the
    /// runway, unless StartOnRunway is off: then it starts in the air wherever
    /// you placed it in the editor.
    public void Init(Game main)
    {
        Main = main;
        if (StartOnRunway && !Settings.StartInAir)
            ToRunwayStart();
        else
        {
            Pos = new Vector2(Position.X, Position.Y);
            Vel = Vector2.FromAngle(Rotation.Z) * StartSpeed;
            Theta = Vel.Angle() + 0.06f;
            _gammaPrev = Vel.Angle();
        }
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
        // Transonic vapour cone: short-lived puffs on the surface of a cone around
        // the wings, narrow at the nose and open at the back. Local coordinates,
        // so the cone rides with the jet instead of smearing behind it.
        var conePts = new Vector3[3000];
        for (int i = 0; i < conePts.Length; i++)
        {
            float t = Mathf.Sqrt(GD.Randf());       // even spread over the surface: more points at the wide end
            float r = Mathf.Lerp(1.8f, 7.5f, t);
            float a = GD.Randf() * Mathf.Tau;
            conePts[i] = new Vector3(Mathf.Lerp(1.5f, -5.5f, t), -0.4f + Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        _shockCone = new CpuParticles3D
        {
            Mesh = Main.SmokeQuad,
            Amount = 15000,
            Lifetime = 0.22,
            LocalCoords = true,
            Emitting = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Points,
            EmissionPoints = conePts,
            Direction = new Vector3(-1, 0, 0),
            Spread = 15f,
            InitialVelocityMin = 2f,
            InitialVelocityMax = 8f,
            Gravity = Vector3.Zero,
            ScaleAmountMin = 0.9f,
            ScaleAmountMax = 2.0f,
            AngleMin = 0f,
            AngleMax = 360f,
            ColorRamp = Fx.Ramp(new[] { 0f, 0.3f, 1f },
                    new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0f) }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _rollNode.AddChild(_shockCone);
        _boomRamp = Fx.Ramp(new[] { 0f, 0.15f, 1f },
                new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0f) });

        // Damage smoke from the engines.
        _smoke = Main.MakeParticles(Main.SmokeQuad, 50, 3f, new Vector2(1, 4), new Vector2(2.5f, 6f),
                Main.SmokeRamp, new Vector3(0, 2, 0), 1f, false, 30f);
        _smoke.Emitting = false;
        _smoke.Position = GetNode<Node3D>("Roll/SmokePoint").Position;
        _rollNode.AddChild(_smoke);

        // Air streaks: faint thin lines hanging still in the air around the jet,
        // so at speed they flash past. Added to Game, not the jet, so the
        // emission box doesn't pitch with it.
        var streakMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = Main.ParticleSoft,
        };
        _streaks = new CpuParticles3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1f, 0.08f), Material = streakMat },
            Amount = 220,
            Lifetime = 0.5,
            LocalCoords = false,
            Emitting = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(220f, 130f, 65f),
            InitialVelocityMin = 0f,
            InitialVelocityMax = 0f,
            Gravity = Vector3.Zero,
            ScaleAmountMin = 6f,
            ScaleAmountMax = 14f,
            ColorRamp = Fx.Ramp(new[] { 0f, 0.3f, 1f },
                    new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0.5f), new Color(1, 1, 1, 0f) }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Main.AddChild(_streaks);

        // Dust kicked up off the ground on a fast, very low pass.
        _spray = Main.MakeParticles(Main.SmokeQuad, 160, 1.8f, new Vector2(8, 25), new Vector2(4, 10),
                Fx.Ramp(new[] { 0f, 0.15f, 1f }, new[] {
                    new Color(0.6f, 0.53f, 0.4f, 0f), new Color(0.6f, 0.53f, 0.4f, 0.6f), new Color(0.65f, 0.6f, 0.5f, 0f) }),
                new Vector3(0, -6f, 0), 1.5f, false, 35f, null, 6f);
        _spray.Emitting = false;
        Main.AddChild(_spray);
    }

    // ---------------------------------------------------------------- simulation

    public void Sim(float dt)
    {
        RwrLock = Mathf.Max(RwrLock - dt, 0f);
        HitFlash = Mathf.Max(HitFlash - dt * 2f, 0f);
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
        Transonic(dt);
        SpeedCues();
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
        if (Input.IsActionJustPressed("airbrake"))
        {
            Airbrake = !Airbrake;
            Main.Msg((OnGround ? "BRAKES " : "SPEEDBRAKE ") + (Airbrake ? "ON" : "OFF"), new Color(0.6f, 0.9f, 1.0f), 2f);
        }
        if (Input.IsActionJustPressed("flip") && !OnGround)
            _phiTarget += Mathf.Pi;
        if (Input.IsActionJustPressed("gear") && !OnGround)
        {
            GearDown = !GearDown;
            Main.Msg("GEAR " + (GearDown ? "DOWN" : "UP"), new Color(0.6f, 0.9f, 1.0f), 2f);
        }
        if (Settings.StallProtection && !OnGround && !GearDown && Vel.Length() < 110f && Fuel > 0f)
            Throttle = Mathf.Max(Throttle, 1.25f);
        // Auto-GCAS: about to fly into the ground (and not landing), so roll
        // upright and pull up, like the real system in modern fighters.
        if (Settings.AutoGcas && !GearDown && Main.PullUpWarning())
        {
            if (Mathf.Cos(_phiTarget) < 0f)
                _phiTarget += Mathf.Pi;
            _stick = UpSign();
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

    /// Transonic drag rise between Mach 0.95 and 1.05: a bump you feel, but
    /// full afterburner pushes through it even low down (about 1280 km/h fully
    /// loaded at sea level, 1550 clean), easing a little past Mach 1.15.
    static float WaveDrag(float m) =>
        Mathf.SmoothStep(0.95f, 1.05f, m) * 0.026f - Mathf.SmoothStep(1.15f, 1.5f, m) * 0.004f;

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
            float aPos = Mathf.Min(Settings.StallProtection ? AlphaStall - 0.05f : AlphaLimit, (GLimit * m * G / qs - flaps) / claEff);
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
            if (!Settings.UnlimitedFuel)
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
            bool ok = GearDown && runway && upright && pitchRel > -0.06f && pitchRel < TailStrike;
            if (!ok)
            {
                Crash(TouchdownFailure(runway, upright, pitchRel));
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
        // Same upper limit as the touchdown check, so a nose-high landing isn't
        // snapped down; the ground fly-by-wire lowers it from there.
        float clamped = Mathf.Clamp(pitchRel, 0f, TailStrike);
        if (clamped != pitchRel)
        {
            Theta = f.X >= 0f ? clamped : Mathf.Pi - clamped;
            _thetaDot = 0f;
        }
    }

    /// Why a touchdown failed, most specific reason first.
    string TouchdownFailure(bool runway, bool upright, float pitchRel)
    {
        if (!runway)
            return GearDown && upright ? "LANDED OFF THE RUNWAY" : "CRASHED";
        if (!GearDown)
            return "GEAR-UP LANDING";
        if (!upright)
            return "LANDED INVERTED";
        if (pitchRel >= TailStrike)
            return "TAIL STRIKE - NOSE TOO HIGH";
        return "NOSE GEAR FIRST - NOSE TOO LOW";
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
                ToRunwayStart();
                Main.Msg("READY - RELEASE BRAKES (Z) AND TAKE OFF", new Color(0.6f, 1.0f, 0.6f), 4f);
            }
        }
        else if (!OnGround)
            RearmTimer = 0f;
    }

    public void Rearm()
    {
        Ammo = MaxAmmo;
        Bombs = MaxBombs;
        Mavs = MaxMavs;
        Flares = MaxFlares;
        Fuel = FuelMax;
        Hp = 100f;
    }

    /// Puts the jet at the start of the runway, stopped, upright and facing
    /// the front, brakes on and engines at idle.
    void ToRunwayStart()
    {
        float x = Terrain.RunwayX0 + 60f;
        Pos = new Vector2(x, Main.Terrain.Height(x) + GearH);
        Vel = Vector2.Zero;
        Theta = 0f;
        _thetaDot = 0f;
        _gammaPrev = 0f;
        _phi = _phiTarget = 0f;
        _stick = 0f;
        Throttle = EngineSpool = 0f;
        Airbrake = true;
        GearDown = true;
        OnGround = true;
        Agl = 0f;
        GLoad = 1f;
    }

    public void TakeDamage(float d)
    {
        if (!Alive)
            return;
        d *= Settings.DamageTaken;
        Hp -= d;
        // Bigger hits flash harder; even a graze is noticeable.
        HitFlash = Mathf.Max(HitFlash, Mathf.Clamp(0.35f + d / 40f, 0.35f, 1f));
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
        _shockCone.Emitting = false;
        _streaks.Emitting = false;
        _spray.Emitting = false;
        Main.OnJetDestroyed(reason);
    }

    /// Air streaks above about Mach 0.6, and a dust trail under the jet when
    /// it's fast and very low.
    void SpeedCues()
    {
        float v = Vel.Length();
        float st = Mathf.SmoothStep(0.55f, 0.95f, Mach);
        _streaks.Emitting = st > 0.01f;
        _streaks.Color = new Color(1, 1, 1, st);
        // Billboard angles turn the other way to the jet's pitch.
        _streaks.AngleMin = _streaks.AngleMax = -Mathf.RadToDeg(Vel.Angle());
        _streaks.Position = new Vector3(Pos.X + Vel.X * 0.35f, Pos.Y + Vel.Y * 0.35f, 25f);

        float low = OnGround ? 0f : 1f - Mathf.SmoothStep(8f, 35f, Agl);
        float sp = low * Mathf.SmoothStep(120f, 300f, v);
        _spray.Emitting = sp > 0.01f;
        _spray.Color = new Color(1, 1, 1, sp);
        _spray.Position = new Vector3(Pos.X - Vel.X * 0.05f, Main.Terrain.Height(Pos.X), 0f);
    }

    // ---------------------------------------------------------------- sound barrier

    /// A brief vapour cone (thicker in the damp low air) and a sonic boom
    /// each time the jet goes through Mach 1.
    void Transonic(float dt)
    {
        // Flashes up as the jet crosses Mach 1 and fades out within a fraction of a second.
        _coneFlash = Mathf.Max(_coneFlash - dt, 0f);
        float flash = _coneFlash / ConeFlashTime;
        float humid = Mathf.Clamp(1f - Pos.Y / 9000f, 0.25f, 1f);
        float s = flash * flash * humid * Fx.Rand(0.85f, 1.05f);
        _shockCone.Emitting = s > 0.01f;
        _shockCone.Color = new Color(1, 1, 1, Mathf.Min(s * 1.3f, 1f));
        _shockCone.Scale = new Vector3(1f + (Mach - 0.95f), 1f, 1f);

        // Hysteresis, so hovering right at Mach 1 doesn't boom every frame.
        if (!_supersonic && Mach >= 1f)
        {
            _supersonic = true;
            _coneFlash = ConeFlashTime;
            SonicBoom();
        }
        else if (_supersonic && Mach < 0.97f)
            _supersonic = false;
    }

    void SonicBoom()
    {
        var p = GlobalPosition;
        Main.Audio.Play("sonic_boom");
        Main.Shake = Mathf.Max(Main.Shake, 0.5f);
        var puff = Main.MakeParticles(Main.SmokeQuad, 80, 3f, new Vector2(15, 35), new Vector2(5, 11), _boomRamp, Vector3.Zero, 1.5f);
        Main.FireOnce(puff, p, 4f);
        Main.Msg("MACH 1", new Color(0.6f, 0.9f, 1.0f), 2f);
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
            // With a tracked target the pod lases it and the bomb guides; otherwise it falls ballistic.
            var tgt = Lasing ? LockTarget : null;
            Main.SpawnBomb(Pos + d * 1.2f, Vel + d * 2.5f, tgt);
            if (tgt != null)
                Main.Msg("LGB AWAY - KEEP LASING", new Color(0.6f, 1.0f, 0.6f), 2f);
        }
        UpdateLock(dt);
        if (Input.IsActionJustPressed("fire_missile") && !OnGround)
        {
            if (Mavs <= 0)
                Main.Msg("NO MAVERICKS", new Color(1, 0.75f, 0.2f), 1.5f);
            else if (LockTarget == null || LockTime < 0.8f)
                Main.Msg("NO LOCK", new Color(1, 0.75f, 0.2f), 1.5f);
            else if (!LockTarget.Alive)
                Main.Msg("TARGET DESTROYED - DESIGNATE A NEW ONE", new Color(1, 0.75f, 0.2f), 1.5f);
            else if (!InSeeker(LockTarget, 0.55f))
                Main.Msg("OFF BORESIGHT - POINT THE NOSE AT IT", new Color(1, 0.75f, 0.2f), 1.5f);
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

    /// Locks a target picked with the targeting pod (null clears the lock).
    /// False if the pod can't hold that target from where the jet is.
    public bool Designate(Unit u)
    {
        if (u != null && !PodCanSee(u))
            return false;
        LockTarget = u;
        LockTime = 0f;
        return true;
    }

    /// The targeting pod keeps tracking anything not above the jet, or near the nose,
    /// including a target that has just been destroyed (to watch the wreck).
    bool PodCanSee(Unit u)
    {
        if (u == null || !IsInstanceValid(u) || !u.IsInsideTree())
            return false;
        var rel = u.CPos - Pos;
        float d = rel.Length();
        if (d < 50f || d > 12000f)
            return false;
        return rel.Y / d < 0.05f || Fwd().Dot(rel / d) > Mathf.Cos(0.55f);   // below ~3 degrees above the horizon, or in the nose cone
    }

    /// Picks the target near the nose (T cycles), then holds it with the pod.
    void UpdateLock(float dt)
    {
        if (OnGround)
        {
            LockTarget = null;
            return;
        }
        if (LockTarget != null && !PodCanSee(LockTarget))
            LockTarget = null;
        bool cycle = Input.IsActionJustPressed("cycle_target");
        if (cycle)
            Main.Flir.Manual = false;   // T hands the pod back to automatic
        if ((LockTarget == null && !Main.Flir.Manual) || cycle)
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
