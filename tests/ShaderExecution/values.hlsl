cbuffer Settings : register(b0) {
    bool enabled : packoffset(c1.x);
    int signedValue : packoffset(c1.y);
    uint token : packoffset(c1.z);
    float3 vectors[2] : packoffset(c2);
};

float4 main(float4 position : SV_Position) : SV_Target {
    uint index = (uint)position.x & 1;
    float3 value = vectors[index];
    return float4(enabled ? value.x : 0, signedValue, token >> 29, value.z);
}
