using System;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class EnemyPrefabTests
{
	public static void Variants()
	{
		CheckPrefab(
			fileName: "game_enemy.prefab",
			type: "GameEnemy",
			health: 30f,
			experience: 1,
			scale: "1,1,1"
		);
		CheckPrefab(
			fileName: "game_enemy_ranged.prefab",
			type: "GameEnemyRanged",
			health: 30f,
			experience: 4,
			scale: "1,1,1"
		);
		CheckPrefab(
			fileName: "game_enemy_exploding.prefab",
			type: "GameEnemyExploding",
			health: 30f,
			experience: 1,
			scale: "1,1,1"
		);
		CheckPrefab(
			fileName: "game_enemy_boss.prefab",
			type: "GameEnemyBoss",
			health: 1000f,
			experience: 0,
			scale: "1.8,1.8,1.8"
		);
	}

	public static void SpawnerReferences()
	{
		var path = Path.Combine(
			path1: AppContext.BaseDirectory,
			path2: "EnemyPrefabs",
			path3: "game.scene"
		);
		var json = File.ReadAllText(path);
		using var document = JsonDocument.Parse(json);
		var spawner = document.RootElement.GetProperty("GameObjects").EnumerateArray()
			.SelectMany(gameObject => gameObject.GetProperty("Components").EnumerateArray())
			.Single(component => component.GetProperty("__type").GetString() == "GameEnemySpawner");
		CheckReference(
			spawner: spawner,
			property: "EnemyPrefab",
			fileName: "game_enemy.prefab"
		);
		CheckReference(
			spawner: spawner,
			property: "RangedEnemyPrefab",
			fileName: "game_enemy_ranged.prefab"
		);
		CheckReference(
			spawner: spawner,
			property: "ExplodingEnemyPrefab",
			fileName: "game_enemy_exploding.prefab"
		);
		CheckReference(
			spawner: spawner,
			property: "BossPrefab",
			fileName: "game_enemy_boss.prefab"
		);
	}

	private static void CheckPrefab(
		string fileName,
		string type,
		float health,
		int experience,
		string scale
	)
	{
		var path = Path.Combine(
			path1: AppContext.BaseDirectory,
			path2: "EnemyPrefabs",
			path3: fileName
		);
		var json = File.ReadAllText(path);
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement.GetProperty("RootObject");
		var components = root.GetProperty("Components").EnumerateArray().ToArray();
		var enemies = components.Where(component =>
		{
			var componentType = component.GetProperty("__type").GetString();
			return componentType is "GameEnemy" or "GameEnemyRanged" or "GameEnemyExploding" or "GameEnemyBoss";
		}).ToArray();
		Check(enemies.Length == 1, $"{fileName} must contain exactly one enemy component.");
		var enemy = enemies[0];
		Check(enemy.GetProperty("__type").GetString() == type, $"{fileName} has the wrong enemy type.");
		Check(enemy.GetProperty("MaxHealth").GetSingle() == health, $"{fileName} changed its health tuning.");
		Check(enemy.GetProperty("ExperienceDrop").GetInt32() == experience, $"{fileName} changed its XP reward.");
		Check(root.GetProperty("Scale").GetString() == scale, $"{fileName} changed its authored scale.");
		Check(root.GetProperty("Enabled").GetBoolean(), $"{fileName} must be enabled.");
		var controllerId = enemy.GetProperty("Controller").GetProperty("component_id").GetString();
		var controller = components.Single(component => component.GetProperty("__guid").GetString() == controllerId);
		var controllerType = controller.GetProperty("__type").GetString();
		Check(controllerType == "Sandbox.PlayerController",
			$"{fileName} must reference its own player controller.");
	}

	private static void CheckReference(
		JsonElement spawner,
		string property,
		string fileName
	)
	{
		var reference = spawner.GetProperty(property).GetProperty("prefab").GetString();
		Check(reference == $"prefabs/{fileName}", $"The gameplay spawner must reference {fileName}.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
