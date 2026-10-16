# Chapter 3: The jet (`jet.tscn`)

You'll build the F/A-18 as a scene: the model, two engine flames, two lights, four bombs and two Mavericks under the wings, and markers where effects come out. Then you'll see how `Jet.cs` flies it.

The finished scene tree:

```
Jet                      Node3D + Jet.cs        ← pitches (rotates around Z)
└── Roll                 Node3D                 ← rolls (rotates around X)
    ├── F18              ← assets/models/f18.glb
    ├── FlameL           Node3D
    │   ├── Outer        MeshInstance3D (orange cone)
    │   └── Inner        MeshInstance3D (blue core)
    ├── FlameR           (copy of FlameL)
    ├── AfterburnerLight OmniLight3D
    ├── MuzzleLight      OmniLight3D
    ├── Bomb0 … Bomb3    ← assets/models/mk82.glb
    ├── Maverick0, Maverick1   ← assets/models/agm65.glb
    ├── VaporL, VaporR   Marker3D
    └── SmokePoint       Marker3D
```

**Why two levels, `Jet` and `Roll`?** The script pitches the root node, the nose going up and down, and rolls the `Roll` node, upside down and back. With separate nodes the two rotations never interfere. Everything that should roll with the jet goes under `Roll`.

The jet model `f18.glb` is **18.3 m long, centred on (0, 0, 0), with its nose pointing along +X**. All the positions below are measured from that model.

---

## 3.1 Root and model

1. **Scene → New Scene → 3D Scene.** Rename the root **`Jet`**.
2. Drag **`scripts/Jet.cs`** onto `Jet`. The Inspector shows **Start Speed: 190**, the speed the jet starts flying at, in m/s. Leave it.
3. Select `Jet`, **Ctrl+A** → **Node3D**, and rename it **`Roll`**.
4. Drag **`assets/models/f18.glb`** onto **`Roll`**. Rename it **`F18`**.
5. **Ctrl+S** → `res://scenes/jet.tscn`.

Press **F** in the viewport to frame the jet. The nose points to the right (+X).

---

## 3.2 Engine flames

Each engine gets a flame made of two cones: an orange outer flame and a hot blue core. The script stretches them with the throttle, and makes them long in afterburner.

**The left flame:**

1. Select `Roll`, **Ctrl+A** → **Node3D**. Rename it **`FlameL`**. **Position:** `-7.95`, `-1.14`, `-0.55`. That's the left engine nozzle.
2. Select `FlameL`, **Ctrl+A** → **MeshInstance3D**. Rename it **`Outer`**. Then in the Inspector:
   - **Mesh** → **New CylinderMesh**, then click it:

     | Setting | Value |
     |---|---|
     | Top Radius | `0.08` |
     | Bottom Radius | `0.42` |
     | Height | `1` |
     | Radial Segments | `10` |
     | Rings | `1` |

   - **Transform → Position:** `-0.5`, `0`, `0`. **Rotation:** `0`, `0`, `90`.
   - **Geometry → Cast Shadow:** `Off`.
   - **Geometry → Material Override** → **New StandardMaterial3D**, then click it:

     | Section → setting | Value |
     |---|---|
     | Transparency → Transparency | `Alpha` |
     | Transparency → Blend Mode | `Add` |
     | Transparency → Cull Mode | `Disabled` |
     | Shading → Shading Mode | `Unshaded` |
     | Albedo → Color | `ff8c33d9` (orange, 85% opaque) |

3. Select `FlameL`, **Ctrl+A** → **MeshInstance3D**. Rename it **`Inner`**:
   - **Mesh** → **New CylinderMesh**: Top Radius `0.05`, Bottom Radius `0.3`, Height `0.55`, Radial Segments `10`, Rings `1`.
   - **Position:** `-0.28`, `0`, `0`. **Rotation:** `0`, `0`, `90`. **Cast Shadow:** `Off`.
   - **Material Override** → New StandardMaterial3D with the same settings as `Outer`, but **Albedo → Color** `8ca6ffe6` (pale blue).

