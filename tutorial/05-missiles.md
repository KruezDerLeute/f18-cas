# Chapter 5: Missiles (`scenes/weapons/`)

Two missile scenes share one script, `Missile.cs`:

| | `maverick.tscn` (yours) | `sam_missile.tscn` (enemy) |
|---|---|---|
| Model | `agm65.glb` | `sa8_missile.glb` |
| Chases | the vehicle you locked | your jet, or a flare |
| Feels like | slow, accurate, hard-hitting | very fast, but turns badly once its motor burns out |

```
Maverick       Node3D + Missile.cs   ← numbers in the Inspector
├── Model      ← assets/models/agm65.glb
└── Flame      MeshInstance3D: the rocket exhaust
```

---

## 5.1 The Maverick

1. **Scene → New Scene → 3D Scene.** Rename the root **`Maverick`**.
2. Drag **`scripts/Missile.cs`** onto it, and set:

   | Property | Value | Meaning |
   |---|---|---|
   | **Is Agm** | ✔ | it's yours: a direct hit damages the vehicle you locked |
   | **Ignition** | `0.35` | drops clear of the jet for 0.35 s before the motor lights |
   | **Motor** | `4` | burn time, in seconds |
   | **Boost** | `70` | motor acceleration, in m/s² |
   | **Drag K** | `0.00005` | air drag |
   | **Max G** | `14` | hardest turn when fast |
   | **Nav** | `3` | guidance strength (see 5.3) |
   | **Life** | `35` | self-destructs after this many seconds |
   | **Prox** | `4.5` | explodes when this close to the target, in metres |
   | **Damage** | `450` | direct-hit damage: enough for any tank |

3. Drag **`assets/models/agm65.glb`** onto `Maverick`. Rename it **`Model`**.
4. Select `Maverick`, **Ctrl+A** → **MeshInstance3D**, and rename it **`Flame`**:
   - **Mesh** → New CylinderMesh: Top Radius `0.03`, Bottom Radius `0.22`, Height `1.6`, Radial Segments `8`, Rings `1`.
   - **Position:** `-2.2`, `0`, `0`. **Rotation:** `0`, `0`, `90`. **Cast Shadow:** `Off`.
   - **Material Override** → New StandardMaterial3D: Transparency `Alpha`, Blend Mode `Add`, Cull Mode `Disabled`, Shading Mode `Unshaded`, Albedo Color `ffb359e6`.

   This is the same recipe as the jet's engine flames in chapter 3: a cone pointing backwards, glowing and see-through.
5. **Ctrl+S** → `res://scenes/weapons/maverick.tscn`.

## 5.2 The SAM missile

1. With `maverick.tscn` open: **Scene → Save Scene As…** → `res://scenes/weapons/sam_missile.tscn`.
2. Rename the root **`SamMissile`**.
3. Delete `Model`. Drag **`assets/models/sa8_missile.glb`** onto the root and rename it **`Model`**.
4. Set the root's values:

   | Is Agm | Ignition | Motor | Boost | Drag K | Max G | Nav | Life | Prox | Damage |
   |---|---|---|---|---|---|---|---|---|---|
   | ☐ (off) | `0` | `3.5` | `190` | `0.00012` | `25` | `3.5` | `13` | `14` | `70` |

5. **Ctrl+S.**

The SAM's motor pushes at 190 m/s², about 19 g, so it reaches high speed fast. But it only burns for 3.5 s. **After that it's coasting and slowing down**, and that's your chance.

---

## 5.3 How `Missile.cs` works

Open `scripts/Missile.cs`.

**Flying.** Each step: gravity, air drag, and, while `Burning()`, the motor's push along the missile's nose. The `Flame` you built is shown only while the motor burns, and flickers. A smoke trail streams behind it.

**Guidance: proportional navigation.** Real guided missiles steer with one idea:

> **If the direction to the target isn't changing, you're on a collision course.**

Picture two ships whose bearing to each other stays constant: they will collide. So each step the missile measures **how fast the line of sight to its target is turning**, and steers to stop it turning:

```csharp
float losRate = (los.X * rv.Y - los.Y * rv.X) / (dist * dist);   // how fast the line of sight turns
float vc = -los.Dot(rv) / dist;                                    // how fast we're closing
float aCmd = Nav * Mathf.Max(vc, 60f) * losRate + 9.81f * perp.Y;  // steering command + gravity compensation
```

`Nav` is how hard it steers. Real missiles use 3–4.

**What makes missiles beatable**, which is where the fun is:

| Limit | In the code | How to use it |
|---|---|---|
| **Turn rate falls with speed** | `amax = MaxG · g · (speed/450)² · air density` | after the motor burns out, the missile slows down, so **turn hard late** and it can't follow |
| **Seeker can't look far sideways** | guidance is lost if the target is more than 63° off its nose | a hard break across its path |
| **Ground clutter** | a SAM can't track a jet below 30 m above the ground | dive for the deck |
| **Flares** | each flare in front of a SAM has a **33% chance** to steal it | pop several (X), then turn |

**Exploding.** Within `Prox` metres of the target it detonates as a direct hit:
- **a Maverick** deals `Damage` to the vehicle, plus blast damage to anything nearby;
- **a SAM** damages your jet if it's within 30 m, less the further away.

Hitting the ground, or reaching the end of its `Life`, it explodes harmlessly.

Next: [Chapter 6: The main scene](06-main-scene.md)
