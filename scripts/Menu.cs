using System;
using Godot;

/// Title screen: mission briefing, difficulty choice and the controls list.
/// The layout lives in scenes/menu.tscn; this script only wires up the buttons.
/// Created by Game when the game first starts (Settings.SkipMenu is false).
/// Works with the mouse or the keyboard (arrows + Enter).
public partial class Menu : Control
{
    /// Called with the chosen difficulty.
    public Action<Difficulty> OnStart;

    public override void _Ready()
    {
        var easy = GetNode<Button>("%Easy");
        easy.Pressed += () => OnStart?.Invoke(Difficulty.Easy);
        GetNode<Button>("%Normal").Pressed += () => OnStart?.Invoke(Difficulty.Normal);
        GetNode<Button>("%Realistic").Pressed += () => OnStart?.Invoke(Difficulty.Realistic);

        var controls = GetNode<Label>("%Controls");
        GetNode<Button>("%ControlsButton").Pressed += () => controls.Visible = !controls.Visible;
        GetNode<Button>("%Quit").Pressed += () => GetTree().Quit();

        easy.CallDeferred(Control.MethodName.GrabFocus);

        var music = GetNode<AudioStreamPlayer>("%Music");
        if (music.Stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;
        music.Play();
    }
}
