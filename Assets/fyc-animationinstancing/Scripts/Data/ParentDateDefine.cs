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
                    if (childData != null && childData.UnitName != unitName && childData.InstanceIndex != instanceIndex)
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
            _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
            if (_tempMotionDataAry[0].PathIndex >= 0)
                PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
            _tempMotionDataAry[0].Enable = 0;
            _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
            AvaSlots.Push(index);
        }

        public ref NativeArray<MotionData> GetMotionDataAry()
        {
            return ref _motionDataArray;
        }

        public int AddBuffParent(uint layerMask)
        {
            var index = -1;
            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                _tempMotionDataAry[0].Reset(layerMask);
                _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                return index;
            }
            
            if (_curCount >= _maxCount)
            {
                Debug.LogError("Add Parent Failed! Count exceed max count!");
                return -1;
            }
            _motionDataBuffer.GetData(_tempMotionDataAry, 0, _curCount, 1);
            _tempMotionDataAry[0].Reset(layerMask);
            _motionDataBuffer.SetData(_tempMotionDataAry, 0, _curCount, 1);
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
                
            _animationDrawShader.SetBuffer(_cullingKernel, ComputeShaderIds.MotionBufferName, _motionDataBuffer);
            _animationDrawShader.SetInt(ComputeShaderIds.CullingType, 1);
            _animationDrawShader.SetInt(ComputeShaderIds.InstanceCount, _curCount);

            _animationDrawShader.Dispatch(_cullingKernel, Mathf.CeilToInt(_curCount / 64f), 1, 1);
            
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
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    
                    if (_tempMotionDataAry[0].PathIndex >= 0)
                        PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
                    targetPos.y = _tempMotionDataAry[0].Position.y;
                    _tempMotionDataAry[0].PathIndex = -1;
                    _tempMotionDataAry[0].CurPathIndex = 0;
                    _tempMotionDataAry[0].TargetPos = targetPos;
                    _tempMotionDataAry[0].MoveSpeed = moveSpeed;
                    _tempMotionDataAry[0].RotateYSpeed = rotateSpeed;
                    _tempMotionDataAry[0].RotateYSpeedSetting = rotateSpeed;
                    moveVec = targetPos - _tempMotionDataAry[0].Position;
                    _tempMotionDataAry[0].MoveDirection = math.normalize(moveVec);
                    _tempMotionDataAry[0].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    _tempMotionDataAry[0].RotateYFinal = finalYaw;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    
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
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    if (_tempMotionDataAry[0].PathIndex >= 0)
                        PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
                    pathIndex = PathData.Instance.AddBufferPoses(targetPos);
                    if (pathIndex < 0)
                    {
                        _tempMotionDataAry[0].StopMove();
                        _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                        break;
                    }
                    _tempMotionDataAry[0].PathIndex = pathIndex + targetPos.Length - 1;
                    _tempMotionDataAry[0].CurPathIndex = pathIndex;//+ targetPos.Length - 1;
                    _tempMotionDataAry[0].MoveSpeed = moveSpeed;
                    _tempMotionDataAry[0].RotateYSpeed = rotateSpeed;
                    _tempMotionDataAry[0].RotateYSpeedSetting = rotateSpeed;
                    firstPos.y = _tempMotionDataAry[0].Position.y;
                    moveVec = firstPos - (Vector3)_tempMotionDataAry[0].Position;
                    _tempMotionDataAry[0].MoveDirection = math.normalize(moveVec);
                    _tempMotionDataAry[0].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    _tempMotionDataAry[0].RotateYFinal = finalYaw;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    
                    // Debug.LogError($" 11set _tempMotionDataAry[0] = {_tempMotionDataAry[0].Position}__{_tempMotionDataAry[0].MoveSpeed}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveDir(int index, float3 dir, float speed)
        {
            SetCacheDirty(index);
            
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    data.MoveSpeed = speed;
                    data.MoveDirection = math.normalize(dir);
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    if (_tempMotionDataAry[0].PathIndex >= 0)
                        PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
                    _tempMotionDataAry[0].MoveSpeed = speed;
                    _tempMotionDataAry[0].MoveDirection = math.normalize(dir); 
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        // stop moving
        public void StopMove(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    data.StopMove();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    if (_tempMotionDataAry[0].PathIndex >= 0)
                        PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
                    _tempMotionDataAry[0].StopMove();
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RotateTo(int index, float targetY, float rotateSpeed)
        {
            SetCacheDirty(index);
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYFinal = targetY;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].RotateYSpeed = rotateSpeed;
                    _tempMotionDataAry[0].RotateYSpeedSetting = rotateSpeed;
                    _tempMotionDataAry[0].RotateYFinal = targetY;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public void RotateY(int index, float speed, string animationName = "")
        {
            SetCacheDirty(index);
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.RotateYSpeed = speed;
                    data.RotateYSpeedSetting = speed;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].RotateYSpeed = speed;
                    _tempMotionDataAry[0].RotateYSpeedSetting = speed;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        // stop rotate
        public void StopRotate(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.StopRotate();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].StopRotate();
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

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
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    posRotCache.IsDirty = _tempMotionDataAry[0].MoveSpeed > 0.001 || _tempMotionDataAry[0].RotateYSpeed >= 0.001;
                    posRotCache.Position = _tempMotionDataAry[0].Position;
                    posRotCache.Rotation = _tempMotionDataAry[0].Rotation;
                    break;
                default:
                    break;
            }
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
            
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.Position = position;
                    data.Rotation = rotation;
                    data.Scale = scale;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].Position = position;
                    _tempMotionDataAry[0].Rotation = rotation;
                    _tempMotionDataAry[0].Scale = scale;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
            }
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
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.Position = position;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].Position = position;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.Rotation = rotation;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].Rotation = -rotation;  //
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void SetRotationY(int index, float yaw)
        {
            SetCacheRotationY(index, yaw);
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.Rotation.y = yaw;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _tempMotionDataAry[0].Rotation.y = -yaw;  //
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