**Why these settings:**
- **Rotation 90° around Z** turns the upright cylinder onto its side. Its wide end (bottom radius) faces the engine, and the narrow tip trails behind.
- **Blend Mode: Add** adds the colour to whatever is behind it. Overlapping flames get brighter, like real fire, and it never looks like a solid object.
- **Unshaded** means sunlight and shadow don't darken it; it glows.
- **Cull Mode: Disabled** draws both sides, so it's visible from any angle.

**The right flame:**
4. Select `FlameL`, press **Ctrl+D**, and rename the copy **`FlameR`**. Change its **Position z** to `0.55`.

---

## 3.3 Lights

1. Select `Roll`, **Ctrl+A** → **OmniLight3D**. Rename it **`AfterburnerLight`**.
   - **Position:** `-10.95`, `-1.14`, `0`, just behind the engines.
   - **Light → Color:** `ff9a4d` (orange). **Light → Energy:** `0`.
   - **Omni → Range:** `25`.
2. Select `Roll`, **Ctrl+A** → **OmniLight3D**. Rename it **`MuzzleLight`**.
   - **Position:** `7.65`, `-0.54`, `0`, near the nose where the gun is.
   - **Light → Color:** `ffcc66`. **Energy:** `0`. **Omni → Range:** `12`.

An *omni light* shines in every direction from a point. Both start at energy 0 (off). The script flickers the afterburner light while you're in afterburner, and the muzzle light while you fire.

---

## 3.4 Weapons on the wings

**Bombs:** drag **`assets/models/mk82.glb`** onto `Roll` four times, and set:

| Name | Position |
|---|---|
| `Bomb0` | `-1`, `-1.78`, `-2.35` |
| `Bomb1` | `-1`, `-1.78`, `2.35` |
| `Bomb2` | `-1`, `-1.78`, `-3.39` |
| `Bomb3` | `-1`, `-1.78`, `3.39` |

**Mavericks:** drag **`assets/models/agm65.glb`** onto `Roll` twice:

| Name | Position |
|---|---|
| `Maverick0` | `-0.7`, `-1.73`, `-4.57` |
| `Maverick1` | `-0.7`, `-1.73`, `4.57` |

Look at the jet from below: two bombs hang under each wing, with a Maverick further out.

The script hides one each time you release it: `Bomb3` first, then `Bomb2`, and so on. When you rearm on the runway, they all reappear.

---

## 3.5 Effect markers

Three invisible markers tell the script where to emit particles:

| Select `Roll`, Ctrl+A → **Marker3D** | Position | Used for |
|---|---|---|
| `VaporL` | `-1.8`, `-1.03`, `-6.32` | white vapour trails from the left wingtip in hard turns |
| `VaporR` | `-1.8`, `-1.03`, `6.32` | the right wingtip |
| `SmokePoint` | `-7.95`, `-1.14`, `0` | engine smoke when the jet is damaged |

Save (**Ctrl+S**). Your scene should match the tree at the top of this chapter. **Check the names carefully**: the script finds every part by name.

---

## 3.6 The bullets and bombs: `Shell.cs`

Everything that flies ballistically is a `Shell`: your cannon rounds, tank shells, AAA rounds, bombs and flares. It's a **plain C# class, not a node**. The gun fires 100 rounds a second, and plain objects are far cheaper than nodes. Only some rounds get a visible tracer.

The physics, every frame:

```csharp
float rho = Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);              // air thins with altitude
Vel += (new Vector2(0, -9.81f) - Vel * sp * DragK * rho) * dt;    // gravity + air drag
Pos += Vel * dt;
```

- **Gravity** pulls down.
- **Drag** pushes against the motion and grows with the square of speed. `DragK` is large for bullets, which slow down quickly, and small for bombs.
- **`Prev`** stores last frame's position. The game checks the whole line from `Prev` to `Pos` for hits, because a bullet moves 17 m per frame, enough to skip right over a truck.

---

## 3.7 How `Jet.cs` flies

Open `scripts/Jet.cs` and read along. The jet is simulated in 2D (x, y) and drawn in 3D.

