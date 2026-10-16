# Chapter 6: The main scene (`game.tscn`)

Time to put it all together and fly. The main scene holds the world, the jet, your friendly tanks, the camera, sound and the HUD. `Game.cs` on the root connects them all.

```
Game              Node3D + Game.cs     ← links to the vehicle and missile scenes in the Inspector
├── World         ← scenes/world.tscn
├── Jet           ← scenes/jet.tscn          position (1500, 650, 0)
├── Units         Node3D
│   ├── Friendly1 ← scenes/units/m1a2.tscn
│   ├── Friendly2
│   ├── Friendly3
│   └── Friendly4
├── Camera        Camera3D
├── Audio         Node + Audio.cs
└── HudLayer      CanvasLayer
    └── Hud       Control + Hud.cs
```

---

## 6.1 Create the scene

1. **Scene → New Scene → 3D Scene.** Rename the root **`Game`**.
2. Drag **`scripts/Game.cs`** onto `Game`.
3. **Ctrl+S** → `res://game.tscn`.
4. **Project → Project Settings → Application → Run → Main Scene** → `res://game.tscn`. F5 now always starts here.

## 6.2 World and jet

1. Drag **`scenes/world.tscn`** onto `Game`. It appears as **`World`**, with a "scene" icon: it's an *instance* of the scene you built. Edit the original `world.tscn` and this copy updates too.
2. Drag **`scenes/jet.tscn`** onto `Game`. It appears as `Jet`; keep that name. Set **Position:** `1500`, `650`, `0`.

**That's where you start: above the runway at 650 m, flying right.** To start somewhere else, move it, but keep z = 0. You could even rotate it around Z to start in a climb.

## 6.3 Your friendly tanks

1. Select `Game`, **Ctrl+A** → **Node3D**. Rename it **`Units`**. Every vehicle lives in here, including the enemies the waves spawn.
2. Drag **`scenes/units/m1a2.tscn`** onto `Units` four times, and set:

   | Name | Position |
   |---|---|
   | `Friendly1` | `3560`, `0`, `-12` |
   | `Friendly2` | `3640`, `0`, `2` |
   | `Friendly3` | `3720`, `0`, `-6` |
   | `Friendly4` | `3790`, `0`, `5` |

- **x** places them behind the `FriendlyLine` marker at 3800.
- **z** spreads them slightly in depth, so they don't overlap on screen. Keep z between about −15 and 15.
- **y doesn't matter**: the script sets each vehicle on the ground.

Add more if you want an easier game; the game keeps track of however many you place.

## 6.4 Camera

1. Select `Game`, **Ctrl+A** → **Camera3D**. Rename it **`Camera`**.
2. Inspector:
   - **Current:** ✔
   - **FOV:** `50`
   - **Near:** `0.5`
   - **Far:** `24000`, so the whole 22 km battlefield is visible.

Leave its position alone; `Game.cs` moves it every frame to follow the jet.

## 6.5 Sound

1. Select `Game`, **Ctrl+A** → **Node** (the plain grey one). Rename it **`Audio`**.
2. Drag **`scripts/Audio.cs`** onto it. **Sound Folder** should read `res://assets/sounds`.

## 6.6 The HUD

1. Select `Game`, **Ctrl+A** → **CanvasLayer**. Rename it **`HudLayer`**. A canvas layer draws 2D on top of the 3D view, and isn't affected by the camera.
2. Select `HudLayer`, **Ctrl+A** → **Control**. Rename it **`Hud`**.
3. With `Hud` selected:
   - in the 2D toolbar, open **Layout**, the anchor icon, and choose **Full Rect**;
   - Inspector → **Mouse → Filter:** `Ignore`, so mouse-wheel zoom reaches the game.
4. Drag **`scripts/Hud.cs`** onto `Hud`.

## 6.7 Connect the scenes to `Game`

Select **`Game`**. The Inspector shows three groups of slots, from the `[Export]` fields in `Game.cs`. Drag a file from the FileSystem dock into each:

| Group → slot | Drag in | Used for |
|---|---|---|
| **Units → Tank Scene** | `scenes/units/t72.tscn` | waves spawn copies of these |
| **Units → Truck Scene** | `scenes/units/truck.tscn` | |
| **Units → Aaa Scene** | `scenes/units/zsu23.tscn` | |
| **Units → Sam Scene** | `scenes/units/sa8.tscn` | |
| **Units → Friendly Scene** | `scenes/units/m1a2.tscn` | reinforcements between waves |
| **Weapons → Maverick Scene** | `scenes/weapons/maverick.tscn` | when you press M |
| **Weapons → Sam Missile Scene** | `scenes/weapons/sam_missile.tscn` | when an SA-8 fires |
| **Weapons → Bomb Model** | `assets/models/mk82.glb` | the falling bomb you see |
| **Effects → Particle Hard** | `assets/textures/particle_hard.png` | fire, sparks, flares |
| **Effects → Particle Soft** | `assets/textures/particle_soft.png` | smoke, dust, vapour |

Save (**Ctrl+S**).

**Check the names one last time.** `Game.cs` finds `World`'s contents (`Terrain`, `Sun`, `FriendlyLine`, `GroundProps`) and `Jet`, `Units`, `Camera`, `Audio` and `HudLayer/Hud` by name.

---

## ✅ Play

Press **F5**.

**What you should see and try:**

1. **The start:** you're flying right over the airbase. "TASKING: CLOSE AIR SUPPORT" appears. There's sky, clouds, hills and trees, and green smoke over your tanks to the east.
2. **Fly:** W/S pitch, and the jet holds its path when you let go. **F** rolls upside down.
3. **Throttle:** hold **E** past the red mark on the throttle bar. The flames stretch long and the afterburner roars.
4. **After about 8 s:** "WAVE 1: 3 TANKS, 3 TRUCKS, 1 AAA, 0 SAM". Red arrows on the screen edge point east, and the nearest shows its distance.
5. **Gun:** dive at a truck, put the small cross on it, and hold **Space**.
6. **Bombs:** from 300–600 m up, put the circle pipper on a tank and press **B**. The boom arrives a moment later, at the speed of sound.
7. **Maverick:** point your nose at a tank 1–5 km away. Wait for "TRACK", then press **M**.
8. **Land:**
   - slow down and press **G** for the gear;
   - descend gently onto the runway;
   - hold **Z** to brake.

   After 4 seconds you're rearmed.
9. **R** restarts.

If something's missing or there are red errors in the **Output** panel, see chapter 8's troubleshooting table. Most problems are a misspelled node name.

Next: [Chapter 7: How the game runs](07-how-it-works.md)
