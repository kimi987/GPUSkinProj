using System;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class ToolTest :SerializedMonoBehaviour
    {
        public Material testMat;
        public Vector3 testData;

        public AnimationData animationData;
        public ComputeShader testCompute;

        private ComputeBuffer _resultBuffer;
        private ComputeBuffer _resultMatrixBuffer;
        [Button]
        public void SetDataTest()
        {
            float value = Utils.PackThreeFloats(testData.x, testData.y, testData.z);
            testMat.SetFloat("_Data", value);
        }
        [Button]
        public void TestCompute()
        {
            _resultBuffer = new ComputeBuffer(1, sizeof(float) * 4);
            float value = Utils.PackThreeFloats(testData.x, testData.y, testData.z);
            int kernel = testCompute.FindKernel("CSTest");
            testCompute.SetFloat("_PackVal", value);
            testCompute.SetBuffer(kernel, "_Result", _resultBuffer);
            testCompute.Dispatch(kernel, 1, 1, 1);
            var ary = new float4[1];
            _resultBuffer.GetData(ary, 0, 0, 1);
            Debug.LogError("Result = " + ary[0]);
            _resultBuffer.Release();
        }
        [Button]
        public void TestComputeMatrix()
        {
            var boneTexture =  animationData.CreateBoneTexture();
            _resultBuffer = new ComputeBuffer(boneTexture.width * boneTexture.height, sizeof(float) * 4);
            _resultMatrixBuffer = new ComputeBuffer(boneTexture.width * boneTexture.height, sizeof(float) * 16);
            
            int kernel = testCompute.FindKernel("CSLoadTexture");
            testCompute.SetTexture(kernel, "InputTexture", boneTexture);
            testCompute.SetBuffer(kernel, "_Result", _resultBuffer);
            testCompute.SetBuffer(kernel, "_MatrixResult", _resultMatrixBuffer);
            testCompute.Dispatch(kernel, boneTexture.width/8, boneTexture.height/8, 1);
            var ary = new float4x4[5];
            _resultMatrixBuffer.GetData(ary, 0, 0, 5);
            foreach (var d in ary)
            {
                Debug.LogError("d = " + d);
            } 
            var ary1 = new float4[5];
            _resultBuffer.GetData(ary1, 0, 0, 5);
            foreach (var d in ary1)
            {
                Debug.LogError("d1 = " + d);
            } 
            _resultBuffer.Release();
        }
        [Button]
        public void TestPosRotToMatrix()
        {
            _resultMatrixBuffer = new ComputeBuffer(1, sizeof(float) * 16);
            int kernel = testCompute.FindKernel("CSTestPosRotToMatrix");
            var pos = transform.localToWorldMatrix.GetPosition();
            var rot = transform.localToWorldMatrix.rotation;
            Debug.LogError("pos = " + pos);
            Debug.LogError("rot = " + rot);
            Debug.LogError("transform.localToWorldMatrix = " + transform.localToWorldMatrix);
            testCompute.SetVector("_InPos", new Vector3(pos.x, pos.y, pos.z));
            testCompute.SetVector("_InQuan", new Vector4(rot.x, rot.y, rot.z, rot.w));
            testCompute.SetBuffer(kernel, "_MatrixResult", _resultMatrixBuffer);
            testCompute.Dispatch(kernel, 1, 1, 1);
            var ary = new float4x4[1];
            _resultMatrixBuffer.GetData(ary, 0, 0, 1);
            foreach (var d in ary)
            {
                Debug.LogError("d = " + d);
            } 
            _resultMatrixBuffer.Release();
        }

        [Button]
        public void TestComputePosMatrix()
        {
            _resultBuffer = new ComputeBuffer(2, sizeof(float) * 4);
            _resultMatrixBuffer = new ComputeBuffer(1, sizeof(float) * 16);
            int kernel = testCompute.FindKernel("CSTestPositon");
            var pos = transform.localToWorldMatrix.GetPosition();
            var rot = transform.localToWorldMatrix.rotation;
            var scale = transform.localToWorldMatrix.lossyScale.x;
            float r1 = Utils.PackTwoFloats(pos.x, rot.x);
            float r2 = Utils.PackTwoFloats(pos.y, rot.y);
            float r3 = Utils.PackTwoFloats(pos.z, rot.z);
            float r4 = Utils.PackTwoFloats(rot.w, scale);
            testCompute.SetVector("_PackColor", new Vector4(r1, r2, r3, r4));
            testCompute.SetBuffer(kernel, "_MatrixResult", _resultMatrixBuffer);
            testCompute.SetBuffer(kernel, "_Result", _resultBuffer);
            Debug.LogError("origin = "  + transform.localToWorldMatrix);
            Debug.LogError("pos = " + pos);
            Debug.LogError("rot = " + rot);
            testCompute.Dispatch(kernel, 1, 1, 1);
            
            var ary = new float4x4[1];
            _resultMatrixBuffer.GetData(ary, 0, 0, 1);
            foreach (var d in ary)
            {
                Debug.LogError("d = " + d);
            } 
            
            var ary1 = new float4[2];
            _resultBuffer.GetData(ary1, 0, 0, 2);
            foreach (var d in ary1)
            {
                Debug.LogError("d1 = " + d);
            } 
            _resultBuffer.Release();
            _resultMatrixBuffer.Release();
        }
        
        [Button]
        public void TestBake2Data(float f1, float f2)
        {
            float value = Utils.PackTwoFloats(f1, f2);
            Debug.LogError(value);
            Debug.LogError(BitConverter.ToUInt32(BitConverter.GetBytes(value)));
            uint packed = BitConverter.ToUInt32(BitConverter.GetBytes(value));
            
            uint sign1 = (packed>>31) & 0x1;
            uint sign2 = (packed>>15) & 0x1;
            Debug.LogError($"sign1 = {sign1}, sign2 = {sign2}");
            uint u1 = (packed >> 16) & 0x7FFF;
            uint u2 = packed & 0x7FFF;
            
            Debug.LogError($"u1 = {u1}, u2 = {u2}");
            
            float r1 = u1 / 20000f * (sign1 * -2 + 1);
            float r2 = u2 / 20000f * (sign2 * -2 + 1);
            
            Debug.LogError($"r1 = {r1}, r2 = {r2}");
        }
        
        [Button]
        public void TestBake3Data(float f1, float f2, float f3)
        {
            float value = Utils.PackThreeFloats(f1, f2, f3);
            Debug.LogError(value);
            Debug.LogError(BitConverter.ToUInt32(BitConverter.GetBytes(value)));
            uint packed = BitConverter.ToUInt32(BitConverter.GetBytes(value));

            uint sign1 = packed >> 31;
            uint sign2 = (packed >> 20) & 0x1;
            uint sign3 = (packed >> 9) & 0x1;
            
            Debug.LogError($"sign1 = {sign1}, sign2 = {sign2},sign3 = {sign3}");

            uint u1 = packed >> 21 & 0x3FF;
            uint u2 = (packed >> 10) & 0x3FF;
            uint u3 = packed & 0x1FF;
            Debug.LogError($"u1 = {u1}, u2 = {u2},u3 = {u3}");

            float r1 = u1 / 500.0f * (sign1 * -2 + 1);
            float r2 = u2 / 1000.0f * (sign2 * -2 + 1);
            float r3 = u3 / 500.0f * (sign3 * -2 + 1);
            
            Debug.LogError($"r1 = {r1}, r2 = {r2}, r3 = {r3}");
        }
        [Button]
        public void DataTest(uint inNum)
        {
            Debug.LogError((inNum & 0x000001FF) + (1 << 9));
        }
        
    }
    
}