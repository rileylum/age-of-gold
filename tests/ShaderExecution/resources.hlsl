Texture2D<float4> first : register(t0);
Texture2D<float4> middle : register(t3);
Texture2D<float4> last : register(t4);
SamplerState repeatSample : register(s0);
SamplerState edgeSample : register(s3);
SamplerState borderSample : register(s4);

float4 main(float4 position : SV_Position) : SV_Target {
    float2 uv = float2(1.25, 0.5);
    return float4(first.SampleLevel(repeatSample, uv, 0).x,
                  middle.SampleLevel(edgeSample, uv, 0).x,
                  last.SampleLevel(borderSample, uv, 0).x,
                  last.SampleLevel(repeatSample, uv, 0).x);
}
