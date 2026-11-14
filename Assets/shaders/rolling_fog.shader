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
	#include "procedural.hlsl"

	RenderState(CullMode, NONE);
	RenderState(DepthWriteEnable, false);
	RenderState(BlendEnable, true);
	RenderState(SrcBlend, SRC_ALPHA);
	RenderState(DstBlend, INV_SRC_ALPHA);

	float g_flFogAge < Attribute("FogAge"); Default(0); >;
	float g_flFogOpacity < Attribute("FogOpacity"); Default(0); >;
	float4 g_vFogLayer < Attribute("FogLayer"); >;
	float4 g_vFogBounds < Attribute("FogBounds"); >;

	float4 MainPs(PixelInput i) : SV_Target0
	{
		float3 worldPosition = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float2 phase = (worldPosition.xy - g_vFogLayer.xy * g_flFogAge) / g_vFogLayer.z;
		phase += g_vFogLayer.w * float2(7.13, 13.71);
		float2 curl = float2(
			ValueNoise(phase * 0.7 + g_flFogAge * 0.012),
			ValueNoise(phase * 0.7 + 17.3 - g_flFogAge * 0.01)
		);
		float billows = ValueNoise(phase + curl * 1.3) * 0.65
			+ ValueNoise(phase * 2.15 - curl * 0.6) * 0.25
			+ ValueNoise(phase * 4.2) * 0.10;
		float coverage = smoothstep(0.42, 0.68, billows);
		float2 edgeDistance = g_vFogBounds.zw - abs(worldPosition.xy - g_vFogBounds.xy);
		float edgeFade = saturate(min(edgeDistance.x, edgeDistance.y) / 300.0);
		float3 surfacePosition = Depth::GetWorldPosition(i.vPositionSs.xy);
		float intersectionFade = saturate(distance(surfacePosition, worldPosition) / 28.0);
		float opacity = coverage * edgeFade * intersectionFade * g_flFogOpacity;
		float3 color = float3(0.78, 0.84, 0.88) * lerp(0.8, 1.05, billows);
		return float4(color, opacity);
	}
}
