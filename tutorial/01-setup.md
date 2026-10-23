# Chapter 1: Setup

In this chapter you'll install the tools, create the project, copy in the finished assets and scripts, and set up the folders for your scenes.

---

## 1.1 Install the tools

| Tool | Where | Check it works |
|---|---|---|
| **Godot 4.7, .NET edition** | godotengine.org → Download → the **.NET** build. The standard build can't run C#. | The splash screen or title bar says **.NET** or **mono** |
| **.NET SDK 8 or newer** | dotnet.microsoft.com, or on Ubuntu: `sudo apt install dotnet-sdk-8.0` | `dotnet --list-sdks` prints a version |
| **A code editor** | VS Code with "C# Dev Kit", or Rider | — |

---

## 1.2 Create the project

1. Start Godot (.NET). In the Project Manager click **Create**.
2. Choose a name and an empty folder.
3. **Renderer:** **Compatibility**. It runs on almost any graphics card.
4. **Create & Edit**.
5. **Project → Tools → C# → Create C# solution.** This creates the `.csproj` file that builds your scripts. Godot's FileSystem dock doesn't show it, and that's normal.

Open the new `.csproj` in a text editor and make sure it looks like this. Keep your own file name:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
```

The `4.7.2` must be exactly your Godot version.

---

## 1.3 Project settings

**Project → Project Settings**, then tick **Advanced Settings** at the top right:

| Setting | Value | Why |
|---|---|---|
| Display → Window → Size → Viewport Width / Height | `1600` / `900` | the HUD layout is designed for this size |
| Display → Window → Stretch → Mode | `canvas_items` | the HUD scales with the window |
| Display → Window → Stretch → Aspect | `expand` | wider windows show more world, not black bars |
| Physics → 3D → Physics Engine | `Jolt Physics` | flying debris and thrown tank turrets use it |

---

## 1.4 Copy in the assets and scripts

Everything the game is made of comes from the finished project, `~/f18-cas-csharp`. You'll **assemble** it in the editor.

**The assets** (3D models, terrain, textures, sounds):
```bash
cp -r ~/f18-cas-csharp/assets  /path/to/your/project/
```

**The game scripts:** 11 files, the game's behaviour.
```bash
mkdir -p /path/to/your/project/scripts
cd ~/f18-cas-csharp/scripts
cp Types.cs Fx.cs Terrain.cs Clouds.cs Shell.cs Jet.cs Unit.cs Missile.cs Audio.cs Hud.cs Game.cs  /path/to/your/project/scripts/
```

Replace `/path/to/your/project/` with your project folder. If you already have a `scripts/` folder from an earlier attempt, **empty it first**, so no old versions are left behind.

**What you've got:**

```
assets/
├── terrain.glb                     the ground: hills, colours and trees
├── models/
│   ├── f18.glb                     the jet: 18.3 m long, centred, nose pointing +X
│   ├── t72.glb  m1a2.glb  truck.glb  zsu23.glb  sa8.glb      vehicles
│   ├── mk82.glb  agm65.glb  sa8_missile.glb                  weapons
│   └── airbase.glb  sandbag.glb                              scenery
├── textures/   particle_hard.png  particle_soft.png  cloud.png
└── sounds/     18 .wav files: engine, explosions, gun, warnings, ...
```

Switch back to Godot: it imports everything, so watch the progress bar. Then click the **Build** button (the hammer, top right) or run `dotnet build` in the project folder. It should succeed.

> **Why build now?** Godot only shows a C# script's settings in the Inspector after the project has been built. Building now means every scene you make in the next chapters can be configured straight away.

---

## 1.5 Folders for your scenes

In the **FileSystem** dock, right-click `res://` → **New → Folder**, and create:

```
res://scenes
res://scenes/units
res://scenes/weapons
```

---

## 1.6 Editor basics you'll use constantly

| To do this | Do this |
|---|---|
| **Make a new scene** | **Scene → New Scene**, then click **3D Scene** in the Scene dock. You get a `Node3D` root. |
| **Rename a node** | double-click it in the Scene dock, or select it and press F2 |
| **Add a child node** | select the parent, press **Ctrl+A** (or the `+` button), search for the type, **Create** |
| **Put a model or scene inside another** (*instancing*) | drag the `.glb` or `.tscn` from the FileSystem dock onto the parent node in the Scene dock |
| **Attach a script** | drag the `.cs` file from the FileSystem dock onto the node |
| **Set a position** | Inspector → **Node3D → Transform → Position**: type x, y and z. Rotation is in degrees. |
| **Duplicate a node** | select it, press **Ctrl+D** |
| **Save** | **Ctrl+S**, and choose a path the first time |

**A few C# facts that explain what you'll see:**
- **`[Export]`** in a script makes that value appear in the Inspector, so you set it per scene without touching code. The vehicles' health, speed and armour all work this way.
- **Scripts find nodes by name** with `GetNode("Roll/FlameL")`. So **the node names in the chapters matter**: type them exactly, including capital letters.
- Every script that extends a Godot node type is a `public partial class` whose name matches its file name.

Next: [Chapter 2: The world](02-world.md)
