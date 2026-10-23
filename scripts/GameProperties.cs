
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
