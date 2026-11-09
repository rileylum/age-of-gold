RWStructuredBuffer<float4> Output : register(u1);
float4 main(float4 position : SV_Position) : SV_Target {
    Output[uint(position.x)].z = position.y;
    return position;
}
