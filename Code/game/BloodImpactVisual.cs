using System;
using Sandbox;

/// <summary>Local death mist, fine spray and temporary ground splats.</summary>
public sealed class BloodImpactVisual : Component
{
	private const int MaximumImpacts = 24;
	private const float Lifetime = 4f;
	private static readonly System.Random VisualRandom = new();
	private static Texture _mistTexture;
	private static Texture _splatTexture;
	private static Sprite _mistSprite;
	private static Sprite _splatSprite;
	private readonly List<Particle> _particles = new();
	private float _startedAt;
	private float _groundHeight;

	private sealed class Particle
	{
		public SpriteRenderer Renderer;
		public Vector3 Velocity;
		public float Size;
		public float Lifetime;
		public float Opacity;
		public bool Mist;
		public bool Landed;
	}

	public static void Spawn(
		Scene scene,
		Vector3 position,
		Vector3 direction
	)
	{
		if (Application.IsDedicatedServer)
		{
			return;
		}

		if (scene == null || !position.IsFinite || !direction.IsFinite || direction.LengthSquared <= 0.001f)
		{
			Log.Error("Blood impacts require a scene, finite position and nonzero finite direction.");
			return;
		}

		var active = scene.GetAllComponents<BloodImpactVisual>().OrderBy(impact => impact._startedAt).ToArray();
		for (var index = 0; index <= active.Length - MaximumImpacts; index++)
		{
			active[index].GameObject.Destroy();
		}

		var ground = GameEntity.FindSpawnGround(scene, position);

		_mistTexture ??= CreateTexture(mist: true);
		_splatTexture ??= CreateTexture(mist: false);
		_mistSprite ??= Sprite.FromTexture(_mistTexture);
		_splatSprite ??= Sprite.FromTexture(_splatTexture);
		var root = scene.CreateObject();
		root.Name = "Death Blood";
		root.WorldPosition = position;
		root.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var visual = root.AddComponent<BloodImpactVisual>();
		visual._startedAt = Time.Now;
		visual._groundHeight = ground.Hit ? ground.EndPosition.z + 0.4f : float.NegativeInfinity;
		var forward = direction.WithZ(0f);
		forward = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
		for (var index = 0; index < 18; index++)
		{
			var mist = index < 9;
			var angle = VisualRandom.Float(-180f, 180f);
			var outward = Rotation.FromYaw(angle) * forward;
			var speed = mist ? VisualRandom.Float(20f, 65f) : VisualRandom.Float(55f, 125f);
			var rise = mist ? VisualRandom.Float(8f, 35f) : VisualRandom.Float(35f, 90f);
			var size = mist ? VisualRandom.Float(55f, 80f) : VisualRandom.Float(2f, 4f);
			var particlePosition = position + outward * VisualRandom.Float(0f, 8f);
			visual.AddParticle(
				position: particlePosition,
				velocity: outward * speed + Vector3.Up * rise,
				size: size,
				mist: mist,
				landed: false
			);
		}

		for (var index = 0; index < 12; index++)
		{
			var angle = index * 30f + VisualRandom.Float(-12f, 12f);
			var outward = Rotation.FromYaw(angle) * forward;
			var speed = VisualRandom.Float(140f, 230f);
			var rise = VisualRandom.Float(70f, 150f);
			var particlePosition = position + outward * VisualRandom.Float(4f, 12f);
			visual.AddParticle(
				position: particlePosition,
				velocity: outward * speed + forward * 30f + Vector3.Up * rise,
				size: VisualRandom.Float(6f, 10f),
				mist: false,
				landed: false
			);
		}

		if (ground.Hit)
		{
			var splatPosition = ground.EndPosition.WithZ(visual._groundHeight);
			visual.AddParticle(
				position: splatPosition,
				velocity: Vector3.Zero,
				size: VisualRandom.Float(32f, 44f),
				mist: false,
				landed: true
			);
		}
	}

