# Chapter 4: Ground vehicles (`scenes/units/`)

Five vehicle scenes, all built the same way. Each has a root with `Unit.cs` and its stats in the Inspector, and a `Body` node holding the 3D model:

```
T72            Node3D + Unit.cs     ← stats set in the Inspector
└── Body       Node3D               ← the script turns this to face left or right
    └── Model  ← assets/models/t72.glb
```

You build the T-72 step by step, then make the other four by copying it and changing the model and the numbers.

---

## 4.1 The T-72 tank

1. **Scene → New Scene → 3D Scene.** Rename the root **`T72`**.
2. Drag **`scripts/Unit.cs`** onto `T72`.
3. In the Inspector, set the script's values:

   | Property | Value | Meaning |
   |---|---|---|
   | **Kind** | `Tank` | which AI it runs (a dropdown) |
   | **Display Name** | `T-72` | the label the HUD shows |
   | **Team** | `1` | 0 = friendly, 1 = enemy |
   | **Hp** | `100` | health |
   | **Radius** | `4.5` | hit circle, in metres |
   | **Center Height** | `1.6` | height of the hull's centre above the ground |
   | **Speed** | `7` | driving speed in m/s |
   | **Gun Armor** | `0.3` | multiplies cannon damage: tanks shrug off your gun |
   | **Value** | `100` | points for destroying it |

4. Select `T72`, **Ctrl+A** → **Node3D**. Rename it **`Body`**.
5. Drag **`assets/models/t72.glb`** onto **`Body`**. Rename it **`Model`**.
6. **Ctrl+S** → `res://scenes/units/t72.tscn`.

The tank's front points along **+X**. Leave everything at position 0; the script places the vehicle on the ground and turns `Body` to face the right way.

**Look inside the model:** right-click `Model` → **Editable Children** (you can turn it off again after). You'll see `Turret`, and `GunPivot` inside it. The AI rotates `Turret` to aim and tilts `GunPivot` to raise the gun. The script finds them **by name**, so if you ever replace the model with your own, keep those two names.

---

## 4.2 The other four

For each row below:

1. With `t72.tscn` open, **Scene → Save Scene As…** with the new file name.
2. Rename the root node.
3. Select `Model` under `Body` and **delete** it. Drag the new `.glb` onto `Body`, and rename it `Model`.
4. Set the root's Inspector values.
5. **Ctrl+S.**

| File (`scenes/units/`) | Root name | Model (`assets/models/`) | Kind | Display Name | Team | Hp | Radius | Center Height | Speed | Gun Armor | Value |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `truck.tscn` | `Truck` | `truck.glb` | Truck | `TRUCK` | 1 | 35 | 4 | 1.4 | 11 | 1 | 50 |
| `zsu23.tscn` | `ZSU23` | `zsu23.glb` | Aaa | `ZSU-23` | 1 | 45 | 4 | 1.6 | 7 | 0.7 | 150 |
| `sa8.tscn` | `SA8` | `sa8.glb` | Sam | `SA-8` | 1 | 50 | 4.5 | 1.8 | 8 | 0.8 | 250 |
| `m1a2.tscn` | `M1A2` | `m1a2.glb` | FriendlyTank | `M1A2` | **0** | 160 | 4.8 | 1.6 | 0 | 0.25 | 0 |

- **The M1A2 is yours**: Team **0**, and Speed 0, because friendlies hold their position. It carries a glowing green panel on its turret so you can tell it apart from the air.
- **The SA-8** has four missiles on its launcher, named `Missile0` to `Missile3`. The script hides one each time it fires.

You now have five vehicle scenes. Because the stats live in the Inspector, you can rebalance the game later without touching code: make trucks tougher, AAA cheaper, and so on.

---

## 4.3 How `Unit.cs` works

Open `scripts/Unit.cs`.

### The Inspector values

```csharp
[Export] public UnitKind Kind = UnitKind.Tank;
[Export] public string DisplayName = "T-72";
[Export] public int Team = 1;
[Export] public float Hp = 100f;
...
```

**`[Export]`** is what put these in the Inspector. `UnitKind` is an *enum*, a fixed list of options (`Tank, Truck, Aaa, Sam, FriendlyTank`), so the Inspector shows a dropdown.

### Starting up

```csharp
public void Init(Game main, float x, float laneZ)
{
    ...
    _body = GetNode<Node3D>("Body");
    _body.Rotation = new Vector3(0, _facing > 0f ? 0f : Mathf.Pi, 0);
    ...
    FindParts();
    Place();
}
```

`Game` calls `Init` for every vehicle: the friendlies you'll place in chapter 6, and every enemy a wave spawns.
- **Enemies (Team 1) face left**, towards your side. `Body` is turned 180°, since the models face +X.
- **`FindParts()`** finds `Turret`, `GunPivot` and `Missile0`… inside the model, and collects every mesh so they can all be blackened when the vehicle is destroyed.
- **`Place()`** puts the vehicle on the ground with `Terrain.Height(x)` and tilts it to match the slope.

### Moving

Each physics frame, enemies drive left at `Speed` until they reach their stopping point, which the wave decides (chapter 7). Tanks close in, while SAMs hang back.

### The AI, per `Kind`

**Tank** (both sides):
1. Find the nearest enemy vehicle within 2.4 km.
2. Work out the gun elevation needed for a shell at 950 m/s to arc onto it: `atan(dy/dx) + ½·asin(g·dx/v²)`, the projectile formula from physics class.
3. Raise `GunPivot` towards that angle, slowly.
4. Fire every 4.5–7.5 s, less accurately while moving.

**AAA (ZSU-23):**
- It **leads** you: it aims where you'll be when the bullets arrive, given your velocity, and repeats the calculation 3 times to refine it.
- It fires bursts of 18 rounds/s at targets within 2.8 km.
- ⭐ **The turret and guns turn at a limited speed.** A jet crossing **fast and close** sweeps across the sky faster than they can follow. That's the real way to survive AAA: never hang around slowly in front of one.

**SAM (SA-8):**
- The radar **can't lock a jet flying below 45 m above the ground** (ground clutter), or beyond 8 km.
- After **2.5 s of continuous lock** it launches a missile, at most once every 10 s.
- While it's locking, your HUD shows **SAM RADAR LOCK** and you hear a warning tone. **Fly low to break the lock.**

### Being destroyed

`TakeDamage()` lowers `Hp`. At zero, `Die()`:
1. Turns every mesh charred black.
2. Spawns an explosion, physics debris, and a fire that keeps burning on the wreck.
3. Then, depending on the vehicle:
   - **Tanks:** either the turret is **thrown off** at once, becoming a tumbling physics body, or the **ammunition cooks off** a moment later and throws it then.
   - **AAA:** loses its turret.
   - **SAMs:** any unfired missiles explode one after another.

Delayed effects use Godot timers, with an `IsInstanceValid(this)` check. Timers keep running after you press R to restart, and without the check they'd touch a deleted vehicle and crash.

Next: [Chapter 5: Missiles](05-missiles.md)
