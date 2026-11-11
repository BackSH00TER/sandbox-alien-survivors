using Sandbox;

/// <summary>An authored weapon visual, with its grip at the root and a muzzle marker for shots and lasers.</summary>
public sealed class GameWeaponModel : Component
{
	[Property] public GameObject Muzzle { get; set; }

	public bool TryAttachToHand(SkinnedModelRenderer body, Rotation facing)
	{
		if (!body.IsValid() || !body.TryGetBoneTransform("DEF_hand_R", out var hand))
		{
			return false;
		}

		WorldPosition = hand.Position;
		WorldRotation = facing;
		return true;
	}

	public static GameWeaponModel Create(GameWeaponDefinition weapon, GameObject parent)
	{
		if (weapon == null || weapon.ModelPrefab == null)
		{
			Log.Error($"Weapon '{weapon?.ResourcePath}' requires a model prefab.");
			return null;
		}

		var config = new CloneConfig
		{
			Parent = parent,
			Transform = parent.WorldTransform,
			StartEnabled = false,
			Name = weapon.DisplayName
		};
		var instance = GameObject.Clone(weapon.ModelPrefab, config);
		instance.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		instance.NetworkMode = NetworkMode.Never;
		var visual = instance.GetComponent<GameWeaponModel>(includeDisabled: true);
		if (!visual.IsValid() || !visual.Muzzle.IsValid()
			|| visual.Muzzle.GetComponentInParent<GameWeaponModel>(includeDisabled: true) != visual)
		{
			Log.Error($"Weapon '{weapon.ResourcePath}' requires a GameWeaponModel with a muzzle inside its prefab.");
			instance.Destroy();
			return null;
		}

		instance.Enabled = true;
		return visual;
	}
}