### Finding the parts you built

When the game starts, `Game` calls `Jet.Init()`:

```csharp
Pos = new Vector2(Position.X, Position.Y);              // start where you placed it in the editor
Vel = Vector2.FromAngle(Rotation.Z) * StartSpeed;       // flying the way it's rotated
```

`FindParts()` then looks up your nodes by name: `GetNode<Node3D>("Roll/FlameL")`, `"Roll/AfterburnerLight"`, `$"Roll/Bomb{i}"`, and so on. It finds the gear meshes inside `F18` by name too (`F-18LandingOn`, `F-18LandingOff`), and creates the vapour and smoke particles at your markers.

### The three angles

```
            nose points here (Theta)
           ╱
          ╱  ← Alpha (angle of attack) = Theta − flight path angle
  ✈ ────────── jet actually moves here (Vel.Angle())
```

**The wing makes lift because the air meets it at an angle.** That angle is `Alpha`. No alpha means no lift; more alpha means more lift, until the stall at 26°.

### Forces, every physics step

```
dynamic pressure  q = ½ · air density · speed²
lift  = q · wing area · CL(alpha)    perpendicular to the flight path
drag  = q · wing area · CD           against the flight path
thrust                               along the nose
gravity                              down
```

- **`Cl(alpha)`:** lift rises in a straight line with alpha, then collapses past the stall.
- **Drag (`CD`)** adds up several parts:
  - the airframe itself;
  - the bombs and missiles you carry;
  - the gear and the speedbrake;
  - **induced drag**: lift costs drag, so hard turns bleed speed;
  - stall drag;
  - **wave drag**, which rises sharply near Mach 1.
- **Thrust** comes from two F414 engines: 124 kN at full power and 196 kN in afterburner, past 100% throttle.
  - The engines take time to spool up.
  - They lose power in thin air at altitude.
  - They burn fuel; afterburner burns about 2.3 times as much per newton.

The real numbers (wing area 46.5 m², 14.5 t empty, 4.9 t of fuel) make it fly like a Hornet.

### Fly-by-wire: why it's easy to fly

W and S don't move the tail directly. **They command an angle of attack**, and the script works out how to get there, just like the real jet's flight computer:

- **Hands off:** it holds exactly enough alpha for **1 g**, so the jet flies straight.
- **Full pull:** whatever alpha gives **+7.5 g** at your current speed, but no more than the 0.5 rad alpha limit.
- **Full push:** −3 g.

A damped spring then turns the nose towards the commanded alpha. It's stiffer at high speed, so the nose is crisp when you're fast and mushy when you're slow.

**F** rolls the jet 180°. Inverted, "pull" points the other way, so pulling turns you towards the ground.

### Landing

`GroundContact()` checks the moment the wheels touch, 3.4 m below the jet's centre with the gear down. It's a landing only if all of these are true:
- the gear is down;
- you're over the runway;
- you're sinking slower than 6.5 m/s;
- you're upright, with the nose roughly level.

Otherwise you crash, with the reason shown on screen. Stop on the runway for 4 seconds and you're rearmed, refuelled and repaired.

### Weapons

- **Gun:** 100 rounds/s, 578 rounds. Each round slightly slows the jet (recoil).
- **Bombs:** dropped from below the belly. The script simulates a bomb's fall every frame to find where it would land: that's the **CCIP pipper** on the HUD.
- **Mavericks:** lock the enemy closest to your nose, within a cone and a range. You need 0.8 s of lock before you can fire. T switches target.
- **Flares:** a pair at a time. Each has a chance to pull an enemy SAM off you.

### Visuals, every frame

`UpdateVisual()` makes your scene match the simulation:
- it moves and pitches `Jet`, and rolls `Roll`;
- it switches the gear meshes;
- it stretches and flickers `FlameL` and `FlameR` with the throttle;
- it flickers `AfterburnerLight`;
- it streams vapour from the wingtip markers in hard turns;
- it shows damage smoke;
- it hides the bombs and Mavericks you've used.

Next: [Chapter 4: Ground vehicles](04-units.md)
