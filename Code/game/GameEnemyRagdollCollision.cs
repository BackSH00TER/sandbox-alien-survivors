using Sandbox;

public sealed class GameEnemyRagdollCollision : Component
{
	internal bool ApplyCollisionTags()
	{
		var modelPhysics = GetComponent<ModelPhysics>();
		if (!modelPhysics.IsValid())
		{
			return false;
		}

		var bodyCount = 0;
		foreach (var body in modelPhysics.Bodies)
		{
			if (!GameEntity.AddCollisionTag(body.Component, GameEntity.EnemyRagdollCollisionTag))
			{
				return false;
			}

			bodyCount++;
		}

		return bodyCount > 0;
	}

	protected override void OnStart()
	{
		if (!ApplyCollisionTags())
		{
			Log.Error("Enemy ragdoll requires model physics and collision shapes on this peer.");
		}
	}
}
