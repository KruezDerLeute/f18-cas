using System.Globalization;
using System.Linq;
using Godot;

/// Developer tools, run from the command line (not part of the game):
///   godot --headless --path . res://tools/tools.tscn -- --export-f18
///   godot --headless --path . res://tools/tools.tscn -- --bake-assets
public partial class Tools : Node
{
    const string PackPath = "res://free_-_fighter_jet_collection_-_low_poly.glb";
    const float JetLength = 18.3f;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var args = OS.GetCmdlineUserArgs();
        bool ok = true;
        if (args.Contains("--bake-assets"))
            ok &= AssetBaker.BakeAll();
        if (args.Contains("--export-f18"))
            ok &= ExportF18();
        GetTree().Quit(ok ? 0 : 1);
    }

    /// Cuts the F-18 out of the six-jet pack, scales it to 18.3 m, centres it on
    /// the origin (nose along +X) and saves it as assets/models/f18.glb.
    bool ExportF18()
    {
        var root = new Node3D { Name = "F18" };
        AddChild(root);
        var model = new Node3D { Name = "Model" };
        root.AddChild(model);
        var inst = GD.Load<PackedScene>(PackPath).Instantiate<Node3D>();
        model.AddChild(inst);
        var any18 = inst.FindChild("F-18*", true, false);
        var parent = any18 != null ? any18.GetParent() : inst;
        foreach (var c in parent.GetChildren())
        {
            if (!c.Name.ToString().StartsWith("F-18"))
            {
                parent.RemoveChild(c);
                c.Free();
            }
        }
        var raw = Measure(inst, false);
        model.Scale = Vector3.One * (JetLength / raw.Size.X);
        var box = Measure(inst, false);
        model.Position = -box.GetCenter();
        box = Measure(inst, false);
        var withGear = Measure(inst, true);

        float half = box.Size.Z * 0.5f;
        float tailX = box.Position.X + 1.2f;
        float midY = box.Position.Y + box.Size.Y * 0.22f;
        float stY = box.Position.Y + 0.25f;
        GD.Print($"MEASURE box(gear up) min={V(box.Position)} max={V(box.End)} size={V(box.Size)}");
        GD.Print($"MEASURE box(gear down) min={V(withGear.Position)} max={V(withGear.End)}");
        GD.Print($"MEASURE model scale={model.Scale.X:F5} offset={V(model.Position)}");
        GD.Print($"MEASURE flames x={tailX:F2} y={midY:F2} z=+-0.55");
        GD.Print($"MEASURE abLight x={tailX - 3f:F2} y={midY:F2}");
        GD.Print($"MEASURE muzzle x={box.End.X - 1.5f:F2} y={midY + 0.6f:F2}");
        GD.Print($"MEASURE bombs x=-1 y={stY:F2} z=+-{0.36f * half:F2} +-{0.52f * half:F2}");
        GD.Print($"MEASURE mavs x=-0.7 y={stY + 0.05f:F2} z=+-{0.7f * half:F2}");
        GD.Print($"MEASURE vapor x=-1.8 y={box.Position.Y + 1f:F2} z=+-{half - 0.2f:F2}");
        GD.Print($"MEASURE smoke x={tailX:F2} y={midY:F2}");
        foreach (var n in inst.FindChildren("F-18*", "", true, false))
            GD.Print($"MEASURE node {n.Name}");

        RemoveChild(root);
        var err = AssetBaker.ExportGlb(root, Assets.ModelPath("f18"));
        GD.Print(err == Error.Ok ? "Exported res://assets/models/f18.glb" : $"Export FAILED: {err}");
        return err == Error.Ok;
    }

    Aabb Measure(Node inst, bool includeGear)
    {
        var result = new Aabb();
        bool first = true;
        foreach (var node in inst.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (!includeGear && IsGearDown(mi))
                continue;
            var b = mi.GlobalTransform * mi.GetAabb();
            result = first ? b : result.Merge(b);
            first = false;
        }
        return result;
    }

    static bool IsGearDown(Node n)
    {
        for (; n != null; n = n.GetParent())
            if (n.Name.ToString().Contains("LandingOn"))
                return true;
        return false;
    }

    static string V(Vector3 v) => $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
}
