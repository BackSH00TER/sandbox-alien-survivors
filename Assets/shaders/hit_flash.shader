FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
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

	PixelInput MainVs( VertexInput i )
	{
		return FinalizeVertex( ProcessVertex( i ) );
	}
}

PS
{
	#include "common/pixel.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		return float4( 1, 1, 1, 1 );
	}
}
