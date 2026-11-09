float unused : register(c0);
float amount : register(c2);
float2 axis : register(c4);
cbuffer Settings : register(b2) {
    float control : packoffset(c1.y);
    float2 weights : packoffset(c2.z);
};

float4 main(float4 position : SV_Position) : SV_Target {
    return float4(amount + control, axis * weights, amount - axis.y);
}
