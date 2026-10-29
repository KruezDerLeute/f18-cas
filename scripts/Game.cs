using System.Collections.Generic;
using System.Linq;
using Godot;

/// Game root, attached to the "Game" node of game.tscn. It connects the scenes
/// you built in the editor (world, jet, units, camera, HUD), spawns waves,
/// projectiles and effects, checks hits and moves the camera.
/// Gameplay is 2D (x right, y up, everything on z = 0); rendering is full 3D.
public partial class Game : Node3D
{
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _zoom = Mathf.Clamp(_zoom * 0.9f, 0.3f, 3f);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                _zoom = Mathf.Clamp(_zoom * 1.1f, 0.3f, 3f);
        }
    }

    public override void _Ready()
    {
        GD.Randomize();
        SetupInput();
        BuildResources();

        // Nodes you placed in the editor. FindChild(..., owned: false) also
        // searches inside instanced scenes such as world.tscn.
        Audio = GetNode<Audio>("Audio");
        Audio.Main = this;

        Terrain = (Terrain)FindChild("Terrain", true, false);
        Terrain.Build();
        (FindChild("Foreground", true, false) as Foreground)?.Build(Terrain);

        _sun = FindChild("Sun", true, false) as DirectionalLight3D;
        if (FindChild("FriendlyLine", true, false) is Node3D line)
            FriendlyLine = line.GlobalPosition.X;

        PlaceGroundProps();
        AddFriendlySmoke();

        _unitsNode = GetNode<Node3D>("Units");
        foreach (var u in _unitsNode.GetChildren().OfType<Unit>())
        {
            u.Init(this, u.Position.X, u.Position.Z);
            Units.Add(u);
        }
        FriendlyStart = CountTeam(0);
        Jet = GetNode<Jet>("Jet");
        Jet.Init(this);
        Camera = GetNode<Camera3D>("Camera");
        _baseFov = Camera.Fov;
        Camera.SetCullMaskValue(Flir.HotLayer, false);
        Flir = new Flir { Main = this };
        AddChild(Flir);
        _hud = GetNode<Hud>("HudLayer/Hud");
        _hud.Main = this;

        if (!Settings.SkipMenu)
        {
            ShowMenu();
            return;
        }
        if (Settings.ShowBriefing)
        {
            Settings.ShowBriefing = false;
            ShowBriefing();
            return;
        }
        StartMission();
    }

    void StartMission()
    {
        Msg($"ДААЛГАВАР: ОЙРЫН АГААРЫН ДЭМЖЛЭГ  ({Settings.DifficultyName})", new Color(0.6f, 1.0f, 0.6f), 6f);
        Msg("Ногоон утааны дэргэдэх манай танкуудыг хамгаал. Дайсан баруун талаас ирж байна.", Colors.White, 7f);
        if (Jet.OnGround)
            Msg("ТООРМОСОО СУЛЛА (Z), ТҮЛХЭЛТЭЭ БҮРЭН НЭМЭЭД (E) ХӨӨР", new Color(0.6f, 1.0f, 0.6f), 7f);
        else
            Msg("W - өгсөх, S - буух, SPACE - буудах. F дарвал эргэж буцна.", new Color(0.6f, 1.0f, 0.6f), 9f);
        UpdateCamera(1f);
    }

    /// Title screen over a slow fly-by of the battlefield.
    void ShowMenu()
    {
        var menu = MenuScene.Instantiate<Menu>();
        menu.OnStart = d =>
        {
            Settings.Difficulty = d;
            Settings.SkipMenu = true;
            Settings.ShowBriefing = true;
            GetTree().ReloadCurrentScene();
        };
        _flyby = true;
        Jet.StartFlyby();
        OpenOverlay(menu);
    }

    /// Mission briefing, panning along the friendly line, before the jet is handed over.
    void ShowBriefing()
    {
        _menuX0 = FriendlyLine - 500f;
        var briefing = new Cutscene
        {
            Header = "ДААЛГАВРЫН ТАНИЛЦУУЛГА",
            Title = "\"ТӨМӨР БАМБАЙ\" АЖИЛЛАГАА",
            Info = $"ДУУДЛАГЫН НЭР: ВАЙПЕР 1        ТҮВШИН: {Settings.DifficultyName}",
            Body = BriefingText(),
            ContinueText = "ДААЛГАВАР ЭХЛҮҮЛЭХ",
            OnDone = () =>
            {
                _jetShotAt = -1f;
                if (_flyby)
                {
                    _flyby = false;
                    _briefShot = false;
                    Jet.RestoreStart();
                }
                InMenu = false;
                _hud.Visible = true;
                _menuLayer.QueueFree();
                StartMission();
            },
        };
        OpenOverlay(briefing);

        // Each difficulty can have its own recording, with its own camera cut time.
        // The player is a child of the briefing, so it stops when the mission starts.
        var voiceStream = BriefingVoice;
        float shotTime = BriefingJetShotTime;
        if (Settings.Difficulty == Difficulty.Normal && BriefingVoiceNormal != null)
        {
            voiceStream = BriefingVoiceNormal;
            shotTime = BriefingJetShotTimeNormal;
        }
        if (voiceStream != null)
        {
            var voice = new AudioStreamPlayer { Stream = voiceStream };
            briefing.AddChild(voice);
            voice.Play();
            _jetShotAt = shotTime;
        }
    }

    static string BriefingText()
    {
        string start = Settings.StartInAir
            ? "Та нисэх буудлын дээгүүр аль хэдийн нисч байна. Фронт руу хүрэхийн тулд БАРУУН тийш нис."
            : "Таны онгоц хөөрөх зурвас дээр байна. Тоормосоо сулла (Z), түлхэлтээ бүрэн нэм (E), тэгээд хөөр.";
        return
            "06:00 ЦАГ. Дайсны хуягт хүчин зүүн талын голын хөндийг нэвтэрлээ.\n\n" +
            "Манай сүүлчийн танкийн взвод НОГООН УТААНЫ дэргэд бэхлэлт хийж байна. Тэд ганцаараа " +
            "тэсэхгүй. Та бол Вайпер 1 - ойр орчимд байгаа цорын ганц онгоц.\n\n" +
            "Дайсны цувааг манай танкуудад хүрэхээс өмнө устга. Нэмэлт хүч " +
            $"{MissionWaves} довтолгооны дараа ирнэ: тэд ирэх хүртэл шугамаа барь. Зенитийн буу болон " +
            "ЗУП (зенитийн удирдлагат пуужин) таныг хүлээж байна: пуужингийн дохио дуугарвал " +
            "ХУУРАМЧ БАЙ (X) хаяж, огцом эргэ.\n\n" +
            start + "\n\n" +
            "Амжилт хүсье, Вайпер 1.";
    }

    /// Victory or overrun: an ending cutscene, then the debrief.
    void ShowEnding()
    {
        _endingShown = true;
        bool won = State == GameState.Victory;
        int saved = CountTeam(0);
        _menuX0 = FriendlyLine - 500f;
        _menuTime = 0f;
        string body = won
            ? "Вайпер 1, энэ бол Газрын хяналт. Нэмэлт хүчний цуваа хөндийд хүрлээ. " +
              "Дайсны хуягт хүчин голын нөгөө эрэг рүү ухарч байна.\n\n" +
              (saved == FriendlyStart
                  ? "Взводын бүх танк амьд үлдлээ. Нэг ч багийн гишүүн эндсэнгүй."
                  : $"Взводын {FriendlyStart} танкаас {saved} нь амьд үлдлээ.") + "\n\n" +
              "Шугам таны ачаар тэслээ. Бааздаа буц. Өнөө орой бидний даалгавар."
            : "Манай сүүлчийн танк чимээгүй боллоо. Дайсны хуягт хүчин нээгдсэн цоорхойгоор " +
              "нисэх буудал руу давшиж байна.\n\n" +
              "\"Вайпер 1, энэ бол Командлал. Бид хөндийг алдлаа. Тулалдаанаас гарч бааздаа буц. " +
              "Бид эргэж ирнэ.\"";
        var ending = new Cutscene
        {
            Header = won ? "ДААЛГАВАР БИЕЛЛЭЭ" : "ДААЛГАВАР БҮТЭЛГҮЙТЛЭЭ",
            Title = won ? "ШУГАМ ТЭСЛЭЭ" : "БАЙРЛАЛ ЭЗЛЭГДЛЭЭ",
            TitleColor = won ? new Color(0.35f, 1.0f, 0.45f) : new Color(1.0f, 0.3f, 0.2f),
            Info = "\"ТӨМӨР БАМБАЙ\" АЖИЛЛАГАА",
            Body = body,
            ContinueText = "ДҮГНЭЛТ",
            OnDone = () =>
            {
                _menuLayer.QueueFree();
                OpenOverlay(new Debrief
                {
                    Main = this,
                    Won = won,
                    OnFlyAgain = () => GetTree().ReloadCurrentScene(),
                    OnMenu = BackToMenu,
                });
            },
        };
        OpenOverlay(ending);
        if (won)
            SpawnConvoy();

        // The recording says every tank made it, so only play it on a perfect run.
        if (won && saved == FriendlyStart && VictoryVoice != null)
        {
            var voice = new AudioStreamPlayer { Stream = VictoryVoice };
            ending.AddChild(voice);
            voice.Play();
        }
    }

    void EndMission(GameState outcome)
    {
        State = outcome;
        StateTime = 0f;
    }

    void OpenOverlay(Control c)
    {
        InMenu = true;
        _hud.Visible = false;
        _menuLayer = new CanvasLayer { Layer = 10 };
        AddChild(_menuLayer);
        _menuLayer.AddChild(c);
        UpdateMenuCamera(0f);
    }

    void UpdateMenuCamera(float dt)
    {
        _menuTime += dt;
        // Briefing: cut to the player's jet when the voice names it.
        if (_jetShotAt >= 0f && _menuTime >= _jetShotAt)
        {
            _jetShotAt = -1f;
            Jet.SaveStart();
            _flyby = true;
            _briefShot = true;
        }
        if (_convoy.Count > 0)
        {
            UpdateConvoyCamera(dt);
            return;
        }
        if (_flyby)
        {
            UpdateFlybyCamera(dt);
            return;
        }
        float x = _menuX0 + _menuTime * 25f;
        float gh = Terrain.Height(x);
        Camera.Position = new Vector3(x, gh + 90f, 260f);
        Camera.LookAt(new Vector3(x + 120f, gh + 40f, 0f), Vector3.Up);
    }

    /// Victory ending: tanks and trucks roll in from the west and pull up in a
    /// line just behind the friendly platoon.
    void SpawnConvoy()
    {
        var scenes = new[] { FriendlyTankScene, FriendlyTankScene, TruckScene, FriendlyTankScene, TruckScene, FriendlyTankScene };
        const float gap = 28f;
        // The lead vehicle stops a little behind the westernmost friendly tank.
        float firstStop = Units.Where(u => u.Team == 0).Select(u => u.X).DefaultIfEmpty(FriendlyLine).Min() - 35f;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i] == null)
                continue;
            var u = scenes[i].Instantiate<Unit>();
            u.Team = 0;   // the truck scene is an enemy truck by default; this makes it face east
            _unitsNode.AddChild(u);
            float stop = firstStop - i * gap;
            u.Init(this, stop - 260f, i % 2 == 0 ? -6f : 3f);
            _convoy.Add(u);
            _convoyStop.Add(stop);
        }
    }

    /// Rides alongside the column, just behind its lead vehicle, from the
    /// player's side of the lane. The column slows to a stop at its places.
    void UpdateConvoyCamera(float dt)
    {
        const float speed = 12f;
        for (int i = 0; i < _convoy.Count; i++)
        {
            float left = _convoyStop[i] - _convoy[i].X;
            float s = speed * Mathf.Clamp(left / 30f, 0f, 1f);
            if (s > 0.05f)
                _convoy[i].Drive(dt, s);
        }
        float x = _convoy[0].X - 45f;
        Camera.Position = new Vector3(x, Terrain.Height(x) + 14f, 70f);
        Camera.LookAt(new Vector3(x + 35f, Terrain.Height(x + 35f) + 2f, 0f), Vector3.Up);
    }

    /// Close behind the afterburners. The camera sits a little to the jet's
    /// left and looks straight ahead, so the jet fills the right half of the
    /// screen, clear of the menu on the left.
    void UpdateFlybyCamera(float dt)
    {
        // A jet waiting on the runway (Realistic briefing) stays parked.
        if (!Jet.OnGround)
            Jet.Flyby(dt, _menuTime);
        var p = new Vector3(Jet.Pos.X, Jet.Pos.Y, 0f);
        var sway = new Vector3(0f, Mathf.Sin(_menuTime * 1.3f) * 0.6f, Mathf.Sin(_menuTime * 0.8f) * 1.2f);
        var shake = new Vector3(Fx.Rand(-1, 1), Fx.Rand(-1, 1), 0) * 0.06f;
        if (_briefShot)
        {
            UpdateBriefShot(p, sway + shake);
            return;
        }
        Camera.Position = p + new Vector3(-24f, 3.5f, -10f) + sway + shake;
        Camera.LookAt(Camera.Position + new Vector3(100f, -4f, 0f), Vector3.Up);
    }

    /// Briefing shot of the player's jet: ahead of it and off to the side,
    /// looking back at the nose from a little above the wing. The aim point
    /// is shifted left so the jet sits in the right half of the screen, clear
    /// of the briefing text on the left.
    void UpdateBriefShot(Vector3 jet, Vector3 wobble)
    {
        const float angle = 0.7f;      // radians around from the side (0 = straight side-on)
        const float dist = 34f;
        float yaw = angle + Mathf.Sin(_menuTime * 0.25f) * 0.05f;   // slow drift around the jet
        var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        Camera.Position = jet + dir * dist + new Vector3(0f, 4f, 0f) + wobble;
        // The camera's right; aiming left of the jet pushes it to the right on screen.
        var right = new Vector3(dir.Z, 0f, -dir.X);
        Camera.LookAt(jet - right * 9f + new Vector3(0f, 1f, 0f), Vector3.Up);
    }

    void BackToMenu()
    {
        Settings.SkipMenu = false;
        GetTree().ReloadCurrentScene();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (InMenu)
        {
            UpdateMenuCamera(dt);
            return;
        }
        // A few seconds of the battlefield after the last shot, then the ending.
        if ((State == GameState.Victory || State == GameState.Overrun) && !_endingShown && StateTime > 3f)
        {
            ShowEnding();
            return;
        }
        if (Input.IsActionJustPressed("main_menu") && (Paused || State != GameState.Play))
        {
            BackToMenu();
            return;
        }
        if (Input.IsActionJustPressed("restart"))
        {
            GetTree().ReloadCurrentScene();
            return;
        }
        if (Input.IsActionJustPressed("pause"))
            Paused = !Paused;
        if (Input.IsActionJustPressed("help"))
            ShowHelp = !ShowHelp;
        if (Input.IsActionPressed("zoom_in"))
            _zoom = Mathf.Clamp(_zoom * (1f - dt), 0.3f, 3f);
        if (Input.IsActionPressed("zoom_out"))
            _zoom = Mathf.Clamp(_zoom * (1f + dt), 0.3f, 3f);
        if (!Paused)
        {
            foreach (var m in Messages)
                m.Time -= dt;
            Messages.RemoveAll(m => m.Time <= 0f);
        }
        UpdateCamera(dt);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Paused || InMenu)
            return;
        float dt = (float)delta;
        StateTime += dt;
        Jet.Sim(dt);
        foreach (var u in Units)
            u.Sim(dt);
        UpdateShells(dt);
        foreach (var m in Missiles)
            m.Sim(dt);
        Missiles.RemoveAll(m => m.Dead);
        UpdateWaves(dt);
        _debris.RemoveAll(d => !IsInstanceValid(d));
    }

    // ---------------------------------------------------------------- setup
    /// Drops every child of the "GroundProps" node onto the terrain, so you can
    /// place props in the editor by x/z alone.
    void PlaceGroundProps()
    {
        if (FindChild("GroundProps", true, false) is not Node3D props)
            return;
        foreach (var c in props.GetChildren().OfType<Node3D>())
        {
            var p = c.GlobalPosition;
            c.GlobalPosition = new Vector3(p.X, Terrain.Height(p.X), p.Z);
        }
    }

    /// Green smoke marks the friendly line so you know where your side is.
    void AddFriendlySmoke()
    {
        var g = Fx.Ramp(new[] { 0f, 0.1f, 1f }, new[] {
            new Color(0.3f, 0.9f, 0.35f, 0f), new Color(0.3f, 0.85f, 0.35f, 0.65f), new Color(0.6f, 0.9f, 0.6f, 0f) });
        var smoke = MakeParticles(SmokeQuad, 60, 9f, new Vector2(2, 5), new Vector2(6, 18), g, new Vector3(2.5f, 2.5f, 0), 0.3f, false, 15f);
        smoke.Position = new Vector3(FriendlyLine + 80f, Terrain.Height(FriendlyLine + 80f) + 1f, -8f);
        AddChild(smoke);
    }

    // ---------------------------------------------------------------- spawning

    Unit SpawnUnit(PackedScene scene, float x, float z)
    {
        var u = scene.Instantiate<Unit>();
        _unitsNode.AddChild(u);
        u.Init(this, x, z);
        Units.Add(u);
        return u;
    }

    void StartWave()
    {
        Wave++;
        WaveActive = true;
        // Clear out old wrecks.
        foreach (var u in Units.Where(u => !u.Alive && u.WreckT > 15f).ToList())
        {
            Units.Remove(u);
            u.QueueFree();
        }
        var comp = new Dictionary<UnitKind, int>
        {
            [UnitKind.Tank] = 2 + Wave,
            [UnitKind.Truck] = 2 + Wave,
            [UnitKind.Aaa] = Settings.Difficulty == Difficulty.Easy ? Wave / 2 : 1 + Wave / 2,
            [UnitKind.Sam] = Settings.Difficulty == Difficulty.Easy ? Wave / 3 : Wave / 2,
        };
        var order = new List<UnitKind>();
        EnemiesSpawned += comp.Values.Sum();
        foreach (var (k, n) in comp)
            for (int i = 0; i < n; i++)
                order.Add(k);
        // Shuffle so each wave's column is in a different order.
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = GD.RandRange(0, i);
            (order[i], order[j]) = (order[j], order[i]);
        }
        float x = FriendlyLine + 4400f;
        foreach (var k in order)
        {
            var scene = k switch
            {
                UnitKind.Tank => TankScene,
                UnitKind.Truck => TruckScene,
                UnitKind.Aaa => AaaScene,
                _ => SamScene,
            };
            var u = SpawnUnit(scene, x, Fx.Rand(-14f, 10f));
            u.StopX = FriendlyLine + k switch
            {
                UnitKind.Tank => Fx.Rand(900f, 1500f),
                UnitKind.Truck => Fx.Rand(1800f, 2400f),
                UnitKind.Aaa => Fx.Rand(1500f, 2600f),
                _ => Fx.Rand(3000f, 3800f),   // SAMs stay far back
            };
            x += Fx.Rand(70f, 160f);
        }
        Msg($"ДОВТОЛГОО {Wave}: {comp[UnitKind.Tank]} ТАНК, {comp[UnitKind.Truck]} АЧААНЫ МАШИН, {comp[UnitKind.Aaa]} ЗЕНИТ, {comp[UnitKind.Sam]} ЗУП",
                new Color(1, 0.75f, 0.3f), 6f);
    }

    public void SpawnRound(Vector2 p, Vector2 v, int team, bool byPlayer, bool tracer, ShellKind kind, float dmg, float z = 0f)
    {
        var s = new Shell
        {
            Kind = kind,
            Team = team,
            ByPlayer = byPlayer,
            Pos = p,
            Prev = p,
            Vel = v,
            Z = z,
            Damage = dmg,
            DragK = kind == ShellKind.Shell ? ShellK : BulletK,
            Life = kind == ShellKind.Aaa ? 3.2f : 4f,
        };
        if (tracer)
        {
            var mi = new MeshInstance3D
            {
                Mesh = _tracerMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = kind switch
                {
                    ShellKind.Aaa => _enemyTracerMat,
                    ShellKind.Shell => _shellTracerMat,
                    _ => _tracerMat,
                },
                Position = new Vector3(p.X, p.Y, z),
                Rotation = new Vector3(0, 0, v.Angle()),
            };
            AddChild(mi);
            s.Node = mi;
        }
        _shells.Add(s);
    }

    public void SpawnBomb(Vector2 p, Vector2 v, Unit laserTarget = null)
    {
        var s = new Shell
        {
            Kind = ShellKind.Bomb,
            Team = 0,
            ByPlayer = true,
            Pos = p,
            Prev = p,
            Vel = v,
            DragK = BombK,
            Life = 90f,
            Damage = 520f,
            Blast = 22f,
            ArmTime = 1f,   // fuze arms after 1 s of fall
            Target = laserTarget,
            Node = BombModel.Instantiate<Node3D>(),
        };
        Audio.Play("release", -4f, Fx.Rand(0.95f, 1.05f));
        AddChild(s.Node);
        s.Node.Position = new Vector3(p.X, p.Y, 0);
        _shells.Add(s);
    }

    public void SpawnFlare(Vector2 p, Vector2 v)
    {
        var s = new Shell { Kind = ShellKind.Flare, Pos = p, Prev = p, Vel = v, DragK = FlareK, Life = 3.5f };
        Audio.Play("flare", -10f, Fx.Rand(0.9f, 1.1f));
        var n = new Node3D();
        n.AddChild(Fx.Sphere(0.6f, _flareMat));
        n.AddChild(MakeParticles(FireQuad, 40, 0.6f, new Vector2(0, 1), new Vector2(1.5f, 3f), FireRamp, Vector3.Zero, 0f, false, 180f));
        n.Position = new Vector3(p.X, p.Y, 0);
        AddChild(n);
        s.Node = n;
        _shells.Add(s);
        foreach (var m in Missiles)
            m.ConsiderFlare(s);
    }

    public void SpawnMaverick(Vector2 p, Vector2 v, Unit target)
    {
        var m = MaverickScene.Instantiate<Missile>();
        AddChild(m);
        m.Init(this, p, v, target);
        Missiles.Add(m);
        Audio.Play("launch", -5f, 1.2f);
    }

    public void SpawnSam(Vector2 p, Vector2 v, ITarget target, float z)
    {
        var m = SamMissileScene.Instantiate<Missile>();
        AddChild(m);
        m.Init(this, p, v, target, z);
        Missiles.Add(m);
        Msg("ЗУП ХӨӨРЛӨӨ!", new Color(1, 0.3f, 0.2f), 3f);
        var p3 = new Vector3(p.X, p.Y, z);
        Audio.PlayAt("launch", p3, 2f, 1f, Audio.SoundDelay(p3));
        MuzzleFlash(p3, 1.6f);
    }

    // ---------------------------------------------------------------- simulation


    static bool CanHit(Shell s, Unit u)
    {
        if (s.ByPlayer)
            return true;   // the player's rounds hit everything: friendly fire is possible
        return s.Team == 1 ? u.Team == 0 : u.Team == 1;
    }

    /// Distance from point p to the segment a-b. Fast rounds move ~17 m per
    /// physics tick, so we test the whole path, not just the end point.
    static float SegDist(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float l2 = ab.LengthSquared();
        if (l2 < 0.0001f)
            return a.DistanceTo(p);
        float t = Mathf.Clamp((p - a).Dot(ab) / l2, 0f, 1f);
        return (a + ab * t).DistanceTo(p);
    }

    /// GBU-12 style guidance: steers the bomb's flight path at the laser spot
    /// with its fins, bleeding speed as it turns. Fin force grows with airspeed
    /// and air density (~2.5 g at 200 m/s at sea level), so a slow bomb can't
    /// stretch its glide much. If the jet stops lasing that target the bomb goes
    /// ballistic for good.
    void GuideBomb(Shell s, float dt)
    {
        var t = s.Target;
        if (!Jet.Lasing || Jet.LockTarget != t)
        {
            s.Target = null;
            if (t != null && IsInstanceValid(t) && t.Alive)
                Msg("ЛАЗЕР ТАСАРЛАА - БОМБ ЧӨЛӨӨТ УНАЛТТАЙ", new Color(1, 0.75f, 0.2f), 2f);
            return;
        }
        if (s.Age < 0.6f)   // clear of the jet before the fins move
            return;
        var los = t.CPos - s.Pos;
        float sp = s.Vel.Length();
        float rho = Mathf.Exp(-Mathf.Max(s.Pos.Y, 0f) / 8500f);
        float maxAcc = Mathf.Min(25f * (sp / 200f) * (sp / 200f) * rho, 40f);
        var dir = s.Vel / Mathf.Max(sp, 1f);
        var left = new Vector2(-dir.Y, dir.X);   // 90 degrees anticlockwise from the flight path
        float err = Mathf.Wrap(los.Angle() - dir.Angle(), -Mathf.Pi, Mathf.Pi);
        // Turn towards the line of sight, plus cancel gravity's pull across the flight path
        // so the bomb flies the line instead of sagging under it.
        float acc = Mathf.Clamp(err * sp * 2.5f + 9.81f * dir.X, -maxAcc, maxAcc);
        s.Vel += left * acc * dt;
        s.Vel -= dir * Mathf.Abs(acc) * 0.08f * dt;   // induced drag
        s.Z = Mathf.MoveToward(s.Z, t.LaneZ, dt * 15f);
    }

    /// Seconds until the first laser-guided bomb in flight reaches its target, or -1 if none.
    public float LgbTimeToImpact()
    {
        float best = -1f;
        foreach (var s in _shells)
        {
            if (s.Dead || s.Kind != ShellKind.Bomb || s.Target == null)
                continue;
            float t = s.Pos.DistanceTo(s.Target.CPos) / Mathf.Max(s.Vel.Length(), 1f);
            if (best < 0f || t < best)
                best = t;
        }
        return best;
    }

    void UpdateShells(float dt)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            var s = _shells[i];
            if (s.Dead)
                continue;
            if (s.Kind == ShellKind.Bomb && s.Target != null)
                GuideBomb(s, dt);
            s.Step(dt);
            if (s.Kind == ShellKind.Flare)
            {
                if (s.Age > s.Life || s.Pos.Y < Terrain.Height(s.Pos.X))
                    s.Kill();
                continue;
            }
            float reach = Mathf.Abs(s.Vel.X) * dt + 12f;
            Unit hit = null;
            foreach (var u in Units)
            {
                if (!u.Alive || Mathf.Abs(u.CPos.X - s.Pos.X) > reach || !CanHit(s, u))
                    continue;
                if (SegDist(s.Prev, s.Pos, u.CPos) < u.Radius)
                {
                    hit = u;
                    break;
                }
            }
            if (hit != null)
            {
                ShellHitUnit(s, hit);
                continue;
            }
            if (s.Team == 1 && Jet.Alive && Mathf.Abs(Jet.Pos.X - s.Pos.X) < reach + 10f
                    && SegDist(s.Prev, s.Pos, Jet.Pos) < 7.5f)
            {
                Jet.TakeDamage(s.Damage);
                s.Kill();
                continue;
            }
            float gh = Terrain.Height(s.Pos.X);
            if (s.Pos.Y <= gh)
            {
                ShellGround(s, new Vector2(s.Pos.X, gh));
                continue;
            }
            if (s.Age > s.Life)
            {
                if (s.Kind == ShellKind.Aaa)   // self-destruct puff
                {
                    var f = MakeParticles(SmokeQuad, 6, 2f, new Vector2(1, 3), new Vector2(3, 6), _flakRamp, Vector3.Zero, 1f);
                    FireOnce(f, new Vector3(s.Pos.X, s.Pos.Y, s.Z), 3f);
                }
                s.Kill();
            }
        }
        _shells.RemoveAll(s => s.Dead);
    }

    void ShellHitUnit(Shell s, Unit u)
    {
        var p = s.Pos;
        switch (s.Kind)
        {
            case ShellKind.Bullet:
            case ShellKind.Aaa:
                u.TakeDamage(s.Damage * u.GunArmor, s.ByPlayer, s.Vel.Normalized());
                _impactCount++;
                if (_impactCount % 2 == 0)
                    Sparks(new Vector3(p.X, p.Y, u.LaneZ), 6);
                break;
            case ShellKind.Shell:
                u.TakeDamage(s.Damage, s.ByPlayer, s.Vel.Normalized());
                SpawnExplosion(new Vector3(p.X, p.Y, u.LaneZ), 0.35f);
                break;
            case ShellKind.Bomb:
                if (s.Age >= s.ArmTime)
                {
                    Explode(p, u.LaneZ, s.Blast, s.Damage, true, 1f);
                    Scorch(p.X, s.Blast * 0.5f);
                }
                else
                {
                    u.TakeDamage(60f, true, s.Vel.Normalized());
                    Msg("ДЭЛБЭРСЭНГҮЙ - бомб идэвхжээгүй (илүү өндрөөс хая)", new Color(1, 0.75f, 0.2f), 2.5f);
                }
                break;
        }
        s.Kill();
    }

    void ShellGround(Shell s, Vector2 p)
    {
        switch (s.Kind)
        {
            case ShellKind.Bullet:
            case ShellKind.Aaa:
                _impactCount++;
                if (_impactCount % 3 == 0)
                {
                    var d = MakeParticles(SmokeQuad, 5, 0.9f, new Vector2(4, 12), new Vector2(1f, 2.6f), _dustRamp, new Vector3(0, -6, 0), 2f, true, 30f);
                    FireOnce(d, new Vector3(p.X, p.Y, s.Z), 1.5f);
                }
                break;
            case ShellKind.Shell:
                Explode(p, s.Z, 5f, 30f, s.ByPlayer, 0.35f, false);
                break;
            case ShellKind.Bomb:
                if (s.Age >= s.ArmTime)
                {
                    Explode(p, s.Z, s.Blast, s.Damage, true, 1f);
                    Scorch(p.X, s.Blast * 0.5f);
                }
                else
                {
                    var d = MakeParticles(SmokeQuad, 20, 2f, new Vector2(4, 12), new Vector2(3, 6), _dustRamp, new Vector3(0, -4, 0), 1f);
                    FireOnce(d, new Vector3(p.X, p.Y, 0), 3f);
                    Msg("ДЭЛБЭРСЭНГҮЙ - бомб идэвхжээгүй (илүү өндрөөс хая)", new Color(1, 0.75f, 0.2f), 2.5f);
                }
                break;
        }
        s.Kill();
    }

    /// Blast damage with falloff, frag damage to a jet that is too close, and a push on loose debris.
    public void Explode(Vector2 p, float z, float radius, float damage, bool byPlayer, float size, bool hurtJet = true)
    {
        SpawnExplosion(new Vector3(p.X, p.Y, z), size);
        foreach (var u in Units)
        {
            if (!u.Alive)
                continue;
            float d = u.CPos.DistanceTo(p);
            float dmg = 0f;
            if (d < radius)
                dmg = damage * (1f - 0.5f * d / radius);
            else if (d < radius * 2.5f)
                dmg = damage * 0.5f * Mathf.Pow(1f - (d - radius) / (radius * 1.5f), 2f);
            if (dmg > 0f)
                u.TakeDamage(dmg, byPlayer, (u.CPos - p).Normalized());
        }
        if (hurtJet && Jet.Alive)
        {
            float frag = radius * 7f * size;
            float dj = Jet.Pos.DistanceTo(p);
            if (dj < frag)
                Jet.TakeDamage(45f * size * Mathf.Pow(1f - dj / frag, 2f));
        }
        var p3 = new Vector3(p.X, p.Y, z);
        foreach (var rb in _debris)
        {
            if (!IsInstanceValid(rb))
                continue;
            var off = rb.GlobalPosition - p3;
            float d = off.Length();
            if (d < radius * 3f && d > 0.01f)
                rb.ApplyCentralImpulse(off / d * rb.Mass * 25f * size * (1f - d / (radius * 3f)));
        }
    }

    // ---------------------------------------------------------------- effects

    public CpuParticles3D MakeParticles(Mesh quad, int amount, float life, Vector2 vel, Vector2 scl, Gradient ramp,
            Vector3 grav, float damp = 0f, bool oneShot = true, float spread = 180f, Vector3? dir = null, float radius = 0f)
    {
        var p = new CpuParticles3D
        {
            Mesh = quad,
            Amount = amount,
            Lifetime = life,
            OneShot = oneShot,
            Explosiveness = oneShot ? 0.95f : 0f,
            Direction = dir ?? Vector3.Up,
            Spread = spread,
            InitialVelocityMin = vel.X,
            InitialVelocityMax = vel.Y,
            ScaleAmountMin = scl.X,
            ScaleAmountMax = scl.Y,
            ScaleAmountCurve = _growCurve,
            ColorRamp = ramp,
            Gravity = grav,
            DampingMin = damp,
            DampingMax = damp,
            AngleMin = 0f,
            AngleMax = 360f,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (radius > 0f)
        {
            p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
            p.EmissionSphereRadius = radius;
        }
        return p;
    }

    /// Adds a one-shot emitter at `at`, starts it and frees it later.
    public void FireOnce(CpuParticles3D p, Vector3 at, float freeAfter)
    {
        p.Position = at;
        AddChild(p);
        p.Emitting = true;
        FreeLater(p, freeAfter);
    }

    void FreeLater(Node n, float seconds)
    {
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (IsInstanceValid(n))
                n.QueueFree();
        };
    }

    public void SpawnExplosion(Vector3 p, float size)
    {
        bool nearGround = p.Y - Terrain.Height(p.X) < 6f;
        string snd = size >= 0.9f ? "boom" : "boom_small";
        float pitch = Fx.Rand(0.85f, 1.1f) * (size < 0.5f ? 1.25f : 1f);
        Audio.PlayAt(snd, p, Mathf.LinearToDb(Mathf.Clamp(size, 0.3f, 2.5f)) + 2f, pitch, Audio.SoundDelay(p));
        FireOnce(MakeParticles(FireQuad, (int)(18 + 14 * size), 0.9f, new Vector2(6, 22) * size, new Vector2(5, 11) * size,
                FireRamp, new Vector3(0, 4, 0), 4f, true, 180f, Vector3.Up, 1.5f * size), p, 2f);
        FireOnce(MakeParticles(SmokeQuad, (int)(10 + 10 * size), 4.5f, new Vector2(3, 11) * size, new Vector2(9, 18) * size,
                SmokeRamp, new Vector3(1.5f, 3.5f, 0), 1.5f, true, 120f, Vector3.Up, 2f * size), p, 6f);
        FireOnce(MakeParticles(_sparkQuad, (int)(20 * size) + 6, 1.6f, new Vector2(20, 55) * size, new Vector2(0.6f, 1.4f),
                _sparkRamp, new Vector3(0, -9.8f, 0), 0.5f), p, 2.5f);
        if (nearGround)
            FireOnce(MakeParticles(SmokeQuad, (int)(14 * size) + 4, 2.5f, new Vector2(10, 30) * size, new Vector2(4, 9) * size,
                    _dustRamp, new Vector3(0, -2, 0), 2.5f, true, 80f, Vector3.Up), p, 3.5f);
        var light = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.65f, 0.3f),
            OmniRange = 40f * size,
            LightEnergy = 8f,
            Position = p + new Vector3(0, 3, 0),
        };
        AddChild(light);
        var tw = CreateTween();
        tw.TweenProperty(light, "light_energy", 0f, 0.5);
        tw.TweenCallback(Callable.From(light.QueueFree));
        if (size >= 0.6f)
            SpawnDebris(p, (int)(5 * size), 14f * size);
        float camD = Camera != null ? new Vector2(Camera.Position.X, Camera.Position.Y).DistanceTo(new Vector2(p.X, p.Y)) : 1e9f;
        Shake = Mathf.Max(Shake, size * Mathf.Clamp(1.2f - camD / 1800f, 0f, 1f));
    }

    void Sparks(Vector3 p, int n) =>
        FireOnce(MakeParticles(_sparkQuad, n, 0.4f, new Vector2(8, 25), new Vector2(0.6f, 1.2f), _sparkRamp, new Vector3(0, -9.8f, 0)), p, 1f);

    public void MuzzleFlash(Vector3 p, float size)
    {
        FireOnce(MakeParticles(FireQuad, 8, 0.25f, new Vector2(4, 10) * size, new Vector2(1.5f, 3f) * size, FireRamp,
                Vector3.Zero, 3f), p, 1f);
        FireOnce(MakeParticles(SmokeQuad, 6, 1.6f, new Vector2(2, 6) * size, new Vector2(2.5f, 5f) * size, _dustRamp,
                new Vector3(0, 1, 0), 2f), p, 2.5f);
    }

    /// Physics-driven chunks that bounce off the terrain collision band.
    public void SpawnDebris(Vector3 p, int count, float speed, Vector3 inherit = default)
    {
        for (int i = 0; i < count; i++)
        {
            var rb = new RigidBody3D
            {
                Mass = Fx.Rand(10f, 40f),
                Position = p + new Vector3(Fx.Rand(-1.5f, 1.5f), Fx.Rand(0.5f, 2f), Fx.Rand(-1.5f, 1.5f)),
            };
            rb.AddChild(new MeshInstance3D
            {
                Mesh = _debrisMesh,
                MaterialOverride = _debrisMat,
                Scale = new Vector3(Fx.Rand(0.6f, 2f), Fx.Rand(0.5f, 1.5f), Fx.Rand(0.6f, 1.8f)),
            });
            rb.AddChild(new CollisionShape3D { Shape = _debrisShape });
            AddChild(rb);
            var d = new Vector3(Fx.Rand(-1, 1), Fx.Rand(0.4f, 1.2f), Fx.Rand(-0.6f, 0.6f)).Normalized();
            rb.LinearVelocity = inherit + d * speed * Fx.Rand(0.5f, 1.3f);
            rb.AngularVelocity = new Vector3(Fx.Rand(-8, 8), Fx.Rand(-8, 8), Fx.Rand(-8, 8));
            if (i % 3 == 0)
            {
                var t = MakeParticles(SmokeQuad, 24, 1.4f, new Vector2(0, 1), new Vector2(1.5f, 3.5f), SmokeRamp, new Vector3(0, 2, 0), 1f, false);
                rb.AddChild(t);
                GetTree().CreateTimer(Fx.Rand(1.5f, 3f)).Timeout += () =>
                {
                    if (IsInstanceValid(t))
                        t.Emitting = false;
                };
            }
            TrackDebris(rb, Fx.Rand(6f, 10f));
        }
    }

    void TrackDebris(RigidBody3D rb, float life)
    {
        _debris.Add(rb);
        FreeLater(rb, life);
        while (_debris.Count > MaxDebris)
        {
            var old = _debris[0];
            _debris.RemoveAt(0);
            if (IsInstanceValid(old))
                old.QueueFree();
        }
    }

    /// Turns a node (e.g. a turret) into a burning rigid body flung into the air.
    public void TossPart(Node3D node, Vector3 v, Vector3 extents, float m)
    {
        var rb = new RigidBody3D();
        var gt = node.GlobalTransform;
        AddChild(rb);
        rb.GlobalTransform = gt;
        node.Reparent(rb);
        rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = extents } });
        rb.Mass = m;
        rb.LinearVelocity = v;
        rb.AngularVelocity = new Vector3(Fx.Rand(-4, 4), Fx.Rand(-4, 4), Fx.Rand(-4, 4));
        var fire = MakeWreckFire();
        fire.Scale = Vector3.One * 0.6f;
        rb.AddChild(fire);
        _debris.Add(rb);
        FreeLater(rb, 30f);
    }

    public Node3D MakeWreckFire()
    {
        var n = new Node3D();
        n.AddChild(MakeParticles(FireQuad, 16, 0.7f, new Vector2(1, 3), new Vector2(1.5f, 3.5f), FireRamp, new Vector3(0, 5, 0), 1f, false, 25f));
        n.AddChild(MakeParticles(SmokeQuad, 30, 6f, new Vector2(2, 5), new Vector2(4, 10), SmokeRamp, new Vector3(1.5f, 3f, 0), 0.6f, false, 15f));
        return n;
    }

    void Scorch(float x, float r)
    {
        float slope = (Terrain.Height(x + 2f) - Terrain.Height(x - 2f)) / 4f;
        var mi = new MeshInstance3D
        {
            Mesh = _scorchMesh,
            Scale = new Vector3(r, 1, r),
            Position = new Vector3(x, Terrain.Height(x), Fx.Rand(-3f, 3f)),
            Rotation = new Vector3(0, 0, Mathf.Atan(slope)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        _scorch.Add(mi);
        if (_scorch.Count > MaxScorch)
        {
            _scorch[0].QueueFree();
            _scorch.RemoveAt(0);
        }
    }

    // ---------------------------------------------------------------- game flow

    public Unit FindEnemyUnit(Unit from, float maxRange)
    {
        Unit best = null;
        float bd = maxRange;
        foreach (var u in Units)
        {
            if (u.Alive && u.Team != from.Team)
            {
                float d = Mathf.Abs(u.X - from.X);
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
        }
        return best;
    }

    public int CountTeam(int t) => Units.Count(u => u.Alive && u.Team == t);

    /// True while a guided SAM is chasing the jet (drives the HUD and audio warnings).
    public bool SamInbound() => Missiles.Any(m => !m.IsAgm && !m.Dead && m.Guided && m.Target == Jet);

    /// Ground-proximity warning: less than 4.5 s to impact at the current sink rate.
    public bool PullUpWarning()
    {
        if (Jet.OnGround)
            return false;
        float sink = -Jet.Vel.Y;
        return sink > 5f && Jet.Agl / sink < 4.5f && !(Jet.GearDown && Terrain.OnRunway(Jet.Pos.X));
    }

    public void OnUnitKilled(Unit u, bool byPlayer)
    {
        if (u.Team == 1)
        {
            Kills++;
            if (byPlayer)
            {
                PlayerKills++;
                Score += u.Value;
                Msg($"УСТЛАА: {u.DisplayName}  +{u.Value}", new Color(0.6f, 1.0f, 0.6f), 3f);
            }
            else
                Msg($"Манай танк {u.DisplayName}-г устгалаа", new Color(0.8f, 0.9f, 0.8f), 3f);
        }
        else if (byPlayer)
        {
            Score -= 400;
            FriendlyKills++;
            Msg("ӨӨРСДИЙГӨӨ БУУДЛАА! ГАЛАА ЗОГСОО!  -400", new Color(1, 0.25f, 0.2f), 4f);
        }
        else
            Msg($"Манай {u.DisplayName} устлаа", new Color(1, 0.55f, 0.3f), 3f);
    }

    /// Ends the game when the jet is lost. The jet just disappears.
    public void OnJetDestroyed(string reason)
    {
        Msg(reason, new Color(1, 0.3f, 0.2f), 8f);
        if (State != GameState.Play)
            return;   // the mission already ended
        State = GameState.Dead;
        StateTime = 0f;
    }

    void UpdateWaves(float dt)
    {
        if (State != GameState.Play)
            return;
        if (FriendlyStart > 0 && CountTeam(0) == 0)
        {
            Msg("МАНАЙ БҮХ ТАНК УСТЛАА", new Color(1, 0.3f, 0.2f), 6f);
            EndMission(GameState.Overrun);
            return;
        }
        if (!WaveActive)
        {
            WaveTimer -= dt;
            if (WaveTimer <= 0f)
                StartWave();
            return;
        }
        if (CountTeam(1) == 0)
        {
            WaveActive = false;
            WaveTimer = 25f;
            Score += 300 * Wave;
            if (Wave >= MissionWaves)
            {
                Msg($"ДОВТОЛГОО {Wave} НЯЦААГДЛАА  +{300 * Wave}  -  НЭМЭЛТ ХҮЧ ИРЛЭЭ!", new Color(0.6f, 1.0f, 0.6f), 7f);
                EndMission(GameState.Victory);
            }
            else if (Settings.AirRearm)
            {
                Jet.Rearm();
                Msg($"ДОВТОЛГОО {Wave} НЯЦААГДЛАА  +{300 * Wave}  -  зэвсэг, түлш нөхөгдөж, засварлагдлаа", new Color(0.6f, 1.0f, 0.6f), 7f);
            }
            else
                Msg($"ДОВТОЛГОО {Wave} НЯЦААГДЛАА  +{300 * Wave}  -  зэвсэглэхийн тулд зурвас дээр буу", new Color(0.6f, 1.0f, 0.6f), 7f);
        }
    }

    public void Msg(string text, Color? col = null, float t = 4f)
    {
        Messages.Insert(0, new Message { Text = text, Time = t, Color = col ?? Colors.White });
        if (Messages.Count > 7)
            Messages.RemoveAt(Messages.Count - 1);
    }

    // ---------------------------------------------------------------- frame


    /// Side-on camera: pulls back with height (only a little with speed, so the
    /// ground rushes past faster the faster you go), leads the velocity and
    /// tries to keep the ground in frame. The view widens with speed and
    /// buffets when fast and low.
    void UpdateCamera(float dt)
    {
        var p = Jet.Pos;
        var v = Jet.Alive ? Jet.Vel : Vector2.Zero;
        float spd = v.Length();
        float gh = Terrain.Height(p.X);
        float agl = Mathf.Max(p.Y - gh, 0f);
        float want = Mathf.Max(150f + spd * 0.1f, agl * 1.25f + 80f);
        want = Mathf.Clamp(want, 110f, 2400f) * _zoom;
        CamDist = Mathf.Lerp(CamDist, want, 1f - Mathf.Exp(-dt * 1.5f));
        float fovWant = _baseFov + 14f * Mathf.SmoothStep(200f, 410f, spd);
        Camera.Fov = Mathf.Lerp(Camera.Fov, fovWant, 1f - Mathf.Exp(-dt * 2f));
        float buffet = Mathf.SmoothStep(220f, 380f, spd) * (1f - Mathf.SmoothStep(60f, 300f, agl));
        if (!Paused)
            Shake = Mathf.Max(Shake, buffet * 0.18f);
        float halfH = CamDist * Mathf.Tan(Mathf.DegToRad(Camera.Fov * 0.5f));
        var lead = v * 0.45f;
        if (lead.Length() > halfH * 0.9f)
            lead = lead.Normalized() * halfH * 0.9f;
        var wantOff = lead;
        // Keep the ground in view when it's reachable.
        float low = p.Y + wantOff.Y - halfH * 0.8f;
        if (low > gh)
            wantOff.Y -= Mathf.Min(low - gh, halfH * 0.6f);
        _camOffset = _camOffset.Lerp(wantOff, 1f - Mathf.Exp(-dt * 2.5f));
        var focus = p + _camOffset;
        Shake = Mathf.Max(Shake - dt * 1.8f, 0f);
        var sh = new Vector3(Fx.Rand(-1, 1), Fx.Rand(-1, 1), 0) * Shake * CamDist * 0.012f;
        Camera.Position = new Vector3(focus.X, focus.Y + CamDist * 0.07f, CamDist) + sh;
        Camera.LookAt(new Vector3(focus.X, focus.Y, 0) + sh * 0.5f, Vector3.Up);
        // Keep the sun's shadow map centred on the action.
        if (_sun != null)
            _sun.DirectionalShadowMaxDistance = Mathf.Clamp(CamDist * 2.5f, 600f, 4000f);

        // Afterburner: a kick when it lights, then a light rumble while it burns.
        float ab = Jet.Alive && Jet.Fuel > 0f ? Mathf.Clamp((Jet.EngineSpool - 1f) / 0.25f, 0f, 1f) : 0f;
        bool abOn = ab > 0.5f;
        if (!Paused)
        {
            Shake = Mathf.Max(Shake, ab * 0.15f);
            if (abOn && !_abWasOn)
                Shake = Mathf.Max(Shake, 1.0f);
        }
        _abWasOn = abOn;
    }
}
