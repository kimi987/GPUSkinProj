using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class AnimationDrawTest : SerializedMonoBehaviour
    {
        public AnimationData data;
        public Camera drawCamera;
        public int drawCount;
        [OnValueChanged("OnRotationChanged")]
        public float rotationY;
        [OnValueChanged("OnScaleChanged")]
        public float scale;
        [OnValueChanged("OnAnimationSpeedChanged")]
        public float animationSpeed = 1;

        public List<Vector3> movePoses = new();
         
        public TextAsset textAsset;
        public Texture2D oldTexture;
        public Texture2D newTexture;
        
        public ComputeShader computeShader;

        public bool activeBufferDraw;
        
        public AnimationDrawMgr animationDrawMgr;

        public uint currentDrawLayer = 0xFFFFFFFF;
        
        private DrawInstancingData _instancingData;
        private DrawInstancingData _bufferInstancingData;

        private ParentDataDefine _parentDataInstanceDefine;

        private ParentDataDefine _parentDataBufferDefine;
        //Optimal GC
        private NativeArray<Plane> _planeNativeArray;
        private readonly Vector4[] _planeArray = new Vector4[6];
        private readonly Plane[] _frustumPlanes = new Plane[6];
        
        

        private void Start()
        {
            _planeNativeArray = new NativeArray<Plane>(6, Allocator.Persistent);
            _parentDataInstanceDefine = new ParentDataDefine();
            _parentDataBufferDefine = new ParentDataDefine(computeShader);
        }

        [Button]
        public void TestOldTexture()
        {
            if (!textAsset)
                return;
            using MemoryStream stream = new MemoryStream(textAsset.bytes);
            using BinaryReader reader = new BinaryReader(stream);
            ReadAnimationInfo(reader);
            ReadExtraBoneInfo(reader);
            ReadTexture(reader,"aa");
        }
        [Button]
        public void TestNewTexture()
        {
            if (!data)
                return;
            newTexture = data.CreateBoneTexture();
        }
        [Button]
        public void SetOldBoneTexture()
        {
            if (_instancingData != null)
            {
                _instancingData.SetBoneTexture(oldTexture);
            }
        }
        
        [Button]
        public void SetNewBoneTexture()
        {
            if (_instancingData != null)
            {
                _instancingData.SetBoneTexture(newTexture);
            }
        }
        
        [Button]
        public void AddNum()
        {
            if (_instancingData == null)
                _instancingData = new DrawInstancingData(data, drawCount);

            for (int i = 0; i < drawCount; i++)
            {
                var index = _instancingData.AddInstance(1);
                var pos = new float3(i % 100, 0, i / 100) * 0.1f;
                _instancingData.SetPosition(index, pos);
            }
        }

        [Button]
        public void AddBufferNum()
        {
            if (_bufferInstancingData == null)
                _bufferInstancingData = new DrawInstancingData(data, computeShader, drawCount);
            
            for (int i = 0; i < drawCount; i++)
            {
                var index = _bufferInstancingData.AddInstance(1);
                var pos = new float3(i % 100, 0, i / 100) * 0.1f;
                _bufferInstancingData.SetPosition(index, pos);
            }
        }

        [Button]
        public void PlayAnimation(string animationName)
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_instancingData != null)
                    _instancingData.PlayAnimation(i, animationName);
                if (_bufferInstancingData != null)
                    _bufferInstancingData.PlayAnimation(i, animationName);
            }
        }
        
        [Button]
        public void MoveTo(float moveSpeed)
        {
            var moves = movePoses.ToArray();
            for (int i = 0; i < drawCount; i++)
            {
                if (_instancingData != null)
                    _instancingData.MoveTo(i, moves, moveSpeed, 360);
                if (_bufferInstancingData != null)
                    _bufferInstancingData.MoveTo(i, moves, moveSpeed, 360);
            } 
            PlayAnimation("run");
        }
        
        [Button]
        public void MoveTo(float moveSpeed, float3 dir)
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_instancingData != null)
                    _instancingData.MoveDir(i, dir, moveSpeed);
                if (_bufferInstancingData != null)
                    _bufferInstancingData.MoveDir(i, dir, moveSpeed);
            }
        }
        
        [Button]
        public void RotateTo(float rotateSpeed)
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_instancingData != null)
                    _instancingData.RotateY(i, rotateSpeed);
                if (_bufferInstancingData != null)
                    _bufferInstancingData.RotateY(i, rotateSpeed);
            }
        }
        [Button]
        public void AddInstance(string unitName)
        {
            animationDrawMgr.Init("Assets/Res/Army/Export", true);
            animationDrawMgr.SetCullingCamera(Camera.main);
            animationDrawMgr.AddInstance(unitName, 1);
        }

        private void UpdateBaseData()
        {
            if (_instancingData != null)
            {
                GeometryUtility.CalculateFrustumPlanes(drawCamera, _frustumPlanes);
                _planeNativeArray.CopyFrom(_frustumPlanes);
            }

            if (_bufferInstancingData != null)
            {
                GeometryUtility.CalculateFrustumPlanes(drawCamera, _frustumPlanes);
                for (int i = 0; i < 6; i++)
                {
                    _planeArray[i].x = _frustumPlanes[i].normal.x;
                    _planeArray[i].y = _frustumPlanes[i].normal.y;
                    _planeArray[i].z = _frustumPlanes[i].normal.z;
                    _planeArray[i].w = _frustumPlanes[i].distance;
                }
                computeShader.SetFloat(ComputeShaderIds.DeltaTimeName, Time.deltaTime);
                computeShader.SetVectorArray(ComputeShaderIds.PlaneName, _planeArray);
                computeShader.SetInt(ComputeShaderIds.DrawLayerMask, (int)currentDrawLayer);
            }
        }
        private void Update()
        {
            
            if (!drawCamera)
                return;

            UpdateBaseData();
            if (_instancingData != null)
            {
                _instancingData.ScheduleCullingJob(currentDrawLayer, Time.deltaTime, ref _planeNativeArray, ref _parentDataInstanceDefine.GetMotionDataAry());
                _instancingData.CompleteScheduledCullingJob();
                _instancingData.ScheduleAnimationJob(Time.deltaTime, ref _parentDataInstanceDefine.GetMotionDataAry());
                _instancingData.CompleteScheduledAnimationJob();
                if (activeBufferDraw)
                    _instancingData.Draw();
            }

            if (_bufferInstancingData != null)
            {
                _bufferInstancingData.DoComputeCullingAndAnimation();
                if (activeBufferDraw)
                    _bufferInstancingData.IndirectDraw();
            }
        }

        private void OnDestroy()
        {
            if (_instancingData != null)
            {
                _instancingData.Dispose();
                _instancingData = null;
            }


            if (_bufferInstancingData != null)
            {
                _bufferInstancingData.Dispose();
                _bufferInstancingData = null;
            }
                
        }
        
        //test
        private List<AnimationInfo> ReadAnimationInfo(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            List<AnimationInfo>  listInfo = new List<AnimationInfo>();
            for (int i = 0; i != count; ++i)
            {
                AnimationInfo info = new AnimationInfo();
                //info.animationNameHash = reader.ReadInt32();
                string animationName = reader.ReadString();
                // var animationNameHash = info.animationName.GetHashCode();
                var animationIndex = reader.ReadInt32();
                var textureIndex = reader.ReadInt32();
                var totalFrame = reader.ReadInt32();
                var fps = reader.ReadInt32();
                var rootMotion = reader.ReadBoolean();
                var wrapMode = (WrapMode)reader.ReadInt32();
                if (rootMotion)
                {
                    var velocity = new Vector3[totalFrame];
                    var angularVelocity = new Vector3[totalFrame];
                    for (int j = 0; j != totalFrame; ++j)
                    {
                        velocity[j].x = reader.ReadSingle();
                        velocity[j].y = reader.ReadSingle();
                        velocity[j].z = reader.ReadSingle();

                        angularVelocity[j].x = reader.ReadSingle();
                        angularVelocity[j].y = reader.ReadSingle();
                        angularVelocity[j].z = reader.ReadSingle();
                    }
                }
                int evtCount = reader.ReadInt32();
                // var eventList = new List<AnimationEvent>();
                for (int j = 0; j != evtCount; ++j)
                {
                    AnimationEvent evt = new AnimationEvent();
                    var function = reader.ReadString();
                    evt.floatParameter = reader.ReadSingle();
                    evt.intParameter = reader.ReadInt32();
                    evt.stringParameter = reader.ReadString();
                    evt.time = reader.ReadSingle();
                    var objectParameter = reader.ReadString();
                    // info.eventList.Add(evt);
                }
                listInfo.Add(info);
            }
            // listInfo.Sort(new ComparerHash());
            return listInfo;
        }
        
        private void ReadExtraBoneInfo(BinaryReader reader)
        {
            // ExtraBoneInfo info = null;
            if (reader.ReadBoolean())
            {
                // info = new ExtraBoneInfo();
                int count = reader.ReadInt32();
                var extraBone = new string[count];
                var extraBindPose = new Matrix4x4[count];
                for (int i = 0; i != extraBone.Length; ++i)
                {
                    extraBone[i] = reader.ReadString();
                }
                for (int i = 0; i != extraBindPose.Length; ++i)
                {
                    for (int j = 0; j != 16; ++j)
                    {
                        extraBindPose[i][j] = reader.ReadSingle();
                    }
                }
            }
        }
        
        private void ReadTexture(BinaryReader reader, string prefabName)
        {
            TextureFormat format = TextureFormat.RGBAHalf;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2)
            {
                //todo
                format = TextureFormat.RGBA32;
            }
            int count = reader.ReadInt32();
            int blockWidth = reader.ReadInt32();
            int blockHeight = reader.ReadInt32();

            // AnimationTexture aniTexture = new AnimationTexture();
            var boneTexture = new Texture2D[count];
            var name = prefabName;
            // animationTextureList.Add(aniTexture);

            for (int i = 0; i != count; ++i)
            {
                int textureWidth = reader.ReadInt32();
                int textureHeight = reader.ReadInt32();
                int byteLength = reader.ReadInt32();
                byte[] b = new byte[byteLength];
                b = reader.ReadBytes(byteLength);
                oldTexture = new Texture2D(textureWidth, textureHeight, format, false);
                oldTexture.LoadRawTextureData(b);
                oldTexture.filterMode = FilterMode.Point;
                oldTexture.Apply();
            }
        }

        private void OnRotationChanged()
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_bufferInstancingData != null)
                    _bufferInstancingData.SetRotation(i, new float3(0, rotationY, 0));
                if (_instancingData != null)
                    _instancingData.SetRotation(i, new float3(0, rotationY, 0));
            }
        }

        private void OnScaleChanged()
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_bufferInstancingData != null)
                    _bufferInstancingData.SetScale(i, scale);
                if (_instancingData != null)
                    _instancingData.SetScale(i, scale);
            } 
        }

        private void OnAnimationSpeedChanged()
        {
            for (int i = 0; i < drawCount; i++)
            {
                if (_bufferInstancingData != null)
                    _bufferInstancingData.SetAnimationSpeed(i, animationSpeed);
                if (_instancingData != null)
                    _instancingData.SetAnimationSpeed(i, animationSpeed);
            } 
        }
        
        [Button]
        public void TestCalRotationY(float3 dir)
        {
            dir = math.normalize(dir);
            float radians = Mathf.Atan2(dir.x, dir.z);
            float degrees = radians * (180f / 3.14159265f);
            
            degrees = (degrees < 0f) ? (degrees + 360.0f) : degrees;
            Debug.LogError("degrees = " + degrees);

            var angles = Quaternion.LookRotation(dir).eulerAngles;
            
            Debug.LogError("angles = " + angles);
        }

        private string drawNumTex = "5000";

        #region Parent测试

        [Title("Parent测试配置")]
        public int parentCreateCount = 5;
        public uint parentLayerMask = 1;
        public int childCount = 10;                   // 每个 Parent 的子对象数量
        public List<string> childUnitNames = new();   // 子对象的 unit 名称，不足时循环复用
        public float childSpacing = 1f;               // 阵型间距（5个一排）
        public float parentSpawnRange = 20f;          // Parent 随机分布范围（XZ 平面正负范围）
        [Range(0f, 1f)]
        public float removeRatio = 0.5f;

        [Title("统计结果")]
        public int expectedParentCount;
        public int expectedChildCount;
        public int actualParentCount;
        public int actualChildCount;
        public bool isVerified;

        private List<int> _parentTestIndices = new();

        [Button("创建所有Parent")]
        public void CreateParents()
        {
            animationDrawMgr.Init("Assets/Res/Army/Export", true);
            animationDrawMgr.SetCullingCamera(Camera.main);

            for (int i = 0; i < parentCreateCount; i++)
            {
                var parentIndex = animationDrawMgr.AddParentData(parentLayerMask);
                if (parentIndex < 0)
                {
                    Debug.LogError($"[ParentTest] 创建 Parent 失败（第{i}个）");
                    continue;
                }

                var parentPos = new float3(
                    UnityEngine.Random.Range(-parentSpawnRange, parentSpawnRange),
                    0,
                    UnityEngine.Random.Range(-parentSpawnRange, parentSpawnRange));
                animationDrawMgr.SetParentPos(parentIndex, parentPos);

                for (int j = 0; j < childCount; j++)
                {
                    var unitName = childUnitNames[j % childUnitNames.Count];
                    var pos = new float3((j % 5) * childSpacing, 0, (j / 5) * childSpacing);
                    var instanceIndex = animationDrawMgr.AddInstanceToParent(
                        unitName, parentIndex, j, parentLayerMask, pos, float3.zero, 1f);
                    if (instanceIndex < 0)
                        Debug.LogWarning($"[ParentTest] Parent[{parentIndex}] 添加子对象失败: {unitName}");
                }

                _parentTestIndices.Add(parentIndex);
            }
            Debug.Log($"[ParentTest] 已创建 {_parentTestIndices.Count} 个 Parent，每个有 {childCount} 个子对象");
        }

        [Button("随机删除Parent")]
        public void RandomRemoveParents()
        {
            if (_parentTestIndices.Count == 0)
            {
                Debug.LogWarning("[ParentTest] 没有可删除的 Parent");
                return;
            }

            int removeCount = Mathf.FloorToInt(_parentTestIndices.Count * removeRatio);
            if (removeCount <= 0)
            {
                Debug.LogWarning("[ParentTest] 删除数量为0，请调整 removeRatio");
                return;
            }

            for (int i = _parentTestIndices.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (_parentTestIndices[i], _parentTestIndices[j]) = (_parentTestIndices[j], _parentTestIndices[i]);
            }

            var toRemove = _parentTestIndices.GetRange(0, removeCount);
            foreach (var idx in toRemove)
                animationDrawMgr.RemoveParentData(idx);

            _parentTestIndices.RemoveRange(0, removeCount);
            Debug.Log($"[ParentTest] 已删除 {removeCount} 个 Parent，剩余 {_parentTestIndices.Count} 个");
        }

        [Button("验证数量")]
        public void VerifyParentCounts()
        {
            expectedParentCount = _parentTestIndices.Count;
            expectedChildCount = expectedParentCount * childCount;
            actualParentCount = animationDrawMgr.GetActiveParentCount();
            actualChildCount = animationDrawMgr.GetActiveChildCount();
            isVerified = expectedParentCount == actualParentCount && expectedChildCount == actualChildCount;

            animationDrawMgr.GetActiveDataCount();
            var result = isVerified ? "通过" : "失败";
            Debug.Log($"[ParentTest] 验证结果：{result}\n" +
                      $"  Parent  期望={expectedParentCount}  实际={actualParentCount}\n" +
                      $"  子对象  期望={expectedChildCount}  实际={actualChildCount}");
        }

        [Button("清空所有Parent")]
        public void ClearAllParents()
        {
            foreach (var idx in _parentTestIndices)
                animationDrawMgr.RemoveParentData(idx);
            _parentTestIndices.Clear();

            expectedParentCount = 0;
            expectedChildCount = 0;
            actualParentCount = 0;
            actualChildCount = 0;
            isVerified = false;
            Debug.Log("[ParentTest] 已清空所有 Parent");
        }

        #endregion
        FrameTiming[] timings = new FrameTiming[1];
        private float createTime = 0;
        private void OnGUI()
        {
            GUILayout.BeginVertical();
            if (GUILayout.Button("Reset", GUILayout.Width(200), GUILayout.Height(100)))
            {
                if (_instancingData != null)
                {
                    _instancingData.Dispose();
                    _instancingData = null;
                }
                if (_bufferInstancingData != null)
                {
                    _bufferInstancingData.Dispose();
                    _bufferInstancingData = null;
                }
            }
            
            drawNumTex = GUILayout.TextField(drawNumTex, GUILayout.Width(200), GUILayout.Height(50));

            if (GUILayout.Button("AddOrigin", GUILayout.Width(200), GUILayout.Height(100)))
            {
                
            }
            if (GUILayout.Button("AddInstance", GUILayout.Width(200), GUILayout.Height(100)))
            {
                float startTime = Time.realtimeSinceStartup;
                drawCount = int.Parse(drawNumTex);
                this.AddNum();
                createTime = Time.realtimeSinceStartup - startTime;
            }
            
            if (GUILayout.Button("AddBuff", GUILayout.Width(200), GUILayout.Height(100)))
            {
                float startTime = Time.realtimeSinceStartup;
                drawCount = int.Parse(drawNumTex);
                this.AddBufferNum();
                createTime = Time.realtimeSinceStartup - startTime;
            }
            GUIStyle myStyle = new GUIStyle(GUI.skin.label);
            myStyle.fontSize = 30; // 设置字体大小
            myStyle.normal.textColor = Color.red; // 顺便还可以改颜色
            
            GUILayout.Label($" Time {Time.unscaledDeltaTime * 1000} ms", myStyle);
            
            FrameTimingManager.CaptureFrameTimings();
            uint frameCount = FrameTimingManager.GetLatestTimings(1, timings);

            if (frameCount > 0)
            {
                // CPU 时间（主线程耗时）
                float cpuTimeMs = (float)timings[0].cpuFrameTime;
            
                // GPU 时间（渲染耗时）
                // 注意：如果硬件不支持或未开启相关设置，此值可能为 0
                float gpuTimeMs = (float)timings[0].gpuFrameTime;

                GUILayout.Label($" CPU Time {cpuTimeMs} ms, GPU Time {gpuTimeMs} ms", myStyle);
            }

            if (createTime > 0)
            {
                GUILayout.Label($" CreatTime {createTime} s", myStyle);
            }
            
            GUILayout.EndVertical();
        }
    }
}