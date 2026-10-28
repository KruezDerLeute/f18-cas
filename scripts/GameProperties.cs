
using System.Collections.Generic;
using Godot;


public partial class Game : Node3D
{
    public const float BulletK = 2.0e-4f;   // drag constants: a = -k * v * |v| * density
    public const float ShellK = 3.0e-5f;
    public const float BombK = 1.1e-4f;
    public const float FlareK = 2.5e-3f;
    const int MaxDebris = 140;
    const int MaxScorch = 60;
    /// Waves to clear for the reinforcements to arrive and the mission to be won.
    public const int MissionWaves = 1;   // TEMP: testing the ending. Set back to 3.

    [ExportGroup("Units")]
    [Export] public PackedScene TankScene;
    [Export] public PackedScene TruckScene;
    [Export] public PackedScene AaaScene;
    [Export] public PackedScene SamScene;
    [ExportGroup("Weapons")]
    [Export] public PackedScene MaverickScene;
    [Export] public PackedScene SamMissileScene;
    [Export] public PackedScene BombModel;
    [ExportGroup("Effects")]
    [Export] public Texture2D ParticleHard;
    [Export] public Texture2D ParticleSoft;
    [ExportGroup("UI")]
    [Export] public PackedScene MenuScene;
    /// AWACS voice line played over the mission briefing.
    [Export] public AudioStream BriefingVoice;
    /// Ground's radio call over the victory ending (it says every tank made it).
    [Export] public AudioStream VictoryVoice;

    /// X of the friendly front line; read from the "FriendlyLine" marker in world.tscn.
    public float FriendlyLine = 3800f;
    public Terrain Terrain;
    public Jet Jet;
    public Camera3D Camera;
    public Audio Audio;
    public Flir Flir;
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
    public int PlayerKills;
    public int EnemiesSpawned;
    public int FriendlyStart;
    bool _endingShown;
    public float Shake;
    float _zoom = 1f;
    public float CamDist = 220f;
    float _baseFov = 50f;
    Vector2 _camOffset;
    public bool Paused;
    /// True while the title screen or briefing is up: nothing simulates and the HUD is hidden.
    public bool InMenu;
    CanvasLayer _menuLayer;
    float _menuTime;
    float _menuX0 = 2200f;
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
    public StandardMaterial3D HotMat;
    public StandardMaterial3D WreckHotMat;
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
}
