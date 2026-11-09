using System;
using System.Collections.Generic;
using System.Linq;

public enum LootKind
{
	Health = 0,
	Rifle = 1,
	Shotgun = 2,
	Flare = 3,
	Armor = 4,
	RerollDice = 5
}

public enum LootEffect
{
	Health,
	Weapon,
	ReviveFlare,
	Armor,
	RerollDice
}

public interface ILootCollector
{
	float Health { get; }
	float MaxHealth { get; }
	int Armor { get; }
	int RerollDice { get; }
	float HealthPickupMultiplier { get; }
	float ArmorPickupMultiplier { get; }
	bool IsRoundOver { get; }
	bool HasDeadTeammate { get; }
	bool RestoreHealth(float amount);
	bool AddArmor(int amount);
	bool AddRerollDice(int amount);
	bool FireReviveFlare();
}

public enum LootWeaponSlot
{
	None,
	Rifle,
	Shotgun
}

public sealed class LootVisualPart
{
	public string Name { get; init; }
	public string ModelPath { get; init; } = "models/dev/box.vmdl";
	public Vector3 Position { get; init; }
	public Vector3 Size { get; init; }
	public Color Tint { get; init; }
	public bool ScaleToModelWidth { get; init; }
}

public sealed class LootDefinition
{
	public LootKind Id { get; init; }
	public string Name { get; init; }
	public LootEffect Effect { get; init; }
	public int Amount { get; init; }
	public bool CanBeMagnetAttracted { get; init; }
	public GameRarity? Rarity { get; init; }
	public Func<ILootCollector, int, bool> CanCollect { get; init; }
	public Func<ILootCollector, int, bool> Collect { get; init; }
	public string WeaponResourcePath { get; init; }
	public LootWeaponSlot WeaponSlot { get; init; }
	public IReadOnlyList<LootVisualPart> VisualParts { get; init; }
}

/// <summary>Shared loot data. After the contextual flare roll, choose a rarity bucket and then an equally likely item within it.</summary>
public static class GameLootCatalog
{
	public static IReadOnlyList<RarityBucket> Rarities { get; } = Array.AsReadOnly(new[]
	{
		new RarityBucket(id: GameRarity.Common, chance: 0.9f),
		new RarityBucket(id: GameRarity.Uncommon, chance: 0f),
		new RarityBucket(id: GameRarity.Rare, chance: 0.1f),
		new RarityBucket(id: GameRarity.Epic, chance: 0f),
		new RarityBucket(id: GameRarity.Legendary, chance: 0f)
	});

	public static IReadOnlyList<LootDefinition> All { get; } = Array.AsReadOnly(new[]
	{
		new LootDefinition
		{
			Id = LootKind.Health,
			Name = "Health +50",
			Effect = LootEffect.Health,
			Amount = 50,
			CanBeMagnetAttracted = true,
			Rarity = GameRarity.Common,
			CanCollect = (collector, amount) => collector.Health < collector.MaxHealth,
			Collect = (collector, amount) =>
			{
				var healing = amount * collector.HealthPickupMultiplier;
				return collector.RestoreHealth(healing);
			},
			VisualParts = HealthParts()
		},
		new LootDefinition
		{
			Id = LootKind.Rifle,
			Name = "Rifle",
			Effect = LootEffect.Weapon,
			CanBeMagnetAttracted = false,
			Rarity = GameRarity.Common,
			WeaponResourcePath = "weapons/rifle.weapon",
			WeaponSlot = LootWeaponSlot.Rifle,
			VisualParts = RifleParts()
		},
		new LootDefinition
		{
			Id = LootKind.Shotgun,
			Name = "Shotgun",
			Effect = LootEffect.Weapon,
			CanBeMagnetAttracted = false,
			Rarity = GameRarity.Common,
			WeaponResourcePath = "weapons/shotgun.weapon",
			WeaponSlot = LootWeaponSlot.Shotgun,
			VisualParts = ShotgunParts()
		},
		new LootDefinition
		{
			Id = LootKind.Flare,
			Name = "Flare Gun",
			Effect = LootEffect.ReviveFlare,
			CanBeMagnetAttracted = true,
			CanCollect = (collector, amount) => collector.HasDeadTeammate,
			Collect = (collector, amount) => collector.FireReviveFlare(),
			VisualParts = FlareParts()
		},
		new LootDefinition
		{
			Id = LootKind.Armor,
			Name = "Armor +5",
			Effect = LootEffect.Armor,
			Amount = 5,
			CanBeMagnetAttracted = true,
			Rarity = GameRarity.Rare,
			CanCollect = (collector, amount) =>
			{
				var armor = ArmorPickupAmount(collector, amount);
				return armor <= int.MaxValue - collector.Armor;
			},
			Collect = (collector, amount) =>
			{
				var armor = ArmorPickupAmount(collector, amount);
				if (armor > int.MaxValue - collector.Armor)
				{
					return false;
				}
				return collector.AddArmor((int)armor);
			},
			VisualParts = ArmorParts()
		},
		new LootDefinition
		{
			Id = LootKind.RerollDice,
			Name = "Reroll Dice +1",
			Effect = LootEffect.RerollDice,
			Amount = 1,
			CanBeMagnetAttracted = true,
			Rarity = GameRarity.Common,
			CanCollect = (collector, amount) => !collector.IsRoundOver && collector.RerollDice <= int.MaxValue - amount,
			Collect = (collector, amount) => collector.AddRerollDice(amount),
			VisualParts = DiceParts()
		}
	});

