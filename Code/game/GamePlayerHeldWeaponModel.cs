using Sandbox;

public sealed class GamePlayerHeldWeaponModel : Component
{
	private GamePlayer _player;
	private SkinnedModelRenderer _bodyRenderer;
	private bool _missingHandLogged;
	private GameWeaponDefinition _weapon;
	private GameObject _model;
	private GameWeaponModel _weaponModel;
	private LineRenderer _laser;

	protected override void OnStart()
	{
		_player = GetComponent<GamePlayer>();
		_bodyRenderer = GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault();
	}

	protected override void OnPreRender()
	{
		UpdateModel();
		if (!_player.IsValid() || _player.IsProxy || _player.IsDead || _player.IsRoundOver
			|| _player.EquippedWeapon == null || !_player.TryGetShotTransform(out var position, out var forward))
		{
			if (_laser.IsValid())
			{
				_laser.Enabled = false;
			}

			return;
		}

		if (!_laser.IsValid())
		{
			_laser = GameObject.AddComponent<LineRenderer>();
			_laser.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
			_laser.UseVectorPoints = true;
			_laser.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
			_laser.Color = Color.Red;
			_laser.Opaque = false;
			_laser.Additive = true;
			_laser.CastShadows = false;
			_laser.Lighting = false;
		}

		_laser.Enabled = true;
		_laser.Width = _player.LaserWidth;
		_laser.VectorPoints[0] = position;
		_laser.VectorPoints[1] = position + forward * _player.EffectiveAttackRange;
	}

	private bool UpdateModel()
	{
		if (!_player.IsValid())
		{
			return false;
		}

		if (_weapon != _player.EquippedWeapon)
		{
			_weapon = _player.EquippedWeapon;
			if (_model.IsValid())
			{
				_model.Destroy();
			}

			_weaponModel = null;
			if (_weapon != null)
			{
				CreateModel();
			}
		}

		if (!_model.IsValid())
		{
			return false;
		}

		if (_player.IsDead)
		{
			_model.Enabled = false;
			return false;
		}

		var forward = _player.FacingDirection.WithZ(0f);
		if (forward.LengthSquared <= 0.001f)
		{
			forward = Vector3.Forward;
		}

		var facing = Rotation.LookAt(forward, Vector3.Up);
		if (!_weaponModel.TryAttachToHand(body: _bodyRenderer, facing: facing))
		{
			_model.WorldPosition = _player.WorldPosition
				+ Vector3.Up * GameWeaponProjectile.CollisionHeight
				+ forward.Normal * 12f;
			_model.WorldRotation = facing;
			if (!_missingHandLogged)
			{
				Log.Error("Player weapon could not find the animated DEF_hand_R bone; using the fixed weapon position.");
				_missingHandLogged = true;
			}
		}

		_model.Enabled = true;
		return true;
	}

	private void CreateModel()
	{
		_weaponModel = GameWeaponModel.Create(weapon: _weapon, parent: GameObject);
		if (_weaponModel.IsValid())
		{
			_model = _weaponModel.GameObject;
		}
	}

	public bool TryGetMuzzlePosition(out Vector3 position)
	{
		position = default;
		if (!UpdateModel())
		{
			return false;
		}

		position = _weaponModel.Muzzle.WorldPosition;
		return true;
	}

	protected override void OnDisabled()
	{
		if (_laser.IsValid())
		{
			_laser.Destroy();
		}

		if (_model.IsValid())
		{
			_model.Destroy();
		}

		_weapon = null;
	}
}
