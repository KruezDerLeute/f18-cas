# Chapter 9: The scripts, file by file

The earlier chapters explain what each script *does*. This chapter explains **how the code is written**: every class, every method, and the C# and Godot features they use. Open each file in your code editor and read along, top to bottom.

The files are in reading order: small helpers first, then the things that move, then `Game.cs`, which ties everything together.

| § | File | What it is |
|---|---|---|
| 9.1 | `Types.cs` | shared interface, enums and the message class |
| 9.2 | `Fx.cs` | helper functions for materials, meshes and rotations |
| 9.3 | `Terrain.cs` | ground heights and ground collision |
| 9.4 | `Clouds.cs` | the sky's clouds |
| 9.5 | `Shell.cs` | bullets, shells, bombs and flares |
| 9.6 | `Jet.cs` | the F/A-18's flight model, weapons and visuals |
| 9.7 | `Unit.cs` | ground vehicles and their AI |
| 9.8 | `Missile.cs` | Mavericks and SAMs |
| 9.9 | `Game.cs` | the main loop, spawning, hits, effects, waves and camera |
| 9.10 | `Hud.cs` | everything drawn on screen |
| 9.11 | `Audio.cs` | sound |
| 9.12 | C# quick reference | the language features used, in one table |

---

## 9.0 Before you start: how Godot runs the scripts

**Methods Godot calls for you.** A script that extends a node type (`Node3D`, `Control`, `Node`) can *override* these, and Godot calls them at the right time:

| Method | When Godot calls it | Used for |
|---|---|---|
| `_Ready()` | once, when the node enters the scene. Children are ready before their parent | setup |
| `_PhysicsProcess(double delta)` | exactly 60 times a second, `delta` = 1/60 s | simulation: anything that must behave the same on every computer |
| `_Process(double delta)` | once per drawn frame, so it can run faster or slower than 60 times a second | camera, input that isn't gameplay, sound |
| `_UnhandledInput(InputEvent e)` | for each input event that no UI control used | mouse-wheel zoom |
| `_Draw()` | when a `Control` is asked to redraw itself | the HUD |

`delta` is a `double`; the scripts immediately turn it into a `float dt = (float)delta;`, because Godot's vectors use `float`.

**Only one script is "in charge".** Only `Game`, `Hud`, `Audio` and `Clouds` override those methods. The jet, the vehicles and the missiles do *not* have their own `_PhysicsProcess`. Instead, `Game._PhysicsProcess` calls `Jet.Sim(dt)`, then each `Unit.Sim(dt)`, then each `Missile.Sim(dt)`, in that order. That fixed order makes the game predictable: the jet always moves before bullets check for hits.

**Who calls whom:**

```
Game._Ready()
 ├─ Terrain.Build()              read ground heights from the mesh
 ├─ Unit.Init()  ×4              your friendly tanks
 └─ Jet.Init()

Game._PhysicsProcess()  (60/s)
 ├─ Jet.Sim()                    fly, fire  ──► Game.SpawnRound / SpawnBomb / SpawnMaverick / SpawnFlare
 ├─ Unit.Sim()  each vehicle     drive, AI  ──► Game.SpawnRound / SpawnSam
 ├─ Game.UpdateShells()          move every Shell, check hits ──► Unit.TakeDamage / Jet.TakeDamage / Game.Explode
 ├─ Missile.Sim()  each          guidance   ──► Detonate ──► Game.Explode / Jet.TakeDamage
 └─ Game.UpdateWaves()           next wave?  ──► Game.StartWave ──► Unit.Init

Game._Process()  (every frame)   restart, pause, zoom, messages, UpdateCamera()
Hud._Process() ──► _Draw()       draw everything on screen
Audio._Process()                 engine, wind, warning tones
```

**`public partial class`.** Every Godot script class is `partial`, because Godot generates a second half of the class behind the scenes (the code that makes `[Export]` and signals work). The class name must match the file name.

**Two coordinate systems.** Remember the idea from the start page: the simulation uses `Vector2` (x along the battlefield, y up), and only the visuals use `Vector3`. You'll see this line, in some form, in every moving script:

```csharp
Position = new Vector3(Pos.X, Pos.Y, LaneZ);   // 2D simulation → 3D node
```

---

## 9.1 `Types.cs`: shared definitions

This file has no logic. It defines types that several other files use.

```csharp
public interface ITarget
{
    Vector2 TargetPos { get; }
    Vector2 TargetVel { get; }
    bool IsTargetValid();
}
```

- An **interface** is a list of members a class promises to have. `Jet`, `Unit` and `Shell` (for flares) all say `: ITarget` and provide these three members.
- `Missile` stores its target as an `ITarget`. It doesn't know or care whether it's chasing a jet, a tank or a flare; it just reads `TargetPos` and `TargetVel`. That's why a flare can "steal" a SAM: the missile simply swaps one `ITarget` for another.
- `{ get; }` means a read-only *property*: it looks like a field from outside, but the class decides how to produce the value.

```csharp
public enum UnitKind { Tank, Truck, Aaa, Sam, FriendlyTank }
public enum ShellKind { Bullet, Shell, Aaa, Bomb, Flare }
public enum GameState { Play, Dead, Overrun }
```

An **enum** is a fixed set of named values. Writing `UnitKind.Sam` instead of a number like `3` means typos are compile errors, and the Inspector can show a dropdown for an `[Export]`ed enum.

> `GameState.Overrun` is still defined, and `Hud.DrawEnd()` still has a title for it, but no code sets it any more: the game now ends only when the jet is lost.

```csharp
public class Message { public string Text; public float Time; public Color Color; }
```

One line in the message feed. `Time` counts down; `Game._Process` removes the message when it reaches 0, and `Hud` fades it out over the last second.

---

## 9.2 `Fx.cs`: small helpers

`public static class Fx` holds functions that don't belong to any object. You call them with the class name: `Fx.Rand(1, 5)`.

**Random numbers:**
```csharp
public static float Rand(float a, float b) => (float)GD.RandRange(a, b);
```
`=>` is an *expression body*: a one-line method that returns the value on the right. `GD.RandRange` returns a `double`, so it's cast to `float`.

**Changing one part of a rotation:**
```csharp
public static void SetRotZ(this Node3D n, float z)
{
    var r = n.Rotation;
    r.Z = z;
    n.Rotation = r;
}
```
- Godot's `Vector3` is a **struct**, a value type. `n.Rotation` hands you a *copy*, so `n.Rotation.Z = 1` would only change the copy, and C# refuses to compile it. The fix is always the same three steps: copy it out, change it, write it back.
- The `this` before the first parameter makes it an **extension method**. You can call it as if it belonged to `Node3D`: `_gunPivot.SetRotZ(angle)` instead of `Fx.SetRotZ(_gunPivot, angle)`. `SetRotY` is the same for Y.

**Materials:**

| Method | Makes | Used for |
|---|---|---|
| `Mat(color, rough, metal)` | a normal lit material | charred wrecks, debris, scorch marks |
| `Glow(color, energy)` | black, but *emits* light of `color` | tracers, flares |
| `Additive(color)` | unshaded, see-through, adds its colour to what's behind | flame-like meshes |

They use **object initializers**: `new() { AlbedoColor = c, Roughness = rough }` creates the object and sets those properties in one expression. The `new()` without a type name is *target-typed*: C# knows the type from the method's return type.

**Meshes:** `Box`, `Cyl` and `Sphere` each return a ready-made `MeshInstance3D`. Parameters like `Vector3 p = default` are **optional**: leave them out and you get `(0, 0, 0)`. `AlongX` is the rotation that lays Godot's upright cylinder along +X.

**Colour ramps:** `Ramp(offsets, colors)` builds a `Gradient`. Particles use these to change colour over their lifetime: fire starts white-yellow, turns orange, then fades out.

---

## 9.3 `Terrain.cs`: the ground

### Constants and the table

```csharp
public const float XMin = -5000f;
public const float XMax = 17000f;
public const float Step = 10f;
public static int Count => (int)((XMax - XMin) / Step) + 1;   // 2201 samples
readonly float[] _table = new float[Count];
```

