using Godot;

/// Everything a guided missile can chase: the jet, ground units and flares.
public interface ITarget
{
	Vector2 TargetPos { get; }
	Vector2 TargetVel { get; }
	bool IsTargetValid();
}

public enum UnitKind { Tank, Truck, Aaa, Sam, FriendlyTank }

public enum ShellKind { Bullet, Shell, Aaa, Bomb, Flare }

public enum GameState { Play, Dead, Overrun }

/// One line in the message feed at the top of the screen.
public class Message
{
	public string Text;
	public float Time;
	public Color Color;
}
