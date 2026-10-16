# Chapter 2: The world (`world.tscn`)

The world scene holds everything that **doesn't move**: the sky, the sun, the terrain, the airbase, sandbags at the front line, and the clouds. You'll build it once, and it gets placed into the main scene in chapter 6.

The finished scene tree:

```
World                  Node3D
├── WorldEnvironment   sky, ambient light, glow, fog
├── Sun                DirectionalLight3D
├── Terrain            Node3D + Terrain.cs
│   └── TerrainMesh    ← assets/terrain.glb
├── Airbase            ← assets/models/airbase.glb
├── FriendlyLine       Marker3D: where your tanks hold the line
├── GroundProps        Node3D: anything in here is dropped onto the ground
│   └── Sandbag1 … Sandbag8   ← assets/models/sandbag.glb
└── Clouds             Node3D + Clouds.cs
```

---

## 2.1 Create the scene

1. **Scene → New Scene → 3D Scene.** Rename the root **`World`**.
2. **Ctrl+S** → save as `res://scenes/world.tscn`.

---

## 2.2 Sky, light and fog: `WorldEnvironment`

`WorldEnvironment` controls the look of the whole 3D world: the sky, how bright shadows are, glow, and fog.

1. Select `World`, **Ctrl+A** → **WorldEnvironment** → Create.
2. In the Inspector, click **Environment: `<empty>`** → **New Environment**, then click the new Environment to open its settings.
3. Set the following, opening each section by clicking its name:

| Section → setting | Value | What it does |
|---|---|---|
| **Background → Mode** | `Sky` | draw a sky behind everything |
| **Sky → Sky** | click the **▾** arrow → **New: Sky**. Click the new Sky to expand it, then **Sky Material** → **▾** → **New: ProceduralSkyMaterial**. Click that to expand it for the colours below | a sky gradient computed by Godot, no image needed |
| Procedural sky: **Sky → Top Color** | `386bc7` | deep blue overhead |
| Procedural sky: **Sky → Horizon Color** | `adc4e0` | pale at the horizon |
| Procedural sky: **Ground → Bottom Color** | `4d4d47` | below the horizon |
| Procedural sky: **Ground → Horizon Color** | `99a6b3` | |
| **Ambient Light → Source** | `Sky` | shadows are lit by the sky's colour, so they're bluish, not black |
| **Ambient Light → Energy** | `0.7` | |
| **Tonemap → Mode** | `Filmic` | bright highlights roll off softly like film |
| **Glow → Enabled** | ✔ | glowing things (tracers, flares, flames) bloom |
| **Glow → Intensity** | `0.6` | |
| **Fog → Enabled** | ✔ | distant hills fade into blue haze, which gives a sense of scale |
| **Fog → Light Color** | `a8bdd9` | |
| **Fog → Density** | `0.00009` | very light; it only shows over kilometres |
| **Fog → Sky Affect** | `0` | keeps the sky itself clear |

> To type a colour, click the colour box, then type the code into the **Hex** field of the colour picker.

---

## 2.3 The sun

1. Select `World`, **Ctrl+A** → **DirectionalLight3D**. Rename it **`Sun`**. The name matters: the game adjusts its shadows as you zoom.
2. Inspector:
   - **Transform → Rotation:** `-48`, `-35`, `0`. That's tilted 48° downward and 35° to the side, an afternoon sun.
   - **Light → Energy:** `1.2`
   - **Shadow → Enabled:** ✔
   - **Directional Shadow → Max Distance:** `1500`

A *directional* light shines in parallel rays from infinitely far away, like the real sun, so only its rotation matters, not its position.

---

## 2.4 The terrain

1. Select `World`, **Ctrl+A** → **Node3D**. Rename it **`Terrain`**.
2. Drag **`scripts/Terrain.cs`** from the FileSystem dock onto `Terrain`.
3. Drag **`assets/terrain.glb`** onto **`Terrain`**, so it becomes its child. It arrives **also named `Terrain`**, with a 🎬 scene icon, because that's the name of the model's root node inside the file. Rename this **child** to **`TerrainMesh`**:
   ```
   Terrain          ← Node3D with Terrain.cs (script icon)
   └── TerrainMesh  ← terrain.glb (🎬 icon)
   ```

Leave both at position (0, 0, 0). The terrain model is already in world coordinates: 22 km long along x, from x = −5000 to 17000, with hills behind the lane and the flat airbase near x = 1500.

**Zoom out** with the mouse wheel to see the whole thing. It's big.

> **Why is it grey or white in the editor?** The terrain's colours are painted on its vertices, and Godot doesn't always switch on "use vertex colour" when it imports a `.glb`. `Terrain.cs` switches it on when the game runs, so it'll be green, brown, rocky and snowy in game. To see the colours in the editor too: right-click `TerrainMesh` → **Editable Children**. Then select `Ground`, and in **Surface Material Override → 0** choose **New StandardMaterial3D**, and in it tick **Vertex Color → Use as Albedo**. Do the same for `Trees`.

