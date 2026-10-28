using System.Collections.Generic;
using Godot;

/// Plays the game's sounds from assets/sounds. Put it on a plain Node named
/// "Audio" in the main scene.
///
/// World sounds are positional (AudioStreamPlayer3D, heard from the camera) and
/// distant explosions arrive late: sound travels at 343 m/s from where they
/// happen to the jet. Above Mach 1 the jet outruns sound, so sounds made behind
/// it never arrive, and the jet's own engines fade out.
public partial class Audio : Node
{
    const float SpeedOfSound = 343f;

    /// Overall level of the engine and afterburner loops (1 = full).
    [Export] public float EngineVolume = 0.3f;

    /// Fraction of the engine and afterburner level still heard past Mach 1.
    [Export] public float SupersonicEngine = 0.08f;

    /// Overall level of the rushing-air loop (1 = full).
    [Export] public float WindVolume = 0.4f;

    /// Fraction of the wind level still heard past Mach 1.
    [Export] public float SupersonicWind = 0.1f;

    /// How much quieter the stall tone gets while the RWR is sounding, in dB.
    [Export] public float StallDuck = -12f;

    /// Folder holding the .wav files listed in Sounds.
    [Export] public string SoundFolder = "res://assets/sounds";

    /// Every sound and whether it loops.
    static readonly (string name, bool loop)[] Sounds =
    {
        ("boom", false), ("boom_small", false), ("sonic_boom", false), ("cannon", false), ("gun", true),
        ("aaa", false), ("launch", false), ("flare", false), ("release", false),
        ("thump", false), ("hit", false), ("engine", true), ("afterburner", true),
        ("wind", true), ("rwr", true), ("missile_warn", true), ("pull_up", true),
        ("stall", true), ("lock_tone", true),
    };

    public Game Main;
    readonly Dictionary<string, AudioStreamWav> _sounds = new();
    readonly Dictionary<string, AudioStreamPlayer> _warn = new();
    AudioStreamPlayer _engine;
    AudioStreamPlayer _ab;
    AudioStreamPlayer _wind;
    AudioStreamPlayer _gun;

    public override void _Ready()
    {
        LoadAll();
        _engine = LoopPlayer("engine", true);
        _ab = LoopPlayer("afterburner", true);
        _wind = LoopPlayer("wind", true);
        _gun = LoopPlayer("gun", false);
        foreach (var n in new[] { "rwr", "missile_warn", "pull_up", "stall", "lock_tone" })
            _warn[n] = LoopPlayer(n, false);
        GD.Print($"AUDIO: device '{AudioServer.OutputDevice}', {(int)AudioServer.GetMixRate()} Hz, {_sounds.Count} sounds");
    }

    void LoadAll()
    {
        foreach (var (name, loop) in Sounds)
        {
            var s = GD.Load<AudioStreamWav>($"{SoundFolder}/{name}.wav");
            if (loop)
            {
                // Plain WAV files carry no loop points, so set them here: loop the whole file.
                s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                s.LoopBegin = 0;
                s.LoopEnd = (int)System.Math.Round(s.GetLength() * s.MixRate);
            }
            _sounds[name] = s;
        }
    }

    AudioStreamPlayer LoopPlayer(string sound, bool start)
    {
        var p = new AudioStreamPlayer { Stream = _sounds[sound], VolumeDb = -80f };
        AddChild(p);
        if (start)
            p.Play();
        return p;
    }

    static void SetLoop(AudioStreamPlayer p, bool on, float volDb)
    {
        p.VolumeDb = volDb;
        if (on && !p.Playing)
            p.Play();
        else if (!on && p.Playing)
            p.Stop();
    }

    static float Db(float x) => Mathf.LinearToDb(Mathf.Max(x, 0.00001f));

    // ---------------------------------------------------------------- playback

    /// Non-positional one-shot (cockpit sounds: release, hits, flares).
    public void Play(string sound, float volDb = 0f, float pitch = 1f)
    {
        if (GetChildCount() > 64)
            return;
        var p = new AudioStreamPlayer { Stream = _sounds[sound], VolumeDb = volDb, PitchScale = pitch };
        AddChild(p);
        p.Play();
        p.Finished += p.QueueFree;
    }

