using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fyc.AnimationInstancing
{
    public enum AnimationDrawType
    {
        Instance,
        Buff,
    }
    public class AnimationDrawMgr : SerializedMonoBehaviour
    {
        public static AnimationDrawMgr Instance;
        public bool enableDraw = true; //是否绘制
        public ShadowCastingMode castShadow = ShadowCastingMode.On;
        public bool receiveShadow;
        public AnimationDrawType animationDrawType = AnimationDrawType.Instance;
        public ComputeShader animationComputeShader;
        public uint currentDrawLayer = 0xFFFFFFFF;

        private ParentDateDefine _parentDateDefine; //for group data
        private Dictionary<string, int> _drawInstanceIndexDict;

        private DrawInstancingData[] _drawInstanceData;
        private int _drawInstanceDataCount;
        
        private Camera _cullingCamera;

        private string _loadPath;  //加载的前缀
        private int _defaultNum;  //默认容器的大小
        private const int _defaultTypeNum = 128;

        private bool _init = false;
        
        //Optimal GC
        private NativeArray<Plane> _planeNativeArray;
        private readonly Vector4[] _planeArray = new Vector4[6];
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private void Awake()
        {
            DontDestroyOnLoad(this);
        }

        private void Start()
        {
            Instance = this;
        }

        //Setting
        public void Init(string loadPath, bool needParentSupport = false, int defaultNum = 2048)
        {
            if (_init)
                return;
            _init = true;
            _planeNativeArray = new NativeArray<Plane>(6, Allocator.Persistent);
            _drawInstanceData = new DrawInstancingData[_defaultTypeNum];
            _drawInstanceDataCount = 0;
            _drawInstanceIndexDict = new();
            _loadPath = loadPath;
            _defaultNum = defaultNum;

            animationDrawType = AnimationDrawType.Instance;
            //升级到2022要判定Indirect Draw
            if (SystemInfo.supportsComputeShaders && SystemInfo.supportsIndirectArgumentsBuffer)
            {
                bool isGLES31 = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && 
                                SystemInfo.graphicsDeviceVersion.Contains("OpenGL ES 3.1");
                if (!isGLES31)
                    animationDrawType = AnimationDrawType.Buff;
            }

            if (needParentSupport)
            {
                switch (animationDrawType)
                {
                    case AnimationDrawType.Instance:
                        _parentDateDefine = new ParentDateDefine();
                        break;
                    case AnimationDrawType.Buff:
                        _parentDateDefine = new ParentDateDefine(animationComputeShader);
                        
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        
        //Set Camera for culling
        public void SetCullingCamera(Camera inCamera)
        {
            _cullingCamera = inCamera;
        }

        public void SetDefaultMainCamera()
        {
            if (!_cullingCamera)
                _cullingCamera = Camera.main;
        }
        //enable or diable draw
        public void SetEnableDraw(bool enable)
        {
            enableDraw = enable;
        }

        // turn on shadow cast
        public void SetCastShadow(bool enable)
        {
            castShadow = enable? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        //turn on receive shadow
        public void SetReceiveShadow(bool enable)
        {
            receiveShadow = enable;
        }
        
        //Layer
        public void SetLayer(uint layerMask)
        {
            currentDrawLayer = layerMask;
        }
        //Add Layer
        public void AddLayer(uint layerMask)
        {
            currentDrawLayer |= layerMask;
        }
        //Remove Layer
        public void RemoveLayer(uint layerMask)
        {
            currentDrawLayer &= ~layerMask;
        }
        
        private DrawInstancingData GetDrawInstanceData(string unitName)
        {
            if (_drawInstanceIndexDict.TryGetValue(unitName, out var dataIndex))
            {
                return _drawInstanceData[dataIndex];
            }
            return null;
        }

        #region manager parent data
        
        public bool GetParentIsDirty(int parentIndex)
        {
            if (_parentDateDefine != null)
                return _parentDateDefine.GetIsDirty(parentIndex);
            return false;
        }
        
        public float3 GetParentPos(int parentIndex)
        {
            if (_parentDateDefine != null)
                return _parentDateDefine.GetPosition(parentIndex);
            return float3.zero;
        }
        
        public float GetParentRotY(int parentIndex)
        {
            if (_parentDateDefine != null)
                return _parentDateDefine.GetRotationY(parentIndex);
            return 0f;
        }

        public void SetParentPos(int parentIndex, float3 pos)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.SetPosition(parentIndex, pos);
        }

        public void SetParentRotY(int parentIndex, float rotY)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.SetRotationY(parentIndex, rotY);
        }
        
        private NativeArray<MotionData> _default;
        public ref NativeArray<MotionData> GetParentMotionData()
        {
            if (_parentDateDefine != null)
                return ref _parentDateDefine.GetMotionDataAry();

            if (!_default.IsCreated)
                _default = new NativeArray<MotionData>(1, Allocator.Persistent);
            
            return ref _default;
        }
        public int AddParentData(uint layerMask)
        {
            if (_parentDateDefine != null)
                return _parentDateDefine.AddParent(layerMask);

            return -1;
        }
        
        public void RemoveParentData(int index)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.RemoveParent(index);
        }
        
        //Parent Move
        public void MoveParentTo(int parentIndex, Vector3[] targetPos, float moveSpeed, float rotateSpeed, float finalYaw = Single.PositiveInfinity)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.MoveTo(parentIndex, targetPos, moveSpeed, rotateSpeed, finalYaw);
        }
        
        public void MoveParentTo(int parentIndex, float3 targetPos, float moveSpeed, float rotateSpeed, float finalYaw = Single.PositiveInfinity)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.MoveTo(parentIndex, targetPos, moveSpeed, rotateSpeed, finalYaw);
        }
        
        public void RotateParentTo(int parentIndex, float targetYaw, float rotateSpeed)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.RotateTo(parentIndex, targetYaw, rotateSpeed);
        }
        
        public void StopParentMove(int parentIndex)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.StopMove(parentIndex);
        }
        
        public void StopParentRotate(int parentIndex)
        {
            if (_parentDateDefine != null)
                _parentDateDefine.StopRotate(parentIndex);
        }
        
        #endregion

        # region manager instance
        
        //add unit instance
        public int AddInstance(string unitName, uint layerMask)
        {
            return AddInstance(unitName, layerMask, float3.zero, float3.zero);
        }
        
        public int AddInstanceToParent(string unitName, int parentIndex, int posId, uint layerMask,  float3 pos, float3 rotation, float scale = 1)
        {
            var index = AddInstance(unitName, layerMask, pos, rotation, scale);
            if (index != -1 && _parentDateDefine != null)
                _parentDateDefine.SetChild(parentIndex, posId, unitName, index);

            if (index != -1)
                SetInstanceParent(unitName, index, parentIndex);
            
            return index;
        }
        
        public void SetInstanceParent(string unitName, int index, int parentIndex)
        {
            var data = GetDrawInstanceData(unitName);
            if (data == null)
                return;
            data.SetParent(index, parentIndex);
        }

        //add unit instance with TRS
        public int AddInstance(string unitName, uint layerMask, float3 pos, float3 rotation, float scale = 1)
        {
            var index = 0;
            DrawInstancingData data = null;
            if (_drawInstanceIndexDict.TryGetValue(unitName, out var dataIndex))
            {
                data = _drawInstanceData[dataIndex];
                index = data.AddInstance(layerMask);
                data.SetTRS(index, pos,rotation, scale);
                return index;
            }
            var animationData = LoadAnimationData(unitName);
            if (!animationData)
                return -1;
            switch (animationDrawType)
            {
                case AnimationDrawType.Instance:
                    data = new DrawInstancingData(animationData, _defaultNum);
                    break;
                case AnimationDrawType.Buff:
                    data = new DrawInstancingData(animationData, animationComputeShader, _defaultNum);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _drawInstanceIndexDict[unitName] = _drawInstanceDataCount;
            _drawInstanceData[_drawInstanceDataCount++] = data;
            index = data.AddInstance(layerMask);
            data.SetTRS(index, pos,rotation, scale);
            return index;
        }

        public void RemoveInstance(string unitName, int index)
        {
            if (_drawInstanceIndexDict.TryGetValue(unitName, out var dataIndex))
            {
                var data = _drawInstanceData[dataIndex];
                data.RemoveInstance(index);
            }
        }

        public void SetVisible(string unitName, int index, int visible)
        {
            if (_drawInstanceIndexDict.TryGetValue(unitName, out var dataIndex))
            {
                var data = _drawInstanceData[dataIndex];
                data.SetVisible(index, visible);
            }
        }
        
        #endregion

        #region Update

        private void UpdateBaseData()
        {
            switch (animationDrawType)
            {
                case AnimationDrawType.Instance:
                    GeometryUtility.CalculateFrustumPlanes(_cullingCamera, _frustumPlanes);
                    _planeNativeArray.CopyFrom(_frustumPlanes);
                    break;
                case AnimationDrawType.Buff:
                    GeometryUtility.CalculateFrustumPlanes(_cullingCamera, _frustumPlanes);
                    for (int i = 0; i < 6; i++)
                    {
                        _planeArray[i].x = _frustumPlanes[i].normal.x;
                        _planeArray[i].y = _frustumPlanes[i].normal.y;
                        _planeArray[i].z = _frustumPlanes[i].normal.z;
                        _planeArray[i].w = _frustumPlanes[i].distance;
                    }
                    animationComputeShader.SetFloat(ComputeShaderIds.DeltaTimeName, Time.deltaTime);
                    animationComputeShader.SetVectorArray(ComputeShaderIds.PlaneName, _planeArray);
                    animationComputeShader.SetInt(ComputeShaderIds.DrawLayerMask, (int)currentDrawLayer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private void UpdateParentData()
        {
            if (_parentDateDefine == null)
                return;
            
            switch (animationDrawType)
            {
                case AnimationDrawType.Instance:
                    _parentDateDefine.UpdateParentMotionJob(currentDrawLayer, Time.deltaTime, _planeNativeArray);
                    break;
                case AnimationDrawType.Buff:
                    _parentDateDefine.UpdateParentMotionBuffer();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        private void Update()
        {
            if (_drawInstanceDataCount == 0 || !_cullingCamera || !enableDraw || !_init)
                return;

            UpdateBaseData();
            UpdateParentData();
            
            for (int i = 0; i < _drawInstanceDataCount; i++)
            {
                switch (animationDrawType)
                {
                    case AnimationDrawType.Instance:
                        _drawInstanceData[i].DoCullingAndAnimationJob(currentDrawLayer, Time.deltaTime, _planeNativeArray);
                        _drawInstanceData[i].Draw(castShadow, receiveShadow);
                        break;
                    case AnimationDrawType.Buff:
                        _drawInstanceData[i].DoComputeCullingAndAnimation();
                        _drawInstanceData[i].IndirectDraw(castShadow, receiveShadow);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion
        
        #region TRS
        //Transform
        public void SetTRS(string unitName, int index, float3 pos, float3 rotation, float scale)
        {
            var data = GetDrawInstanceData(unitName);
            data.SetTRS(index, pos, rotation, scale);
        }
        //Position
        public void SetPosition(string unitName, int index, float3 pos)
        {
            var data = GetDrawInstanceData(unitName);
            data.SetPosition(index, pos);
        }
        //Rotation
        public void SetRotation(string unitName, int index, float3 rotation)
        {
            var data = GetDrawInstanceData(unitName);
            data.SetRotation(index, rotation);
        }

        public void SetRotationY(string unitName, int index, float yaw)
        {
            var data = GetDrawInstanceData(unitName);
            data.SetRotationY(index, yaw);
        }
        //Scale
        public void SetScale(string unitName, int index, float scale)
        {
            var data = GetDrawInstanceData(unitName);
            data.SetScale(index, scale);
        }
        #endregion

        #region Animation
        //play animation
        public bool PlayAnimation(string unitName, int index, string animationName, float startFrame = 0f, float transitionTime = 0.25f, bool resetToLast = false)
        {
            var data = GetDrawInstanceData(unitName);
            return data.PlayAnimation(index, animationName, startFrame, transitionTime, resetToLast);
        }

        #endregion
        
        #region Motion

        //Move to single pos
        public void MoveTo(string unitName, int index, float3 targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity, bool changeRot = true)
        {
            var data = GetDrawInstanceData(unitName);
            data.MoveTo(index, targetPos, moveSpeed, rotateSpeed, finalYaw, changeRot);
        }
        //Move to Multi poses
        public void MoveTo(string unitName, int index, Vector3[] targetPos, float moveSpeed, float rotateSpeed,
            float finalYaw = Single.PositiveInfinity)
        {
            var data = GetDrawInstanceData(unitName);
            data.MoveTo(index, targetPos, moveSpeed, rotateSpeed, finalYaw);
        }
        
        public void RotateTo(string unitName, int index, float targetYaw, float rotateSpeed)
        {
            var data = GetDrawInstanceData(unitName);
            data.RotateTo(index, targetYaw, rotateSpeed);
        }
        
        public bool GetPos(string unitName, int index, out float3 pos)
        {
            var data = GetDrawInstanceData(unitName);
            return data.GetPosition(index, out pos);
        }
        
        public float GetRotationY(string unitName, int index)
        {
            var data = GetDrawInstanceData(unitName);
            return data.GetRotationY(index);
        }
        
        //move forward to same dir
        public void MoveDir(string unitName, int index, float3 dir, float speed, string animationName = "")
        {
            var data = GetDrawInstanceData(unitName);
            data.MoveDir(index, dir, speed, animationName);
        }
        //rotate Y with speed
        public void RotateY(string unitName, int index, float speed, string animationName = "")
        {
            var data = GetDrawInstanceData(unitName);
            data.RotateY(index, speed, animationName);
        }
        
        public void StopMove(string unitName, int index)
        {
            var data = GetDrawInstanceData(unitName);
            data.StopMove(index);
        }

        public void StopRotate(string unitName, int index)
        {
            var data = GetDrawInstanceData(unitName);
            data.StopRotate(index);
        }

        #endregion
        
        private AnimationData LoadAnimationData(string unitName)
        {
            var data = Resources.Load<AnimationData>(unitName);

            return data;
        }

        private void ReleaseData()
        {
            if (_drawInstanceData == null)
                return;
            foreach (var data in _drawInstanceData)
            {
                data?.Dispose();
            }
        }
        public void ClearData()
        {
            ReleaseData();
            _drawInstanceIndexDict?.Clear();
            _drawInstanceDataCount = 0;
        }
        private void OnDestroy()
        {
            if (_default.IsCreated)
                _default.Dispose();
            ClearData();
            if (_parentDateDefine != null)
                _parentDateDefine.Dispose();
            PathData.Instance.Dispose(); //销毁路径数据
            _drawInstanceData = null;
            if (_planeNativeArray.IsCreated)
                _planeNativeArray.Dispose();
            _init = false;
        }
    }
}
