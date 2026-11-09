cbuffer Params : register(b0) { uint mode; float3 padding; };

float4 main(float4 position : SV_Position) : SV_Target {
    uint3 id = uint3(position.xy, 0);
    float x = (int(id.x) - 16) * 0.0625;
    float4 t = float4(x, -x, x*x, 0.25 + x);
    float4 v = t > 0 ? t : -t;
    float y = 0;
    [loop] for (uint j = 0; j < (mode & 3) + 1; j++) y += v.x * (j + 1);
    return float4(saturate(v.x), y, dot(v.xyz, float3(0.3, 0.7, 1.1)),
                  asfloat(asuint(x) ^ 0x80000000u));
}
