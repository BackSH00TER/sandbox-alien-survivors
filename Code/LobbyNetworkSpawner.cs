using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Owns the per-connection lobby player spawns and the lobby slot assignment.
/// The host creates the lobby, spawns the preview player for each connection, and
/// keeps the connection-to-slot mapping stable while players are active in the lobby.
/// </summary>
public sealed class LobbyNetworkSpawner : Component, Component.INetworkListener
{
    /// <summary>True while the host should create the lobby automatically when the scene starts.</summary>
    [Property] public bool StartServer { get; set; } = true;

    /// <summary>List of display spawn points used for player preview slots in the lobby.</summary>
    [Property] public List<SpawnPoint> LobbySpawnPoints { get; set; } = new();

    /// <summary>Prefab cloned for each connected player.</summary>
    [Property] public GameObject PlayerPrefab { get; set; }

    /// <summary>Height above the player origin where the bottom of the nameplate sits.</summary>
    [Property] public float NameplateHeight { get; set; } = 80f;

    private readonly Dictionary<ulong, GameObject> _playerLookup = new();
    private readonly Dictionary<ulong, int> _spawnIndexByConnection = new();

    protected override async Task OnLoad()
    {
        if ( Scene.IsEditor )
            return;

        if ( StartServer && !Networking.IsActive )
        {
            await Task.DelayRealtimeSeconds( 0.1f );
            Networking.CreateLobby( new() );
        }
    }

    protected override void OnStart()
    {
        if ( !Networking.IsHost || !Networking.IsActive )
            return;

        foreach ( Connection connection in Connection.All )
        {
            EnsurePlayerForConnection( connection );
        }
    }

    public void OnActive( Connection connection )
    {
        if ( !Networking.IsHost || !Networking.IsActive )
            return;

        EnsurePlayerForConnection( connection );
    }

    public void OnDisconnected( Connection connection )
    {
        if ( connection == null )
            return;

        if ( _spawnIndexByConnection.Remove( connection.SteamId ) )
        {
            // This slot is released and may be reused by the next connection.
        }

        if ( _playerLookup.TryGetValue( connection.SteamId, out GameObject playerObject ) )
        {
            if ( playerObject.IsValid() )
                playerObject.Destroy();

            _playerLookup.Remove( connection.SteamId );
        }
    }

    private void EnsurePlayerForConnection( Connection connection )
    {
        if ( connection == null )
            return;

        ulong steamId = connection.SteamId;
        if ( _playerLookup.TryGetValue( steamId, out GameObject existingPlayer ) && existingPlayer.IsValid() )
        {
            ApplySpawnTransform( existingPlayer, steamId );
            return;
        }

        int spawnIndex = GetSpawnIndexForConnection( steamId );
        if ( spawnIndex < 0 )
            return;

        List<SpawnPoint> spawnPoints = GetSpawnPoints();
        if ( spawnIndex >= spawnPoints.Count )
            return;

        GameObject playerObject = CreatePlayerForConnection( connection, spawnPoints[spawnIndex] );
        ApplySpawnTransform( playerObject, steamId );
    }

    private GameObject CreatePlayerForConnection( Connection connection, SpawnPoint spawnPoint )
    {
        if ( PlayerPrefab == null )
        {
            throw new InvalidOperationException( "LobbyNetworkSpawner requires a valid PlayerPrefab to spawn lobby players." );
        }

        GameObject playerObject = PlayerPrefab.Clone( spawnPoint.WorldPosition, spawnPoint.WorldRotation );
        playerObject.Name = $"LobbyPlayer_{connection.DisplayName}";
        playerObject.NetworkMode = NetworkMode.Object;
        playerObject.SetParent( GameObject );

        // Created before NetworkSpawn so it replicates with the player to every client.
        SpawnNameplateForPlayer( playerObject, connection );

        playerObject.NetworkSpawn( connection );

        _playerLookup[connection.SteamId] = playerObject;

        return playerObject;
    }

    private void SpawnNameplateForPlayer( GameObject playerObject, Connection connection )
    {
        GameObject nameplateObject = Scene.CreateObject();
        nameplateObject.Name = $"LobbyNameplate_{connection.DisplayName}";
        nameplateObject.NetworkMode = NetworkMode.Object;
        nameplateObject.SetParent( playerObject );
        nameplateObject.LocalPosition = Vector3.Up * NameplateHeight;

        // 0.05 world units per pixel. World panels also scale UI 2x, so CSS space is half of PanelSize (460 x 80).
        WorldPanel worldPanel = nameplateObject.Components.Create<WorldPanel>();
        worldPanel.PanelSize = new Vector2( 920f, 160f );
        worldPanel.VerticalAlign = WorldPanel.VAlignment.Bottom;
        worldPanel.LookAtCamera = true;

        nameplateObject.Components.Create<LobbyPlayerNameplate>();
    }

    private void ApplySpawnTransform( GameObject playerObject, ulong steamId )
    {
        List<SpawnPoint> spawnPoints = GetSpawnPoints();
        if ( spawnPoints.Count == 0 )
            return;

        if ( !_spawnIndexByConnection.TryGetValue( steamId, out int slotIndex ) )
            return;

        if ( slotIndex < 0 || slotIndex >= spawnPoints.Count )
            return;

        SpawnPoint spawnPoint = spawnPoints[slotIndex];
        playerObject.SetParent( GameObject );
        playerObject.WorldPosition = spawnPoint.WorldPosition;
        playerObject.WorldRotation = spawnPoint.WorldRotation;
    }

    private int GetSpawnIndexForConnection( ulong steamId )
    {
        if ( _spawnIndexByConnection.TryGetValue( steamId, out int existingIndex ) )
            return existingIndex;

        List<SpawnPoint> spawnPoints = GetSpawnPoints();
        if ( spawnPoints.Count == 0 )
            return -1;

        HashSet<int> usedIndices = new( _spawnIndexByConnection.Values );
        for ( int index = 0; index < spawnPoints.Count; index++ )
        {
            if ( !usedIndices.Contains( index ) )
            {
                _spawnIndexByConnection[steamId] = index;
                return index;
            }
        }

        int fallbackIndex = _spawnIndexByConnection.Count % spawnPoints.Count;
        _spawnIndexByConnection[steamId] = fallbackIndex;
        return fallbackIndex;
    }

    private List<SpawnPoint> GetSpawnPoints()
    {
        if ( LobbySpawnPoints == null )
            return new List<SpawnPoint>();

        return LobbySpawnPoints.Where( x => x != null ).Distinct().ToList();
    }
}
