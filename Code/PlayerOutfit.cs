using Sandbox;
using System.Threading.Tasks;

/// <summary>
/// Defers a <see cref="Dresser.Apply"/> call after spawn. With Source = OwnerConnection
/// the Dresser's built-in auto-apply often runs before the owner's avatar data has
/// loaded, leaving the player naked. Runs on every client so each machine dresses
/// its local copy from the owning connection's avatar.
/// </summary>
public sealed class PlayerOutfit : Component
{
    [Property] public Dresser Dresser { get; set; }
    [Property] public float StartDelay { get; set; } = 0.05f;

    protected override void OnStart()
    {
        _ = ApplyOutfitAsync();
    }

    private async Task ApplyOutfitAsync()
    {
        Dresser dresser = Dresser ?? GetComponent<Dresser>();
        if (dresser == null) return;

        await Task.DelayRealtimeSeconds(StartDelay);
        if (!this.IsValid()) return;

        await dresser.Apply();
    }
}