### How `Terrain.cs` works

Open `scripts/Terrain.cs` in your code editor and read along.

The game needs to know **how high the ground is** at any point along the flight lane. Wheels, bombs, bullets, missiles, tanks and the camera all ask, many times a frame. They call:

```csharp
public float Height(float x)
{
    float f = (Mathf.Clamp(x, XMin, XMax - Step) - XMin) / Step;
    int i = (int)f;
    return Mathf.Lerp(_table[i], _table[i + 1], f - i);
}
```

`_table` holds the ground height every 10 m along the lane, and `Height` interpolates between the two nearest values.

**Where do those heights come from? From the model you just placed.** When the game starts, `Game` calls `Terrain.Build()`. It finds the `TerrainMesh` child, takes the mesh named **`Ground`**, and slices it with the plane z = 0, the flight lane. Each triangle that crosses the plane leaves a short line at the ground height, and `SampleLane` writes those heights into `_table`.

So **the gameplay ground is exactly the ground you see**. If you sculpt `terrain.glb` in Blender, the jet, the bombs and the tanks follow your changes.

`Build()` also does two more things:
- switches on vertex colours for every terrain material, as described above;
- builds **collision** (a `ConcavePolygonShape3D`) for a strip near the lane, so tumbling debris and flying turrets bounce off the ground.

The other members:
- **`OnRunway(x)`:** true between x = 300 and 2700. The landing code uses it.
- **`RunwayX0`, `RunwayX1`, `BaseH`:** the runway's ends and its height, 40 m.

---

## 2.5 The airbase

1. Drag **`assets/models/airbase.glb`** onto **`World`**. Rename it **`Airbase`**.
2. Leave it at position (0, 0, 0).

Like the terrain, it's built in world coordinates: a 2.4 km runway from x = 300 to 2700 at height 40, with an apron, four hangars and a control tower. Find it near the left end of the terrain.

> **Don't move the terrain or the airbase.** The landing code expects the runway exactly there, and the terrain heights are read in world space.

---

## 2.6 The front line and sandbags

**The friendly line marker:**
1. Select `World`, **Ctrl+A** → **Marker3D**. Rename it **`FriendlyLine`**.
2. **Position:** `3800`, `0`, `0`.

A `Marker3D` is an invisible point that shows as a small cross in the editor. The game reads its x position to know where your side's front line is:
- your tanks defend around there;
- green smoke marks it;
- enemy waves spawn 4.4 km east of it.

**Move this marker and the whole battle moves with it.**

**Sandbags:**
1. Select `World`, **Ctrl+A** → **Node3D**. Rename it **`GroundProps`**.
2. Drag **`assets/models/sandbag.glb`** onto `GroundProps`. Rename it `Sandbag1`, and set **Position:** `3840`, `0`, `12`.
3. Select `Sandbag1` and press **Ctrl+D** seven times. Set the copies' x positions to `3845`, `3850`, `3855`, `3860`, `3865`, `3870`, `3875`. Keep y = 0 and z = 12.

**Why y = 0?** Anything inside `GroundProps` is dropped onto the terrain when the game starts. `Game` sets each child's height to `Terrain.Height(x)`. So you only choose x and z, and the props always sit on the ground, even after you edit the terrain.

In the editor they're at height 0, so they're **hidden under the terrain**. That's expected. To find them, click one in the Scene dock and press **F** in the viewport to focus it.

You can put anything else in `GroundProps` the same way: more sandbags, buildings, or a parked truck model as decoration.

---

## 2.7 Clouds

1. Select `World`, **Ctrl+A** → **Node3D**. Rename it **`Clouds`**.
2. Drag **`scripts/Clouds.cs`** onto it.
3. Inspector → **Texture:** drag in **`assets/textures/cloud.png`**.

Leave **Count** at 70 and **Seed** at 5. Those are `[Export]` settings from the script, shown in the Inspector.

### How `Clouds.cs` works

When the game starts, `_Ready()` creates `Count` clusters of 5 soft, see-through squares:
- 1.4–3.2 km up;
- 0.6–4.5 km **behind** the lane;
- spread along the whole battlefield.

Each square uses **billboard** mode, so it always faces the camera and looks like a puffy cloud from any angle. Because they're far behind the lane, they slide past more slowly than the ground as you fly: *parallax*, which sells the depth.

The same **Seed** gives the same sky every time. Change it for a different sky.

> The clouds only appear when the game runs. The script creates them in `_Ready()`, which doesn't run in the editor.

---

## ✅ Check

Save (**Ctrl+S**). Your Scene dock should match the tree at the top of this chapter.

In the 3D viewport you should see:
- the long terrain with hills and trees;
- the runway and hangars near the left end;
- the `FriendlyLine` cross at x = 3800.

Now that the scene has its own `Sun` and `WorldEnvironment`, the editor uses them instead of its grey preview lighting, so the viewport looks the way the game will: blue sky, sunlight, light fog.

Next: [Chapter 3: The jet](03-jet.md)
