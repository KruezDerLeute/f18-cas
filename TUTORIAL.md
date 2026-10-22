# F/A-18 Close Air Support: a Godot 4 + C# tutorial

Build a side-on combat flight game in the Godot editor:

- An **F/A-18** with a real flight model: lift, drag, stall, thrust, fuel, g-limits, landing and rearming.
- **Enemy tanks, trucks, ZSU-23 AAA and SA-8 SAMs** driving at your friendly tanks.
- **Weapons:** a cannon, bombs with an aiming pipper, Maverick missiles, and flares against SAMs.
- **Effects:** explosions, smoke, fire, physics debris, and a HUD with threat warnings.

You **build every scene yourself in the editor**: the world, the jet, each vehicle and the missiles. You place models, lights and markers, and set each vehicle's stats in the Inspector. The C# scripts provide the behaviour (flying, AI, guidance, effects), and each chapter explains the script behind the scene you just built.

The finished project is in this folder (`game.tscn`, `scenes/`, `scripts/`, `assets/`). Open it any time to compare with your work.

---

## Chapters

| # | Chapter | You build | You learn |
|---|---|---|---|
| 1 | [Setup](tutorial/01-setup.md) | the project; copy in the assets and scripts | project settings, the folder layout, C# in Godot |
| 2 | [The world](tutorial/02-world.md) | `world.tscn`: sky, sun, terrain, runway, sandbags, clouds | environments, lights, instancing models, how the terrain script reads ground height |
| 3 | [The jet](tutorial/03-jet.md) | `jet.tscn`: F-18, engine flames, lights, bombs, Mavericks | building a scene from parts, materials, the flight model |
| 4 | [Ground vehicles](tutorial/04-units.md) | `t72`, `truck`, `zsu23`, `sa8`, `m1a2` scenes | exported stats in the Inspector, reusing a scene, enemy AI |
| 5 | [Missiles](tutorial/05-missiles.md) | `maverick.tscn`, `sam_missile.tscn` | missile guidance, and why missiles can be dodged |
| 6 | [The main scene](tutorial/06-main-scene.md) | `game.tscn`: everything together, plus camera, sound and HUD | wiring scenes together; **play the game** |
| 7 | [How the game runs](tutorial/07-how-it-works.md) | — | `Game.cs`, `Hud.cs`, `Audio.cs` and the helpers explained |
| 8 | [Playing, editing, troubleshooting](tutorial/08-finish.md) | — | controls, changing the game in the editor, fixing problems, ideas |
| 9 | [The scripts, file by file](tutorial/09-scripts.md) | — | every `.cs` file explained method by method, plus a C# quick reference |

---

## The one idea to understand first

**The game is 2D; the picture is 3D.**

All gameplay happens on a flat vertical plane: **x is to the right, y is up, and z = 0**. The jet's position, bullets, bombs and missiles are all `Vector2` in the scripts. That keeps the physics simple.

Everything is *drawn* in 3D, with a camera off to the side looking at that plane. So in the editor:

- **x** runs along the battlefield: the runway is at x = 300–2700 m, your friendly tanks at about 3800 m, and enemies arrive from the east, which is larger x;
- **y** is height;
- **z** is depth: the flight lane is at z = 0, hills sit behind it (negative z), and the camera is in front (positive z).

Keep that in mind whenever you type a position into the Inspector.

Start with [chapter 1](tutorial/01-setup.md).
