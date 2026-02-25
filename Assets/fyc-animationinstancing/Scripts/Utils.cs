using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sirenix.Utilities;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public static class Utils
    {
        // Merge all bones to a single array and merge all bind pos
        public static Transform[] MergeBone(SkinnedMeshRenderer[] skinnedMeshRenderers, List<Matrix4x4> outBindPoses)
        {
            var boneTransforms = new List<Transform>(150);

            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                var bones = skinnedMeshRenderers[i].bones;
                var bindPoses = skinnedMeshRenderers[i].sharedMesh.bindposes;

                for (int j = 0; j < bones.Length; j++)
                {
                    Debug.Assert(bindPoses[j].determinant != 0, "The bind pose can not be zero.");
                    var index = boneTransforms.FindIndex(q => q == bones[j]);
                    if (index < 0)
                    {
                        // not find
                        boneTransforms.Add(bones[j]);
                        outBindPoses.Add(bindPoses[j]);
                    } else 
                        outBindPoses[index] = bindPoses[j];
                }

                skinnedMeshRenderers[i].enabled = false;
            }
            return boneTransforms.ToArray();
        }

        public static Quaternion QuaternionFromMatrix(Matrix4x4 mat)
        {
            Vector3 forward;
            forward.x = mat.m02;
            forward.y = mat.m12;
            forward.z = mat.m22;
            
            Vector3 up;
            up.x = mat.m01;
            up.y = mat.m11;
            up.z = mat.m21;
            
            return Quaternion.LookRotation(forward, up);
        }

        public static Matrix4x4[] CalculateSkinMatrix(Transform[] bonePose, Matrix4x4[] bindPose)
        {
            if (bonePose.Length == 0)
                return null;
            var root = bonePose[0];
            while (root.parent != null)
            {
                root = root.parent;
            }

            var rootMat = root.worldToLocalMatrix;
            
            var matrix = new Matrix4x4[bonePose.Length];

            for (int i = 0; i < bonePose.Length; i++)
            {
                matrix[i] = rootMat * bonePose[i].localToWorldMatrix * bindPose[i];
            }
            return matrix;
        }

        public static Color[] Convert2Color(Matrix4x4[] boneMatrix)
        {
            var colors = new Color[boneMatrix.Length * 4];
            int index = 0;
            foreach (var obj in boneMatrix)
            {
                colors[index++] = obj.GetRow(0);
                colors[index++] = obj.GetRow(1);
                colors[index++] = obj.GetRow(2);
                colors[index++] = obj.GetRow(3);
            }

            return colors;
        }

        /// <summary>
        /// 数据压缩后存入RGBA32
        /// 10 11 11?
        /// </summary>
        /// <param name="boneMatrix"></param>
        /// <returns></returns>
        public static Color[] Convert2SingleColor(Matrix4x4[] boneMatrix)
        {
            var colors = new Color[boneMatrix.Length];

            for (int i = 0; i < boneMatrix.Length; i++)
            {
                var c = new Color();
                var row1 =  boneMatrix[i].GetRow(0);
                var row2 =  boneMatrix[i].GetRow(1);
                var row3 =  boneMatrix[i].GetRow(2);
                Debug.LogError("boneMatrix[i] = " + boneMatrix[i]);
                c.r = PackThreeFloats(row1.x, row2.x, row3.x);
                c.g = PackThreeFloats(row1.y, row2.y, row3.y);
                c.b = PackThreeFloats(row1.z, row2.z, row3.z);
                c.a = PackThreeFloats(row1.w, row2.w, row3.w);
                colors[i] = c;
                Debug.LogError("PackThreeFloats(row1.x, row2.x, row3.x) = " + PackThreeFloats(row1.x, row2.x, row3.x));
                Debug.LogError("Color = " + c.r);
            }
            return colors;
        }

        public static Color[] ConvertToPosRotColor(Matrix4x4[] boneMatrix)
        {
            var colors = new Color[boneMatrix.Length];
            
            for (int i = 0; i < boneMatrix.Length; i++)
            {
                var c = new Color();
                var position = boneMatrix[i].GetPosition();
                var rotation = boneMatrix[i].rotation;
                
                var scale = boneMatrix[i].lossyScale.x;
                c.r = PackTwoFloats(position.x, rotation.x);
                c.g = PackTwoFloats(position.y, rotation.y);
                c.b = PackTwoFloats(position.z, rotation.z);
                c.a = PackTwoFloats(rotation.w, scale);
                colors[i] = c;
                
                // Debug.LogError($"111 {colors[i].r},{colors[i].g},{colors[i].b},{colors[i].a}");
            }

            return colors;
        }
        
        public static Color[] ConvertToPosRotColor2(Matrix4x4[] boneMatrix)
        {
            var colors = new Color[boneMatrix.Length * 2];
            
            int index = 0;
            foreach (var obj in boneMatrix)
            {
                colors[index++] = new Color(obj.GetPosition().x, obj.GetPosition().y, obj.GetPosition().z, obj.lossyScale.x);
                colors[index++] = new Color(obj.rotation.x, obj.rotation.y, obj.rotation.z, obj.rotation.w);
            }

            return colors;
        }
        
        // 定义内存对齐结构，用于 float 和 uint 的无损转换
        [StructLayout(LayoutKind.Explicit)]
        struct FloatIntUnion
        {
            [FieldOffset(0)] public float f;
            [FieldOffset(0)] public uint u;
        }
        
        public static float PackTwoFloats(float f1, float f2)
        {
            // 1. 将两个 float 转为 16 位半精度表示 (ushort)
            ushort h1 = Mathf.FloatToHalf(f1);
            ushort h2 = Mathf.FloatToHalf(f2);

            // 2. 拼成一个 32 位的 uint
            // h1 放在高 16 位，h2 放在低 16 位
            uint packed = ((uint)h1 << 16) | (uint)h2;

            // 3. 将 uint 的位数据直接解释为 float 返回
            // 使用 System.BitConverter.Int32BitsToSingle 效率最高（无 GC）
            return System.BitConverter.Int32BitsToSingle((int)packed);
        }
        // public static float PackTwoFloats(float f1, float f2)
        // {
        //     uint u1 = (uint)(Mathf.Abs(f1 * 30000f));
        //     uint u2 = (uint)(Mathf.Abs(f2 * 30000f));
        //     
        //     uint sign1 = f1 >= 0 ? 0u : 1u;
        //     uint sign2 = f2 >= 0 ? 0u : 1u;
        //
        //     uint packed = (uint)((sign1 << 31) + (u1 << 16 & 0x7FFF0000) + (sign2 << 15) + (u2 & 0x00007FFF));
        //     
        //     return BitConverter.ToSingle(BitConverter.GetBytes(packed), 0);
        // }

        public static float PackThreeFloats(float f1, float f2, float f3)
        {
            uint u1 = (uint)(Mathf.Abs(f1 * 500f));
            uint u2 = (uint)(Mathf.Abs(f2 * 1000f));
            uint u3 = (uint)(Mathf.Abs(f3 * 500f));
            
            uint sign1 = f1 >= 0 ? 0u : 1u;
            uint sign2 = f2 >= 0 ? 0u : 1u;
            uint sign3 = f3 >= 0 ? 0u : 1u;
            uint packed = (uint)((sign1 << 31) + (u1 << 21 & 0x7FE00000) + (sign2 << 20) + (u2 << 10 & 0x000FFC00) + (sign3 << 9) + (u3 & 0x000001FF));
            // int packed = (int)((union1.i << 21 & 0xFFE00000 ) | (union2.i << 10 & 0x001FFC00) | (union3.i & 0x000003FF));
            
            return BitConverter.ToSingle(BitConverter.GetBytes(packed), 0);
        }
        
        
        // 方向向量转欧拉角
        public static Vector3 DirectionToEulerAngles(Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                return Vector3.zero;
            }
            return Quaternion.LookRotation(direction).eulerAngles;
        }
        //Get Delta Angle
        public static float DeltaAngle(float current, float target)
        {
            float num = Mathf.Repeat(target - current, 360f);
            if (num > 180.0)
                num -= 360f;
            return num;
        }

    }
}