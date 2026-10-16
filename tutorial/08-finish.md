# Chapter 8: Playing, editing, troubleshooting

---

## 8.1 Controls

| Key | Action |
|---|---|
| **W / S** (or ↑ / ↓) | pull / push, relative to the canopy |
| **F** | half-roll: upright ↔ inverted |
| **E / Q** (or Shift / Ctrl) | throttle up / down; past the red mark = afterburner |
| **Space** | cannon |
| **B** | drop a bomb |
| **M** | fire a Maverick at the locked target |
| **T** | switch Maverick target |
| **X** | flares |
| **Z** | speedbrake in the air, wheel brakes on the ground |
| **G** | landing gear |
| **Mouse wheel**, **+ / −** | zoom |
| **P / Esc** | pause |
| **R** | restart |
| **H** | show / hide help |

## 8.2 How to fly it well

- **Let go to fly straight**: the jet trims itself to level flight.
- **Speed is life.** Hard turns bleed speed; below about 130 knots you'll stall.
- **Bombs:**
  - dive gently at 300–600 m above the ground;
  - put the pipper on the target;
  - check the time shown is over 1.0 s, or the bomb won't arm;
  - press B, then **pull up**, because your own fragments reach about 150 m.
- **Gun:** good against trucks and AAA, weak against tanks.
- **Mavericks:** the tank killers. Lock from 2–6 km, fire, and turn away.
- **AAA:** pass fast and close. Don't fly slowly in front of a ZSU.
- **SAMs:**
  - stay **below 45 m** above the ground and their radar can't lock you;
  - if **MISSILE** appears, pop flares several times, then turn hard.
- **Landing:**
  - about 150 knots, gear down (G);
  - aim for a vertical speed of about −500 to −1000 FPM onto the runway;
  - hold Z to stop.

  Wait 4 s to rearm and refuel.

## 8.3 Changing the game in the editor

Because the game is built from scenes, most changes need no code:

| To… | Do this |
|---|---|
| **Move the front line** | in `world.tscn`, move the `FriendlyLine` marker. Enemies spawn relative to it, so move your friendly tanks in `game.tscn` to match |
| **Start somewhere else** | in `game.tscn`, move `Jet`. Keep z = 0; rotate it around Z to start climbing or diving |
| **More or fewer friendlies** | in `game.tscn`, add or delete `m1a2.tscn` instances under `Units` |
| **Tougher, faster or worth more vehicles** | open the vehicle's scene and change **Hp**, **Speed**, **Gun Armor** or **Value** in the Inspector |
| **Faster or deadlier missiles** | open `sam_missile.tscn` or `maverick.tscn` and change **Boost**, **Max G**, **Damage**… |
| **Scenery** | drop any model into `world.tscn`. Put it under `GroundProps` (with y = 0) to have it placed on the ground automatically |
| **A different sky** | edit the `WorldEnvironment` colours, or the `Sun`'s rotation and energy. Try a low orange sun for dusk |
| **More or fewer clouds** | select `Clouds` and change **Count** or **Seed** |
| **Sound volume** | Project → Project Settings → Audio, or the Audio tab at the bottom of the editor |

**Replacing a model with your own**, made in Blender or downloaded:

- **The front points along +X**, the bottom sits at y = 0 (for vehicles), and the size is in metres.
- In a vehicle model, **keep a node called `Turret`**, which turns, with **`GunPivot`** inside it, which tilts the gun. On a SAM launcher, put **`Missile0`–`Missile3`** inside `GunPivot`.
- Open the vehicle scene, delete `Model` under `Body`, and drag your `.glb` in, named `Model`.

**Editing the terrain** in Blender:
1. Import `assets/terrain.glb`, sculpt it, and export it back over the same file (glTF binary, +Y up).
2. Keep the walkable mesh named **`Ground`**.
3. Keep the runway **flat at height 40 between x = 300 and 2700**.

The game reads the ground height from the mesh, so gameplay follows your edits.

## 8.4 Troubleshooting

| Problem | Fix |
|---|---|
| **"Cannot instantiate C# script"**, or scripts don't run | use the **.NET** build of Godot, and click **Build** (the hammer, top right) |
| **The Inspector doesn't show a script's settings** (Kind, Hp, Start Speed…) | build the project (the hammer), then click another node and back |
| **Red errors like `Node not found: "Roll/FlameL"`** | a node name or position in the tree doesn't match the chapter. Names are case-sensitive |
| **`Terrain: add assets/terrain.glb as a child named "TerrainMesh"`** | in `world.tscn`, the terrain model must be a child of `Terrain`, named exactly `TerrainMesh` |
| **Crash at start mentioning `TankScene`, `MaverickScene`, `ParticleHard`…** | a slot on `Game` in the Inspector is empty (chapter 6.7) |
| **No HUD** | `Hud` must be under `HudLayer`, with Layout **Full Rect** and `Hud.cs` attached |
| **The jet sinks into the runway or floats** | `f18.glb` must sit at position (0, 0, 0) inside `Roll`, unscaled |
| **Vehicles face the wrong way** | the model must be inside `Body`, not directly under the root |
| **Terrain white in the editor** | normal: it gets its colours when the game runs (see chapter 2.4 to fix the editor view) |
| **Sandbags missing in the editor** | they're at y = 0, under the ground, and placed on the ground when the game runs |
| **No sound** | check the mute button on the editor's Game tab, and the console line `AUDIO: device ...`. On Linux with PipeWire, install `pipewire-pulse` |
| **Numbers like `1,5` in the HUD** | the locale line at the top of `Game._Ready()` is missing |

## 8.5 Ideas

- **Night missions:** darken the sky colours and the sun. The tracers, flares and explosions already glow.
- **A moving front:** after each wave, move the `FriendlyLine` east from code (`FriendlyLine += 500`) and shift your tanks with it.
- **New vehicles:** make a new scene with `Unit.cs`, for example an infantry fighting vehicle with Kind `Tank` but less armour, and add it to the waves in `Game.StartWave`.
- **Rockets:** a third missile scene with **Nav** `0`, so it flies straight, fired in salvos.
- **A gamepad:** add joystick events to the actions in `Game.SetupInput()`.

## 8.6 Appendix: where the assets came from

All the assets were made by code, so the project needs no downloads beyond the jet pack. The generator scripts are in the finished project's `scripts/` folder, but **not** in yours, because the game doesn't need them:

| Tool | Made |
|---|---|
| `TerrainBaker.cs` | `terrain.glb`: noise-generated hills, vertex colours, 3500 trees |
| `ModelBuilder.cs` | the vehicle, weapon and airbase models, built from boxes and cylinders |
| `TextureBaker.cs` | the particle and cloud textures |
| `SoundBaker.cs` | every sound, synthesized from noise and sine waves |
| `Tools.cs` (`--export-f18`) | `f18.glb`, cut out of the six-jet pack, scaled to 18.3 m and centred |

To regenerate them, in the finished project:

```bash
godot --headless --path . res://tools/tools.tscn -- --bake-assets
godot --headless --path . res://tools/tools.tscn -- --export-f18
```

⚠️ This **overwrites** the files in `assets/`.

← Back to the [start page](../TUTORIAL.md)
