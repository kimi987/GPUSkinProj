#ifndef UNPACKED_INCLUDED
#define UNPACKED_INCLUDED

float2 simpleUnpack16(float packedData)
{
    uint packed = asuint(packedData);
    float f1 = f16tof32(packed >> 16); 
    float f2 = f16tof32(packed);

    return float2(f1, f2);
}

float2 unpack1616(float packedData)
{
    uint u = asuint(packedData);

    uint sign1 = (u>>31) & 0x1;
    uint sign2 = (u>>15) & 0x1;

    uint u1 = (u >> 16) & 0x7FFF;
    uint u2 = u & 0x7FFF;

    float x = u1 / 30000.0 * (sign1 * -2.0 + 1.0);
    float y = u2 / 30000.0 * (sign2 * -2.0 + 1.0);

    return float2(x, y);
}
float3 unpack111110(float packedData)
{
    uint u = asuint(packedData);

    uint sign1 = (u>>31) & 0x1;
    uint sign2 = (u>>20) & 0x1;
    uint sign3 = (u>>9) & 0x1;

    uint u1 = (u >> 21) & 0x3FF;
    uint u2 = (u >> 10) & 0x3FF;
    uint u3 = u & 0x1FF;

    float x = u1 / 500.0 * (sign1 * -2.0 + 1.0);
    float y = u2 / 1000.0 * (sign2 * -2.0 + 1.0);
    float z = u3 / 500.0 * (sign3 * -2.0 + 1.0);
    
    return float3(x, y, z);
}



#endif