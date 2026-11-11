using Sandbox;
using Sandbox.Animation;

public sealed class LobbyPlayerAnimation : Component
{
	[Property] public GameWeaponDefinition Weapon { get; set; }

	private const string SkeletonPath = "models/soldier_rigify.skel";
	private SkinnedModelRenderer _renderer;
	private GameWeaponModel _weaponModel;
	private float _idleDuration;
	private float _idlePhase;

	protected override void OnStart()
	{
		_renderer = GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault();
		if (!_renderer.IsValid())
		{
			Log.Error("Lobby animation requires a SkinnedModelRenderer.");
			Enabled = false;
			return;
		}

		var idle = Clip.Load("models/soldier_animations/idle.anim");
		if (idle.IsError || idle.SkeletonName != SkeletonPath || idle.Duration <= 0f)
		{
			Log.Error("Lobby animation requires the Soldier idle clip and matching skeleton.");
			Enabled = false;
			return;
		}

		_idleDuration = idle.Duration;
		var graph = Graph.Load("models/animation_graphs/soldier_lobby.ag");
		if (graph.IsError)
		{
			Log.Error("Lobby animation could not load the Soldier lobby graph.");
			Enabled = false;
			return;
		}

		_renderer.UseAnimGraph = true;
		_renderer.Animation.Graph = graph;

		if (!IsProxy)
		{
			Mouse.Visibility = MouseVisibility.Visible;
			Mouse.CursorType = "default";
		}
	}

	protected override void OnUpdate()
	{
		if (!_renderer.IsValid() || !_renderer.Animation.IsValid || _idleDuration <= 0f)
		{
			return;
		}

		_idlePhase = (_idlePhase + Time.Delta / _idleDuration) % 1f;
		_renderer.Animation.Parameters.Set("idle_phase", _idlePhase);
	}

	protected override void OnDisabled()
	{
		if (_weaponModel.IsValid())
		{
			_weaponModel.GameObject.Destroy();
			_weaponModel = null;
		}

		if (!IsProxy)
		{
			Mouse.Visibility = MouseVisibility.Auto;
			Mouse.CursorType = "default";
		}
	}

	protected override void OnPreRender()
	{
		if (!_renderer.IsValid() || _idleDuration <= 0f)
		{
			return;
		}

		if (!_weaponModel.IsValid())
		{
			_weaponModel = GameWeaponModel.Create(weapon: Weapon, parent: GameObject);
			if (!_weaponModel.IsValid())
			{
				Enabled = false;
				return;
			}
		}

		if (!_weaponModel.TryAttachToHand(body: _renderer, facing: WorldRotation))
		{
			Log.Error("Lobby weapon requires the Soldier's animated DEF_hand_R bone.");
			Enabled = false;
		}
	}
}