    /// Positional one-shot in the world, optionally delayed. A negative delay
    /// (from SoundDelay: the jet outran it) means it's never heard.
    public void PlayAt(string sound, Vector3 pos, float volDb = 0f, float pitch = 1f, float delay = 0f)
    {
        if (delay < 0f)
            return;
        if (delay > 0.02f)
        {
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (IsInstanceValid(this))
                    PlayAt(sound, pos, volDb, pitch, 0f);
            };
            return;
        }
        if (GetChildCount() > 64)
            return;
        var p = new AudioStreamPlayer3D
        {
            Stream = _sounds[sound],
            VolumeDb = volDb,
            PitchScale = pitch * Doppler(pos),
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            UnitSize = 160f,
            MaxDb = 6f,
            MaxDistance = 9000f,
            Position = pos,
        };
        AddChild(p);
        p.Play();
        p.Finished += p.QueueFree;
    }

    /// Pitch shift from the jet (where you hear from) moving relative to a
    /// still sound source: higher flying towards it, lower flying away.
    float Doppler(Vector3 pos)
    {
        if (Main?.Jet == null || !Main.Jet.Alive)
            return 1f;
        var j = Main.Jet;
        var toSrc = pos - new Vector3(j.Pos.X, j.Pos.Y, 0f);
        float d = toSrc.Length();
        if (d < 1f)
            return 1f;
        float vr = new Vector3(j.Vel.X, j.Vel.Y, 0f).Dot(toSrc / d);   // closing speed
        return Mathf.Clamp((SpeedOfSound + vr) / SpeedOfSound, 0.5f, 2f);
    }

    /// Seconds until a sound made at pos now catches the moving jet (capped),
    /// or -1 if it never does: a supersonic jet flying away from it.
    public float SoundDelay(Vector3 pos)
    {
        if (Main?.Jet == null || !Main.Jet.Alive)
            return 0f;
        var j = Main.Jet;
        // The wavefront (radius c*t) meets the jet at P + V*t:
        // |D + V t| = c t  ->  (V.V - c^2) t^2 + 2 (D.V) t + D.D = 0,  with D = P - source.
        var d = new Vector3(j.Pos.X, j.Pos.Y, 0f) - pos;
        var v = new Vector3(j.Vel.X, j.Vel.Y, 0f);
        float a = v.LengthSquared() - SpeedOfSound * SpeedOfSound;
        float b = 2f * d.Dot(v);
        float c = d.LengthSquared();
        float t;
        if (Mathf.Abs(a) < 1f)   // right at Mach 1
            t = b < 0f ? -c / b : -1f;
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f)
                return -1f;   // supersonic, and the source is outside the Mach cone ahead
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a);
            float t2 = (-b + sq) / (2f * a);
            // Subsonic: one root is negative, take the positive one. Supersonic: both
            // roots positive only when flying towards the source; take the first.
            t = a < 0f ? Mathf.Max(t1, t2) : (t1 > 0f && t2 > 0f ? Mathf.Min(t1, t2) : -1f);
        }
        return t < 0f ? -1f : Mathf.Min(t, 3f);
    }

    public override void _Process(double delta)
    {
        if (Main?.Jet == null)
            return;
        var j = Main.Jet;
        bool paused = Main.Paused || Main.InMenu;
        foreach (var p in new[] { _engine, _ab, _wind, _gun })
            p.StreamPaused = paused;
        foreach (var p in _warn.Values)
            p.StreamPaused = paused;
        if (paused)
            return;
        bool alive = j.Alive;
        bool running = alive && j.Fuel > 0f;
        float eng = running ? Mathf.Clamp(j.EngineSpool, 0f, 1f) : 0f;
        float ab = running ? Mathf.Clamp((j.EngineSpool - 1f) / 0.25f, 0f, 1f) : 0f;
        // The engines are behind the nose: past Mach 1 most of their noise can't
        // catch up, leaving only a faint hum carried through the airframe.
        float rear = alive ? Mathf.Lerp(1f, SupersonicEngine, Mathf.SmoothStep(0.96f, 1.02f, j.Mach)) : 1f;
        _engine.VolumeDb = running ? Db((0.06f + eng * 0.3f) * rear * EngineVolume) : -80f;
        _engine.PitchScale = 0.55f + eng * 0.6f;
        _ab.VolumeDb = Db(ab * 0.75f * rear * EngineVolume);
        _ab.PitchScale = 0.9f + ab * 0.15f;
        float spd = alive ? j.Vel.Length() : 0f;
        float sf = Mathf.Clamp(spd / 350f, 0f, 1.3f);
        // Rushing air: rises steeply with speed and roars louder near the ground.
        float lowRush = 1f + 0.6f * (1f - Mathf.SmoothStep(30f, 400f, j.Agl)) * sf;
        // Past Mach 1 the shock wave carries most of the rush away behind the jet.
        float windSuper = alive ? Mathf.Lerp(1f, SupersonicWind, Mathf.SmoothStep(0.96f, 1.02f, j.Mach)) : 1f;
        _wind.VolumeDb = Db((sf * sf * 0.6f + sf * 0.05f) * lowRush * windSuper * WindVolume);
        _wind.PitchScale = 0.6f + sf * 0.85f;
        SetLoop(_gun, alive && j.Firing, -3f);

        // Warnings: each one sounds whenever its HUD warning is up (same
        // conditions as Hud.DrawWarnings), independently, so several can play at once.
        bool locked = alive && j.RwrLock > 0f;
        bool launch = alive && Main.SamInbound();
        // The RWR sounds one of its two: the launch warning replaces the lock warning.
        SetLoop(_warn["rwr"], locked && !launch, 0f);
        SetLoop(_warn["missile_warn"], launch, 0f);
        SetLoop(_warn["pull_up"], alive && Main.PullUpWarning(), 0f);
        // The stall tone is a steady buzz that drowns out the RWR's beeps, so it
        // ducks under them while the RWR is sounding.
        SetLoop(_warn["stall"], alive && j.Stalled && !j.OnGround, locked || launch ? -11f + StallDuck : -11f);
        var lt = j.LockTarget;
        bool tracking = alive && lt != null && IsInstanceValid(lt) && lt.Alive && j.LockTime > 0.8f;
        SetLoop(_warn["lock_tone"], tracking, -22f);
    }
}
