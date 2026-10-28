using Sandbox;

public sealed class HeldWeaponModel : Component
{
	private SurvivorController _survivor;
	private WeaponDefinition _weapon;
	private GameObject _model;
	private bool _missingAttachmentLogged;

	protected override void OnStart()
	{
		_survivor = GetComponent<SurvivorController>();
	}

	protected override void OnPreRender()
	{
		if (!_survivor.IsValid() || !_survivor.Controller.Renderer.IsValid())
			return;

		if (_weapon != _survivor.EquippedWeapon)
		{
			_weapon = _survivor.EquippedWeapon;
			if (_model.IsValid())
				_model.Destroy();
			if (_weapon != null)
				CreateModel();
		}

		if (!_model.IsValid())
			return;

		var hand = _survivor.Controller.Renderer.GetAttachment("hold_R");
		if (hand is not { } transform)
		{
			if (!_missingAttachmentLogged)
			{
				Log.Error("Held weapons require the character's hold_R attachment.");
				_missingAttachmentLogged = true;
			}
			_model.Enabled = false;
			return;
		}

		_model.Enabled = true;
		_model.WorldTransform = transform;
	}

	private void CreateModel()
	{
		_model = new GameObject(GameObject, true, "Held Weapon");
		_model.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		if (_weapon.WorldModel != null)
		{
			_model.AddComponent<ModelRenderer>().Model = _weapon.WorldModel;
			return;
		}

		// A simple pistol silhouette until an authored world model is assigned.
		AddPart("Slide", new Vector3(3f, 0f, 3f), new Vector3(10f, 3f, 3f), new Color(0.18f, 0.2f, 0.23f));
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
