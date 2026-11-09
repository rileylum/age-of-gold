float4 main(uint id : SV_VertexID) : SV_Position {
    float2 p = float2(id == 1 ? 3 : -1, id == 2 ? -3 : 1);
    return float4(p, 0, 1);
}
