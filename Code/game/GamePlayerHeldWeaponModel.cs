using Sandbox;

public sealed class GamePlayerHeldWeaponModel : Component
{
	private GamePlayer _player;
	private GameWeaponDefinition _weapon;
	private GameObject _model;
	private ModelRenderer _authoredRenderer;
	private Vector3 _muzzleOffset;
	private bool _missingAttachmentLogged;
	private bool _missingMuzzleLogged;
	private LineRenderer _laser;

	protected override void OnStart()
	{
		_player = GetComponent<GamePlayer>();
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
		if (!_player.IsValid() || !_player.Controller.Renderer.IsValid())
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

			_authoredRenderer = null;
			_missingMuzzleLogged = false;
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

		var hand = _player.Controller.Renderer.GetAttachment("hold_R");
		if (hand is not { } transform)
		{
			if (!_missingAttachmentLogged)
			{
				Log.Error("Held weapons require the character's hold_R attachment.");
				_missingAttachmentLogged = true;
			}
			_model.Enabled = false;
			return false;
		}

		_model.Enabled = true;
		_model.WorldTransform = transform;
		_missingAttachmentLogged = false;
		return true;
	}

	private void CreateModel()
	{
		_model = new GameObject(GameObject, true, "Held Weapon");
		_model.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		if (_weapon.WorldModel != null)
		{
			_authoredRenderer = _model.AddComponent<ModelRenderer>();
			_authoredRenderer.Model = _weapon.WorldModel;
			return;
		}

		if (_weapon.HoldType == Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Shotgun)
		{
			var metal = new Color(
				0.18f,
				0.2f,
				0.23f
			);
			var wood = new Color(
				0.35f,
				0.2f,
				0.1f
			);
			var receiverPosition = new Vector3(
				4f,
				0f,
				2f
			);
			var receiverSize = new Vector3(
				9f,
				2.5f,
				3f
			);
			AddPart(
				name: "Receiver",
				position: receiverPosition,
				size: receiverSize,
				color: metal
			);
			var barrelPosition = new Vector3(
				15f,
				0f,
				3f
			);
			var barrelSize = new Vector3(
				14f,
				1.2f,
				1.2f
			);
			AddPart(
				name: "Barrel",
				position: barrelPosition,
				size: barrelSize,
				color: metal
			);
			var magazinePosition = new Vector3(
				13f,
				0f,
				1.5f
			);
			var magazineSize = new Vector3(
				10f,
				1.1f,
				1.1f
			);
			AddPart(
				name: "Magazine Tube",
				position: magazinePosition,
				size: magazineSize,
				color: metal
			);
			var pumpPosition = new Vector3(
				11f,
				0f,
				1.5f
			);
			var pumpSize = new Vector3(
				5f,
				2f,
				2f
			);
			AddPart(
				name: "Pump",
				position: pumpPosition,
				size: pumpSize,
				color: wood
			);
			var stockNeckPosition = new Vector3(
				-3f,
				0f,
				1f
			);
			var stockNeckSize = new Vector3(
				5f,
				2f,
				2.5f
			);
			AddPart(
				name: "Stock Neck",
				position: stockNeckPosition,
				size: stockNeckSize,
				color: wood
			);
			var stockPosition = new Vector3(
				-8f,
				0f,
				0f
			);
			var stockSize = new Vector3(
				6f,
				2.5f,
				4f
			);
			AddPart(
				name: "Stock",
				position: stockPosition,
				size: stockSize,
				color: wood
			);
			_muzzleOffset = new Vector3(
				22f,
				0f,
				3f
			);
			return;
		}

		if (_weapon.HoldType == Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Rifle)
		{
			var color = new Color(0.2f, 0.35f, 0.45f);
			AddPart("Receiver", new Vector3(8f, 0f, 3f), new Vector3(20f, 4f, 5f), color);
			AddPart("Barrel", new Vector3(24f, 0f, 4f), new Vector3(12f, 2f, 2f), new Color(0.18f, 0.2f, 0.23f));
			_muzzleOffset = new Vector3(30f, 0f, 4f);
			AddPart("Stock", new Vector3(-8f, 0f, 1f), new Vector3(12f, 4f, 7f), color);
			AddPart("Grip", new Vector3(0f, 0f, -2f), new Vector3(3f, 3f, 6f), color);
			return;
		}

		// Simple silhouettes until authored world models are assigned.
		AddPart("Slide", new Vector3(3f, 0f, 3f), new Vector3(10f, 3f, 3f), new Color(0.18f, 0.2f, 0.23f));
		_muzzleOffset = new Vector3(8f, 0f, 3f);
		AddPart("Grip", new Vector3(-0.5f, 0f, -0.5f), new Vector3(3f, 2.5f, 6f), new Color(0.07f, 0.08f, 0.09f));
	}

	public bool TryGetMuzzlePosition(out Vector3 position)
	{
		position = default;
		if (!UpdateModel())
		{
			return false;
		}

		if (_authoredRenderer.IsValid())
		{
			if (_authoredRenderer.Model.Attachments.GetTransform("muzzle") is not { } muzzle)
			{
				if (!_missingMuzzleLogged)
				{
					Log.Error($"Weapon '{_weapon.ResourcePath}' requires a muzzle attachment on its world model.");
					_missingMuzzleLogged = true;
				}

				return false;
			}

			position = _model.WorldTransform.PointToWorld(muzzle.Position);
			return true;
		}

		position = _model.WorldTransform.PointToWorld(_muzzleOffset);
		return true;
	}

	private void AddPart(string name, Vector3 position, Vector3 size, Color color)
	{
		var part = new GameObject(_model, true, name);
		part.LocalPosition = position;
		part.LocalScale = size / 50f;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/box.vmdl");
		renderer.Tint = color;
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
