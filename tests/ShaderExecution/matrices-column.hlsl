cbuffer Frames : register(b0) {
    column_major float2x3 frames[2] : packoffset(c2);
};

float4 main(float4 position : SV_Position) : SV_Target {
    uint index = (uint)position.x & 1;
    return float4(frames[index][0], frames[index][1].z);
}