	private static readonly IReadOnlyDictionary<LootKind, LootDefinition> ById = All.ToDictionary(item => item.Id);
	private static readonly IReadOnlyDictionary<GameRarity, LootDefinition[]> RarityDrops = Rarities.ToDictionary(
		keySelector: rarity => rarity.Id,
		elementSelector: rarity => All.Where(item => item.Rarity == rarity.Id && item.Effect != LootEffect.Weapon).ToArray()
	);
	private static readonly LootDefinition[] WeaponDrops = All.Where(item => item.Effect == LootEffect.Weapon).ToArray();

	static GameLootCatalog()
	{
		foreach (var item in All)
		{
			if (string.IsNullOrWhiteSpace(item.Name) || !Enum.IsDefined(item.Effect)
				|| item.VisualParts == null || item.VisualParts.Count == 0)
			{
				throw new InvalidOperationException($"Loot '{item.Id}' needs a name, valid effect and visual parts.");
			}
			if (item.Effect is LootEffect.Health or LootEffect.Armor or LootEffect.RerollDice && item.Amount <= 0)
			{
				throw new InvalidOperationException($"Loot '{item.Id}' needs a positive effect amount.");
			}
			if (item.Effect == LootEffect.Weapon && string.IsNullOrWhiteSpace(item.WeaponResourcePath))
			{
				throw new InvalidOperationException($"Loot '{item.Id}' needs a weapon resource path.");
			}
			if (item.Effect != LootEffect.Weapon && (item.CanCollect == null || item.Collect == null))
			{
				throw new InvalidOperationException($"Loot '{item.Id}' needs eligibility and collection callbacks.");
			}
			if (item.Id != LootKind.Flare && (!item.Rarity.HasValue || !RarityDrops.ContainsKey(item.Rarity.Value)))
			{
				throw new InvalidOperationException($"Loot '{item.Id}' needs a configured rarity bucket.");
			}
			if (item.Id == LootKind.Flare && item.Rarity.HasValue)
			{
				throw new InvalidOperationException($"Contextual flare loot '{item.Id}' must not also belong to a rarity bucket.");
			}
		}
		foreach (var rarity in Rarities)
		{
			if (rarity.Chance > 0f && RarityDrops[rarity.Id].Length == 0)
			{
				throw new InvalidOperationException($"Enabled rarity '{rarity.Id}' needs at least one loot item.");
			}
		}
		var totalChance = Rarities.Sum(rarity => rarity.Chance);
		if (MathF.Abs(totalChance - 1f) > 0.0001f)
		{
			throw new InvalidOperationException("Loot rarity chances must total 100%.");
		}
	}

