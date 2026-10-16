using Godot;

/// Guided missile with a rocket motor, drag, gravity and proportional
/// navigation. Turn capability falls off with speed and air density, so a
/// missile that has burned out can be out-turned. Attached to the root of
/// maverick.tscn and sam_missile.tscn; each scene sets its own numbers in the
/// Inspector and has a "Flame" mesh for the motor exhaust.
public partial class Missile : Node3D
{
    /// true = the player's AGM-65, false = an enemy SAM.
    [Export] public bool IsAgm;
    /// Seconds after launch before the motor lights.
    [Export] public float Ignition;
    /// Motor burn time, seconds.
    [Export] public float Motor = 3.5f;
    /// Motor acceleration, m/s^2.
    [Export] public float Boost = 190f;
    [Export] public float DragK = 1.2e-4f;
    /// Hardest turn when fast, in g.
    [Export] public float MaxG = 25f;
    /// Proportional navigation constant.
    [Export] public float Nav = 3.5f;
    /// Self-destructs after this many seconds.
    [Export] public float Life = 13f;
    /// Proximity fuze radius, metres.
    [Export] public float Prox = 14f;
    [Export] public float Damage = 70f;

    public Game Main;
    public Vector2 Pos;
    public Vector2 Vel;
    public float LaneZ;
    public ITarget Target;
    public bool Guided = true;
    public bool Dead;
    float _age;
    CpuParticles3D _trail;
    Node3D _flame;

    public void Init(Game m, Vector2 p, Vector2 v, ITarget tgt, float z = 0f)
    {
        Main = m;
        Pos = p;
        Vel = v;
        Target = tgt;
        LaneZ = z;
        _flame = GetNode<Node3D>("Flame");
        _trail = Main.MakeParticles(Main.SmokeQuad, 160, 2.6f, new Vector2(0f, 1f), new Vector2(1.6f, 3.4f),
                Main.TrailRamp, new Vector3(0, 0.8f, 0), 0.5f, false, 20f);
        _trail.Emitting = false;
        Main.AddChild(_trail);
        Position = new Vector3(Pos.X, Pos.Y, LaneZ);
        this.SetRotZ(Vel.Angle());
    }

    bool Burning() => _age > Ignition && _age < Ignition + Motor;

    public void Sim(float dt)
    {
        if (Dead)
            return;
        const int n = 4;
        for (int i = 0; i < n; i++)
        {
            Step(dt / n);
            if (Dead)
                return;
        }
        Position = new Vector3(Pos.X, Pos.Y, LaneZ);
        this.SetRotZ(Vel.Angle());
        bool b = Burning();
        _flame.Visible = b;
        _flame.Scale = new Vector3(1, Fx.Rand(0.8f, 1.2f), 1);
        _trail.Position = Position - new Vector3(Mathf.Cos(Rotation.Z), Mathf.Sin(Rotation.Z), 0) * 2f;
        _trail.Emitting = b || (_age > Ignition && _age < Ignition + Motor + 1f);
    }

    /// A flare in front of a SAM's seeker has a one-in-three chance of stealing it.
    public void ConsiderFlare(Shell f)
    {
        if (IsAgm || !Guided || Target != Main.Jet)
            return;
        var rel = f.Pos - Pos;
        float d = rel.Length();
        if (d < 3000f && Vel.Normalized().Dot(rel / Mathf.Max(d, 1f)) > Mathf.Cos(0.5f) && GD.Randf() < 0.33f)
            Target = f;
    }

    void Step(float h)
    {
        _age += h;
        float rho = Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);
        float sp = Vel.Length();
        var dir = Vel / Mathf.Max(sp, 0.1f);
        var perp = new Vector2(-dir.Y, dir.X);
        var acc = new Vector2(0, -9.81f) - Vel * sp * DragK * rho;
        if (Burning())
            acc += dir * Boost;
        bool gone = Target is GodotObject go && !IsInstanceValid(go);
        if (Target != null && (gone || !Target.IsTargetValid()))
        {
            Target = null;
            Guided = false;
        }
        if (Guided && Target != null && _age > Ignition)
        {
            var tp = Target.TargetPos;
            var tv = Target.TargetVel;
            var los = tp - Pos;
            float dist = los.Length();
            if (dist < Prox)
            {
                Detonate(true);
                return;
            }
            // Seeker gimbal limit.
            if (dir.Dot(los / dist) < Mathf.Cos(1.1f))
                Guided = false;
            bool radarOk = !(Target == Main.Jet && Main.Jet.Agl < 30f && dist > 600f);  // lost in ground clutter
            if (radarOk)
            {
                var rv = tv - Vel;
                float losRate = (los.X * rv.Y - los.Y * rv.X) / Mathf.Max(dist * dist, 1f);
                float vc = -los.Dot(rv) / Mathf.Max(dist, 1f);   // closing speed
                float aCmd = Nav * Mathf.Max(vc, 60f) * losRate + 9.81f * perp.Y;
                float ratio = sp / 450f;
                float amax = MaxG * 9.81f * Mathf.Clamp(ratio * ratio * rho, 0.05f, 1f);
                acc += perp * Mathf.Clamp(aCmd, -amax, amax);
            }
        }
        Vel += acc * h;
        Pos += Vel * h;
        if (Pos.Y <= Main.Terrain.Height(Pos.X))
            Detonate(false);
        else if (_age > Life)
            Detonate(false);
    }

    void Detonate(bool direct)
    {
        Dead = true;
        _trail.Emitting = false;
        var trail = _trail;
        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            if (IsInstanceValid(trail))
                trail.QueueFree();
        };
        if (!IsAgm)
        {
            var jet = Main.Jet;
            float jd = jet.Pos.DistanceTo(Pos);
            if (jet.Alive && jd < 30f)
                jet.TakeDamage(Damage * Mathf.Clamp(1.2f - jd / 30f, 0.2f, 1f));
            // If that hit destroyed the jet, it just disappears: no explosion there.
            if (jet.Alive)
                Main.SpawnExplosion(new Vector3(Pos.X, Pos.Y, LaneZ), 0.7f);
        }
        else
        {
            if (direct && Target is Unit u)
                u.TakeDamage(Damage, true, Vel.Normalized());
            Main.Explode(Pos, LaneZ, 10f, 140f, true, 0.9f);
        }
        QueueFree();
    }
}
