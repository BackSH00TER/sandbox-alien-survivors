using Sandbox;

public sealed class HeldWeaponModel : Component
{
	private SurvivorController _survivor;
	private WeaponDefinition _weapon;
	private GameObject _model;
	private ModelRenderer _authoredRenderer;
	private Vector3 _muzzleOffset;
	private bool _missingAttachmentLogged;
	private bool _missingMuzzleLogged;

	protected override void OnStart()
	{
		_survivor = GetComponent<SurvivorController>();
	}

	protected override void OnPreRender()
	{
		UpdateModel();
	}

	public bool TryGetMuzzlePosition(out Vector3 position)
	{
		position = default;
		if (!UpdateModel())
			return false;

		if (_authoredRenderer.IsValid())
		{
			if (_authoredRenderer.Model.GetAttachment("muzzle") is not { } muzzle)
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

	private bool UpdateModel()
	{
		if (!_survivor.IsValid() || !_survivor.Controller.Renderer.IsValid())
			return false;

		if (_weapon != _survivor.EquippedWeapon)
		{
			_weapon = _survivor.EquippedWeapon;
			if (_model.IsValid())
				_model.Destroy();
			_authoredRenderer = null;
			_missingMuzzleLogged = false;
			if (_weapon != null)
				CreateModel();
		}

		if (!_model.IsValid())
			return false;

		if (_survivor.IsDead)
		{
			_model.Enabled = false;
			return false;
		}

		var hand = _survivor.Controller.Renderer.GetAttachment("hold_R");
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

		if (_weapon.HoldType is Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Rifle
			or Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Shotgun)
		{
			var color = _weapon.HoldType == Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Rifle
				? new Color(0.2f, 0.35f, 0.45f) : new Color(0.45f, 0.28f, 0.12f);
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
		if (_model.IsValid())
			_model.Destroy();
		_weapon = null;
	}
}