	public static LootDefinition Get(LootKind kind)
	{
		if (!ById.TryGetValue(kind, out var definition))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), "Loot kind must have a catalog definition.");
		}

		return definition;
	}

	public static double ArmorPickupAmount(ILootCollector collector, int amount)
	{
		return Math.Ceiling(amount * (double)collector.ArmorPickupMultiplier);
	}

	public static LootDefinition Roll(float flareChance, Func<float> random)
	{
		if (!float.IsFinite(flareChance) || flareChance < 0f || flareChance > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(flareChance), "Flare chance must be between zero and one.");
		}

		if (NextRoll(random) < flareChance)
		{
			return Get(LootKind.Flare);
		}

		return RarityDraw.Pick(
			buckets: Rarities,
			getCandidates: rarity => RarityDrops[rarity],
			random: () => NextRoll(random)
		);
	}

	public static LootDefinition RollWeapon(Func<float> random)
	{
		var roll = NextRoll(random);
		var index = Math.Min((int)(roll * WeaponDrops.Length), WeaponDrops.Length - 1);
		return WeaponDrops[index];
	}

	private static float NextRoll(Func<float> random)
	{
		ArgumentNullException.ThrowIfNull(random);
		var roll = random();
		if (!float.IsFinite(roll) || roll < 0f || roll > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(random), "Loot rolls must be between zero and one.");
		}

		return roll;
	}

	private static IReadOnlyList<LootVisualPart> HealthParts()
	{
		return Array.AsReadOnly(new[]
		{
			new LootVisualPart
			{
				Name = "Medkit",
				Size = new Vector3(
					x: 18f,
					y: 24f,
					z: 12f
				),
				Tint = new Color(
					r: 0.9f,
					g: 0.15f,
					b: 0.2f
				)
			},
			new LootVisualPart
			{
				Name = "Cross Vertical",
				Position = Vector3.Up * 7f,
				Size = new Vector3(
					x: 5f,
					y: 16f,
					z: 2f
				),
				Tint = Color.White
			},
			new LootVisualPart
			{
				Name = "Cross Horizontal",
				Position = Vector3.Up * 7f,
				Size = new Vector3(
					x: 13f,
					y: 5f,
					z: 2f
				),
				Tint = Color.White
			}
		});
	}

	private static IReadOnlyList<LootVisualPart> RifleParts()
	{
		return WeaponParts(
			red: 0.25f,
			green: 0.65f,
			blue: 1f
		);
	}

	private static IReadOnlyList<LootVisualPart> ShotgunParts()
	{
		return WeaponParts(
			red: 1f,
			green: 0.65f,
			blue: 0.15f
		);
	}

	private static IReadOnlyList<LootVisualPart> WeaponParts(
		float red,
		float green,
		float blue
	)
	{
		var tint = new Color(
			r: red,
			g: green,
			b: blue
		);
		return Array.AsReadOnly(new[]
		{
			new LootVisualPart
			{
				Name = "Receiver",
				Size = new Vector3(
					x: 26f,
					y: 5f,
					z: 6f
				),
				Tint = tint
			},
			new LootVisualPart
			{
				Name = "Barrel",
				Position = Vector3.Forward * 20f,
				Size = new Vector3(
					x: 16f,
					y: 3f,
					z: 3f
				),
				Tint = new Color(
					r: 0.2f,
					g: 0.23f,
					b: 0.26f
				)
			},
			new LootVisualPart
			{
				Name = "Stock",
				Position = Vector3.Backward * 18f + Vector3.Down * 2f,
				Size = new Vector3(
					x: 12f,
					y: 6f,
					z: 9f
				),
				Tint = tint
			},
			new LootVisualPart
			{
				Name = "Grip",
				Position = Vector3.Backward * 4f + Vector3.Down * 7f,
				Size = new Vector3(
					x: 5f,
					y: 4f,
					z: 10f
				),
				Tint = tint
			}
		});
	}

	private static IReadOnlyList<LootVisualPart> ArmorParts()
	{
		var tint = new Color(
			r: 0.2f,
			g: 0.75f,
			b: 1f
		);
		return Array.AsReadOnly(new[]
		{
			new LootVisualPart
			{
				Name = "Armor Plate",
				Size = new Vector3(
					x: 8f,
					y: 22f,
					z: 26f
				),
				Tint = tint
			},
			new LootVisualPart
			{
				Name = "Armor Base",
				Position = Vector3.Down * 16f,
				Size = new Vector3(
					x: 8f,
					y: 14f,
					z: 8f
				),
				Tint = tint
			},
			new LootVisualPart
			{
				Name = "Armor Stripe",
				Position = Vector3.Forward * 5f,
				Size = new Vector3(
					x: 2f,
					y: 4f,
					z: 20f
				),
				Tint = Color.White
			}
		});
	}

	private static IReadOnlyList<LootVisualPart> FlareParts()
	{
		var tint = new Color(
			r: 1f,
			g: 0.25f,
			b: 0.15f
		);
		return Array.AsReadOnly(new[]
		{
			new LootVisualPart
			{
				Name = "Flare Body",
				Size = new Vector3(
					x: 20f,
					y: 7f,
					z: 8f
				),
				Tint = tint
			},
			new LootVisualPart
			{
				Name = "Flare Barrel",
				Position = Vector3.Forward * 14f,
				Size = new Vector3(
					x: 12f,
					y: 8f,
					z: 8f
				),
				Tint = new Color(
					r: 0.2f,
					g: 0.23f,
					b: 0.26f
				)
			},
			new LootVisualPart
			{
				Name = "Flare Grip",
				Position = Vector3.Backward * 6f + Vector3.Down * 8f,
				Size = new Vector3(
					x: 5f,
					y: 5f,
					z: 10f
				),
				Tint = tint
			}
		});
	}

	private static IReadOnlyList<LootVisualPart> DiceParts()
	{
		var parts = new List<LootVisualPart>
		{
			new LootVisualPart
			{
				Name = "Die",
				Size = Vector3.One * 22f,
				Tint = new Color(
					r: 0.95f,
					g: 0.9f,
					b: 0.75f
				)
			}
		};
		// Opposite faces add to seven. Raised dark pips remain readable as the pickup rotates.
		AddDiceFace(
			parts: parts,
			normal: Vector3.Up,
			horizontal: Vector3.Forward,
			vertical: Vector3.Left,
			value: 1
		);
		AddDiceFace(
			parts: parts,
			normal: Vector3.Down,
			horizontal: Vector3.Forward,
			vertical: Vector3.Left,
			value: 6
		);
		AddDiceFace(
			parts: parts,
			normal: Vector3.Forward,
			horizontal: Vector3.Left,
			vertical: Vector3.Up,
			value: 2
		);
		AddDiceFace(
			parts: parts,
			normal: Vector3.Backward,
			horizontal: Vector3.Left,
			vertical: Vector3.Up,
			value: 5
		);
		AddDiceFace(
			parts: parts,
			normal: Vector3.Left,
			horizontal: Vector3.Forward,
			vertical: Vector3.Up,
			value: 3
		);
		AddDiceFace(
			parts: parts,
			normal: Vector3.Right,
			horizontal: Vector3.Forward,
			vertical: Vector3.Up,
			value: 4
		);
		return parts.AsReadOnly();
	}

	private static void AddDiceFace(
		List<LootVisualPart> parts,
		Vector3 normal,
		Vector3 horizontal,
		Vector3 vertical,
		int value
	)
	{
		var positions = new List<Vector2>();
		if (value % 2 == 1)
		{
			positions.Add(Vector2.Zero);
		}
		if (value >= 2)
		{
			positions.Add(new Vector2(-1f, -1f));
			positions.Add(new Vector2(1f, 1f));
		}
		if (value >= 4)
		{
			positions.Add(new Vector2(-1f, 1f));
			positions.Add(new Vector2(1f, -1f));
		}
		if (value == 6)
		{
			positions.Add(new Vector2(-1f, 0f));
			positions.Add(new Vector2(1f, 0f));
		}
		foreach (var position in positions)
		{
			var pip = new LootVisualPart
			{
				Name = "Die Pip",
				ModelPath = "models/dev/sphere.vmdl",
				Position = normal * 11f + horizontal * (position.x * 5.5f) + vertical * (position.y * 5.5f),
				Size = Vector3.One * 3.5f,
				Tint = new Color(
					r: 0.08f,
					g: 0.06f,
					b: 0.12f
				),
				ScaleToModelWidth = true
			};
			parts.Add(pip);
		}
	}
}
