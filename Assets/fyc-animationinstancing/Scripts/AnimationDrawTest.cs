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

        private ParentDateDefine _parentDateInstanceDefine;

        private ParentDateDefine _parentDataBufferDefine;
        //Optimal GC
        private NativeArray<Plane> _planeNativeArray;
        private readonly Vector4[] _planeArray = new Vector4[6];
        private readonly Plane[] _frustumPlanes = new Plane[6];
        
        

        private void Start()
        {
            _planeNativeArray = new NativeArray<Plane>(6, Allocator.Persistent);
            _parentDateInstanceDefine = new ParentDateDefine();
            _parentDataBufferDefine = new ParentDateDefine(computeShader);
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
            animationDrawMgr.Init("Assets/DataExport/AnimationInstancingAnimationTextureData", true);
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
                _instancingData.DoCullingAndAnimationJob(currentDrawLayer, Time.deltaTime, _planeNativeArray);
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