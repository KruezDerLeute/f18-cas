# Chapter 7: How the game runs

You've built the scenes and played the game. This chapter explains the scripts that tie it together: `Game.cs`, `Hud.cs`, `Audio.cs`, and the two helper files, `Types.cs` and `Fx.cs`. Open each in your code editor as you go.

---

## 7.1 `Game.cs`: the conductor

### Starting up: `_Ready()`

Godot calls `_Ready()` on every node when the scene starts, children first, so `Game`'s runs last, after all your scenes are loaded. It:

1. **Registers the keys** in `SetupInput()`. The 17 controls are added from code, so you don't have to type them into the Input Map.
2. **Builds shared effect resources** (`BuildResources()`): particle materials made from the two textures you assigned, colour ramps for fire, smoke, dust and sparks, and tracer and debris meshes. They're made once and shared by every explosion.
3. **Finds your nodes:**
   ```csharp
   Terrain = (Terrain)FindChild("Terrain", true, false);
   ```
   `FindChild(name, recursive: true, owned: false)` searches the whole tree, **including inside instanced scenes** like `World`. `owned: false` is what allows that.
   - It calls `Terrain.Build()` to read the ground heights.
   - It reads the `FriendlyLine` marker's x.
   - It drops `GroundProps`' children onto the ground.
   - It adds the green smoke.
4. **Starts every vehicle** in `Units` (your four friendlies) with `u.Init(...)`, and **starts the jet** with `Jet.Init(this)`.
5. **Connects the HUD and audio** by giving them a reference to `Game`.

### Every physics frame: `_PhysicsProcess()`

60 times a second, in a fixed order:
1. The jet flies.
2. Every vehicle drives and runs its AI.
3. Every projectile moves and checks for hits (`UpdateShells`).
4. Every missile flies.
5. The waves are updated.

**Hit detection.** For every projectile, the game:
- tests the line it travelled this frame (`SegDist`) against each vehicle's hit circle (`Radius`). A cheap x-distance check comes first, so most pairs are skipped;
- checks enemy rounds against the jet;
- checks every projectile against the ground.

**Your rounds hit everything**, so strafing near your own tanks costs 400 points.

**Explosions** (`Explode()`):
- **Damage** is full inside the blast radius, then fades out to 2.5 × the radius.
- **Frag damage to the jet:** drop a bomb too low and its fragments hit *you*, out to about 150 m.
- **Debris:** nearby physics debris gets blown away from the blast.

### Waves: `StartWave()` and `UpdateWaves()`

| Wave | Tanks | Trucks | AAA | SAM |
|---|---|---|---|---|
| 1 | 3 | 3 | 1 | 0 |
| 2 | 4 | 4 | 2 | 1 |
| 3 | 5 | 5 | 2 | 1 |
| 4 | 6 | 6 | 3 | 2 |

- Each vehicle is a copy of one of the scenes you linked in the Inspector (`TankScene.Instantiate<Unit>()`).
- They spawn 4.4 km east of `FriendlyLine`, shuffled into a column.
- **Each kind stops at a different distance from your line:**
  - tanks: 0.9–1.5 km;
  - AAA: 1.5–2.6 km;
  - trucks: 1.8–2.4 km;
  - SAMs: 3–3.8 km, far back.
- **Clear a wave** for a bonus, a 25 s breather (land and rearm), and up to two friendly reinforcements.
- **Lose all your tanks** and the game is over.

### Effects

All effects are made from code, at run time, because they appear wherever things happen:
- **`MakeParticles()`** configures one `CpuParticles3D` for any effect: fire, smoke, dust, sparks or trails.
- **`SpawnExplosion()`** layers a fireball, smoke, sparks, ground dust, a flash of light that fades out, debris, a sound, and camera shake.
- **`SpawnDebris()`** throws physics chunks that bounce off the terrain.
- **`TossPart()`** turns a turret into a burning rigid body.

### The camera: `UpdateCamera()`

- **Pulls back** with speed and with height above the ground.
- **Looks ahead** along your velocity.
- **Tilts down** to keep the ground in view.
- **Shakes** for nearby explosions.

All motion is smoothed with `1 − e^(−dt·k)`, which behaves the same at any frame rate.

---

## 7.2 `Hud.cs`: drawing the HUD

The `Hud` control draws everything itself, every frame, in `_Draw()`, using Godot's 2D drawing calls: `DrawString`, `DrawLine`, `DrawArc`, `DrawColoredPolygon`. There are no Label nodes.

- **Markers on the world:** `Camera.UnprojectPosition()` converts a 3D position into a screen pixel. That's how it draws:
  - red triangles and health bars over enemies;
  - blue chevrons over friendlies;
  - the **flight-path marker** (where you're actually going);
  - the **bomb pipper** with its fall time;
  - the **gun cross**;
  - the Maverick **lock diamond**.
- **Off-screen arrows** at the screen edge for enemies you can't see. They're amber and labelled for SAM and AAA, and the nearest shows its range.
- **Flight data:** speed in knots, Mach, altitude and height above the ground in feet, g, angle of attack, climb rate, throttle (the red mark is the afterburner), fuel, weight and damage.
- **A strip map** of the whole battlefield, seen from the side.
- **Warnings:** MISSILE, SAM RADAR LOCK, PULL UP, STALL, OVER-G, BINGO FUEL. Red ones blink.

---

## 7.3 `Audio.cs`: sound

- **Loads** every `.wav` from `Sound Folder`. Looping sounds (engine, afterburner, wind, gun, warning beeps) are set to loop over the whole file.
- **`Play()`** is for cockpit sounds: release, hits, flares.
- **`PlayAt()`** is for world sounds, played as positional 3D sound and **delayed by the speed of sound** (343 m/s). A bomb 1 km away is seen at once and heard about 3 seconds later.
- **Every frame**, it sets the engine's pitch and volume from the throttle, the wind from your speed, and switches the warning tones on and off.

---

## 7.4 The helpers

**`Types.cs`:**
- **`ITarget`**: an *interface*, a promise that a class can say where it is (`TargetPos`), how it's moving (`TargetVel`) and whether it still exists. The jet, vehicles and flares all implement it, which is why one missile script can chase any of them.
- **Enums:** `UnitKind`, `ShellKind` and `GameState`, named options checked by the compiler.
- **`Message`**: a line in the on-screen message feed.

**`Fx.cs`:** one-line shortcuts for making materials and simple meshes (`Fx.Mat`, `Fx.Glow`, `Fx.Box`, ...), plus:
- `Fx.Rand(a, b)` for random numbers;
- `node.SetRotZ(angle)`, because C# can't change one part of a node's rotation directly.

Next: [Chapter 8: Playing, editing, troubleshooting](08-finish.md)