- `const` values are fixed at compile time. Other scripts use them too: `Terrain.RunwayX0`, `Terrain.BaseH`.
- `readonly` means the *array* can't be replaced after construction, though its contents can change.
- `_table[i]` is the ground height at `x = XMin + i * Step`.

### `Height(x)`: the most-called function in the game

```csharp
float f = (Mathf.Clamp(x, XMin, XMax - Step) - XMin) / Step;   // 1. which sample, as a fraction
int i = (int)f;                                                  // 2. the sample to the left
return Mathf.Lerp(_table[i], _table[i + 1], f - i);              // 3. blend towards the one on the right
```

For `x = 3815`: `f = (3815 + 5000) / 10 = 881.5`, so `i = 881` and the result is halfway between `_table[881]` and `_table[882]`. `Clamp` keeps `i + 1` inside the array even if something flies off the map.

`Lerp(a, b, t)` (linear interpolation) returns `a` when `t = 0`, `b` when `t = 1`, and a straight-line blend in between. You'll see it everywhere in this project.

### `Build()`: reading the heights from the mesh

1. `GetNodeOrNull<Node3D>("TerrainMesh")` finds the model you placed. It returns `null` instead of throwing an error, so the script can print a helpful message.
2. `System.Array.Fill(_table, float.NegativeInfinity)` marks every sample as "not found yet".
3. `FindChildren("*", "MeshInstance3D", true, false)` finds every mesh inside the model.
   - For **every** mesh, it switches on `VertexColorUseAsAlbedo` on each material (the fix for the grey terrain).
   - Only meshes whose name contains `"Ground"` are used for height. The trees are skipped with `continue`.
4. `mi.Mesh.GetFaces()` returns every triangle as three corners in a row. `xf * faces[t]` moves a corner from the mesh's own coordinates into world coordinates.
5. Each triangle goes to `SampleLane`. If all three corners are near the lane (`InBand`), the triangle is also kept for collision.
6. `FillGaps()` fills any samples the mesh didn't cover by copying the last good height.
7. The kept triangles become a `ConcavePolygonShape3D` (a collision shape that can be any shape) inside a `StaticBody3D` (a physics body that never moves). Debris and thrown turrets land on it.

### `SampleLane` and `Cross`: slicing a triangle

`Cross(a, b, ...)` checks one edge of a triangle. If `a` and `b` are on opposite sides of z = 0, the edge crosses the lane at

```csharp
float t = a.Z / (a.Z - b.Z);          // how far along the edge z reaches 0
pts[n++] = new Vector2(Mathf.Lerp(a.X, b.X, t), Mathf.Lerp(a.Y, b.Y, t));
```

A triangle that crosses the lane produces two such points: a short line from `p0` (left) to `p1` (right). `SampleLane` writes that line's height into every 10 m sample between them. `Mathf.Max(_table[i], y)` keeps the highest value if two triangles cover the same sample.

`stackalloc` and `Span<Vector2>` make a small temporary array without creating garbage. It's called hundreds of thousands of times at startup, so this keeps loading fast. `ref int n` lets `Cross` add to the caller's counter.

---

## 9.4 `Clouds.cs`: the sky

```csharp
[Export] public Texture2D Texture;
[Export] public int Count = 70;
[Export] public int Seed = 5;
```

`[Export]` puts these in the Inspector. The `= 70` is the default you see there.

