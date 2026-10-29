using Sandbox;
using Sandbox.Rendering;

public sealed class BloodiedModel : Component
{
	[Property, Sync(SyncFlags.FromHost)] public float Amount { get; set; }

	private readonly Dictionary<ModelRenderer, (CommandList Pass, CommandList Previous)> _passes = new();
	private Material _material;
	private float _visibleAmount;

	protected override void OnPreRender()
	{
		var entity = GetComponent<Entity>();
		var target = entity.IsValid() ? entity.BloodAmount : Amount;
		_visibleAmount = float.Lerp(_visibleAmount, target.Clamp(0f, 1f), (Time.Delta * 12f).Clamp(0f, 1f));
		var amount = entity.IsValid() && entity.IsHitFlashing ? 0f : _visibleAmount;
		if (amount <= 0.001f && _passes.Count == 0)
			return;

		_material ??= Material.Create("enemy_blood", "shaders/enemy_blood.shader");
		foreach (var renderer in GetComponentsInChildren<ModelRenderer>())
		{
			if (!_passes.TryGetValue(renderer, out var entry))
			{
				entry = (new CommandList(), renderer.ExecuteAfter);
				_passes.Add(renderer, entry);
				renderer.ExecuteAfter = entry.Pass;
			}

			entry.Pass.Reset();
			if (entry.Previous != null)
				entry.Pass.InsertList(entry.Previous);
			if (amount <= 0.001f)
				continue;

			entry.Pass.Attributes.Set("BloodAmount", amount);
			entry.Pass.DrawRenderer(renderer, new RendererSetup { Material = _material });
		}
	}

	protected override void OnDisabled()
	{
		foreach (var (renderer, entry) in _passes)
		{
			if (renderer.IsValid() && renderer.ExecuteAfter == entry.Pass)
				renderer.ExecuteAfter = entry.Previous;
		}
		_passes.Clear();
	}
}
