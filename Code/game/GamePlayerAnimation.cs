using Sandbox;
using Sandbox.Animation;

public sealed class GamePlayerAnimation : Component
{
	private const float WalkDuration = 40f / 30f;
	private const float IdleDuration = 50f / 30f;
	private const float ShootDuration = 0.2f;
	private const string SkeletonPath = "models/soldier_rigify.skel";
	private GamePlayer _player;
	private SkinnedModelRenderer _renderer;
	private bool _graphReady;
	private float _walkPhase;
	private float _idlePhase;
	private float _shotStarted = float.NegativeInfinity;

	protected override void OnStart()
	{
		_player = GetComponent<GamePlayer>();
		_renderer = GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault();
		if (!_player.IsValid() || !_renderer.IsValid())
		{
			Log.Error("Player animation requires a GamePlayer and SkinnedModelRenderer.");
			Enabled = false;
			return;
		}

		var skeleton = Skeleton.Load(SkeletonPath);
		if (skeleton.IsError || !skeleton.MaskNames.Contains("upper_body") || !skeleton.MaskNames.Contains("lower_body"))
		{
			Log.Error("Player animation requires the Soldier skeleton and upper_body/lower_body masks.");
			Enabled = false;
			return;
		}

		var graph = Graph.Load("models/animation_graphs/soldier_player.ag");
		if (graph.IsError)
		{
			Log.Error("Player animation could not load the Soldier player graph.");
			Enabled = false;
			return;
		}

		_renderer.UseAnimGraph = true;
		_renderer.Animation.Graph = graph;
		_graphReady = true;
	}

	protected override void OnUpdate()
	{
		if (!_graphReady || !_renderer.IsValid() || _player.IsDead)
		{
			return;
		}

		var velocity = _player.MovementVelocity.WithZ(0f);
		if (_player.IsStunned || _player.IsRoundOver || velocity.LengthSquared <= 1f)
		{
			velocity = Vector3.Zero;
		}

		var facing = _player.FacingDirection.WithZ(0f).Normal;
		var right = Vector3.Cross(facing, Vector3.Up);
		var direction = velocity.Normal;
		var moving = velocity.LengthSquared > 1f;
		if (moving)
		{
			_walkPhase = (_walkPhase + Time.Delta / WalkDuration) % 1f;
		}

		_idlePhase = (_idlePhase + Time.Delta / IdleDuration) % 1f;
		var elapsed = Time.Now - _shotStarted;
		var shooting = elapsed >= 0f && elapsed < ShootDuration && !_player.IsRoundOver;
		var weight = 0f;
		if (shooting)
		{
			var fadeIn = (elapsed / 0.025f).Clamp(0f, 1f);
			var fadeOut = ((ShootDuration - elapsed) / 0.05f).Clamp(0f, 1f);
			weight = System.MathF.Min(fadeIn, fadeOut);
		}

		var parameters = _renderer.Animation.Parameters;
		parameters.Set("walk_phase", _walkPhase);
		parameters.Set("idle_phase", _idlePhase);
		parameters.Set("move_forward", moving ? Vector3.Dot(direction, facing) : 0f);
		parameters.Set("move_right", moving ? Vector3.Dot(direction, right) : 0f);
		parameters.Set("shoot_phase", (elapsed / ShootDuration).Clamp(0f, 1f));
		parameters.Set("shoot_weight", weight);
	}

	public void PlayShot()
	{
		if (!_player.IsValid() || _player.IsDead || _player.IsRoundOver)
		{
			return;
		}

		_shotStarted = Time.Now;
	}
}
