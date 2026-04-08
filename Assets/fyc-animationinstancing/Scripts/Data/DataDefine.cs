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
        //Optimal job scheduling
        private JobHandle _pendingCullingJobHandle;
        private JobHandle _pendingAnimationJobHandle;
        private bool _hasPendingCullingJob;
        private bool _hasPendingAnimationJob;
        
        //Pos Array
        private NativeArray<float3> MovePosAry;
        
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
        
        private int[] _pathIndexData;
        
        //Cache
        private MotionData[] _motionDataReadbackCache;
        private int _motionDataReadbackFrame = -1;
        private bool _motionDataReadbackValid;
        private MotionData[] _motionDataWriteCache;
        private int _motionDataWriteCacheFrame = -1;
        private bool _hasPendingMotionWrite;
        private FrameData[] _frameDataReadbackCache;
        private int _frameDataReadbackFrame = -1;
        private bool _frameDataReadbackValid;
        private FrameData[] _frameDataWriteCache;
        private int _frameDataWriteCacheFrame = -1;
        private bool _hasPendingFrameWrite;
        

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
            _pathIndexData = new int[defaultNum];
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

            // TotalFrames = new NativeArray<FrameData>(defaultNum, Allocator.Persistent);
            AvaSlots = new(defaultNum);
            //Kernel
            _cullingAndAnimationKernel = _animationDrawShader.FindKernel(ComputeShaderIds.ComputeCullingAnimationKernel);
            uint[] args = new uint[5] { (uint)mainData.mainMesh.triangles.Length, 0, 0, 0, 0 };
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(args);
            _aniInfoBuffer = new ComputeBuffer(mainData.animationData.Length, Marshal.SizeOf<AnimationSaveData>());
            _aniInfoBuffer.SetData(AniInfoData);
            _totalFramesBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<FrameData>());
            // _totalFramesBuffer.SetData(TotalFrames);
            _avaFramesBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<DrawFrameData>(), ComputeBufferType.Append);
            _motionDataBuffer = new ComputeBuffer(defaultNum, Marshal.SizeOf<MotionData>());

            _motionDataReadbackCache = new MotionData[defaultNum];
            _motionDataWriteCache = new MotionData[defaultNum];
            _frameDataReadbackCache = new FrameData[defaultNum];
            _frameDataWriteCache = new FrameData[defaultNum];
            
            DrawMaterial.SetBuffer(ComputeShaderIds.AvaBufferName, _avaFramesBuffer);
            
            InfoIndexes = new Dictionary<int, int>();

            for (int i = 0; i < mainData.animationData.Length; i++)
            {
                var info = mainData.GetAnimation(i);
                InfoIndexes[info.animationNameHash] = i;
            }

            AniInfoData.Dispose();
            // TotalFrames.Dispose();
            
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
            _pathIndexData = new int[defaultNum];
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

        public string GetName()
        {
            return AniData?.name;
        }

        public string GetActiveCount()
        {
            return $"{InstancingCount}_{AvaSlots.Count}";
        }
        public void SetBoneTexture(Texture2D texture)
        {
            DrawMaterial.SetTexture(ShaderIds.BoneTexture, texture);
        }

        private ref FrameData GetFrameDataBuff(int index)
        {
            if (_hasPendingFrameWrite || _frameDataWriteCacheFrame == Time.frameCount)
                return ref _frameDataWriteCache[index];
            EnsureFrameDataReadback();
            return ref _frameDataReadbackCache[index];
        }

        private void SetFrameDataBuff(int index, ref FrameData frameData)
        {
            EnsureFrameDataWriteCache();
            _frameDataWriteCache[index] = frameData;
            _hasPendingFrameWrite = true;
            InvalidateFrameDataReadback();
        }

        private ref MotionData GetMotionDataBuff(int index)
        {
            if (_hasPendingMotionWrite || _motionDataWriteCacheFrame == Time.frameCount)
                return ref _motionDataWriteCache[index];
            EnsureMotionDataReadback();
            return ref _motionDataReadbackCache[index];
        }
        
        private void SetMotionDataBuff(int index, ref MotionData motionData)
        {
            EnsureMotionDataWriteCache();
            _motionDataWriteCache[index] = motionData;
            _hasPendingMotionWrite = true;
            InvalidateMotionDataReadback();
        }

        private void InvalidateMotionDataReadback()
        {
            _motionDataReadbackValid = false;
            _motionDataReadbackFrame = -1;
        }
        
        private void InvalidateFrameDataReadback()
        {
            _frameDataReadbackValid = false;
            _frameDataReadbackFrame = -1;
        }

        private void EnsureMotionDataReadback()
        {
            if (_motionDataReadbackCache == null)
                _motionDataReadbackCache = new MotionData[MaxCount];
            if (_motionDataReadbackValid && _motionDataReadbackFrame == Time.frameCount)
                return;

            if (InstancingCount > 0)
                _motionDataBuffer.GetData(_motionDataReadbackCache, 0, 0, InstancingCount);

            _motionDataReadbackValid = true;
            _motionDataReadbackFrame = Time.frameCount;
        }

        private void EnsureFrameDataReadback()
        {
            if (_frameDataReadbackCache == null)
                _frameDataReadbackCache = new FrameData[MaxCount];
            if (_frameDataReadbackValid && _frameDataReadbackFrame == Time.frameCount)
                return;

            if (InstancingCount > 0)
                _totalFramesBuffer.GetData(_frameDataReadbackCache, 0, 0, InstancingCount);

            _frameDataReadbackValid = true;
            _frameDataReadbackFrame = Time.frameCount;
        }

        private void EnsureMotionDataWriteCache()
        {
            if (_hasPendingMotionWrite)
                return;
            if (_motionDataWriteCacheFrame == Time.frameCount)
                return;
            
            EnsureMotionDataReadback();
            Array.Copy(_motionDataReadbackCache, _motionDataWriteCache, InstancingCount);
            _motionDataWriteCacheFrame = Time.frameCount;
        }

        private void EnsureFrameDataWriteCache()
        {
            if (_hasPendingFrameWrite)
                return;
            if (_frameDataWriteCacheFrame == Time.frameCount)
                return;
            EnsureFrameDataReadback();
            Array.Copy(_frameDataReadbackCache, _frameDataWriteCache, InstancingCount);
            _frameDataWriteCacheFrame = Time.frameCount;
        }

        private void FlushPendingBufferWrites()
        {
            if (_hasPendingFrameWrite && InstancingCount > 0)
            {
                _totalFramesBuffer.SetData(_frameDataWriteCache, 0, 0, InstancingCount);
                _hasPendingFrameWrite = false;
                InvalidateFrameDataReadback();
            }

            if (_hasPendingMotionWrite && InstancingCount > 0)
            {
                _motionDataBuffer.SetData(_motionDataWriteCache, 0, 0, InstancingCount);
                _hasPendingMotionWrite = false;
                InvalidateMotionDataReadback();
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
                _pathIndexData[index] = -1;
                var frameData = GetFrameDataBuff(index);
                frameData.Active();
                SetFrameDataBuff(index, ref frameData);

                var motionData = GetMotionDataBuff(index);
                motionData.Reset(layerMask);
                SetMotionDataBuff(index, ref motionData);
                return index;
            }
            if (InstancingCount >= MaxCount)
            {
                Debug.LogError("Instance Count Beyond Limit Exceeded");
                return -1;
            }
            
            var initFrameData = GetFrameDataBuff(InstancingCount);
            initFrameData.Active();
            SetFrameDataBuff(InstancingCount, ref initFrameData);
            var initMotionData = GetMotionDataBuff(InstancingCount);
            initMotionData.Reset(layerMask);
            SetMotionDataBuff(InstancingCount, ref initMotionData);
            _pathIndexData[InstancingCount] = -1;
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
                _pathIndexData[index] = -1;
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
                _pathIndexData[InstancingCount] = -1;
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
            var frameData = GetFrameDataBuff(index);
            if (frameData.Enable == 0)
                return;
            frameData.Enable = 0;
            SetFrameDataBuff(index, ref frameData);
            AvaSlots.Push(index);
            
            //remove path
            var motionData = GetMotionDataBuff(index);
            motionData.Enable = 0;
            motionData.ParentIndex = -1;
            SetMotionDataBuff(index, ref motionData);
            
            ResetPath(index);
        }

        public void RemoveInstanceData(int index)
        {
            var instanceData = TotalFrames[index];
            if (instanceData.Enable == 0)
                return;
            instanceData.Enable = 0;
            TotalFrames[index] = instanceData;
            AvaSlots.Push(index);
            
            //remove path
            var motionData = MotionDataAry[index];
            if (_pathIndexData[index] >= 0)
                PathData.Instance.RemovePos(_pathIndexData[index]);
            motionData.Enable = 0;
            motionData.ParentIndex = -1;
            MotionDataAry[index] = motionData;
        }

        private void ResetPath(int index, int setPath = -1)
        {
            if (_pathIndexData[index] >= 0)
                PathData.Instance.RemovePos(_pathIndexData[index]);
            _pathIndexData[index] = setPath;
        }
        //visible 
        public void SetVisible(int index, int visible)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.Visible = visible;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Visible = visible;
                    SetMotionDataBuff(index, ref data);
                    break;
            }
        }
        // speed 
        public void SetAnimationSpeed(int index, float speed)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = TotalFrames[index];
                    data.AniSpeed = speed;
                    TotalFrames[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetFrameDataBuff(index);
                    data.AniSpeed = speed;
                    SetFrameDataBuff(index, ref data);
                    break;
            }
        }

        public void MoveTo(int index, float3 targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity, bool changeRot = true)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    ResetPath(index);
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
                    data = GetMotionDataBuff(index);
                    ResetPath(index);
                    targetPos.y = data.Position.y;
                    data.PathIndex = -1;
                    data.CurPathIndex = 0;
                    data.TargetPos = targetPos;
                    data.MoveSpeed = moveSpeed;
                    
                    moveVec = targetPos - data.Position;
                    data.MoveDirection = math.normalize(moveVec);
                    
                    if (changeRot)
                    {
                        data.RotateYSpeed = rotateSpeed;
                        data.RotateYSpeedSetting = rotateSpeed;
                        data.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                        data.RotateYFinal = finalYaw;
                    }
                    SetMotionDataBuff(index, ref data);
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
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
         
                    pathIndex = PathData.Instance.AddBufferPoses(targetPos);
                    ResetPath(index, pathIndex);
                    data.PathIndex = pathIndex + targetPos.Length - 1;
                    data.CurPathIndex = pathIndex;
                    data.MoveSpeed = moveSpeed;
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    
                    firstPos.y = data.Position.y;
                    moveVec = firstPos - (Vector3)data.Position;
                    data.MoveDirection = math.normalize(moveVec);
                    data.RotateYTarget = Utils.DirectionToEulerAngles(moveVec).y;
                    data.RotateYFinal = finalYaw;
                    
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public void MoveDir(int index, float3 dir, float speed, string animationName = "")
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    ResetPath(index);
                    data.MoveSpeed = speed;
                    data.MoveDirection = math.normalize(dir);
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    ResetPath(index);
                    data.MoveSpeed = speed;
                    data.MoveDirection = math.normalize(dir);
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!string.IsNullOrEmpty(animationName))
                PlayAnimation(index, animationName);
        }
        
        // stop moving
        public void StopMove(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    ResetPath(index);
                    data.StopMove();
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    ResetPath(index);
                    data.StopMove();
                    SetMotionDataBuff(index, ref data);
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
                    var data = MotionDataAry[index];
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYFinal = targetY;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.RotateYSpeed = rotateSpeed;
                    data.RotateYSpeedSetting = rotateSpeed;
                    data.RotateYFinal = targetY;
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        //移动相关函数 可以通过设置方向或者设置目的地, 来通过job或者compute计算移动
        //减少每帧设置位移数据的消耗
        public void RotateY(int index, float speed, string animationName = "")
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.RotateYSpeed = speed;
                    data.RotateYSpeedSetting = speed;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.RotateYSpeed = speed;
                    data.RotateYSpeedSetting = speed;
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            if (!string.IsNullOrEmpty(animationName))
                PlayAnimation(index, animationName);
        }
        // stop rotate
        public void StopRotate(int index)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.StopRotate();
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.StopRotate();
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }
        //translation
        // set TRS
        public void SetTRS(int index, float3 position, float3 rotation, float scale)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.Position = position;
                    data.Rotation = rotation;
                    data.Scale = scale;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Position = position;
                    data.Rotation = rotation;
                    data.Scale = scale;
                    SetMotionDataBuff(index, ref data);
                    break;
            }
        }

        public bool GetPosition(int index, out float3 pos)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    pos = data.Position;
                    return true;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    pos = data.Position;
                    return true;
                default:
                    pos = float3.zero;
                    return false;
            }
        }
        
        // set position
        public void SetPosition(int index, float3 position)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.Position = position;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Position = position;
                    SetMotionDataBuff(index, ref data);
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
                    var data = MotionDataAry[index];
                    return data.Rotation.y;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    return data.Rotation.y;
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
                    var data = MotionDataAry[index];
                    data.Rotation = rotation;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Rotation = -rotation;
                    SetMotionDataBuff(index, ref data);
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
                    var data = MotionDataAry[index];
                    data.Rotation.y = yaw;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Rotation.y = -yaw;  //
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        //scale
        public void SetScale(int index, float scale)
        {
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.Scale = scale;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.Scale = scale;
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        #region JobSystem

        public void ScheduleCullingJob(uint layerMask, float deltaTime, ref NativeArray<Plane> planeNativeArray,
            ref NativeArray<MotionData> parentMotionData)
        {
            VisibleIndices.Clear();
            var job = new CullingUnitJob()
            {
                DeltaTime = deltaTime,
                LayerMask = layerMask,
                MotionData = MotionDataAry,
                ParentMotionData = parentMotionData,
                PathAry = PathData.Instance.GetPathAry(),
                VisibleIndices = VisibleIndices.AsParallelWriter(),
                Planes = planeNativeArray,
            };

            _pendingCullingJobHandle = job.Schedule(InstancingCount, 64);
            _hasPendingCullingJob = true;
        }

        public void ScheduleAnimationJob(float deltaTime, ref NativeArray<MotionData> parentMotionData)
        {
            //Debug
            // Debug.LogError("TotalFrames[0].Rotation = "  + TotalFrames[0].Rotation);
            // Debug.LogError("TotalFrames[0].Position = "  + TotalFrames[0].Position);
            // Debug.LogError("PathAry[2] = "  + PathData.Instance.GetPathAry()[MotionDataAry[0].CurPathIndex]);
            // Debug.LogError($"MotionDataAry[0].CurPathIndex = {MotionDataAry[0].CurPathIndex}, Step = {MotionDataAry[0].Step}, Distance = {MotionDataAry[0].Distance}");
            //Debug.LogError($"MotionDataAry[0].DebugY =  {MotionDataAry[0].DebugY}, YSpeed = {MotionDataAry[0].RotateYSpeed}");
            DrawCount = VisibleIndices.Length;
            
            if (DrawCount <= 0)
            {
                _hasPendingAnimationJob = false;
                return;
            }
            // WorldMatrix = new NativeArray<Matrix4x4>(DrawCount, Allocator.TempJob);
            // FrameIndexes = new NativeArray<float>(DrawCount, Allocator.TempJob);
            // PreFrameIndexes = new NativeArray<float>(DrawCount, Allocator.TempJob);
            // TransitionProgress = new NativeArray<float>(DrawCount, Allocator.TempJob);
            
            var aniJob = new AnimationJob()
            {
                DeltaTime = deltaTime,
                FrameData = TotalFrames,
                ParentMotionData = parentMotionData,
                MotionData = MotionDataAry,
                VisibleIndices = VisibleIndices,
                InfoData = AniInfoData,
                WorldMatrix = WorldMatrix,
                FrameIndexes = FrameIndexes,
                PreFrameIndexes = PreFrameIndexes,
                TransitionProgress = TransitionProgress,
            };
            _pendingAnimationJobHandle = aniJob.Schedule(DrawCount, 64);
            _hasPendingAnimationJob = true;
            //Debug
            // Debug.LogError("WorldMatrix[0] = "  + WorldMatrix[0]);
        }
        
        public void CompleteScheduledCullingJob()
        {
            if (!_hasPendingCullingJob)
                return;
            _pendingCullingJobHandle.Complete();
            _hasPendingCullingJob = false;
        }

        public void CompleteScheduledAnimationJob()
        {
            if (!_hasPendingAnimationJob)
                return;

            _pendingAnimationJobHandle.Complete();
            _hasPendingAnimationJob = false;
        }
        
        #endregion

        #region Compute Shader


        
        public void DoComputeCullingAndAnimation()
        {
            FlushPendingBufferWrites();
            
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.AniInfoBufferName, _aniInfoBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.TotalBufferName, _totalFramesBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.AvaBufferName, _avaFramesBuffer);
            _animationDrawShader.SetBuffer(_cullingAndAnimationKernel, ComputeShaderIds.MotionBufferName, _motionDataBuffer);
            _animationDrawShader.SetInt(ComputeShaderIds.InstanceCount, InstancingCount);
            _avaFramesBuffer.SetCounterValue(0);
            
            // Debug.LogError("1111111111_tempFrameDataAry[0].rotation = " + _tempFrameDataAry[0].Rotation + " _" + _tempMotionDataAry[0].RotateYSpeed + "_" + _tempMotionDataAry[0].RotateYSpeedSetting);
            _animationDrawShader.Dispatch(_cullingAndAnimationKernel, Mathf.CeilToInt(InstancingCount / 64f), 1, 1);
            ComputeBuffer.CopyCount(_avaFramesBuffer, _argsBuffer, 4);
            InvalidateMotionDataReadback();
            InvalidateFrameDataReadback();
            
            // _motionDataBuffer.GetData(_motionDataReadbackCache, 0, 0, InstancingCount);
            // _totalFramesBuffer.GetData(_frameDataReadbackCache, 0, 0, InstancingCount);
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

            var data = GetFrameDataBuff(index);

            if (data.Enable == 0)
                return false;
            startFrame -= 1;
            if (InfoIndexes.TryGetValue(animationName.GetHashCode(), out var infoIndex))
            {
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
                SetFrameDataBuff(index, ref data);
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
            if (InfoIndexes.TryGetValue(animationName.GetHashCode(), out var infoIndex))
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
            switch (_drawType)
            {
                case AnimationDrawType.Instance:
                    var data = MotionDataAry[index];
                    data.ParentIndex = parentIndex;
                    MotionDataAry[index] = data;
                    break;
                case AnimationDrawType.Buff:
                    data = GetMotionDataBuff(index);
                    data.ParentIndex = parentIndex;
                    SetMotionDataBuff(index, ref data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        #endregion
        public void ClearPath()
        {
            for (int i = 0; i < InstancingCount; i++)
            {
                ResetPath(i);
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

        // public const string AnimationTextureDataPath = "/DataExport/AnimationInstancingAnimationTextureData/";
    }
}