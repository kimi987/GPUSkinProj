using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fyc.AnimationInstancing
{

    public struct FrameData
    {
        public int Enable;
        public int Pause;
        public float AniSpeed;
        public int PreAniIndex;
        public int CurAniIndex;

        public float FrameIndex;
        public float PreFrameIndex;
        public float TransitionProgress;
        public float TransitionDuration;

        public int ResetAniIndex;

        public void Active()
        {
            Enable = 1;
            Pause = 0;
            AniSpeed = 1;
            PreAniIndex = -1;
            CurAniIndex = 0;
            TransitionProgress = 1;
            ResetAniIndex = -1;
        }
    }

    public struct MotionData
    {
        public int Enable;
        public int Visible;
        public int IsCull;
        public uint LayerMask;
        public int ParentIndex;
        public float3 Position;
        public float3 Rotation;
        public float Scale;
        public float MoveSpeed;
        public float3 MoveDirection;
        public float3 TargetPos;
        public float RotateYSpeedSetting;
        public float RotateYSpeed;
        public float RotateYTarget;
        public float RotateYFinal;
        public int CurPathIndex;
        public int PathIndex;

        // public float DebugFinalY;
        
        public void Reset(uint layerMask)
        {
            Enable = 1;
            Visible = 1;
            IsCull = 0;
            LayerMask = layerMask;
            ParentIndex = -1; // -1 means no parent
            Position = float3.zero;
            Rotation = float3.zero;
            Scale = 1;
            MoveSpeed = 0;
            MoveDirection = float3.zero;
            TargetPos = float3.zero;
            RotateYTarget = float.PositiveInfinity;
            RotateYFinal = float.PositiveInfinity;
            RotateYSpeedSetting = 0;
            RotateYSpeed = 0;
            PathIndex = -1;   //-1 means no path
            CurPathIndex = 0;
        }

        public void StopMove()
        {
            PathData.Instance.RemovePos(PathIndex);
            MoveSpeed = 0;
            MoveDirection = float3.zero;
            PathIndex = -1; 
        }

        public void StopRotate()
        {
            RotateYSpeed = 0;
            RotateYFinal = float.PositiveInfinity;
            RotateYTarget = float.PositiveInfinity;
        }
    }
    public struct DrawFrameData
    {
        public float4x4 WorldMatrix;
        public float FrameIndex;
        public float PreFrameIndex;
        public float TransitionProgress;
        public float Padding;
    }
    public class DrawInstancingData
    {
        static Bounds DefaultBounds = new Bounds(Vector3.zero, new Vector3(3000, 3000, 3000));
        private Mesh DrawMesh;
        private Material DrawMaterial;
        private int MaxCount;
        private int InstancingCount;
        private MaterialPropertyBlock PropertyBlock;

        private Stack<int> AvaSlots;
        private NativeArray<FrameData> TotalFrames; //总的单位
        private NativeArray<FrameData> AvaFrames;  //有效的单位
        private NativeArray<MotionData> MotionDataAry; // 位移数据
        private int DrawCount = 0;
        private NativeArray<float> FrameIndexes;
        private NativeArray<float> PreFrameIndexes;
        private NativeArray<float> TransitionProgress;
        private NativeArray<Matrix4x4> WorldMatrix; //绘制的矩阵
        private NativeArray<AnimationSaveData> AniInfoData;
        
        //Optimal for GC
        private NativeList<int> VisibleIndices;
        private float[] _frameIndexCache = new float[1023];
        private float[] _preFrameIndexCache = new float[1023];
        private float[] _transitionProgressCache = new float[1023];
        private Matrix4x4[] _matrixCache = new Matrix4x4[1023];

        //Animation
        private AnimationData AniData;
        private Dictionary<int, int> InfoIndexes;
        
        //Compute Shader
        private ComputeShader _animationDrawShader;
        private int _cullingAndAnimationKernel;
        private ComputeBuffer _argsBuffer;
        private ComputeBuffer _totalFramesBuffer;
        private ComputeBuffer _avaFramesBuffer;
        private ComputeBuffer _motionDataBuffer;
        private ComputeBuffer _aniInfoBuffer;
        private MotionData[] _motionDataReadbackCache;
        private int _motionDataReadbackFrame = -1;
        private bool _motionDataReadbackValid;

        private FrameData[] _tempFrameDataAry;  //用于读取和设置Buffer
        private MotionData[] _tempMotionDataAry;
        private AnimationDrawType _drawType;

        private Texture2D _boneTexture;
        public DrawInstancingData(AnimationData mainData, ComputeShader animationDrawShader, int defaultNum = 4096)
        {
            _drawType = AnimationDrawType.Buff;
            AniData = mainData;
            _animationDrawShader = animationDrawShader;
            DrawMesh = mainData.mainMesh;
            DrawMaterial = mainData.mainMaterial;
            MaxCount = defaultNum;
            
            _tempFrameDataAry = new FrameData[1];
            _tempMotionDataAry = new MotionData[1];
            //create bone texture
            (int width, int height) = mainData.GetTextureWidthAndHeight();
            //SetMaterial Params
            DrawMaterial.DisableKeyword(ShaderIds.InstanceKeyWord);
            DrawMaterial.EnableKeyword(ShaderIds.IndirectKeyWord);
            _boneTexture = mainData.CreateBoneTexture();
            DrawMaterial.SetTexture(ShaderIds.BoneTexture, _boneTexture);
            DrawMaterial.SetInt(ShaderIds.BoneTextureWidth, width);
            DrawMaterial.SetInt(ShaderIds.BoneTextureHeight, height);
            DrawMaterial.SetInt(ShaderIds.BoneTextureBlockWidth, mainData.textureBlockWidth);
            DrawMaterial.SetInt(ShaderIds.BoneTextureBlockHeight, mainData.textureBlockHeight);
            
            AniInfoData = new NativeArray<AnimationSaveData>(mainData.animationData.Length, Allocator.Persistent);
            AniInfoData.CopyFrom(mainData.animationData);

            TotalFrames = new NativeArray<FrameData>(defaultNum, Allocator.Persistent);
            AvaSlots = new(defaultNum);
            //Kernel
            _cullingAndAnimationKernel = _animationDrawShader.FindKernel(ComputeShaderIds.ComputeCullingAnimationKernel);
            uint[] args = new uint[5] { (uint)mainData.mainMesh.triangles.Length, 0, 0, 0, 0 };
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(args);
            _aniInfoBuffer = new ComputeBuffer(mainData.animationData.Length, Marshal.SizeOf<AnimationSaveData>());
            _aniInfoBuffer.SetData(AniInfoData);
            _totalFramesBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<FrameData>());
            _totalFramesBuffer.SetData(TotalFrames);
            _avaFramesBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<DrawFrameData>(), ComputeBufferType.Append);
            _motionDataBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<MotionData>());
            _motionDataReadbackCache = new MotionData[defaultNum];

            DrawMaterial.SetBuffer(ComputeShaderIds.AvaBufferName, _avaFramesBuffer);
            
            InfoIndexes = new Dictionary<int, int>();

            for (int i = 0; i < mainData.animationData.Length; i++)
            {
                var info = mainData.GetAnimation(i);
                InfoIndexes[info.animationNameHash] = i;
            }

            AniInfoData.Dispose();
            TotalFrames.Dispose();
            
            //set path ary data init
            PathData.Instance.SetComputeShaderData(_animationDrawShader, _cullingAndAnimationKernel);
        }

        public DrawInstancingData(AnimationData mainData, int defaultNum = 1023)
        {
            _drawType = AnimationDrawType.Instance;
            AniData = mainData;
            DrawMesh = mainData.mainMesh;
            DrawMaterial = mainData.mainMaterial;
            MaxCount = defaultNum;
            DrawMaterial.DisableKeyword(ShaderIds.IndirectKeyWord);
            DrawMaterial.EnableKeyword(ShaderIds.InstanceKeyWord);
            //create bone texture
            (int width, int height) = mainData.GetTextureWidthAndHeight();
            //SetMaterial Params
            DrawMaterial.EnableKeyword(ShaderIds.InstanceKeyWord);
            _boneTexture = mainData.CreateBoneTexture();
            DrawMaterial.SetTexture(ShaderIds.BoneTexture, _boneTexture);
            DrawMaterial.SetInt(ShaderIds.BoneTextureWidth, width);
            DrawMaterial.SetInt(ShaderIds.BoneTextureHeight, height);
            DrawMaterial.SetInt(ShaderIds.BoneTextureBlockWidth, mainData.textureBlockWidth);
            DrawMaterial.SetInt(ShaderIds.BoneTextureBlockHeight, mainData.textureBlockHeight);
            
            AniInfoData = new NativeArray<AnimationSaveData>(mainData.animationData.Length, Allocator.Persistent);

            AniInfoData.CopyFrom(mainData.animationData);
            
            VisibleIndices = new NativeList<int>(defaultNum, Allocator.Persistent);
            TotalFrames = new NativeArray<FrameData>(defaultNum, Allocator.Persistent);
            AvaFrames = new NativeArray<FrameData>(defaultNum, Allocator.Persistent);
            MotionDataAry = new NativeArray<MotionData>(defaultNum, Allocator.Persistent);
            WorldMatrix = new NativeArray<Matrix4x4>(defaultNum, Allocator.Persistent);
            FrameIndexes = new NativeArray<float>(defaultNum, Allocator.Persistent);
            PreFrameIndexes =  new NativeArray<float>(defaultNum, Allocator.Persistent);
            TransitionProgress = new NativeArray<float>(defaultNum, Allocator.Persistent);
            
            AvaSlots = new(defaultNum);
            InfoIndexes = new Dictionary<int, int>();

            for (int i = 0; i < mainData.animationData.Length; i++)
            {
                var info = mainData.GetAnimation(i);
                InfoIndexes[info.animationNameHash] = i;
            }
        }

        public void SetBoneTexture(Texture2D texture)
        {
            DrawMaterial.SetTexture(ShaderIds.BoneTexture, texture);
        }

        private FrameData GetFrameData(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return TotalFrames[index];
                case AnimationDrawType.Buff:
                    _totalFramesBuffer.GetData(_tempFrameDataAry, 0, index, 1);
                    return _tempFrameDataAry[0];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void SetFrameData(int index, FrameData frameData)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    TotalFrames[index] = frameData;
                    break;
                case AnimationDrawType.Buff:
                    _tempFrameDataAry[0] = frameData;
                    _totalFramesBuffer.SetData(_tempFrameDataAry, 0, index, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private MotionData GetMotionData(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return MotionDataAry[index];
                case AnimationDrawType.Buff:
                    _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                    return _tempMotionDataAry[0];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void InvalidateMotionDataReadback()
        {
            _motionDataReadbackValid = false;
            _motionDataReadbackFrame = -1;
        }

        private void EnsureMotionDataReadback()
        {
            if (_drawType != AnimationDrawType.Buff)
                return;
            if (_motionDataReadbackCache == null)
                _motionDataReadbackCache = new MotionData[MaxCount];
            if (_motionDataReadbackValid && _motionDataReadbackFrame == Time.frameCount)
                return;

            if (InstancingCount > 0)
                _motionDataBuffer.GetData(_motionDataReadbackCache, 0, 0, InstancingCount);

            _motionDataReadbackValid = true;
            _motionDataReadbackFrame = Time.frameCount;
        }

        private void SetMotionData(int index, MotionData motionData)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    MotionDataAry[index] = motionData;
                    break;
                case AnimationDrawType.Buff:
                    _tempMotionDataAry[0] = motionData;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    InvalidateMotionDataReadback();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public int AddInstance(uint layerMask)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return AddInstanceData(layerMask);
                case AnimationDrawType.Buff:
                    return AddInstanceBuffer(layerMask);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public int AddInstanceBuffer(uint layerMask)
        {
            var index = -1;
            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                _totalFramesBuffer.GetData(_tempFrameDataAry, 0, index, 1);
                _tempFrameDataAry[0].Active();
                _totalFramesBuffer.SetData(_tempFrameDataAry, 0, index, 1);
                
                _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
                _tempMotionDataAry[0].Reset(layerMask);
                _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                InvalidateMotionDataReadback();
                return index;
            }
            if (InstancingCount >= MaxCount)
            {
                Debug.LogError("Instance Count Beyound Limit Exceeded");
                return -1;
            }
            _totalFramesBuffer.GetData(_tempFrameDataAry, 0, InstancingCount, 1);
            _tempFrameDataAry[0].Active();
            _totalFramesBuffer.SetData(_tempFrameDataAry, 0, InstancingCount, 1);
            _motionDataBuffer.GetData(_tempMotionDataAry, 0, InstancingCount, 1);
            _tempMotionDataAry[0].Reset(layerMask);
            _motionDataBuffer.SetData(_tempMotionDataAry, 0, InstancingCount, 1);
            InvalidateMotionDataReadback();
            
            index = InstancingCount;
            InstancingCount++;
            return index;
        }
        public int AddInstanceData(uint layerMask)
        {
            var index = -1;
            if (AvaSlots.Count > 0)
            {
                index = AvaSlots.Pop();
                var data = TotalFrames[index];
                data.Active();
                TotalFrames[index] = data;

                var motionData = MotionDataAry[index];
                motionData.Reset(layerMask);
                MotionDataAry[index] = motionData;
                return index;
            }

            if (InstancingCount >= MaxCount)
            {
                //扩容
                Debug.LogError("Instance Count Beyound Limit Exceeded");
                return -1;
                // MaxCount *= 2;
                // Debug.LogError($"Too many instancing data, Expand to {MaxCount}");
                // var newTotalFrames = new NativeArray<FrameData>(MaxCount, Allocator.Persistent);
                // TotalFrames.CopyTo(newTotalFrames);
                // TotalFrames.Dispose();
                // TotalFrames = newTotalFrames;
                // AvaFrames.Dispose();
                // AvaFrames = new NativeArray<FrameData>(MaxCount, Allocator.Persistent);
                // AvaSlots  = new(MaxCount);
            }

            {
                var frameData = TotalFrames[InstancingCount];
                frameData.Active();
                TotalFrames[InstancingCount] = frameData;
                var motionData = MotionDataAry[InstancingCount];
                motionData.Reset(layerMask);
                MotionDataAry[InstancingCount] = motionData;
                index = InstancingCount;
                InstancingCount++;
            }
            return index;
        }

        public void RemoveInstance(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    RemoveInstanceData(index);
                    break;
                case AnimationDrawType.Buff:
                    RemoveInstanceBuffer(index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RemoveInstanceBuffer(int index)
        {
            _totalFramesBuffer.GetData(_tempFrameDataAry, 0, index, 1);
            _tempFrameDataAry[0].Enable = 0;
            _totalFramesBuffer.SetData(_tempFrameDataAry, 0, index, 1);
            AvaSlots.Push(index);
            
            //remove path
            _motionDataBuffer.GetData(_tempMotionDataAry, 0, index, 1);
            _tempMotionDataAry[0].Enable = 0; //Disable
            _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
            InvalidateMotionDataReadback();
            if (_tempMotionDataAry[0].PathIndex >= 0)
                PathData.Instance.RemovePos(_tempMotionDataAry[0].PathIndex);
        }

        public void RemoveInstanceData(int index)
        {
            var instanceData = TotalFrames[index];
            instanceData.Enable = 0;
            TotalFrames[index] = instanceData;
            AvaSlots.Push(index);
            
            //remove path
            var motionData = MotionDataAry[index];
            if (motionData.PathIndex >= 0)
                PathData.Instance.RemovePos(motionData.PathIndex);
            motionData.Enable = 0;
            MotionDataAry[index] = motionData;
        }
        
        //visible 
        public void SetVisible(int index, int visible)
        {
            var data = GetMotionData(index);
            data.Visible = visible;
            SetMotionData(index, data);
        }
        // speed 
        public void SetAnimationSpeed(int index, float speed)
        {
            var data = GetFrameData(index);
            data.AniSpeed = speed;
            SetFrameData(index, data);
        }

        public void MoveTo(int index, float3 targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity, bool changeRot = true)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    data.PathIndex = -1;
                    data.CurPathIndex = 0;
                    targetPos.y = data.Position.y;
                    data.TargetPos = targetPos;
                    Vector3 moveVec = targetPos - data.Position;
                    data.MoveDirection = math.normalize(moveVec);
                    data.MoveSpeed = moveSpeed;
                    
                    if (changeRot)
                    {
                        data.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                        data.RotateYSpeed = rotateSpeed;
                        data.RotateYSpeedSetting = rotateSpeed;
                        data.RotateYFinal = finalYaw;
                    }
        
                    MotionDataAry[index] = data;
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
  
                    moveVec = targetPos - _tempMotionDataAry[0].Position;
                    _tempMotionDataAry[0].MoveDirection = math.normalize(moveVec);
                    
                    if (changeRot)
                    {
                        _tempMotionDataAry[0].RotateYSpeed = rotateSpeed;
                        _tempMotionDataAry[0].RotateYSpeedSetting = rotateSpeed;
                        _tempMotionDataAry[0].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                        _tempMotionDataAry[0].RotateYFinal = finalYaw;
                    }
            
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    InvalidateMotionDataReadback();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void MoveTo(int index, Vector3[] targetPos, float moveSpeed, 
            float rotateSpeed, float finalYaw = Single.PositiveInfinity)
        {
            var firstPos = targetPos[0];
            
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    
                    var data = MotionDataAry[index];
                    if (data.PathIndex >= 0)
                        PathData.Instance.RemovePos(data.PathIndex);
                    
                    var pathIndex = PathData.Instance.AddInstancePoses(targetPos);
                    if (pathIndex < 0)
                    {
                        data.StopMove();
                        MotionDataAry[index] = data;
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
                    MotionDataAry[index] = data;
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
                        InvalidateMotionDataReadback();
                        break;
                    }
                    _tempMotionDataAry[0].PathIndex = pathIndex + targetPos.Length - 1;
                    _tempMotionDataAry[0].CurPathIndex = pathIndex;
                    _tempMotionDataAry[0].MoveSpeed = moveSpeed;
                    _tempMotionDataAry[0].RotateYSpeed = rotateSpeed;
                    _tempMotionDataAry[0].RotateYSpeedSetting = rotateSpeed;
                    firstPos.y = _tempMotionDataAry[0].Position.y;
                    moveVec = firstPos - (Vector3)_tempMotionDataAry[0].Position;
                    _tempMotionDataAry[0].MoveDirection = math.normalize(moveVec);
                    _tempMotionDataAry[0].RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    _tempMotionDataAry[0].RotateYFinal = finalYaw;
                    _motionDataBuffer.SetData(_tempMotionDataAry, 0, index, 1);
                    InvalidateMotionDataReadback();
                    
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveDir(int index, float3 dir, float speed, string animationName = "")
        {
            var data = GetMotionData(index);
            data.MoveSpeed = speed;
            data.MoveDirection = math.normalize(dir);
            SetMotionData(index, data);

            if (!string.IsNullOrEmpty(animationName))
                PlayAnimation(index, animationName);
        }
        
        // stop moving
        public void StopMove(int index)
        {
            var data = GetMotionData(index);
            data.StopMove();
            SetMotionData(index, data);
        }

        public void RotateTo(int index, float targetY, float rotateSpeed)
        {
            var data = GetMotionData(index);
            data.RotateYSpeed = rotateSpeed;
            data.RotateYSpeedSetting = rotateSpeed;
            data.RotateYFinal = targetY;
            SetMotionData(index, data);
        }
        
        //移动相关函数 可以通过设置方向或者设置目的地, 来通过job或者compute计算移动
        //减少每帧设置位移数据的消耗
        public void RotateY(int index, float speed, string animationName = "")
        {
            var data = GetMotionData(index);
            data.RotateYSpeed = speed;
            data.RotateYSpeedSetting = speed;
            SetMotionData(index, data);
            
            if (!string.IsNullOrEmpty(animationName))
                PlayAnimation(index, animationName);
        }
        // stop rotate
        public void StopRotate(int index)
        {
            var data = GetMotionData(index);
            data.StopRotate();
            SetMotionData(index, data);
        }
        //translation
        // set TRS
        public void SetTRS(int index, float3 position, float3 rotation, float scale)
        {
            var data = GetMotionData(index);
            data.Position = position;
            data.Rotation = rotation;
            data.Scale = scale;
            SetMotionData(index, data);
        }

        public bool GetPosition(int index, out float3 pos)
        {
            if (_drawType == AnimationDrawType.Buff)
            {
                EnsureMotionDataReadback();
                pos = _motionDataReadbackCache[index].Position;
                return true;
            }
            pos = GetMotionData(index).Position;
            return true;
        }
        
        // set position
        public void SetPosition(int index, float3 position)
        {
            var data = GetMotionData(index);
            data.Position = position;
            SetMotionData(index, data);
        }

        public float GetRotationY(int index)
        {
            if (_drawType == AnimationDrawType.Buff)
            {
                EnsureMotionDataReadback();
                return _motionDataReadbackCache[index].Rotation.y;
            }
            return GetMotionData(index).Rotation.y;
        }
        
        //rotation
        public void SetRotation(int index, float3 rotation)
        {
            var data = GetMotionData(index);
            data.Rotation = _drawType == AnimationDrawType.Buff ? -rotation : rotation;
            SetMotionData(index, data);
        }
        
        public void SetRotationY(int index, float yaw)
        {
            var data = GetMotionData(index);
            data.Rotation.y = _drawType == AnimationDrawType.Buff ? -yaw : yaw;
            SetMotionData(index, data);
        }
        
        //scale
        public void SetScale(int index, float scale)
        {
            var data = GetMotionData(index);
            data.Scale = scale;
            SetMotionData(index, data);
        }
        #region JobSystem

        public void DoCullingAndAnimationJob(uint layerMask, float deltaTime, NativeArray<Plane> planeNativeArray)
        {
            VisibleIndices.Clear();
            var job = new CullingUnitJob()
            {
                DeltaTime = deltaTime,
                LayerMask = layerMask,
                MotionData = MotionDataAry,
                ParentMotionData = AnimationDrawMgr.Instance.GetParentMotionData(),
                PathAry = PathData.Instance.GetPathAry(),
                VisibleIndices = VisibleIndices.AsParallelWriter(),
                Planes = planeNativeArray,
            };

            var handle = job.Schedule(InstancingCount, 64);
            handle.Complete();

            //Debug
            // Debug.LogError("TotalFrames[0].Rotation = "  + TotalFrames[0].Rotation);
            // Debug.LogError("TotalFrames[0].Position = "  + TotalFrames[0].Position);
            // Debug.LogError("PathAry[2] = "  + PathData.Instance.GetPathAry()[MotionDataAry[0].CurPathIndex]);
            // Debug.LogError($"MotionDataAry[0].CurPathIndex = {MotionDataAry[0].CurPathIndex}, Step = {MotionDataAry[0].Step}, Distance = {MotionDataAry[0].Distance}");
            //Debug.LogError($"MotionDataAry[0].DebugY =  {MotionDataAry[0].DebugY}, YSpeed = {MotionDataAry[0].RotateYSpeed}");
            DrawCount = VisibleIndices.Length;
            
            // WorldMatrix = new NativeArray<Matrix4x4>(DrawCount, Allocator.TempJob);
            // FrameIndexes = new NativeArray<float>(DrawCount, Allocator.TempJob);
            // PreFrameIndexes = new NativeArray<float>(DrawCount, Allocator.TempJob);
            // TransitionProgress = new NativeArray<float>(DrawCount, Allocator.TempJob);
            
            var aniJob = new AnimationJob()
            {
                DeltaTime = deltaTime,
                FrameData = TotalFrames,
                ParentMotionData = AnimationDrawMgr.Instance.GetParentMotionData(),
                MotionData = MotionDataAry,
                VisibleIndices = VisibleIndices,
                InfoData = AniInfoData,
                WorldMatrix = WorldMatrix,
                FrameIndexes = FrameIndexes,
                PreFrameIndexes = PreFrameIndexes,
                TransitionProgress = TransitionProgress,
            };
            handle = aniJob.Schedule(DrawCount, 64);
            handle.Complete();
            //Debug
            // Debug.LogError("WorldMatrix[0] = "  + WorldMatrix[0]);
        }
        
        #endregion

        #region Compute Shader


        
        public void DoComputeCullingAndAnimation()
        {
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.AniInfoBufferName, _aniInfoBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.TotalBufferName, _totalFramesBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.AvaBufferName, _avaFramesBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.MotionBufferName, _motionDataBuffer);
            _animationDrawShader.SetInt(ComputeShaderIds.CullingType, 0);
            _animationDrawShader.SetInt(ComputeShaderIds.InstanceCount, InstancingCount);
            _avaFramesBuffer.SetCounterValue(0);
            
            // Debug.LogError("1111111111_tempFrameDataAry[0].rotation = " + _tempFrameDataAry[0].Rotation + " _" + _tempMotionDataAry[0].RotateYSpeed + "_" + _tempMotionDataAry[0].RotateYSpeedSetting);
            _animationDrawShader.Dispatch(_cullingAndAnimationKernel, Mathf.CeilToInt(InstancingCount / 64f), 1, 1);
            InvalidateMotionDataReadback();
            ComputeBuffer.CopyCount(_avaFramesBuffer, _argsBuffer, 4);
            
            //Debug
            // var countData = new uint[5];
            // _argsBuffer.GetData(countData, 0, 0, 5);
            // _totalFramesBuffer.GetData(_tempFrameDataAry, 0, 0 , 1);
            // _motionDataBuffer.GetData(_tempMotionDataAry, 0, 0, 1);
            //
            // if (_tempMotionDataAry[0].Rotation.y > 0)
            // {
            //     Debug.LogError("1111111111_tempFrameDataAry[0].rotation = " + _tempMotionDataAry[0].DebugFinalY + "_" + _tempMotionDataAry[0].Rotation+ " _" + _tempMotionDataAry[0].RotateYTarget +
            //                    "_" + _tempMotionDataAry[0].RotateYFinal + "_" + _tempMotionDataAry[0].RotateYSpeed + "_" +_tempFrameDataAry[0].ResetAniIndex);
            //     _motionDataBuffer.GetData(_tempMotionDataAry, 0, 0, 1);
            // }
            // Debug.LogError(_tempMotionDataAry[0].Position + "_" + _tempMotionDataAry[0].ParentIndex);
            // Debug.LogError("_tempFrameDataAry[0].Position = "+ _tempFrameDataAry[0].Position + "_" + 
            //                countData[1] + "_" + _tempFrameDataAry[0].Scale);
            
            // Debug.LogError($"_tempFrameDataAry[0].Position = {_tempFrameDataAry[0].Position} _tempMotionDataAry[0].TargetPos1 = {_tempMotionDataAry[0].TargetPos1}, " +
            //                $"_tempMotionDataAry[0].dir {_tempMotionDataAry[0].MoveDirection}  _tempMotionDataAry[0].step = {_tempMotionDataAry[0].Step}, distance = {_tempMotionDataAry[0].Distance}");
            // Debug.LogError("_tempFrameDataAry[0].rotation = " + _tempFrameDataAry[0].Rotation + " _" + _tempMotionDataAry[0].DebugTargetY);
        }

        #endregion

        #region Animation

        private bool TryGetAnimationIndex(string animationName, out int infoIndex)
        {
            infoIndex = -1;
            if (string.IsNullOrEmpty(animationName))
                return false;

            return InfoIndexes.TryGetValue(animationName.GetHashCode(), out infoIndex);
        }

        public bool PlayAnimation(int index, string animationName, float startFrame = 0f,
            float transitionTime = 0.25f, bool resetToLast = false)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    return PlayAnimationInstance(index, animationName, startFrame, transitionTime, resetToLast);
                case AnimationDrawType.Buff:
                    return PlayAnimationBuffer(index, animationName, startFrame, transitionTime, resetToLast);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool PlayAnimationBuffer(int index, string animationName, float startFrame = 0f,
            float transitionTime = 0.25f, bool resetToLast = false)
        {
            _totalFramesBuffer.GetData(_tempFrameDataAry, 0, index, 1);

            if (_tempFrameDataAry[0].Enable == 0)
                return false;
            startFrame -= 1;
            if (TryGetAnimationIndex(animationName, out var infoIndex))
            {
                var animationSaveData = AniData.GetAnimation(infoIndex);
                startFrame %= animationSaveData.totalFrame;
                if (transitionTime > 0)
                {
                    _tempFrameDataAry[0].PreAniIndex = _tempFrameDataAry[0].CurAniIndex;
                    _tempFrameDataAry[0].PreFrameIndex = Mathf.Floor(_tempFrameDataAry[0].FrameIndex + 0.5f);
                    _tempFrameDataAry[0].TransitionProgress = 0;
                }

                if (resetToLast)
                    _tempFrameDataAry[0].ResetAniIndex = _tempFrameDataAry[0].CurAniIndex;
                
                _tempFrameDataAry[0].CurAniIndex = infoIndex;
                _tempFrameDataAry[0].FrameIndex = startFrame + animationSaveData.animationIndex;
                _tempFrameDataAry[0].TransitionDuration = transitionTime;
                _totalFramesBuffer.SetData(_tempFrameDataAry, 0, index, 1);
                
                return true;
            }
            return false;
            
        }

        private bool PlayAnimationInstance(int index, string animationName, float startFrame = 0f,
            float transitionTime = 0.25f, bool resetToLast = false)
        {
            var data = TotalFrames[index];
            if (data.Enable == 0)
                return false;

            startFrame -= 1;
            if (TryGetAnimationIndex(animationName, out var infoIndex))
            {
                // if (infoIndex == data.CurAniIndex)
                // {
                //     //Same animation
                //     return false;
                // }
                var animationSaveData = AniData.GetAnimation(infoIndex);
                startFrame %= animationSaveData.totalFrame;
                if (transitionTime > 0)
                {
                    data.PreAniIndex = data.CurAniIndex;
                    data.PreFrameIndex = Mathf.Floor(data.FrameIndex + 0.5f);
                    data.TransitionProgress = 0;
                }
                if (resetToLast)
                    data.ResetAniIndex = data.CurAniIndex;

                data.CurAniIndex = infoIndex;
                data.FrameIndex = startFrame + animationSaveData.animationIndex;
                data.TransitionDuration = transitionTime;
                
            
                TotalFrames[index] = data;
                
                return true;
            }
            return false;
        }

        #endregion

        #region Draw
        //Compute Buffer
        public void IndirectDraw(ShadowCastingMode drawShadow = ShadowCastingMode.On, bool receiveShadow = true)
        {
            if (_drawType != AnimationDrawType.Buff)
                return;
            Graphics.DrawMeshInstancedIndirect(DrawMesh, 0, DrawMaterial, DefaultBounds, 
                _argsBuffer, 0, null, drawShadow, receiveShadow);
        }
        
        public void Draw(ShadowCastingMode drawShadow = ShadowCastingMode.On, bool receiveShadow = true, int layer = 0)
        {
            if (_drawType != AnimationDrawType.Instance)
                return;
            //Draw Instance
            if (PropertyBlock == null)
            {
                PropertyBlock = new MaterialPropertyBlock();
                //预分配大小
                PropertyBlock.SetFloatArray(ShaderIds.FrameIndex, new float[1023]);
                PropertyBlock.SetFloatArray(ShaderIds.PreFrameIndex, new float[1023]);
                PropertyBlock.SetFloatArray(ShaderIds.TransitionProgress, new float[1023]);
            }
                
            int remaining = DrawCount;
            int startIndex = 0;

            while (remaining > 0)
            {
                int sliceCount = Mathf.Min(remaining, 1023);
        
                // 使用 CopyTo 填充预分配的数组，不产生新的内存申请
                NativeArray<float>.Copy(FrameIndexes, startIndex, _frameIndexCache, 0, sliceCount);
                NativeArray<float>.Copy(PreFrameIndexes, startIndex, _preFrameIndexCache, 0, sliceCount);
                NativeArray<float>.Copy(TransitionProgress, startIndex, _transitionProgressCache, 0, sliceCount);
                NativeArray<Matrix4x4>.Copy(WorldMatrix, startIndex, _matrixCache, 0, sliceCount);

                PropertyBlock.SetFloatArray(ShaderIds.FrameIndex, _frameIndexCache);
                PropertyBlock.SetFloatArray(ShaderIds.PreFrameIndex, _preFrameIndexCache);
                PropertyBlock.SetFloatArray(ShaderIds.TransitionProgress, _transitionProgressCache);

                Graphics.DrawMeshInstanced(DrawMesh, 0, DrawMaterial, _matrixCache, 
                    sliceCount, PropertyBlock, drawShadow, receiveShadow, layer);

                remaining -= sliceCount;
                startIndex += sliceCount;
            }
        }

        #endregion

        #region Parent

        public void SetParent(int index, int parentIndex)
        {
            var data = GetMotionData(index);
            data.ParentIndex = parentIndex;
            SetMotionData(index, data);
            if (_drawType == AnimationDrawType.Instance)
                Debug.LogError("SetParent = " + parentIndex);
        }

        #endregion

        public void ClearPath()
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    for (int i = 0; i < MotionDataAry.Length; i++)
                    {
                        var data = MotionDataAry[i];
                        if (data.Enable == 1)
                            PathData.Instance.RemovePos(data.PathIndex);
                    }
                    break;
                case AnimationDrawType.Buff:
                    var count = _motionDataBuffer.count;
                    var tempMotionData = new MotionData[count];
                    _motionDataBuffer.GetData(tempMotionData, 0, 0, count);
                    foreach (var m in tempMotionData)
                    {
                        if (m.Enable == 1 )
                            PathData.Instance.RemovePos(m.PathIndex);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Dispose()
        {
            ClearPath();

            if (_boneTexture)
            {
                UnityEngine.Object.DestroyImmediate(_boneTexture);
                _boneTexture = null;
            }
            if (_drawType == AnimationDrawType.Instance)
            {
                VisibleIndices.Dispose();
                FrameIndexes.Dispose();
                PreFrameIndexes.Dispose();
                TransitionProgress.Dispose();
                WorldMatrix.Dispose();
            
                AniInfoData.Dispose();
                TotalFrames.Dispose();
                AvaFrames.Dispose();
                MotionDataAry.Dispose();
                InfoIndexes.Clear();
                InfoIndexes = null;
            } 
            else if (_drawType == AnimationDrawType.Buff)
            {
                InfoIndexes.Clear();
                InfoIndexes = null;
                _argsBuffer.Dispose();
                _totalFramesBuffer.Dispose();
                _avaFramesBuffer.Dispose();
                _aniInfoBuffer.Dispose();
                _motionDataBuffer.Dispose();
            }
        }
    }

    public struct VertexData
    {
        public float3 Position;
        public float3 Normal;
        public half4 Tangent;
        public half4 Color;
        public half2 TexCoord0;
        public half4 TexCoord1;
    }

    public class VertexCache
    {
        public int NameCode;
        public Vector4[] Weights;
        public Vector4[] BoneIndexes;
        public Matrix4x4[] BindPoseAry;
        public Transform[] BoneTransformAry;
        public int BoneTextureIndex = -1;

    }
    
    public class DataDefine
    {
        public static int[] StandardTextureSize = { 64, 128, 256, 512, 1024 };

        public const string AnimationTextureDataPath = "/DataExport/AnimationInstancingAnimationTextureData/";
    }
}
