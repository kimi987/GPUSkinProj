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

    public class ParentDataDefine
    {
        private NativeArray<MotionData> _motionDataArray;
        private MotionData[] _motionCacheArray;
        private int[] _pathIndexData;
        private int _motionCacheLowIndex = int.MaxValue;
        private int _motionCacheHighIndex = -1;
        
        private int _posRotCacheTimeFrame;
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

        public ParentDataDefine(ComputeShader animationDrawShader, int defaultNum = 4096)
        {
            //Compute
            _drawType = AnimationDrawType.Buff;
            _animationDrawShader = animationDrawShader;
            _maxCount = defaultNum;
            _cullingKernel = _animationDrawShader.FindKernel(ComputeShaderIds.ComputeCullingMotionKernel);
            // _motionDataArray = new NativeArray<MotionData>(defaultNum, Allocator.Persistent);
            _motionCacheArray = new MotionData[defaultNum];
            _pathIndexData = new int[defaultNum];
            _motionDataBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<MotionData>());
            _tempMotionDataAry = new MotionData[1];
            var subKernel = _animationDrawShader.FindKernel(ComputeShaderIds.ComputeCullingAnimationKernel);
            _animationDrawShader.SetBuffer(subKernel, ComputeShaderIds.ParentMotionBufferName, _motionDataBuffer);
            PathData.Instance.SetComputeShaderData(_animationDrawShader, _cullingKernel);
            AvaSlots = new(defaultNum);
            // _motionDataArray.Dispose();
        }

        public ParentDataDefine(int defaultNum = 4096)
        {
            _drawType = AnimationDrawType.Instance;
            _maxCount = defaultNum;
            _motionDataArray = new NativeArray<MotionData>(defaultNum, Allocator.Persistent);
            _pathIndexData = new int[defaultNum];
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
                    }
                    return;
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
                // foreach (var kv in childDataDic)
                // {
                //     var childData = kv.Value;
                //     if (childData != null && childData.UnitName != string.Empty && childData.InstanceIndex >= 0)
                //         AnimationDrawMgr.Instance.RemoveInstance(childData.UnitName, childData.InstanceIndex);
                // }
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
                _pathIndexData[index] = -1;
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
            _pathIndexData[_curCount] = -1;
            index = _curCount++;
            return index;
        }

        private void ResetPath(int index, int resetIndex = -1)
        {
            if(_pathIndexData[index] >= 0)
                PathData.Instance.RemovePos(_pathIndexData[index]);
            _pathIndexData[index] = resetIndex;
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

            if (data.Enable == 0)
                return;

            ResetPath(index);

            data.Enable = 0;
            _motionDataArray[index] = data;
            AvaSlots.Push(index);
        }
        
        public void RemoveBuffParent(int index)
        {
            RefreshCache();
            SetWriteCacheDirtyIndex(index);
            
            if (_motionCacheArray[index].Enable == 0)
                return;
            ResetPath(index);
            // _tempMotionDataAry[0] = _motionCacheArray[index];
            _motionCacheArray[index].Enable = 0;
            // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
            // _motionCacheArray[index] = _tempMotionDataAry[0];
            AvaSlots.Push(index);
        }

        public ref NativeArray<MotionData> GetMotionDataAry()
        {
            return ref _motionDataArray;
        }

        public int AddBuffParent(uint layerMask)
        {
            var index = -1;
            RefreshCache();
            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                SetWriteCacheDirtyIndex(index);
                _motionCacheArray[index] = default;
                _motionCacheArray[index].Reset(layerMask);
                _pathIndexData[index] = -1;
                // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                // _motionCacheArray[index] = _tempMotionDataAry[0];
                return index;
            }

            if (_curCount >= _maxCount)
            {
                Debug.LogError("Add Parent Failed! Count exceed max count!");
                return -1;
            }
            _motionCacheArray[_curCount] = default;
            _motionCacheArray[_curCount].Reset(layerMask);
            _pathIndexData[_curCount] = -1;
            SetWriteCacheDirtyIndex(_curCount);
            // _motionDataBuffer.SetData(_tempMotionDataAry, 0, _curCount, 1);
            // _motionCacheArray[_curCount] = _tempMotionDataAry[0];
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
            FlushWriteCache();
            
            _animationDrawShader.SetBuffer(_cullingKernel, ComputeShaderIds.MotionBufferName, _motionDataBuffer);
            _animationDrawShader.SetInt(ComputeShaderIds.InstanceCount, _curCount);
            _animationDrawShader.Dispatch(_cullingKernel, Mathf.CeilToInt(_curCount / 64f), 1, 1);
            
            // _motionDataBuffer.GetData(_motionCacheArray, 0, 0, _curCount);
            //Debug
            // _motionDataBuffer.GetData(_tempMotionDataAry, 0, 0, 1);
            // if (_tempMotionDataAry[0].RotateYSpeed > 0)
            //     Debug.LogError($"_tempMotionDataAry[0] = {_tempMotionDataAry[0].Rotation}_{_tempMotionDataAry[0].RotateYTarget}__{_tempMotionDataAry[0].RotateYSpeed}");
            
        }

        #region move rotate

        public void SetVisible(int index, bool visible)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.Visible = visible ? 1 : 0;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].Visible = visible ? 1 : 0;
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
            }
        }
        public void MoveTo(int index, float3 targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    ResetPath(index);
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
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    ResetPath(index);
                    targetPos.y = _motionCacheArray[index].Position.y;
                    _motionCacheArray[index].PathIndex = -1;
                    _motionCacheArray[index].CurPathIndex = 0;
                    _motionCacheArray[index].TargetPos = targetPos;
                    _motionCacheArray[index].MoveSpeed = moveSpeed;
                    _motionCacheArray[index].RotateYSpeed = rotateSpeed;
                    _motionCacheArray[index].RotateYSpeedSetting = rotateSpeed;
                    moveVec = targetPos - _motionCacheArray[index].Position;
                    _motionCacheArray[index].MoveDirection = math.normalize(moveVec);
                    _motionCacheArray[index].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    _motionCacheArray[index].RotateYFinal = finalYaw;
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    // Debug.LogError($" set _tempMotionDataAry[0] = {_tempMotionDataAry[0].Position}__{_tempMotionDataAry[0].MoveSpeed}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveTo(int index, Vector3[] targetPos, float moveSpeed, float rotateSpeed, float finalYaw = Single.PositiveInfinity)
        {
            var firstPos = targetPos[0];

            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    
                    var data = _motionDataArray[index];
                    
                    var pathIndex = PathData.Instance.AddInstancePoses(targetPos);
                    ResetPath(index, pathIndex);
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
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    pathIndex = PathData.Instance.AddBufferPoses(targetPos);
                    ResetPath(index, pathIndex);
                    _motionCacheArray[index].PathIndex = pathIndex + targetPos.Length - 1;
                    _motionCacheArray[index].CurPathIndex = pathIndex;//+ targetPos.Length - 1;
                    _motionCacheArray[index].MoveSpeed = moveSpeed;
                    _motionCacheArray[index].RotateYSpeed = rotateSpeed;
                    _motionCacheArray[index].RotateYSpeedSetting = rotateSpeed;
                    firstPos.y = _motionCacheArray[index].Position.y;
                    moveVec = firstPos - (Vector3)_motionCacheArray[index].Position;
                    _motionCacheArray[index].MoveDirection = math.normalize(moveVec);
                    _motionCacheArray[index].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    _motionCacheArray[index].RotateYFinal = finalYaw;
                    // _motionDataBuffer.SetData(_motionCacheArray, 0, index, 1);
                    
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    // Debug.LogError($" 11set _tempMotionDataAry[0] = {_tempMotionDataAry[0].Position}__{_tempMotionDataAry[0].MoveSpeed}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveDir(int index, float3 dir, float speed)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];

                    ResetPath(index);
                    data.PathIndex = -1;
                    data.MoveSpeed = speed;
                    data.MoveDirection = math.normalize(dir);
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    ResetPath(index);
                    _motionCacheArray[index].PathIndex = -1;
                    _motionCacheArray[index].MoveSpeed = speed;
                    _motionCacheArray[index].MoveDirection = math.normalize(dir);
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
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
                    ResetPath(index);
                    data.StopMove();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    ResetPath(index);
                    _motionCacheArray[index].StopMove();
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RotateTo(int index, float targetY, float rotateSpeed)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYTarget = Single.PositiveInfinity;
                    data.RotateYFinal = targetY;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].RotateYSpeed = rotateSpeed;
                    _motionCacheArray[index].RotateYSpeedSetting = rotateSpeed;
                    _motionCacheArray[index].RotateYTarget = Single.PositiveInfinity;
                    _motionCacheArray[index].RotateYFinal = targetY;
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public void RotateY(int index, float speed, string animationName = "")
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    data.RotateYSpeed = speed;
                    data.RotateYSpeedSetting = speed;
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].RotateYSpeed = speed;
                    _motionCacheArray[index].RotateYSpeedSetting = speed;
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
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
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].StopRotate();
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
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
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return _motionDataArray[index].MoveSpeed > 0.01 || _motionDataArray[index].RotateYSpeed > 0.01;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    if (_motionCacheArray[index].MoveSpeed > 0.01 || _motionCacheArray[index].RotateYSpeed > 0.01)
                        return true;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void RefreshCache()
        {
            //posRotCache.LastTime = Time.realtimeSinceStartup;
            if (_posRotCacheTimeFrame == Time.frameCount || _curCount <= 0 || _motionCacheHighIndex >= _motionCacheLowIndex)
                return;
            _posRotCacheTimeFrame = Time.frameCount;
            _motionDataBuffer.GetData(_motionCacheArray, 0, 0, _curCount);
        }
        
        private void SetWriteCacheDirtyIndex(int index)
        {
            _motionCacheHighIndex = index > _motionCacheHighIndex? index : _motionCacheHighIndex;
            _motionCacheLowIndex = index < _motionCacheLowIndex? index : _motionCacheLowIndex;
        }

        private void FlushWriteCache()
        {
            if (_motionCacheHighIndex < _motionCacheLowIndex)
                 return;
            _motionDataBuffer.SetData(_motionCacheArray, _motionCacheLowIndex, _motionCacheLowIndex, _motionCacheHighIndex - _motionCacheLowIndex + 1);
            _motionCacheLowIndex = Int32.MaxValue;
            _motionCacheHighIndex = -1;
        }
  
        // set TRS
        public void SetTRS(int index, float3 position, float3 rotation, float scale)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    ResetPath(index);
                    data.Position = position;
                    data.Rotation = rotation;
                    data.Scale = scale;
                    data.StopMove();
                    data.StopRotate();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    
                    ResetPath(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].Position = position;
                    _motionCacheArray[index].Rotation = rotation;
                    _motionCacheArray[index].Scale = scale;
                    _motionCacheArray[index].StopMove();
                    _motionCacheArray[index].StopRotate();
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
            }
        }
        
        public float3 GetPosition(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return _motionDataArray[index].Position;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    ref var motionData = ref _motionCacheArray[index];
                    return motionData.Position;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        // set position
        public void SetPosition(int index, float3 position)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    ResetPath(index);
                    data.Position = position;
                    data.StopMove();
                    data.StopRotate();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    ResetPath(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].Position = position;
                    _motionCacheArray[index].StopMove();
                    _motionCacheArray[index].StopRotate();
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public float GetRotationY(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return _motionDataArray[index].Rotation.y;
                case AnimationDrawType.Buff:
                    // GetIsDirty(index);
                    RefreshCache();
                    
                    ref var motionData = ref _motionCacheArray[index];
                    return motionData.Rotation.y;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        //rotation
        public void SetRotation(int index, float3 rotation)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    ResetPath(index);
                    data.Rotation = rotation;
                    data.StopMove();
                    data.StopRotate();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    ResetPath(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].Rotation = rotation;  //
                    _motionCacheArray[index].StopMove();
                    _motionCacheArray[index].StopRotate();
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public void SetRotationY(int index, float yaw)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = _motionDataArray[index];
                    ResetPath(index);
                    data.Rotation.y = yaw;
                    data.StopMove();
                    data.StopRotate();
                    _motionDataArray[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    ResetPath(index);
                    // _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    _motionCacheArray[index].StopRotate();
                    _motionCacheArray[index].StopMove();
                    _motionCacheArray[index].Rotation.y = yaw;  //
                    // _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    // _motionCacheArray[index] = _tempMotionDataAry[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        #region GetPath Relative

        public int GetTargetPathIndex(int index)
        {
            if (_pathIndexData[index] < 0)
                return -1;
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return _motionDataArray[index].PathIndex - _motionDataArray[index].CurPathIndex;
                case AnimationDrawType.Buff:
                    RefreshCache();
                    SetWriteCacheDirtyIndex(index);
                    return _motionCacheArray[index].PathIndex - _motionCacheArray[index].CurPathIndex;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public float3 GetPathPos(int index, int pathIndex)
        {
            if (_pathIndexData[index] < 0)
                return -1;

            pathIndex += _pathIndexData[index];
            
            return PathData.Instance.GetPathPos(pathIndex);
        }

        #endregion

        public int GetActiveParentCount() => _curCount - AvaSlots.Count;

        public int GetActiveChildCount()
        {
            var count = 0;
            foreach (var dict in _childDataDict.Values)
            {
                count += dict.Count;
            }
                
            return count;
        }

        public void Dispose()
        {
            ClearAllChild();
            for (int i = 0; i < _curCount; i++)
            {
                ResetPath(i);
            }
            if (_motionDataArray.IsCreated)
                _motionDataArray.Dispose();
            if (_motionDataBuffer != null)
                _motionDataBuffer.Dispose();
        }
    }
}