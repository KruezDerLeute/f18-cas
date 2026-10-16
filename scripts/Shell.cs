using Godot;

/// Ballistic projectile: cannon rounds, tank shells, AAA, bombs and flares.
/// Simulated in the 2D gameplay plane; `Node` is an optional 3D visual.
/// A plain C# class, not a Node: hundreds are alive at once while firing.
public class Shell : ITarget
{
	public ShellKind Kind = ShellKind.Bullet;
	public int Team;
	public bool ByPlayer;
	public Vector2 Pos;
	public Vector2 Prev;
	public Vector2 Vel;
	public float Z;
	public float DragK;
	public float Life = 3f;
	public float Age;
	public float Damage;
	public float Blast;
	public float ArmTime;
	public bool Dead;
	public Node3D Node;

	public void Step(float dt)
	{
		Prev = Pos;
		float rho = Mathf.Exp(-Mathf.Max(Pos.Y, 0f) / 8500f);
		float sp = Vel.Length();
		Vel += (new Vector2(0, -9.81f) - Vel * sp * DragK * rho) * dt;
		Pos += Vel * dt;
		Age += dt;
		if (Node != null)
		{
			Node.Position = new Vector3(Pos.X, Pos.Y, Z);
			Node.SetRotZ(Vel.Angle());
		}
	}

	public void Kill()
	{
		Dead = true;
		if (Node != null)
		{
			Node.QueueFree();
			Node = null;
		}
	}

	// Flares can be chased by missiles.
	public Vector2 TargetPos => Pos;
	public Vector2 TargetVel => Vel;
	public bool IsTargetValid() => !Dead;
}