`_Ready()`:
1. Makes **one** material and shares it between all 350 quads. Sharing saves memory and drawing time. `BillboardMode = Enabled` turns each quad to face the camera.
2. Creates its own random number generator with a fixed seed: `new RandomNumberGenerator { Seed = (ulong)Seed }`. A fixed seed gives the same "random" numbers every run, so the sky is the same every time. (`Fx.Rand` uses Godot's global generator, which `Game` seeds randomly.)
3. Two loops: `Count` cluster centres, and 5 quads scattered around each centre. `rng.RandfRange(-4500f, -600f)` for z puts them behind the lane.

---

## 9.5 `Shell.cs`: everything that flies without guidance

```csharp
public class Shell : ITarget
```

It is **not** a Godot node: no `partial`, no `: Node3D`. It's a plain C# object, so creating 100 per second costs almost nothing. `Game` keeps them in a `List<Shell>` and moves them itself.

**Fields:** `Kind`, `Team` (0 or 1), `ByPlayer` (for friendly-fire scoring), `Pos`, `Prev`, `Vel`, `Z` (depth, for drawing), `DragK`, `Life` (max age), `Age`, `Damage`, `Blast` (radius, bombs only), `ArmTime` (bomb fuze delay), `Dead`, and `Node`, the optional 3D object you see (a tracer, a bomb model or a flare). Most rounds have `Node = null`; they're invisible.

**`Step(dt)`:**
```csharp
Prev = Pos;                                                  // remember where we were
float rho = Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);        // air density: 1 at sea level, 0.37 at 8.5 km
float sp = Vel.Length();
Vel += (new Vector2(0, -9.81f) - Vel * sp * DragK * rho) * dt;
Pos += Vel * dt;
```

This is **Euler integration**, the simplest way to simulate motion: work out the acceleration, add `acceleration × dt` to the velocity, add `velocity × dt` to the position. The drag term `Vel * sp` is velocity times speed, so drag grows with speed squared, like real air.

If there's a `Node`, it is moved to match and turned to point along `Vel.Angle()`.

**`Kill()`** marks the shell dead and frees its node with `QueueFree()`. Godot nodes must be freed with `QueueFree`, which deletes them safely at the end of the frame, not immediately.

The last three lines implement `ITarget`, so a flare (a `Shell` with `Kind = Flare`) can be a missile's target.

---

## 9.6 `Jet.cs`: the F/A-18

The biggest script after `Game.cs`. It's split into sections by comment lines: setup, simulation, weapons and visuals.

### Constants

The top of the class lists the real aircraft's numbers as `const`: wing area `S = 46.5`, lift slope `Cla = 3.6`, stall angle, drag coefficients, masses, thrust, g-limits, store weights, `GearH` (centre to wheels) and the weapon loads (`MaxBombs = 4`, `MaxMavs = 2`, `MaxAmmo = 578`, `MaxFlares = 130`). Change one here and the whole flight model follows.

`public const float FuelMax` is `public` because `Hud` doesn't need it, but other code could; the rest are private.

### State fields

| Field | Meaning |
|---|---|
| `Pos`, `Vel` | position and velocity in the 2D plane |
| `Theta`, `_thetaDot` | where the nose points (radians), and how fast it's turning |
| `_phi`, `_phiTarget` | roll angle now and where it's going: 0 upright, π inverted |
| `Throttle`, `EngineSpool` | the lever position (0–1.25), and what the engines are actually giving |
| `Fuel`, `Hp`, `Alive` | |
| `GearDown`, `OnGround`, `Airbrake` | |
| `_stick` | smoothed W/S input, −1 to +1 |
| `GLoad`, `Alpha`, `Mach`, `Agl`, `Stalled` | computed each step; the HUD shows them |
| `Ammo`, `Bombs`, `Mavs`, `Flares` and the `_...Cd` cooldowns | weapons |
| `LockTarget`, `LockTime` | Maverick lock |
| `RwrLock` | set to 0.3 s by a SAM radar each frame it's locking you; counts down |
| `Ccip`, `CcipTime`, `GunCcip` | predicted bomb and bullet impact points. `Vector2?` means "a `Vector2` or `null`" |

Fields starting with `_` are private; the others are `public` so `Game`, `Hud` and `Audio` can read them.

### `Init(Game main)`

Called by `Game._Ready`. It stores the `Game` reference, takes the starting position from where you placed the node, and sets the starting velocity along the node's Z rotation:

```csharp
Vel = Vector2.FromAngle(Rotation.Z) * StartSpeed;   // FromAngle(a) = (cos a, sin a)
Theta = Vel.Angle() + 0.06f;                         // nose slightly above the flight path, so there's lift
```

Then `FindParts()` and a first `UpdateVisual()`.

### Small helpers

```csharp
public float Mass() => EmptyMass + Fuel + Bombs * Mk82Mass + Mavs * Agm65Mass + Ammo * 0.26f;
public float UpSign() => Mathf.Cos(_phi) >= 0f ? 1f : -1f;   // +1 upright, −1 inverted
public Vector2 Fwd() => Vector2.FromAngle(Theta);            // unit vector out of the nose
Vector2 BodyDown() => new Vector2(Mathf.Sin(Theta), -Mathf.Cos(Theta)) * UpSign();   // out of the belly
```

The jet gets lighter as it burns fuel and drops weapons, so it accelerates and turns better.

`UpSign()` appears all over the flight code. Inverted, "up" for the pilot is down for the world, so lift and pull are multiplied by it.

### `FindParts()`

- `GetNode<Node3D>("Roll/FlameL")` finds a child by path. The `<Node3D>` checks the type: if the node is missing or the wrong type, Godot reports an error naming the path.
- The gear meshes are found with a wildcard: `FindChildren("*Landing*", "", true, false)`, then sorted into `_gearOn` and `_gearOff` by name.
- `$"Roll/Bomb{i}"` is an **interpolated string**: `{i}` is replaced by the number, giving `Roll/Bomb0`, `Roll/Bomb1`…
- The vapour emitters are `CpuParticles3D` created in code and placed at your `VaporL`/`VaporR` markers' positions. `LocalCoords = false` means particles stay where they were emitted in the world instead of moving with the jet, which is what leaves a trail.
- The damage smoke uses `Main.MakeParticles(...)`, the shared particle builder in `Game`.

### `Sim(dt)`: one physics frame

```csharp
RwrLock = Mathf.Max(RwrLock - dt, 0f);
if (!Alive) return;
Controls(dt);
const int sub = 3;
for (int i = 0; i < sub; i++)
{
    Integrate(dt / sub);
    if (!Alive) return;
}
GroundService(dt);
Weapons(dt);
UpdateVisual();
ComputeCcip();
```

The flight physics runs **3 times per frame** with a third of the time step each (*sub-stepping*). The pitch control is a stiff spring, and stiff springs simulated with large steps can overshoot and blow up. Smaller steps keep it stable. The `if (!Alive) return;` stops immediately if a step crashed the jet.

### `Controls(dt)`

- `Input.GetAxis("pitch_down", "pitch_up")` returns −1, 0 or +1 from two actions.
- `Mathf.MoveToward(_stick, target, dt * 5f)` moves the stick towards the key's value at 5 units per second, so it takes 0.2 s to go from centre to full: a smooth input instead of an instant jump.
- Throttle changes at 0.5 per second, limited to 0–1.25. Above 1.0 is afterburner.
- `IsActionJustPressed` is true only on the frame the key goes down, so F, G and the like toggle once per press.
- The roll: F adds π to `_phiTarget`, and `_phi` moves towards it at 3.8 rad/s. When a full turn (`Tau` = 2π) is completed, both are reduced by 2π so the numbers don't grow forever.

### Aerodynamics helpers

```csharp
static float Cl(float a)
{
    float aa = Mathf.Abs(a);
    if (aa <= AlphaStall) return Cla * a;                                   // straight line before the stall
    float t = Mathf.Clamp((aa - AlphaStall) / 0.2f, 0f, 1f);
    return Mathf.Lerp(Cla * AlphaStall * Mathf.Sign(a), 0.95f * Mathf.Sin(2f * a), t);   // blend to flat-plate lift
}
```

Past the stall, the lift blends over 0.2 rad (about 11°) into `0.95·sin(2α)`, roughly how a flat plate behaves. That's much lower than the peak, so the wing "lets go".

`WaveDrag(m)` uses `SmoothStep` to add drag rising between Mach 0.85 and 1.05, easing off a little above Mach 1.2. `StoresDrag()` adds drag for each bomb and missile still on the wings, the gear and the speedbrake.

### `Integrate(h)`: the flight model

Read it in five parts.

**1. The situation:**
```csharp
float rho = 1.225f * Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);   // air density, kg/m³
float v = Vel.Length();
var dir = v > 0.5f ? Vel / v : Fwd();     // direction of travel
float gamma = dir.Angle();                // flight-path angle
Alpha = Mathf.Wrap(Theta - gamma, -Mathf.Pi, Mathf.Pi);   // angle of attack
float qbar = 0.5f * rho * v * v;          // dynamic pressure
var perp = new Vector2(-dir.Y, dir.X);    // 90° to the left of travel: the lift direction
```
`Mathf.Wrap` keeps an angle between −π and π, so 350° becomes −10°. Without it, angle differences near ±180° would be wrong.

`flaps` adds extra lift (0.45) at low speed with the gear down, for landing. `Mach` divides by the speed of sound, which falls slightly with altitude.

**2. Fly-by-wire: choose a target angle of attack.** Lift = `qs · CL`, and `CL = Cla · α`, so the α that gives a wanted lift force `F` is `F / (qs · Cla)`. The code uses that to find three angles:

```csharp
float aTrim = ((m * G * perp.Y / qs) - flaps * us) / claEff;      // lift = weight: 1 g, straight flight
float aPos  = Mathf.Min(AlphaLimit, (GLimit * m * G / qs - flaps) / claEff);   // +7.5 g, capped at the alpha limit
float aNeg  = Mathf.Min(0.3f, (-GNeg * m * G / qs + flaps) / claEff);          // −3 g
```

Then the stick blends between them: centred gives `aTrim`, full back gives `aPos`, full forward gives `-aNeg`:

```csharp
if (_stick > 0f) cmdB = Mathf.Lerp(trimB, aPos, _stick);
else if (_stick < 0f) cmdB = Mathf.Lerp(trimB, -aNeg, -_stick);
aCmd = cmdB * us;     // inverted: flip the sign
```

On the ground, pulling just raises the nose up to 0.24 rad for take-off.

**3. Turn the nose towards that angle: a damped spring.**
```csharp
float w = 5.5f * Mathf.Sqrt(qn);    // spring stiffness: higher when fast
float thetaAcc = w * w * (aCmd - Alpha) - 2f * 0.85f * w * (_thetaDot - gammaDot);
_thetaDot = Mathf.Clamp(_thetaDot + thetaAcc * h, -1.6f, 1.6f);
Theta += _thetaDot * h;
```
- The first term pulls the nose towards the commanded alpha, harder the further away it is.
- The second term is the **damper**: it resists the nose rotating faster than the flight path is turning (`gammaDot`), which stops it overshooting and wobbling. 0.85 is the *damping ratio*: just under 1, so it settles quickly with almost no overshoot.
- `_thetaDot` is limited to 1.6 rad/s, a maximum pitch rate.

**4. The forces.**
```csharp
float cd = Cd0 + StoresDrag() + KInd * cl * cl + 1.4f * Mathf.Pow(Mathf.Sin(Alpha), 2) * post + WaveDrag(Mach);
var lift = perp * (qs * cl);
var drag = -dir * (qs * cd);
```
`KInd * cl * cl` is induced drag: more lift costs more drag, which is why hard turns slow you down. `post` is 0 below the stall and up to 1 past it, adding a large stall drag.

**Engines:**
- `EngineSpool` moves towards `Throttle` at 0.55/s when speeding up and 0.9/s when slowing down, or 2/s into afterburner once at full military power.
- Up to 1.0 the thrust scales with spool, from `MilThrust`; from 1.0 to 1.25 it blends up to `AbThrust`.
- `* Mathf.Pow(dens, 0.7f) * (1f + 0.2f * Mach)`: less thrust in thin air, a little more at speed (ram air).
- Below 30 HP the engines are damaged and give 60%.
- Fuel drops in proportion to thrust; the afterburner factor (`5.2e-5`) is about 2.3 times the normal one (`2.3e-5`).
- No fuel, no thrust.

**Ground friction** (only when `OnGround`): the normal force is weight minus lift. Friction is `mu × normal`, with `mu` = 0.02 rolling, or 0.52 with brakes (Z). If the jet is almost stopped and the push is smaller than friction, it's held still.

**5. Move.**
```csharp
GLoad = Mathf.Lerp(GLoad, (force / m).Dot(bodyUp) / G, 0.15f);   // smoothed, for the HUD
Vel += (force / m + new Vector2(0, -G)) * h;                       // F = m·a, plus gravity
Pos += Vel * h;
Stalled = Mathf.Abs(Alpha) > AlphaStall + 0.02f || (v < 65f && !OnGround);
GroundContact();
```
The g-load is the aerodynamic and engine force along the pilot's "up" direction, divided by weight. In level flight it's 1. `Dot` (dot product) measures how much of one vector points along another.

### `GroundContact()`

1. `Agl = Pos.Y - gh - GearH`: the height of the wheels above the ground. If it's above 5 cm, we're flying, and the method returns.
2. If we **just touched down** (`!OnGround`), it checks everything for a safe landing:
   ```csharp
   bool ok = GearDown && runway && sink < 6.5f && upright && pitchRel > -0.06f && pitchRel < 0.32f;
   ```
   If that fails, it picks the most useful reason (`"HARD LANDING - GEAR COLLAPSED"`, `"GEAR-UP LANDING"` or `"CRASHED"`) and calls `Crash(why)`. Otherwise it plays the thump and shows the sink rate. `{sink:F1}` formats the number with one decimal place.
3. While on the ground: it sits the jet exactly on the wheels, stops it sinking, crashes it if it leaves the runway faster than 25 m/s, and keeps the nose between level and 15° up.

### `GroundService`, `Rearm`, `TakeDamage`, `Crash`

- **`GroundService`:** while stopped (< 3 m/s) on the runway, `RearmTimer` counts up. At 4 s it calls `Rearm()` and sets the timer to −999, so it doesn't rearm again until you take off and the timer resets to 0.
- **`Rearm`** refills everything and repairs the jet.
- **`TakeDamage`** lowers `Hp`, and calls `Crash("SHOT DOWN")` at 0.
- **`Crash`** makes the jet invisible, stops the vapour, and tells `Game` with `Main.OnJetDestroyed(reason)`. The `if (!Alive) return;` guard means it can only happen once.

### `Weapons(dt)`

**Cooldowns:** `_bombCd -= dt;` and so on. A weapon can fire when its cooldown is `<= 0`, and firing sets it back up.

**The gun uses an accumulator**, a common pattern for firing faster than the frame rate:
```csharp
_gunAcc += dt * 100f;          // 100 rounds per second → 1.67 rounds per frame
while (_gunAcc >= 1f && Ammo > 0)
{
    _gunAcc -= 1f;
    FireRound();
}
```
It fires one round on some frames and two on others, always 100 per second on average.

**Bombs:** hide the next bomb model (`_bombVis[Bombs].Visible = false` after `Bombs--`, so `Bomb3` goes first), and ask `Game` to spawn a bomb just below the belly, moving with the jet plus a small push downward.

**Mavericks:** if there are none, or no lock older than 0.8 s, it shows a message; otherwise it spawns one with `Main.SpawnMaverick(..., LockTarget)`. "RIFLE" is the real radio call for an air-to-ground missile launch.

**Flares:** two per press, thrown backwards and 0.6 rad (≈34°) to either side, plus downward.

### `FireRound()`

```csharp
var muzzle = Pos + f * (Length * 0.45f) - BodyDown() * 0.6f;           // near the nose
var bv = Vel + Vector2.FromAngle(Theta + spread) * 1050f;               // jet's speed + 1050 m/s muzzle velocity
Main.SpawnRound(muzzle, bv, 0, true, _shots % 3 == 0, ShellKind.Bullet, 7f);
Vel -= f * (18000f / Mass()) * 0.01f;                                   // recoil
```
`_shots % 3 == 0` (the remainder after dividing by 3) is true for every third round, so only every third round gets a visible tracer.

### `InSeeker` and `UpdateLock`: Maverick targeting

`InSeeker(u, cone)` is true if the vehicle is alive, 250 m–9 km away, and within `cone` radians of the nose. The cone test is a dot product: `Fwd().Dot(rel / d)` is the cosine of the angle between the nose and the target, and it's compared with `Mathf.Cos(cone)`.

`UpdateLock`:
1. Drops the lock if the target leaves a 0.55 rad cone.
2. If there's no lock, or T was pressed, it finds candidates with **LINQ**, C#'s query syntax for lists:
   ```csharp
   var cands = Main.Units
       .Where(u => u.Team == 1 && InSeeker(u, 0.45f))                    // enemies in the cone
       .OrderByDescending(u => f.Dot((u.CPos - Pos).Normalized()))       // closest to the nose first
       .ToList();
   ```
   `u => ...` is a **lambda**, a small unnamed function. `Where` keeps the units for which it returns true; `OrderByDescending` sorts by the value it returns.
3. Without T it picks the first; with T it picks the one after the current target, wrapping around with `% cands.Count`.
4. A new target resets `LockTime` to 0. The HUD shows "ACQ" until it reaches 0.8 s, then "TRACK".

### `ComputeCcip()`: where will the bomb land?

CCIP means *continuously computed impact point*. Every frame it runs a **fake bomb** forward in time, with the same physics as a real one (`Game.BombK` drag), in 0.05 s steps for up to 60 s. The first point below the ground is the impact point; the time taken is `CcipTime`. The same is done for a bullet, for up to 2.6 s, to place the gun cross.

That's about 400 small steps a frame, which is fine: it's only arithmetic.

### `UpdateVisual()`

Copies the simulation onto the scene: `Position` and `Rotation` of the root (pitch), `_rollNode.Rotation` (roll), gear visibility, and:
- **flames:** `ab` is 0–1 through the afterburner range. The flame's length grows with it, plus `GD.Randf()` (a random 0–1) for flicker;
- **vapour:** on above 5 g or high alpha, faster than 110 m/s;
- **smoke:** below 55 HP, and darker below 30;
- **stores:** `_bombVis[i].Visible = i < Bombs` shows exactly as many bombs as you have left.

### `Transonic()` and `SonicBoom()`

The vapour cone and sonic boom when you break Mach 1:
- a 3.6 s timer (`_coneFlash`) starts on crossing Mach 1;
- every frame, the timer sets the cone's opacity, with a squared fade and thicker cones in low, damp air;
- a `_supersonic` flag with **hysteresis** means there's one boom per crossing: it triggers at Mach 1.0 and resets below 0.97.

Explained line by line, with a timeline, in [chapter 3](03-jet.md#the-vapour-cone-and-sonic-boom-transonic).

---

## 9.7 `Unit.cs`: ground vehicles

### Fields

The `[Export]` fields are the per-scene stats you set in chapter 4. The `///` comments above them appear as tooltips in the Inspector.

| Runtime field | Meaning |
|---|---|
| `X`, `LaneZ` | position along the lane, and depth |
| `StopX` | where it stops driving; set by `Game.StartWave` |
| `CPos`, `CVel` | hull centre and velocity in 2D, used for hits and targeting |
| `_facing` | −1 facing left (enemies), +1 facing right (friendlies) |
| `_body`, `_turret`, `_gunPivot` | the parts the AI moves |
| `_parts` | every mesh, to blacken when destroyed |
| `_samRounds`, `_ammo` | the SA-8's missiles |
| `_cooldown`, `_burst...`, `_shotAcc`, `_lockT` | AI timers |
| `WreckT` | how long it's been a wreck; old wrecks are removed |

### `Init`, `FindParts`, `Place`

- **`Init`:** works out `_facing` from `Team`, and turns `Body` by π (180°) around Y for enemies. `MaxHp = Hp` remembers the starting health for the HUD's health bar. A random first `_cooldown` stops all tanks firing at the same moment.
- **`FindParts`:** `_body.FindChild("Turret", true, false) as Node3D`. `as` returns `null` instead of failing if it isn't found, so a truck (no turret) is fine. The SA-8's missiles are found with `$"Missile{i}"`, and `_ammo` is however many exist.
- **`Place`:** sets the height from `Terrain.Height(X)`, and the slope from the heights 3 m either side:
  ```csharp
  float slope = (t.Height(X + 3f) - t.Height(X - 3f)) / 6f;   // rise over run
  Rotation = new Vector3(0, 0, Mathf.Atan(slope));             // tilt to match
  ```

### `Sim(dt)`

Wrecks only count `WreckT` up. Otherwise:
1. Enemies (`Team == 1`) drive left at `Speed` until `X` reaches `StopX`.
2. `CVel` is how far it moved this frame divided by `dt`. Mavericks use it to aim ahead of a moving target.
3. `Place()` again, to follow the ground.
4. A `switch` on `Kind` runs the right AI. Trucks have no case: they just drive.

### `AiTank`

1. `Main.FindEnemyUnit(this, 2400f)`: the nearest vehicle of the other team. If none, the gun slowly lowers to level.
2. The elevation for a shell at 950 m/s:
   ```csharp
   float ang = Mathf.Atan2(rel.Y, Mathf.Abs(rel.X))                                    // straight at it
             + 0.5f * Mathf.Asin(Mathf.Clamp(9.81f * Mathf.Abs(rel.X) / (v * v), -1f, 1f));   // + gravity drop
   ```
   The second term comes from the range formula `R = v²·sin(2θ)/g`, solved for θ. `Clamp` keeps `Asin`'s input in its valid range of −1 to 1.
3. `GunPivot` turns towards it at 0.5 rad/s. Subtracting `Rotation.Z * _facing` cancels out the hull's tilt on a slope.
4. When `_cooldown` runs out, it fires: a little random spread (twice as much while driving), a muzzle flash, and a sound delayed by the distance to you.

### `AiAaa`

1. It only engages a flying, living jet within 2.8 km and at least 25 m above the gun.
2. **Lead:** it guesses the bullet's flight time, works out where the jet will be then (plus how far the bullet drops), and repeats 3 times. Each guess makes the next one better:
   ```csharp
   for (int i = 0; i < 3; i++)
   {
       float t = (aim - muzzle).Length() / v;
       aim = jet.Pos + jet.Vel * t + new Vector2(0, 0.5f * 9.81f * t * t);
   }
   ```
3. The turret turns to face the jet's side (`wantYaw` is 0 or π) at 1.2 rad/s; the guns elevate at 0.9 rad/s. **These rate limits are what make fast, close passes safe.**
4. Bursts switch on for 1.2–2.2 s and off for 1.5–3 s, using a timer that flips `_burstOn`.
5. It only fires when the guns are actually pointing at the aim point (`aligned`). It fires 18 rounds per second, using the same accumulator pattern as the jet's gun, and plays the sound once per burst (`_burstHeard`).

### `AiSam`

```csharp
bool masked = jet.Agl < 45f || d > 8000f || rel.Y < 60f;
```
While masked, the lock time drains at twice the rate it builds. While locked:
- `jet.RwrLock = 0.3f` tells the HUD and audio that you're being tracked;
- the launcher turns and elevates towards you;
- after 2.5 s of lock, with the 10 s cooldown done and the launcher facing you, it hides one missile model and calls `Main.SpawnSam(...)`.

### Damage and death

`TargetPos`, `TargetVel` and `IsTargetValid` implement `ITarget`. `IsInsideTree()` makes sure a vehicle that's been deleted isn't chased.

**`TakeDamage(dmg, byPlayer, dir)`:** `dir` is the direction the hit came from, so a turret flies away from the blast.

**`Die`:**
1. Replaces every mesh's material with the charred one.
2. Tells `Game` (`OnUnitKilled`) for scoring.
3. Spawns an explosion, debris, and a wreck fire as a child of the vehicle, so the fire stays on the wreck.
4. A `switch` on `Kind` for the special effects. `GD.Randf() < 0.5f` is a coin toss.

**`After(delay, action)`**, a reusable "do this later":
```csharp
GetTree().CreateTimer(delay).Timeout += () =>
{
    if (IsInstanceValid(this) && IsInsideTree())
        action();
};
```
- `CreateTimer` makes a one-off timer. `.Timeout += ...` attaches code to its signal; `+=` is how C# subscribes to an event.
- `System.Action` is the type of a function with no parameters and no return value. `Die` passes lambdas like `() => Main.SpawnExplosion(...)`.
- The check matters: if you press R, the vehicle is deleted, but the timer still fires.

**`TossTurret`** sets `_turret = null` first, so it can't be thrown twice, then hands it to `Game.TossPart`.

---

## 9.8 `Missile.cs`: guided missiles

One script, two scenes: the numbers come from the Inspector (chapter 5).

### `Init`

Stores the start state and the target. The smoke trail is added to **`Main`**, not to the missile:
```csharp
Main.AddChild(_trail);
```
If it were a child of the missile, it would vanish the instant the missile exploded. As a separate node, its smoke stays and drifts away; `Detonate` frees it 3 s later.

### `Burning()` and `Sim(dt)`

`Burning()` is true between `Ignition` and `Ignition + Motor` seconds of age.

`Sim` runs `Step` **4 times** per frame (sub-stepping again: a fast missile with a sharp turn needs small steps). Then it places the node, points it along its velocity, flickers the `Flame` with a random Y scale, and keeps the trail emitting for 1 s after burnout.

### `ConsiderFlare(Shell f)`

`Game.SpawnFlare` calls this on every missile when a flare appears. It only applies to a SAM that's still guided and chasing the jet. If the flare is within 3 km, within 0.5 rad of the missile's flight direction, and `GD.Randf() < 0.33f` (a 1 in 3 chance), the target becomes the flare.

### `Step(h)`

1. **Forces:** gravity and drag like a `Shell`, plus `dir * Boost` while burning.
2. **Lost target:**
   ```csharp
   bool gone = Target is GodotObject go && !IsInstanceValid(go);
   ```
   `is GodotObject go` checks the type and names the result in one go (*pattern matching*). A `Unit` is a Godot object and can be deleted; a `Shell` isn't. If the target is gone or invalid, the missile goes unguided.
3. **Guidance** (after ignition):
   - within `Prox` → `Detonate(true)`;
   - target more than 1.1 rad (63°) off the missile's direction → `Guided = false` for good;
   - a SAM chasing a jet below 30 m, further than 600 m away, gets no steering this step (ground clutter);
   - otherwise, proportional navigation:
   ```csharp
   var rv = tv - Vel;                                                         // target's velocity relative to us
   float losRate = (los.X * rv.Y - los.Y * rv.X) / Mathf.Max(dist * dist, 1f);   // line-of-sight turn rate
   float vc = -los.Dot(rv) / Mathf.Max(dist, 1f);                               // closing speed
   float aCmd = Nav * Mathf.Max(vc, 60f) * losRate + 9.81f * perp.Y;            // + cancel gravity
   float amax = MaxG * 9.81f * Mathf.Clamp(ratio * ratio * rho, 0.05f, 1f);     // turn limit falls with speed²
   acc += perp * Mathf.Clamp(aCmd, -amax, amax);
   ```
   `los.X * rv.Y - los.Y * rv.X` is the 2D **cross product**. Divided by distance², it gives how fast the line from missile to target is rotating, in rad/s. `Mathf.Max(..., 1f)` avoids dividing by zero.
4. **Move**, then explode on the ground or at the end of `Life`.

### `Detonate(direct)`

- Stops the trail and frees it after 3 s. `var trail = _trail;` copies the reference into a local variable for the lambda, because the missile itself will already be freed when the timer fires.
- **SAM:** damages the jet if within 30 m: `Damage × clamp(1.2 − distance/30, 0.2, 1)`, so full damage within 6 m. There's no explosion if that hit destroyed the jet, because the jet simply disappears.
- **Maverick:** if it was a direct hit on a `Unit` (`Target is Unit u` is pattern matching again), it deals `Damage` to that vehicle, then calls `Main.Explode` for blast damage around it.
- `QueueFree()` deletes the missile. `Game` drops it from its list because `Dead` is true.

---

## 9.9 `Game.cs`: the conductor

### Constants, exports and state

```csharp
public const float BulletK = 2.0e-4f;   // drag: a = -k * v * |v| * density
public const float ShellK  = 3.0e-5f;
public const float BombK   = 1.1e-4f;
public const float FlareK  = 2.5e-3f;
```
`2.0e-4f` is scientific notation: 2.0 × 10⁻⁴ = 0.0002. Flares have the most drag, so they slow down almost at once.

`[ExportGroup("Units")]` groups the `[Export]` fields under it into a collapsible section in the Inspector. The exports are `PackedScene`s: a scene file loaded and ready to copy with `Instantiate()`.

**Lists of things alive in the world:**
```csharp
public readonly List<Unit> Units = new();
readonly List<Shell> _shells = new();
public readonly List<Missile> Missiles = new();
readonly List<RigidBody3D> _debris = new();
readonly List<MeshInstance3D> _scorch = new();
public readonly List<Message> Messages = new();
```

Then the game state (`Wave`, `WaveTimer`, `Score`, `Kills`, `State`…), camera state (`_zoom`, `CamDist`, `_camOffset`, `Shake`), and shared effect resources (meshes, materials, gradients), which `BuildResources` fills in.

### `_Ready()`

In order:
1. `GD.Randomize()` seeds the global random generator differently each run.
2. `SetupInput()` and `BuildResources()`. These come first because `Jet.Init` and `Unit.Init` use the resources.
3. Finds the nodes. `GetNode<Audio>("Audio")` is used for direct children of `Game`; `FindChild(name, true, false)` for nodes inside the instanced `World` scene. The pattern
   ```csharp
   if (FindChild("FriendlyLine", true, false) is Node3D line)
       FriendlyLine = line.GlobalPosition.X;
   ```
   finds, type-checks and names the node in one `if`; if it's missing, the default 3800 stays.
4. `PlaceGroundProps()` and `AddFriendlySmoke()`.
5. Initializes every `Unit` under `Units`. `GetChildren().OfType<Unit>()` (LINQ) keeps only the children that have the `Unit` script.
6. Initializes the jet, finds the camera and HUD, posts the opening messages, and positions the camera at once with `UpdateCamera(1f)`.

### `SetupInput()`

```csharp
var binds = new Dictionary<string, Key[]>
{
    ["pitch_up"] = new[] { Key.W, Key.Up },
    ...
};
foreach (var (action, keys) in binds)
{
    if (InputMap.HasAction(action)) continue;
    InputMap.AddAction(action);
    foreach (var k in keys)
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
}
```
- A `Dictionary` maps each action name to its keys. `var (action, keys)` *deconstructs* each entry into two variables.
- `HasAction` → `continue`: if you've defined the action yourself in **Project Settings → Input Map**, your version wins.
- `PhysicalKeycode` means the key's *position* on the keyboard, so W/S are in the same place on AZERTY keyboards.

### `ParticleMat` and `BuildResources()`

`ParticleMat(additive, tex)` makes the material every particle uses:
- `BillboardMode = Particles`: each particle faces the camera;
- `VertexColorUseAsAlbedo`: the particle's colour, from the colour ramp, tints the texture;
- `Add` blending for fire and sparks (glows), `Mix` for smoke (covers what's behind).

`BuildResources` builds everything shared: three particle quads, tracer and flare materials, the charred, debris and scorch materials, six colour ramps, and `_growCurve`, which makes particles start at 35% size and grow.

### `PlaceGroundProps()` and `AddFriendlySmoke()`

- **`PlaceGroundProps`** loops through `GroundProps`' children and sets each one's y to `Terrain.Height(x)`. It uses `GlobalPosition` (world space) because `GroundProps` could be moved.
- **`AddFriendlySmoke`** makes a looping (`oneShot: false`) green particle emitter 80 m east of the line.

### Spawning

**`SpawnUnit(scene, x, z)`:** `scene.Instantiate<Unit>()` makes a copy of the scene and returns its root as a `Unit`. It's added under `Units`, initialized, and added to the list.

**`StartWave()`:**
1. Removes wrecks that have burned for over 15 s. `.ToList()` makes a copy first, because you can't remove items from a list while looping over it.
2. Works out the wave's make-up. `Wave / 2` is **integer division**: 3 / 2 = 1.
3. Builds a list with one entry per vehicle, then shuffles it with the **Fisher–Yates shuffle**:
   ```csharp
   for (int i = order.Count - 1; i > 0; i--)
   {
       int j = GD.RandRange(0, i);
       (order[i], order[j]) = (order[j], order[i]);   // swap, using a tuple
   }
   ```
4. Spawns them in a column starting 4.4 km east of the line, 70–160 m apart. The `switch` *expression* (`k switch { UnitKind.Tank => TankScene, ... }`) picks a value by case; `_` means "anything else".
5. Sets each vehicle's `StopX`, then posts the wave message.

**`SpawnRound(...)`** creates a `Shell`, picks its drag and lifetime from its kind, and, if `tracer` is true, gives it a glowing box mesh coloured by kind.

**`SpawnBomb`** creates a bomb `Shell` with `Damage = 520`, `Blast = 22` and `ArmTime = 1`, and a copy of the bomb model as its `Node`.

**`SpawnFlare`** creates a flare `Shell` with a glowing sphere and a fire emitter, then offers it to every missile: `foreach (var m in Missiles) m.ConsiderFlare(s);`.

**`SpawnMaverick` / `SpawnSam`** instantiate the missile scene, add it, `Init` it, and add it to `Missiles`. The SAM also posts "SAM LAUNCH!", plays a delayed launch sound, and makes a muzzle flash.

### `_PhysicsProcess(delta)`

```csharp
if (Paused) return;
float dt = (float)delta;
StateTime += dt;
Jet.Sim(dt);
foreach (var u in Units) u.Sim(dt);
UpdateShells(dt);
foreach (var m in Missiles) m.Sim(dt);
Missiles.RemoveAll(m => m.Dead);
UpdateWaves(dt);
_debris.RemoveAll(d => !IsInstanceValid(d));
```
`RemoveAll(condition)` deletes every item for which the lambda returns true. Dead missiles and freed debris are cleaned out after the loops, never during them.

Pausing is done by simply not running this method, so the world freezes while the camera and HUD keep working.

### Hit testing

**`CanHit(s, u)`:** player rounds hit anything. Otherwise, team 1 rounds hit team 0 vehicles and vice versa.

**`SegDist(a, b, p)`:** the distance from point `p` to the line segment `a`–`b`:
```csharp
float t = Mathf.Clamp((p - a).Dot(ab) / l2, 0f, 1f);   // how far along the segment is the closest point (0–1)
return (a + ab * t).DistanceTo(p);
```
This is a *projection*: the dot product measures how far `p` lies along the segment, and clamping keeps the closest point between the ends. Bullets travel ~17 m a frame, so testing only the end point would miss a 4 m truck.

**`UpdateShells(dt)`** for each live shell:
1. `s.Step(dt)`.
2. Flares only expire (old age or hitting the ground), then `continue`.
3. **Vehicles:** `reach` is how far the shell moved in x plus 12 m. Any vehicle further away than that in x is skipped before the more expensive `SegDist`: a cheap test first, the exact test only when needed. The first hit goes to `ShellHitUnit`.
4. **The jet:** only team 1 rounds, within 7.5 m.
5. **The ground:** below `Terrain.Height` → `ShellGround`.
6. **Old age:** AAA rounds self-destruct with a black flak puff.

Finally `_shells.RemoveAll(s => s.Dead)`.

**`ShellHitUnit`:** bullets and AAA deal damage × the vehicle's `GunArmor`, with sparks on every second hit (`_impactCount % 2`). Tank shells deal full damage plus a small explosion. Bombs explode if armed, and otherwise do 60 damage as a dud.

**`ShellGround`:** dust for every third bullet, a small explosion for tank shells, and an explosion plus a scorch mark for armed bombs. An unarmed bomb is a dud.

### `Explode(p, z, radius, damage, byPlayer, size, hurtJet = true)`

```csharp
if (d < radius)
    dmg = damage * (1f - 0.5f * d / radius);                                       // 100% at centre → 50% at the edge
else if (d < radius * 2.5f)
    dmg = damage * 0.5f * Mathf.Pow(1f - (d - radius) / (radius * 1.5f), 2f);     // 50% → 0% further out
```
Then **frag damage to the jet** within `radius * 7 * size` (154 m for a bomb), and a push (`ApplyCentralImpulse`) on every debris body within 3 × radius, away from the blast.

### Effects

**`MakeParticles(...)`** is the one function behind every particle effect. Its parameters, in order: the quad (texture), how many particles, lifetime, a speed range (`Vector2` used as min/max), a size range, the colour ramp, gravity, damping (air drag), one-shot or looping, spread angle, an optional direction, and an optional emission radius. A one-shot emitter uses `Explosiveness = 0.95`, so all its particles come out at once.

**`FireOnce(p, at, freeAfter)`** places an emitter, starts it, and frees it later with **`FreeLater`**, which uses the same timer-and-`IsInstanceValid` pattern as `Unit.After`.

**`SpawnExplosion(p, size)`** layers:
1. a delayed, positional sound: bigger booms for `size >= 0.9`, and `LinearToDb` turns the size into a volume in decibels;
2. a fireball, a smoke cloud and sparks, plus ground dust if it's within 6 m of the ground;
3. an `OmniLight3D` faded out with a **tween**:
   ```csharp
   var tw = CreateTween();
   tw.TweenProperty(light, "light_energy", 0f, 0.5);    // animate a property to 0 over 0.5 s
   tw.TweenCallback(Callable.From(light.QueueFree));    // then delete the light
   ```
4. debris for big explosions;
5. camera shake, smaller the further the explosion is from the camera.

**`Sparks`** and **`MuzzleFlash`** are smaller versions.

**`SpawnDebris`:** each chunk is a `RigidBody3D` (moved by the physics engine) with a mesh and a box collision shape, thrown upward and outward with random spin. Every third one trails smoke for 1.5–3 s. `TrackDebris` frees it after 6–10 s, and if more than 140 exist (`MaxDebris`), the oldest is removed early, which keeps the frame rate up.

**`TossPart(node, v, extents, m)`:**
```csharp
var rb = new RigidBody3D();
var gt = node.GlobalTransform;   // where the turret is now
AddChild(rb);
rb.GlobalTransform = gt;         // put the body exactly there
node.Reparent(rb);               // move the turret into the body, keeping its world position
```
The turret is moved out of the tank and into a physics body, given velocity and spin, set on fire, and freed after 30 s.

**`MakeWreckFire`** is a node with looping fire and smoke emitters. **`Scorch`** lays a flat dark cylinder on the ground, tilted to the slope, and keeps at most 60.

### Game flow

| Method | Does |
|---|---|
| `FindEnemyUnit(from, maxRange)` | nearest living vehicle on the other team, measured along x only |
| `CountTeam(t)` | `Units.Count(u => u.Alive && u.Team == t)` |
| `SamInbound()` | `Missiles.Any(...)`: is any guided SAM chasing the jet? HUD and audio use it |
| `PullUpWarning()` | sinking faster than 5 m/s with under 4.5 s to the ground, unless it's a landing (gear down over the runway) |
| `OnUnitKilled(u, byPlayer)` | enemy killed: +1 kill, and `Value` points if you did it. Friendly killed by you: −400 and `FriendlyKills++` |
| `OnJetDestroyed(reason)` | `State = GameState.Dead`, shows the reason |
| `UpdateWaves(dt)` | between waves: count down `WaveTimer`, then `StartWave()`. During a wave: when no hostiles are left, a bonus of 300 × wave and a 25 s break |
| `Msg(text, col, t)` | adds a message at the top (`Insert(0, ...)`), keeps at most 7. `Color? col = null` with `col ?? Colors.White` means "white unless a colour is given" |

### Per-frame input and the camera

**`_UnhandledInput`:** `e is InputEventMouseButton mb && mb.Pressed` checks the event's type and that the button went down. Each wheel notch changes `_zoom` by 10%, limited to 0.3–3.

**`_Process`:**
- R calls `GetTree().ReloadCurrentScene()`, a full restart from the saved scene.
- P/Esc toggles `Paused`, H toggles help, and +/− zoom smoothly.
- Message timers count down (not while paused).
- `UpdateCamera(dt)`.

**`UpdateCamera(dt)`:**
1. **Distance:** whichever is larger, `150 + 0.32 × speed` or `1.25 × height + 80`, kept between 110 and 2400 m, times `_zoom`.
2. **Smoothing:**
   ```csharp
   CamDist = Mathf.Lerp(CamDist, want, 1f - Mathf.Exp(-dt * 1.5f));
   ```
   This moves a fixed *fraction* of the remaining distance each frame, and `1 − e^(−dt·k)` makes that fraction correct for any frame length. At 30 or 144 fps the camera moves the same.
3. **Look-ahead:** `lead = v * 0.45f`, so the camera aims 0.45 s ahead of the jet, but never pushes the jet more than 90% of the way to the screen edge. `halfH` is half the visible height at that distance, from the field of view: `CamDist × tan(FOV / 2)`.
4. **Ground in view:** if the bottom of the picture would be above the ground, the view is shifted down.
5. **Shake:** decays at 1.8 per second, and moves the camera randomly by an amount that scales with distance.
6. **Place it:** at `z = CamDist` (in front of the lane), slightly above, then `LookAt` the focus point on z = 0.
7. **Shadows:** the sun's shadow distance follows the zoom, so shadows stay sharp up close and still exist when zoomed out.

---

## 9.10 `Hud.cs`: the heads-up display

### Setup

- Colour constants: `CHud` (green), `CDim` (faint green), `CWarn` (red), `CAmber`, `CFriend` (blue). `static readonly` because a `Color` can't be `const`.
- `_Ready` makes the control fill the screen and ignore the mouse. This repeats what you set in the editor in chapter 6, as a safety net. `ThemeDB.FallbackFont` is Godot's built-in font.
- `_Process` keeps the control the size of the window, then calls **`QueueRedraw()`**. That's what makes Godot call `_Draw()` again; without it, the HUD would be drawn once and never change.

### Drawing helpers

- **`Txt`** draws a string twice: first in black, offset 1.5 px, as a drop shadow; then in colour on top. It stays readable against bright sky.
- **`CTxt`** centres text in an 800 px wide box around a point.
- **`W2S(p, z)`** ("world to screen") turns a lane position into a screen pixel with `Camera.UnprojectPosition`. It returns `null` if the point is behind the camera, where the projection would be meaningless.
- **`OnScreen(s, margin)`** checks that a pixel is inside the window.

### `_Draw()`

Calls each part in turn. **Later calls draw on top**, so warnings and the end screen are drawn last. `Main?.Jet` uses `?.`, the *null-conditional* operator: if `Main` is null (not connected yet) the result is null instead of a crash.

### The parts

**`DrawOffscreenEnemies`**: for each enemy whose screen position is off-screen, it takes the direction from the screen centre and scales it until it touches a rectangle 48 px inside the edges:
```csharp
float t = Mathf.Min((center.X - margin) / Mathf.Abs(d.X), (center.Y - margin) / Mathf.Abs(d.Y));
var e = center + d * t;
```
Whichever edge is hit first wins, hence `Min`. A triangle is drawn there with `DrawColoredPolygon`, pointing along `dir`, with `dir.Orthogonal()` (the perpendicular) giving its width. The nearest enemy's distance is printed next to its arrow, with `{nearestD / 1000f:F1}` formatting it to one decimal.

**`DrawWorldMarkers`**:
- **Vehicles:** a red triangle 7 m above each enemy, its name (always for SAMs and AAA, otherwise only when zoomed in), and a health bar when damaged. Friendlies get a blue chevron drawn with `DrawPolyline`.
- **Flight-path marker:** a small circle with wings at a point ahead of the jet along `Vel`, so it shows where you're *going*, not where you're pointing.
- **Bomb pipper:** a dashed line from the jet to `Ccip`, a circle, and the fall time, in red if under 1 s (the bomb wouldn't arm).
  ```csharp
  if (j.Ccip != null && W2S(j.Ccip.Value) is Vector2 cp)
  ```
  `.Value` reads a nullable's contents, and `is Vector2 cp` checks that `W2S` didn't return null.
- **Gun cross** at `GunCcip`, bright while firing.
- **Maverick diamond:** shrinks from 22 to 14 px while acquiring, then turns green with "TRACK".
- **Incoming SAMs:** a red circle if on screen, otherwise a red arrow.

**`DrawFlightData`**: a tuple array of label/value pairs, drawn in a loop. The unit conversions:

| Shown | Code | Conversion |
|---|---|---|
| knots | `j.Vel.Length() * 1.94384f` | m/s → kt |
| feet | `j.Pos.Y * 3.28084f` | m → ft |
| feet per minute | `j.Vel.Y * 196.85f` | m/s → ft/min (3.28084 × 60) |
| pounds | `j.Fuel * 2.20462f` | kg → lb |

Format strings: `{(int)kts,4}` pads to 4 characters so the numbers don't jump around; `ToString("+0.0;-0.0")` always shows a sign. The throttle bar is 100 px for the full 0–1.25 range, so the red afterburner mark is at 80 px (1.0 / 1.25). The damage bar is 1.5 px per HP.

**`DrawWeapons`** and **`DrawScore`**: simple rows of text; a count of 0 turns red.

**`DrawMap`**: the strip map at the top.
- The terrain profile is 97 points sampled from `Terrain.Height`, joined with `DrawPolyline`.
- Then the runway, the friendly line, a dot per vehicle and missile, and a triangle for the jet.
- **`Map(r, p)`** converts a world position to a pixel inside the map box. Height uses a **square root** (`Mathf.Sqrt(v)`), which stretches the low altitudes, so you can still see how low you are when the map covers 5 km of height.
- Screen y points **down**, so the jet's direction is flipped: `j.Fwd() * new Vector2(1, -1)`.

**`DrawWarnings`**:
```csharp
bool on = _blink % 0.5f < 0.3f;   // true for 0.3 s of every 0.5 s
```
Warnings are collected in a list, then drawn; red ones are only drawn when `on`, which makes them blink. MISSILE replaces SAM RADAR LOCK (`else if`), since a launched missile is more urgent. The rearming percentage is shown while on the ground.

**`DrawMessages`**: the message feed. `col.A = Mathf.Clamp(m.Time, 0f, 1f)` makes a message's opacity equal to its remaining time once under 1 s: a fade-out.

**`DrawHelp`** and **`DrawEnd`**: the controls box, and the end screen, which darkens gradually using `StateTime`.

---

## 9.11 `Audio.cs`: sound

### Loading

```csharp
static readonly (string name, bool loop)[] Sounds = { ("boom", false), ..., ("engine", true), ... };
```
An array of **tuples**: each entry has a name and a flag.

`LoadAll()` loads `{SoundFolder}/{name}.wav` for each. For looping sounds it sets the loop points, because a plain WAV file has none:
```csharp
s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
s.LoopBegin = 0;
s.LoopEnd = (int)System.Math.Round(s.GetLength() * s.MixRate);   // length in seconds × samples per second
```

### Players

- **`LoopPlayer(sound, start)`** creates one `AudioStreamPlayer` that's reused all game: the engine, afterburner, wind, gun and five warning tones. They start at −80 dB, which is silent.
- **`SetLoop(p, on, volDb)`** starts or stops a looping player only when its state changes. Calling `Play()` every frame would restart the sound from the beginning each time.
- **`Db(x)`** converts a 0–1 volume into decibels. `Max(x, 0.00001f)` avoids the logarithm of 0, which is minus infinity.

### One-shot sounds

**`Play(sound, volDb, pitch)`**, non-positional:
```csharp
var p = new AudioStreamPlayer { Stream = _sounds[sound], VolumeDb = volDb, PitchScale = pitch };
AddChild(p);
p.Play();
p.Finished += p.QueueFree;   // delete itself when done
```
`if (GetChildCount() > 64) return;` caps how many sounds can play at once, so a huge battle can't overload the audio.

**`PlayAt(sound, pos, volDb, pitch, delay)`**, positional:
- If there's a delay, it sets a timer that calls `PlayAt` **again** with no delay. A method calling itself like this is *recursion*.
- It uses an `AudioStreamPlayer3D` at the world position. Godot makes it quieter with distance (`InverseDistance`, `UnitSize = 160`), and pans it left or right relative to the camera.

**`SoundDelay(pos)`** is the distance to the jet ÷ 343 m/s, capped at 3 s.

### `_Process`: continuous sounds

1. Pauses or resumes every looping player with `StreamPaused` when the game is paused.
2. **Engine:** volume and pitch rise with `EngineSpool`. **Afterburner:** fades in through the afterburner range. **Wind:** rises with speed squared.
3. **Gun:** loops while `j.Firing`. One looping sound is much cheaper than 100 separate sounds per second.
4. **Warnings, with priorities:** the missile warning beats the radar lock tone, PULL UP beats STALL, and the lock tone plays while the Maverick is tracking.

---

## 9.12 C# quick reference

The language features this project uses, and where to see them:

| Feature | Looks like | Example in |
|---|---|---|
| `[Export]` | `[Export] public float Hp = 100f;` | `Unit.cs` |
| interface | `public class Shell : ITarget` | `Types.cs`, `Shell.cs` |
| enum | `UnitKind.Sam` | `Types.cs` |
| `const` / `readonly` | `const float G = 9.81f;` | `Jet.cs` |
| expression-bodied member | `public float Mass() => ...;` | `Jet.cs` |
| object initializer | `new Shell { Pos = p, Vel = v }` | `Game.SpawnRound` |
| target-typed `new()` | `readonly List<Unit> Units = new();` | `Game.cs` |
| optional parameters | `float z = 0f` | `Game.SpawnRound` |
| extension method | `static void SetRotZ(this Node3D n, ...)` | `Fx.cs` |
| interpolated string | `$"WAVE {Wave}"`, `{x:F1}` | everywhere |
| nullable value | `Vector2? Ccip`, `.Value` | `Jet.cs`, `Hud.cs` |
| `??` and `?.` | `col ?? Colors.White`, `Main?.Jet` | `Game.Msg`, `Hud._Draw` |
| pattern matching | `if (x is Node3D line)` | `Game._Ready`, `Missile.Step` |
| `switch` statement / expression | `k switch { UnitKind.Tank => TankScene, _ => SamScene }` | `Game.StartWave` |
| tuples | `(order[i], order[j]) = (order[j], order[i]);` | `Game.StartWave`, `Audio.cs` |
| lambda | `u => u.Alive` | `Game.cs`, `Jet.UpdateLock` |
| LINQ | `.Where(...).OrderBy(...).ToList()` | `Jet.UpdateLock` |
| events | `timer.Timeout += () => ...;` | `Unit.After`, `Game.FreeLater` |
| `List<T>`, `Dictionary<K, V>` | `Units.Add(u)`, `RemoveAll(...)` | `Game.cs` |
| `stackalloc` / `Span<T>` | `Span<Vector2> pts = stackalloc Vector2[6];` | `Terrain.SampleLane` |

And the Godot calls you'll meet most:

| Call | Does |
|---|---|
| `GetNode<T>("path")` | find a child node by path; error if missing |
| `GetNodeOrNull<T>("path")` | same, but returns `null` if missing |
| `FindChild(name, true, false)` | search the whole tree below, including inside instanced scenes |
| `scene.Instantiate<T>()` | make a copy of a `PackedScene` |
| `AddChild(node)` | put a node in the tree, so it exists in the game |
| `QueueFree()` | delete a node safely at the end of the frame |
| `IsInstanceValid(obj)` | has this object been deleted? |
| `GetTree().CreateTimer(s).Timeout += ...` | run code after `s` seconds |
| `CreateTween().TweenProperty(...)` | animate a property over time |
| `Input.IsActionPressed` / `IsActionJustPressed` | is a key held / was it pressed this frame |
| `Mathf.Lerp`, `MoveToward`, `Clamp`, `Wrap` | blend, step towards, limit, keep an angle in range |

← Back to the [start page](../TUTORIAL.md)
