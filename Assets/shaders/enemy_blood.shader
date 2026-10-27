FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#define BLEND_MODE_ALREADY_SET
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs(VertexInput i)
	{
		return FinalizeVertex(ProcessVertex(i));
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState(DepthWriteEnable, false);
	RenderState(DepthFunc, EQUAL);
	RenderState(BlendEnable, true);
	RenderState(SrcBlend, SRC_ALPHA);
	RenderState(DstBlend, INV_SRC_ALPHA);

	float g_flBloodAmount < Attribute("BloodAmount"); Default(0); >;

	float Hash(float2 p)
	{
		return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
	}

	float Noise(float2 p)
	{
		float2 cell = floor(p);
		float2 f = frac(p);
		f = f * f * (3.0 - 2.0 * f);
		return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
			lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), f.x), f.y);
	}

	float4 MainPs(PixelInput i) : SV_Target0
	{
		// UVs keep the patches fixed to the skin during animation and ragdoll motion.
		float2 uv = i.vTextureCoords.xy;
		float mask = Noise(uv * float2(19, 13)) * 0.65
			+ Noise(uv * 47.0) * 0.25 + Noise(uv * 113.0) * 0.10;
		float threshold = lerp(0.82, 0.35, saturate(g_flBloodAmount));
		float coverage = smoothstep(threshold - 0.035, threshold + 0.035, mask);
		coverage *= saturate(g_flBloodAmount * 5.0);
		clip(coverage - 0.001);

		Material m = Material::Init(i);
		m.Albedo = lerp(float3(0.008, 0.045, 0.025), float3(0.025, 0.22, 0.14), Noise(uv * 31));
		m.Roughness = 0.22;
		m.Metalness = 0;
		m.Opacity = coverage * 0.88;
		return ShadingModelStandard::Shade(m);
	}
}
