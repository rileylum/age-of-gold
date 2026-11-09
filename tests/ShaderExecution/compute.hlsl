RWStructuredBuffer<float4> Output : register(u0);
[numthreads(1, 1, 1)]
void main(uint3 id : SV_DispatchThreadID) {
    Output[id.x] = float4(1, 2, 3, 4);
}
