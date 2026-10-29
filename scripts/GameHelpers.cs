using System.Collections.Generic;
using Godot;

public partial class Game : Node3D
{
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
            ["flir"] = new[] { Key.V },
            ["restart"] = new[] { Key.R },
            ["help"] = new[] { Key.H },
            ["pause"] = new[] { Key.P, Key.Escape },
            ["main_menu"] = new[] { Key.Enter, Key.KpEnter },
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

    static StandardMaterial3D ParticleMat(bool additive, Texture2D tex)
    {
        return new StandardMaterial3D()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, //smoothens the edges of the quad
            BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = tex, //soft puff effect
        };
    }

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
        // Thermal copies of the units; the FLIR shader reads magenta as heat (see Flir).
        HotMat = new StandardMaterial3D { AlbedoColor = new Color(1, 0, 1), EmissionEnabled = true, Emission = new Color(0.6f, 0, 0.6f) };
        WreckHotMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0, 0.5f), EmissionEnabled = true, Emission = new Color(0.35f, 0, 0.35f) };
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
}