	private void AddParticle(
		Vector3 position,
		Vector3 velocity,
		float size,
		bool mist,
		bool landed
	)
	{
		var particleObject = new GameObject(
			parent: GameObject,
			enabled: true,
			name: mist ? "Blood Mist" : "Blood Splat"
		);
		particleObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		particleObject.WorldPosition = position;
		var renderer = particleObject.AddComponent<SpriteRenderer>();
		renderer.Sprite = mist ? _mistSprite : _splatSprite;
		renderer.Size = Vector2.One * size;
		renderer.Color = new Color(
			r: 0.015f,
			g: VisualRandom.Float(0.35f, 0.6f),
			b: VisualRandom.Float(0.32f, 0.55f),
			a: 0.85f
		);
		renderer.Additive = false;
		renderer.Opaque = false;
		// Native sprites otherwise clip the low-alpha edges even with blending enabled.
		renderer.AlphaCutoff = 0f;
		renderer.TextureFilter = Sandbox.Rendering.FilterMode.Bilinear;
		renderer.Shadows = false;
		renderer.Lighting = false;
		renderer.DepthFeather = mist ? 8f : 0f;
		renderer.Billboard = landed ? SpriteRenderer.BillboardMode.None : SpriteRenderer.BillboardMode.Always;
		if (landed)
		{
			particleObject.WorldRotation = Rotation.LookAt(Vector3.Up) * Rotation.FromRoll(VisualRandom.Float(0f, 360f));
		}

		var particle = new Particle
		{
			Renderer = renderer,
			Velocity = velocity,
			Size = size,
			Lifetime = mist ? VisualRandom.Float(0.45f, 0.75f) : Lifetime,
			Opacity = renderer.Color.a,
			Mist = mist,
			Landed = landed
		};
		_particles.Add(particle);
	}

	private static Texture CreateTexture(bool mist)
	{
		const int resolution = 128;
		var pixels = new byte[resolution * resolution * 4];
		for (var y = 0; y < resolution; y++)
		{
			for (var x = 0; x < resolution; x++)
			{
				var u = (x + 0.5f) / resolution * 2f - 1f;
				var v = (y + 0.5f) / resolution * 2f - 1f;
				var angle = MathF.Atan2(v, u);
				var radius = MathF.Sqrt(u * u + v * v);
				var lobes = 0.77f + 0.1f * MathF.Sin(angle * 5f) + 0.07f * MathF.Cos(angle * 9f);
				var edge = mist ? 1f : lobes;
				var coverage = (1f - radius / edge).Clamp(0f, 1f);
				var noise = 0.74f + 0.14f * MathF.Sin(u * 15f + MathF.Sin(v * 11f))
					+ 0.12f * MathF.Cos(v * 19f + u * 9f);
				var alpha = mist ? MathF.Pow(coverage, 1.3f) * noise : (coverage * 12f).Clamp(0f, 1f);
				var offset = (y * resolution + x) * 4;
				pixels[offset] = 255;
				pixels[offset + 1] = 255;
				pixels[offset + 2] = 255;
				pixels[offset + 3] = (byte)(alpha * 255f);
			}
		}

		var name = mist ? "death_blood_mist" : "death_blood_splat";
		return Texture.Create(
				width: resolution,
				height: resolution,
				format: ImageFormat.RGBA8888
			)
			.WithName(name)
			.WithData(pixels)
			.Finish();
	}

	protected override void OnUpdate()
	{
		var age = Time.Now - _startedAt;
		if (age >= Lifetime)
		{
			GameObject.Destroy();
			return;
		}

		foreach (var particle in _particles)
		{
			var renderer = particle.Renderer;
			if (!renderer.IsValid())
			{
				continue;
			}
			if (age >= particle.Lifetime)
			{
				renderer.GameObject.Destroy();
				continue;
			}

			if (!particle.Landed)
			{
				if (particle.Mist)
				{
					particle.Velocity *= MathF.Exp(-4f * Time.Delta);
				}
				else
				{
					particle.Velocity -= Vector3.Up * (220f * Time.Delta);
				}
				renderer.WorldPosition += particle.Velocity * Time.Delta;
				if (!particle.Mist && renderer.WorldPosition.z <= _groundHeight)
				{
					particle.Landed = true;
					renderer.WorldPosition = renderer.WorldPosition.WithZ(_groundHeight);
					renderer.Billboard = SpriteRenderer.BillboardMode.None;
					renderer.WorldRotation = Rotation.LookAt(Vector3.Up) * Rotation.FromRoll(VisualRandom.Float(0f, 360f));
					particle.Size *= 2.5f;
				}
			}

			var progress = age / particle.Lifetime;
			var fade = particle.Mist ? (1f - progress) * (1f - progress) : ((Lifetime - age) / 0.8f).Clamp(0f, 1f);
			var size = particle.Mist ? particle.Size * (1f + progress * 1.6f) : particle.Size;
			renderer.Size = Vector2.One * size;
			renderer.Color = renderer.Color.WithAlpha(particle.Opacity * fade);
		}
	}
}
