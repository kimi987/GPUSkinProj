using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class ChildData
    {
        public string UnitName;
        public int InstanceIndex = -1;
    }

    public struct PosRotCache
    {
        public float3 Position;
        public float3 Rotation;
        public bool IsDirty;
        //public float LastTime;

        public bool TimeOut()
        {
            return IsDirty ;//&& Time.realtimeSinceStartup - LastTime > 1f;
        }
    }
    
    public class ParentDateDefine
    {
        private NativeArray<MotionData> _motionDataArray;
        private PosRotCache[] _posRotCacheArray;
        private ComputeBuffer _motionDataBuffer;
        private int _cullingKernel;
        private Stack<int> AvaSlots;
        private int _curCount;
        private int _maxCount;
        private AnimationDrawType _drawType;
        private MotionData[] _tempMotionDataAry;
        private MotionData[] _motionDataReadbackCache;
        private int _motionDataReadbackFrame = -1;
        private bool _motionDataReadbackValid;
        private MotionData[] _motionDataWriteCache;
        private int _motionDataWriteCacheFrame = -1;
        private bool _hasPendingMotionWrite;
        private ComputeShader _animationDrawShader;
        //child Dict
        private Dictionary<int, Dictionary<int, ChildData>> _childDataDict = new(2048);

        public ParentDateDefine(ComputeShader animationDrawShader, int defaultNum = 4096)
        {
            Debug.LogError("Create ParentDateDefine Buffer");
            //Compute
            _drawType = AnimationDrawType.Buff;
            _animationDrawShader = animationDrawShader;
            _maxCount = defaultNum;
            _cullingKernel = _animationDrawShader.FindKernel(ComputeShaderIds.ComputeCullingAnimationKernel);
            _motionDataArray = new NativeArray<MotionData>(defaultNum, Allocator.Persistent);
            _posRotCacheArray = new PosRotCache[defaultNum];
            _motionDataBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<MotionData>());
            _tempMotionDataAry = new MotionData[1];
            _motionDataReadbackCache = new MotionData[defaultNum];
            _motionDataWriteCache = new MotionData[defaultNum];
            _animationDrawShader.SetBuffer(_cullingKernel, ComputeShaderIds.ParentMotionBufferName, _motionDataBuffer);
            AvaSlots = new(defaultNum);
            _motionDataArray.Dispose();
        }

        public ParentDateDefine(int defaultNum = 4096)
        {
            _drawType = AnimationDrawType.Instance;
            _maxCount = defaultNum;
            _motionDataArray = new NativeArray<MotionData>(defaultNum, Allocator.Persistent);
            _posRotCacheArray = new PosRotCache[defaultNum];
            _tempMotionDataAry = new MotionData[1];
            AvaSlots = new(defaultNum);
        }

        public void SetChild(int parentId, int posId, string unitName, int instanceIndex)
        {
            if (_childDataDict.TryGetValue(parentId, out var childDataDic))
            {
                if (childDataDic.TryGetValue(posId, out var childData))
                {
                    if (childData != null && (childData.UnitName != unitName || childData.InstanceIndex != instanceIndex))
                    {
                        AnimationDrawMgr.Instance.RemoveInstance(childData.UnitName, childData.InstanceIndex);
                        childData.UnitName = unitName;
                        childData.InstanceIndex = instanceIndex;
                        return;
                    }
                }
                childData = new ChildData();
                childData.UnitName = unitName;
                childData.InstanceIndex = instanceIndex;
                childDataDic[posId] = childData;
                return;
            }

            childDataDic = new(16);
            _childDataDict[parentId] = childDataDic;
            
            var newChildData = new ChildData();
            newChildData.UnitName = unitName;
            newChildData.InstanceIndex = instanceIndex;
            childDataDic[posId] = newChildData;
        }

        public void RemoveChild(int parentId, int posId)
        {
            if (_childDataDict.TryGetValue(parentId, out var childDataDic))
            {
                if (childDataDic.TryGetValue(posId, out var childData))
                {
                    if (childData != null && childData.UnitName != string.Empty && childData.InstanceIndex >= 0)
                        AnimationDrawMgr.Instance.RemoveInstance(childData.UnitName, childData.InstanceIndex);
                    childDataDic.Remove(posId);
                }
            }
        }

        public void ClearChild(int parentId)
        {
            if (_childDataDict.TryGetValue(parentId, out var childDataDic))
            {
                foreach (var kv in childDataDic)
                {
                    var childData = kv.Value;
                    if (childData != null && childData.UnitName != string.Empty && childData.InstanceIndex >= 0)
                        AnimationDrawMgr.Instance.RemoveInstance(childData.UnitName, childData.InstanceIndex);
                }
                childDataDic.Clear();
            }
        }
        
        public void ClearAllChild() 
        {
            foreach (var childDataDicKV in _childDataDict)
            {
                foreach (var kv in childDataDicKV.Value)
                {
                    var childData = kv.Value;
                    if (childData != null && childData.UnitName != string.Empty && childData.InstanceIndex >= 0)
                        AnimationDrawMgr.Instance.RemoveInstance(childData.UnitName, childData.InstanceIndex);
                }
            }
            _childDataDict.Clear();
        }

        public int AddParent(uint layerMask)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return AddInstanceParent(layerMask);
                case AnimationDrawType.Buff:
                    return AddBuffParent(layerMask);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public int AddInstanceParent(uint layerMask)
        {
            var index = -1;

            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                var data = _motionDataArray[index];
                data.Reset(layerMask);
                _motionDataArray[index] = data;
                return index;
            }

            if (_curCount >= _maxCount)
            {
                Debug.LogError("Add Parent Failed! Count exceed max count!");
                return -1;
            }
            var motionData = _motionDataArray[_curCount];
            motionData.Reset(layerMask);
            _motionDataArray[_curCount] = motionData;
            index = _curCount++;
            return index;
        }

        public void RemoveParent(int index)
        {
            ClearChild(index);
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    RemoveInstanceParent(index);
                    break;
                case AnimationDrawType.Buff:
                    RemoveBuffParent(index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RemoveInstanceParent(int index)
        {
            var data = _motionDataArray[index];
            if (data.PathIndex >= 0)
                PathData.Instance.RemovePos(data.PathIndex);

            data.Enable = 0;
            _motionDataArray[index] = data;
            AvaSlots.Push(index);
        }
        
        public void RemoveBuffParent(int index)
        {
            var data = GetMotionData(index);
            if (data.PathIndex >= 0)
                PathData.Instance.RemovePos(data.PathIndex);
            data.Enable = 0;
            SetMotionData(index, data);
            AvaSlots.Push(index);
        }

        public ref NativeArray<MotionData> GetMotionDataAry()
        {
            return ref _motionDataArray;
        }

        private MotionData GetMotionData(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return _motionDataArray[index];
                case AnimationDrawType.Buff:
                    if (index >= _curCount)
                        return default;
                    if (_hasPendingMotionWrite || _motionDataWriteCacheFrame == Time.frameCount)
                        return _motionDataWriteCache[index];
                    EnsureMotionDataReadback();
                    return _motionDataReadbackCache[index];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void SetMotionData(int index, MotionData data)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    EnsureMotionDataWriteCache();
                    _motionDataWriteCache[index] = data;
                    _hasPendingMotionWrite = true;
                    InvalidateMotionDataReadback();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public int AddBuffParent(uint layerMask)
        {
            var index = -1;
            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                var data = GetMotionData(index);
                data.Reset(layerMask);
                SetMotionData(index, data);
                return index;
            }
            
            if (_curCount >= _maxCount)
            {
                Debug.LogError("Add Parent Failed! Count exceed max count!");
                return -1;
            }
            var initData = GetMotionData(_curCount);
            initData.Reset(layerMask);
            SetMotionData(_curCount, initData);
            index = _curCount++;
            return index;
        }

        public void UpdateParentMotionJob(uint layerMask, float deltaTime, NativeArray<Plane> planeNativeArray)
        {
            if (_curCount <= 0)
                return;
            
            var job = new ParentCullingMotionJob()
            {
                DeltaTime = deltaTime,
                LayerMask = layerMask,
                MotionData = AnimationDrawMgr.Instance.GetParentMotionData(),
                PathAry = PathData.Instance.GetPathAry(),
                Planes = planeNativeArray,
            };
            var handle = job.Schedule(_curCount, 64);
            handle.Complete();
        }

        public void UpdateParentMotionBuffer()
        {
            if (_curCount <= 0)
                return;
            
            FlushPendingMotionWrites();
            _animationDrawShader.SetBuffer(_cullingKernel, ComputeShaderIds.MotionBufferName, _motionDataBuffer);
            _animationDrawShader.SetInt(ComputeShaderIds.CullingType, 1);
            _animationDrawShader.SetInt(ComputeShaderIds.InstanceCount, _curCount);

            _animationDrawShader.Dispatch(_cullingKernel, Mathf.CeilToInt(_curCount / 64f), 1, 1);
            InvalidateMotionDataReadback();
            
            //Debug
            // _motionDataBuffer.GetData(_tempMotionDataAry, 0, 0, 1);
            // if (_tempMotionDataAry[0].RotateYSpeed > 0)
            //     Debug.LogError($"_tempMotionDataAry[0] = {_tempMotionDataAry[0].Rotation}_{_tempMotionDataAry[0].RotateYTarget}__{_tempMotionDataAry[0].RotateYSpeed}");
            
        }

        #region move rotate

        public void MoveTo(int index, float3 targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity)
        {
            SetCacheDirty(index);
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    data.PathIndex = -1;
                    data.CurPathIndex = 0;
                    targetPos.y = data.Position.y;
                    data.TargetPos = targetPos;
                    Vector3 moveVec = targetPos - data.Position;
                    data.MoveDirection = math.normalize(moveVec);
                    data.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    
                    data.MoveSpeed = moveSpeed;
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYFinal = finalYaw;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    var buffData = GetMotionData(index);
                    if (buffData.PathIndex >= 0)
                        PathData.Instance.RemovePos(buffData.PathIndex);
                    targetPos.y = buffData.Position.y;
                    buffData.PathIndex = -1;
                    buffData.CurPathIndex = 0;
                    buffData.TargetPos = targetPos;
                    buffData.MoveSpeed = moveSpeed;
                    buffData.RotateYSpeed = rotateSpeed;
                    buffData.RotateYSpeedSetting = rotateSpeed;
                    moveVec = targetPos - buffData.Position;
                    buffData.MoveDirection = math.normalize(moveVec);
                    buffData.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    buffData.RotateYFinal = finalYaw;
                    SetMotionData(index, buffData);
                    
                    // Debug.LogError($" set _tempMotionDataAry[0] = {_tempMotionDataAry[0].Position}__{_tempMotionDataAry[0].MoveSpeed}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveTo(int index, Vector3[] targetPos, float moveSpeed, float rotateSpeed, float finalYaw = Single.PositiveInfinity)
        {
            SetCacheDirty(index);
            var firstPos = targetPos[0];
            
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    
                    var data = _motionDataArray[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    
                    var pathIndex = PathData.Instance.AddInstancePoses(targetPos);
                    if (pathIndex < 0)
                    {
                        data.StopMove();
                        _motionDataArray[index] = data;
                        break;
                    }
                    data.PathIndex = pathIndex + targetPos.Length - 1;
                    data.CurPathIndex = pathIndex;
                    firstPos.y = data.Position.y;
                    Vector3 moveVec = firstPos - (Vector3)data.Position;
                    data.MoveDirection = math.normalize(moveVec);
                    data.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    data.MoveSpeed = moveSpeed;
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYFinal = finalYaw;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    var buffData = GetMotionData(index);
                    if (buffData.PathIndex >= 0)
                        PathData.Instance.RemovePos(buffData.PathIndex);
                    pathIndex = PathData.Instance.AddBufferPoses(targetPos);
                    if (pathIndex < 0)
                    {
                        buffData.StopMove();
                        SetMotionData(index, buffData);
                        break;
                    }
                    buffData.PathIndex = pathIndex + targetPos.Length - 1;
                    buffData.CurPathIndex = pathIndex;//+ targetPos.Length - 1;
                    buffData.MoveSpeed = moveSpeed;
                    buffData.RotateYSpeed = rotateSpeed;
                    buffData.RotateYSpeedSetting = rotateSpeed;
                    firstPos.y = buffData.Position.y;
                    moveVec = firstPos - (Vector3)buffData.Position;
                    buffData.MoveDirection = math.normalize(moveVec);
                    buffData.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    buffData.RotateYFinal = finalYaw;
                    SetMotionData(index, buffData);
                    
                    // Debug.LogError($" 11set _tempMotionDataAry[0] = {_tempMotionDataAry[0].Position}__{_tempMotionDataAry[0].MoveSpeed}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveDir(int index, float3 dir, float speed)
        {
            SetCacheDirty(index);
            var data = GetMotionData(index);
            if (data.PathIndex >= 0)
                PathData.Instance.RemovePos(data.PathIndex);
            data.MoveSpeed = speed;
            data.MoveDirection = math.normalize(dir);
            SetMotionData(index, data);
        }
        
        // stop moving
        public void StopMove(int index)
        {
            var data = GetMotionData(index);
            if (data.PathIndex >= 0)
                PathData.Instance.RemovePos(data.PathIndex);
            data.StopMove();
            SetMotionData(index, data);
        }

        public void RotateTo(int index, float targetY, float rotateSpeed)
        {
            SetCacheDirty(index);
            var data = GetMotionData(index);
            data.RotateYSpeed = rotateSpeed;
            data.RotateYSpeedSetting = rotateSpeed;
            data.RotateYFinal = targetY;
            SetMotionData(index, data);
        }
        public void RotateY(int index, float speed, string animationName = "")
        {
            SetCacheDirty(index);
            var data = GetMotionData(index);
            data.RotateYSpeed = speed;
            data.RotateYSpeedSetting = speed;
            SetMotionData(index, data);
        }
        
        // stop rotate
        public void StopRotate(int index)
        {
            var data = GetMotionData(index);
            data.StopRotate();
            SetMotionData(index, data);
        }
        #endregion
        
        #region TRS

        //dirty means translation
        public bool GetIsDirty(int index)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            if (!posRotData.TimeOut())
                return posRotData.IsDirty;
            RefreshCache(index, ref posRotData);
            return posRotData.IsDirty;
        }

        private void RefreshCache(int index, ref PosRotCache posRotCache)
        {
            //posRotCache.LastTime = Time.realtimeSinceStartup;
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    posRotCache.IsDirty = data.MoveSpeed > 0.001 || data.RotateYSpeed >= 0.001;
                    posRotCache.Position = data.Position;
                    posRotCache.Rotation = data.Rotation;
                    break;
                case AnimationDrawType.Buff:
                    var bufferData = GetMotionData(index);
                    posRotCache.IsDirty = bufferData.MoveSpeed > 0.001 || bufferData.RotateYSpeed >= 0.001;
                    posRotCache.Position = bufferData.Position;
                    posRotCache.Rotation = bufferData.Rotation;
                    break;
                default:
                    break;
            }
        }

        private void InvalidateMotionDataReadback()
        {
            _motionDataReadbackValid = false;
            _motionDataReadbackFrame = -1;
        }

        private void EnsureMotionDataWriteCache()
        {
            if (_drawType != AnimationDrawType.Buff)
                return;
            if (_hasPendingMotionWrite)
                return;
            if (_motionDataWriteCacheFrame == Time.frameCount)
                return;

            EnsureMotionDataReadback();
            Array.Copy(_motionDataReadbackCache, _motionDataWriteCache, _curCount);
            _motionDataWriteCacheFrame = Time.frameCount;
        }

        private void FlushPendingMotionWrites()
        {
            if (_drawType != AnimationDrawType.Buff)
                return;
            if (!_hasPendingMotionWrite || _curCount <= 0)
                return;

            _motionDataBuffer.SetData(_motionDataWriteCache, 0, 0, _curCount);
            _hasPendingMotionWrite = false;
            InvalidateMotionDataReadback();
        }

        private void EnsureMotionDataReadback()
        {
            if (_drawType != AnimationDrawType.Buff)
                return;
            if (_motionDataReadbackCache == null)
                _motionDataReadbackCache = new MotionData[_maxCount];
            if (_motionDataReadbackValid && _motionDataReadbackFrame == Time.frameCount)
                return;

            if (_curCount > 0)
                _motionDataBuffer.GetData(_motionDataReadbackCache, 0, 0, _curCount);

            _motionDataReadbackValid = true;
            _motionDataReadbackFrame = Time.frameCount;
        }
        
        private void SetCacheDirty(int index, bool isDirty = true)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            posRotData.IsDirty = isDirty;
        }
        
        private void SetCacheTR(int index, float3 position, float3 rotation)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            posRotData.Position = position;
            posRotData.Rotation = rotation;
        }
        private void SetCachePosition(int index, float3 position)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            posRotData.Position = position;
        }
        
        private void SetCacheRotation(int index, float3 rotation)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            posRotData.Rotation = rotation;
        }
        
        private void SetCacheRotationY(int index, float yaw)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            posRotData.Rotation.y = yaw;
        }
        // set TRS
        public void SetTRS(int index, float3 position, float3 rotation, float scale)
        {
            SetCacheTR(index, position, rotation);
            var data = GetMotionData(index);
            data.Position = position;
            data.Rotation = rotation;
            data.Scale = scale;
            SetMotionData(index, data);
        }
        
        public float3 GetPosition(int index)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            if (!posRotData.TimeOut())
                return posRotData.Position;

            RefreshCache(index, ref posRotData);
            
            //posRotData.LastTime = Time.realtimeSinceStartup;

            return posRotData.Position;
        }
        
        // set position
        public void SetPosition(int index, float3 position)
        {
            SetCachePosition(index, position);
            var data = GetMotionData(index);
            data.Position = position;
            SetMotionData(index, data);
        }
        
        public float GetRotationY(int index)
        {
            ref var posRotData = ref _posRotCacheArray[index];
            if (!posRotData.TimeOut())
                return posRotData.Rotation.y;

            RefreshCache(index, ref posRotData);

            return posRotData.Rotation.y;
        }
        
        //rotation
        public void SetRotation(int index, float3 rotation)
        {
            SetCacheRotation(index, rotation);
            var data = GetMotionData(index);
            data.Rotation = _drawType == AnimationDrawType.Buff ? -rotation : rotation;
            SetMotionData(index, data);
        }
        
        public void SetRotationY(int index, float yaw)
        {
            SetCacheRotationY(index, yaw);
            var data = GetMotionData(index);
            data.Rotation.y = _drawType == AnimationDrawType.Buff ? -yaw : yaw;
            SetMotionData(index, data);
        }

        #endregion

        public void Dispose()
        {
            ClearAllChild();
            if (_motionDataArray.IsCreated)
                _motionDataArray.Dispose();
            if (_motionDataBuffer != null)
                _motionDataBuffer.Dispose();
        }
    }
}
