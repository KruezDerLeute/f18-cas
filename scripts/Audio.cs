using System.Collections.Generic;
using Godot;

/// Plays the game's sounds from assets/sounds. Put it on a plain Node named
/// "Audio" in the main scene.
///
/// World sounds are positional (AudioStreamPlayer3D, heard from the camera) and
/// distant explosions arrive late: sound travels at 343 m/s from where they
/// happen to the jet.
public partial class Audio : Node
{
    const float SpeedOfSound = 343f;

    /// Folder holding the .wav files listed in Sounds.
    [Export] public string SoundFolder = "res://assets/sounds";

    /// Every sound and whether it loops.
    static readonly (string name, bool loop)[] Sounds =
    {
        ("boom", false), ("boom_small", false), ("cannon", false), ("gun", true),
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

    /// Positional one-shot in the world, optionally delayed.
    public void PlayAt(string sound, Vector3 pos, float volDb = 0f, float pitch = 1f, float delay = 0f)
    {
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
            PitchScale = pitch,
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

    /// Seconds for sound to travel from pos to the player's jet (capped).
    public float SoundDelay(Vector3 pos)
    {
        if (Main?.Jet == null || !Main.Jet.Alive)
            return 0f;
        var j = Main.Jet.Pos;
        return Mathf.Min(pos.DistanceTo(new Vector3(j.X, j.Y, 0f)) / SpeedOfSound, 3f);
    }

    public override void _Process(double delta)
    {
        if (Main?.Jet == null)
            return;
        var j = Main.Jet;
        bool paused = Main.Paused;
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
        _engine.VolumeDb = running ? Db(0.06f + eng * 0.3f) : -80f;
        _engine.PitchScale = 0.55f + eng * 0.6f;
        _ab.VolumeDb = Db(ab * 0.75f);
        _ab.PitchScale = 0.9f + ab * 0.15f;
        float spd = alive ? j.Vel.Length() : 0f;
        float sf = Mathf.Clamp(spd / 350f, 0f, 1f);
        _wind.VolumeDb = Db(sf * sf * 0.4f + sf * 0.05f);
        _wind.PitchScale = 0.7f + sf * 0.6f;
        SetLoop(_gun, alive && j.Firing, -3f);

        bool missile = Main.SamInbound();
        SetLoop(_warn["missile_warn"], alive && missile, -9f);
        SetLoop(_warn["rwr"], alive && !missile && j.RwrLock > 0f, -11f);
        bool pull = alive && Main.PullUpWarning();
        SetLoop(_warn["pull_up"], pull, -8f);
        SetLoop(_warn["stall"], alive && !pull && j.Stalled && !j.OnGround, -11f);
        var lt = j.LockTarget;
        bool tracking = alive && lt != null && IsInstanceValid(lt) && j.LockTime > 0.8f;
        SetLoop(_warn["lock_tone"], tracking, -22f);
    }
}
