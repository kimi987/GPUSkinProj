#ifndef MATRIX_INCLUDED
#define MATRIX_INCLUDED

//euler to base matrix
float4x4 EulerToMatrix(float3 euler)
{
    float3 s, c;
    sincos(euler, s, c);

    float4x4 m = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);

    m[0][0] = c.y * c.z + s.x * s.y * s.z;
    m[0][1] = c.z * s.x * s.y - c.y * s.z;
    m[0][2] = c.x * s.y;

    m[1][0] = c.x * s.z;
    m[1][1] = c.x * c.z;
    m[1][2] = -s.x;

    m[2][0] = c.y * s.x * s.z - s.y * c.z;
    m[2][1] = s.y * s.z + c.y * c.z * s.x;
    m[2][2] = c.x * c.y;

    return m;
}

float4x4 CreateTRSMatrix(float3 pos, float3 euler, float scale)
{
    euler = -euler;
    float3 eulerRad = euler * 0.0174532925f;
    float3 s, c;
    sincos(eulerRad, s, c);

    float4x4 m;

    // --- 第一行 (X方向轴 + 缩放) ---
    m[0][0] = (c.y * c.z + s.x * s.y * s.z) * scale;
    m[0][1] = (c.z * s.x * s.y - c.y * s.z) * scale;
    m[0][2] = (c.x * s.y) * scale;
    m[0][3] = 0.0; // 注意：这里不是放 pos.x 的地方

    // --- 第二行 (Y方向轴 + 缩放) ---
    m[1][0] = (c.x * s.z) * scale;
    m[1][1] = (c.x * c.z) * scale;
    m[1][2] = (-s.x) * scale;
    m[1][3] = 0.0;

    // --- 第三行 (Z方向轴 + 缩放) ---
    m[2][0] = (c.y * s.x * s.z - s.y * c.z) * scale;
    m[2][1] = (s.y * s.z + c.y * c.z * s.x) * scale;
    m[2][2] = (c.x * c.y) * scale;
    m[2][3] = 0.0;

    // --- 第四行 (真正的 Position 位置) ---
    m[3][0] = pos.x;
    m[3][1] = pos.y;
    m[3][2] = pos.z;
    m[3][3] = 1.0;

    return m;
}

float4x4 ReconstructMatrixScale(float3 pos, float4 quan, float scale)
{
    // 1. 依然建议归一化，防止形变
    // quan = normalize(quan);

    float4x4 m;
    float x2 = quan.x + quan.x; float y2 = quan.y + quan.y; float z2 = quan.z + quan.z;
    float xx = quan.x * x2; float xy = quan.x * y2; float xz = quan.x * z2;
    float yy = quan.y * y2; float yz = quan.y * z2; float zz = quan.z * z2;
    float wx = quan.w * x2; float wy = quan.w * y2; float wz = quan.w * z2;

    // --- 第一列 (对应原来矩阵的第一行) ---
    m[0][0] = (1.0 - (yy + zz)) * scale;
    m[1][0] = (xy - wz) * scale;
    m[2][0] = (xz + wy) * scale;
    m[3][0] = pos.x; // <--- 位移现在在列底部（即第四行的第一个元素）

    // --- 第二列 (对应原来矩阵的第二行) ---
    m[0][1] = (xy + wz) * scale;
    m[1][1] = (1.0 - (xx + zz)) * scale;
    m[2][1] = (yz - wx) * scale;
    m[3][1] = pos.y; // <--- 位移

    // --- 第三列 (对应原来矩阵的第三行) ---
    m[0][2] = (xz - wy) * scale;
    m[1][2] = (yz + wx) * scale;
    m[2][2] = (1.0 - (xx + yy)) * scale;
    m[3][2] = pos.z; // <--- 位移
    
    // --- 第四列 (固定值) ---
    m[0][3] = 0.0;
    m[1][3] = 0.0;
    m[2][3] = 0.0;
    m[3][3] = 1.0;

    return m;
}

float4x4 ReconstructMatrixNoScale(float3 pos, float4 quan)
{
    // 1. 依然建议归一化，防止形变
    // quan = normalize(quan);

    float4x4 m;
    float x2 = quan.x + quan.x; float y2 = quan.y + quan.y; float z2 = quan.z + quan.z;
    float xx = quan.x * x2; float xy = quan.x * y2; float xz = quan.x * z2;
    float yy = quan.y * y2; float yz = quan.y * z2; float zz = quan.z * z2;
    float wx = quan.w * x2; float wy = quan.w * y2; float wz = quan.w * z2;

    // --- 第一列 (对应原来矩阵的第一行) ---
    m[0][0] = 1.0 - (yy + zz);
    m[1][0] = xy - wz;
    m[2][0] = xz + wy;
    m[3][0] = pos.x; // <--- 位移现在在列底部（即第四行的第一个元素）

    // --- 第二列 (对应原来矩阵的第二行) ---
    m[0][1] = xy + wz;
    m[1][1] = 1.0 - (xx + zz);
    m[2][1] = yz - wx;
    m[3][1] = pos.y; // <--- 位移

    // --- 第三列 (对应原来矩阵的第三行) ---
    m[0][2] = xz - wy;
    m[1][2] = yz + wx;
    m[2][2] = 1.0 - (xx + yy);
    m[3][2] = pos.z; // <--- 位移

    // --- 第四列 (固定值) ---
    m[0][3] = 0.0;
    m[1][3] = 0.0;
    m[2][3] = 0.0;
    m[3][3] = 1.0;

    return m;
}

// Cal Y Rotation
float GetYRotation(float3 dir)
{
    float radians = atan2(dir.x, dir.z);

    float degrees = radians * (180 / 3.14159265);

    return (degrees < 0) ? (degrees + 360.0) : degrees;
}

float Repeat(float t, float length)
{
    return t - floor(t / length) * length;
}

float GetDeltaAngle(float current, float target)
{
    float num = Repeat(target - current, 360.0);
    if (num > 180.0)
        num -= 360.0;
    return num;
}

#endif