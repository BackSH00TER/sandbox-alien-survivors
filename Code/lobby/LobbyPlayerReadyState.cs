using Sandbox;

/// <summary>
/// Per-player lobby ready flag. Owned by the player's connection, so only the owner writes it and the value syncs to everyone.
/// </summary>
public sealed class LobbyPlayerReadyState : Component
{
    [Sync] public bool IsReady { get; set; }
}
