cbuffer Settings : register(b0) { uint mode; float3 padding; };

float4 main(float4 position : SV_Position) : SV_Target {
    [branch] switch (mode) {
        case 0: return float4(0, 1, 2, 3);
        case 1: return float4(1, 2, 3, 4);
        case 2: return float4(2, 3, 4, 5);
        case 3: return float4(3, 4, 5, 6);
        default: return float4(-1, -2, -3, -4);
    }
}
