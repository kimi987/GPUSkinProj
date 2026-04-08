using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    //一次行分配大的内存作为路点
    //每个使用路点的单位分配固定大小的路点，这样就不用执行内存整理
    public class PathData
    {
        private static PathData _instance;

        public static PathData Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new PathData();

                return _instance;
            }
        }
        private const int NumOfAll = 65536;
        private const int NumOfEachUnit = 50; //每个角色的路点分配数量
        private NativeArray<float3> _pathAry;
        private ComputeBuffer _pathBuffer;
        private int _lastIndex;
        private Queue<int> _freeIndexes;
        // private bool _computeSet;

        public PathData()
        {
            _pathAry = new NativeArray<float3>(NumOfAll, Allocator.Persistent);
            _freeIndexes = new(512);
            _lastIndex = 0;
        }

        public float3 GetPathPos(int index)
        {
            if (!_pathAry.IsCreated)
                return float3.zero;

            return _pathAry[index];
        }
        public ref NativeArray<float3> GetPathAry()
        {
            if ( !_pathAry.IsCreated)
            {
                //Test Status has been disposed
                _pathAry = new NativeArray<float3>(NumOfAll, Allocator.Persistent);
                _freeIndexes.Clear();
                _lastIndex = 0;

            }
            return ref _pathAry;
        }
        
        //type is compute Shader
        public void SetComputeShaderData(ComputeShader shader, int kernel)
        {
            // if (_computeSet)
            //     return;
            // _computeSet = true;
            if (_pathBuffer == null || !_pathBuffer.IsValid())
            {
                _pathBuffer = new ComputeBuffer(NumOfAll, 12); //float * 3
                _pathBuffer.SetData(_pathAry);
                // _pathAry.Dispose();
            }
            // _pathBuffer = new ComputeBuffer(NumOfAll, 12); //float * 3
            // _pathBuffer.SetData(_pathAry);
            shader.SetBuffer(kernel, ComputeShaderIds.PathAryName, _pathBuffer);
            // _pathAry.Dispose();
        }
        
        

        public int AddBufferPoses(Vector3[] poses)
        {
            if (poses.Length > NumOfEachUnit)
            {
                Debug.LogError($"Path point count {poses.Length} exceeds limit {NumOfEachUnit}.");
                return -1;
            }
            var resultIndex = _lastIndex;
            var fromLast = true;
            if (_freeIndexes.Count > 0)
            {
                fromLast = false;
                resultIndex = _freeIndexes.Dequeue();
            }
            var length = poses.Length;
            _pathBuffer.SetData(poses, 0, resultIndex, length);

            for (int i = 0; i < length; i++)
            {
                _pathAry[resultIndex + i] = poses[i];
            }
            
            if (fromLast)
            {
                if (NumOfAll - _lastIndex < NumOfEachUnit)
                {
                    Debug.LogError("Total path point count exceeds limit.");
                    return -1;
                }
                _lastIndex += NumOfEachUnit;
            }
            return resultIndex;
        }

        public int AddInstancePoses(Vector3[] poses)
        {
            if (poses.Length > NumOfEachUnit)
            {
                Debug.LogError($"Path point count {poses.Length} exceeds limit {NumOfEachUnit}.");
                return -1;
            }
            
            var resultIndex = _lastIndex;
            var fromLast = true;
            if (_freeIndexes.Count > 0)
            {
                fromLast = false;
                resultIndex = _freeIndexes.Dequeue();
            }
            var length = poses.Length;

            for (int i = 0; i < length; i++)
            {
                _pathAry[resultIndex + i] = poses[i];
            }
            
            if (fromLast)
            {
                if (NumOfAll - _lastIndex < NumOfEachUnit)
                {
                    Debug.LogError("Total path point count exceeds limit.");
                    return -1;
                }
                _lastIndex += NumOfEachUnit;
            }

            return resultIndex;
        }

        public void RemovePos(int index)
        {
            if (index < 0)
                return;
            
            _freeIndexes.Enqueue(index);
        }

        public void Dispose()
        {
            // _computeSet = false;
            if (_pathAry.IsCreated)
                _pathAry.Dispose();
            _pathBuffer?.Dispose();
        }
    }
}