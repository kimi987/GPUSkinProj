using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

//暂时只支持Animator

namespace Fyc.AnimationInstancing
{
    public class AnimationGenerator
    {
        private static AnimationGenerator _instance;

        public static AnimationGenerator Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;
                
                _instance =  new AnimationGenerator();
                return _instance;
            }
        }
        private GameObject _prefabToBake;
        private GameObject _prefabInstance;

        private Mesh _currentMesh;
        private Material _currentMaterial;
        private int _aniFPS = 15;
        private int _workingCount = 0;
        private List<AnimationBakeInfo> _workingInfoList; // 当前需要烘焙的列表
        private AnimationBakeInfo _workingInfo; //当前正在烘焙的动画
        private int _totalFrame;

        private int _boundCount = 0;
        private int _textureBlockWidth = 1;
        private int _textureBlockHeight = 0;
        private Dictionary<Transform, int> _mergedBoneIndexMap;
        
        private Dictionary<AnimatorState, AnimatorStateTransition[]> _cacheTransitions;
        private Dictionary<int, VertexCache> _vertexCaches;
        private List<GenerateObjectInfo>  _generateObjectInfos;  //TODO 好像没啥用
        private Dictionary<int, ArrayList> _generateMatrixDataCache;

        private ArrayList _aniInfoAry;

        private Texture2D[] _bakedBoneTexture = null;
        // ************Bake

        public string SavePath = "Assets/DataExport/AnimationInstancingAnimationTextureData/";
        public void CallBake(GameObject prefabToBake)
        {
            _prefabToBake = prefabToBake;
            _workingInfoList = new();
            _workingInfo = null;
            _workingCount = 0;
            _totalFrame = 0;
            _cacheTransitions = new();
            _vertexCaches = new();
            _generateMatrixDataCache = new();
            _generateObjectInfos = new(1024);
            _aniInfoAry = new();
            //Call Bake Animator
            BakeWithAnimator();
        }

        private void BakeWithAnimator()
        {
            if (!_prefabToBake)
            {
                Debug.LogError("[Error] BakeWithAnimator() called on null prefab");
                return;
            }
            var animator =  _prefabToBake.GetComponentInChildren<Animator>();
            if (!animator)
            {
                Debug.LogError("[Error] BakeWithAnimator() called on null animator");
                return;
            }

            var clips = GetClips(animator);

            if (clips == null)
                return;
            DoBakeAnimator();
            while (_workingInfoList.Count > 0 || _workingInfo != null) 
            {
                GenerateAnimation();
            }

            if (_prefabInstance)
                Object.DestroyImmediate(_prefabInstance);
        }

        private void DoBakeAnimator()
        {
            if (!_prefabToBake)
                return;
            _prefabInstance = Object.Instantiate(_prefabToBake);
            Selection.activeGameObject = _prefabInstance;
            _prefabInstance.transform.position = Vector3.zero;
            _prefabInstance.transform.rotation = Quaternion.identity;
            _prefabInstance.transform.localScale = Vector3.one;
            var animator = _prefabInstance.GetComponentInChildren<Animator>();
            
            var skinnedMeshRenders = _prefabInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bindPos = new List<Matrix4x4>(150);
            var boneTransforms = Utils.MergeBone(skinnedMeshRenders, bindPos);
            
            //Bake Mesh
            GenerateMesh(skinnedMeshRenders, boneTransforms, 2);
            GenerateMaterial(skinnedMeshRenders);
            
            //Bake Animation Texture And Info
            AddMeshVertex2Generate(skinnedMeshRenders, boneTransforms, bindPos.ToArray());

            _totalFrame = 0;
            var controller = animator.runtimeAnimatorController as AnimatorController;
            Debug.Assert(controller.layers.Length > 0);
            
            AnimatorControllerLayer layer = controller.layers[0];

            AnalyzeStateMachine(layer.stateMachine, animator, skinnedMeshRenders, 0, _aniFPS, 0);
            _workingCount = _workingInfoList.Count;
        }
        
        // ************ Vertex and Mesh Generate


        private void AddMeshVertex2Generate(SkinnedMeshRenderer[] skinnedMeshRenderers, Transform[] boneTransforms,
            Matrix4x4[] bindPose)
        {
            _boundCount = boneTransforms.Length;
            _textureBlockWidth = 1;  //TODO 之后优化数据存储
            _textureBlockHeight = _boundCount;

            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                var m = skinnedMeshRenderers[i].sharedMesh;

                if (!m)
                    continue;

                var nameCode = skinnedMeshRenderers[i].name.GetHashCode();

                if (_vertexCaches.ContainsKey(nameCode))
                    continue;
                
                VertexCache vertexCache = new();
                _vertexCaches[nameCode] = vertexCache;
                vertexCache.NameCode = nameCode;
                vertexCache.BoneTransformAry = boneTransforms;
                vertexCache.BindPoseAry = bindPose;
            }
        }

        private void GenerateMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, Transform[] mergedBones, int bonePerVertex)
        {
            _mergedBoneIndexMap = new Dictionary<Transform, int>(mergedBones.Length);
            for (int i = 0; i < mergedBones.Length; i++)
            {
                _mergedBoneIndexMap[mergedBones[i]] = i;
            }

            var totalVertexCount = 0;
            var totalIndexCount = 0;
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                var m =  skinnedMeshRenderers[i].sharedMesh;
                if (m)
                {
                    totalVertexCount += m.vertexCount;
                    totalIndexCount += m.triangles.Length;
                }
            }
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshData = meshDataArray[0];

            // set mesh attribute descriptor
            var layout = new NativeArray<VertexAttributeDescriptor>(6, Allocator.Temp);
            layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal,  VertexAttributeFormat.Float32, 3);
            layout[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4);
            layout[3] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4);
            layout[4] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2);
            layout[5] = new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4);
          
            // set vertex number and index layout
            meshData.SetVertexBufferParams(totalVertexCount, layout);
            meshData.SetIndexBufferParams(totalIndexCount, IndexFormat.UInt16);

            var vertexDataAry = meshData.GetVertexData<VertexData>();
            var indexDataAry = meshData.GetIndexData<UInt16>();
            
            var triangleStart = 0;
            var vertexStart = 0;
            
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                var m = skinnedMeshRenderers[i].sharedMesh;
                var rendererBones = skinnedMeshRenderers[i].bones;
                var localToMerged = new int[rendererBones.Length];
                for (int j = 0; j < rendererBones.Length; j++)
                {
                    if (_mergedBoneIndexMap.TryGetValue(rendererBones[j], out int mergedIndex))
                        localToMerged[j] = mergedIndex;
                    else
                        localToMerged[j] = -1;
                }

                if (!m)
                    continue;
                for (int j = 0; j < m.vertexCount; j++)
                {
                    var data = vertexDataAry[j + vertexStart];
                    data.Position = new float3(m.vertices[j].x, m.vertices[j].y, m.vertices[j].z);
                    data.TexCoord0 = new half2((half)m.uv[j].x, (half)m.uv[j].y);
                    data.Normal = new float3(m.normals[j].x, m.normals[j].y, m.normals[j].z);
                    data.Tangent = new half4((half)m.tangents[j].x, (half)m.tangents[j].y, (half)m.tangents[j].z, (half)m.tangents[j].w);

                    var boneWeight = m.boneWeights[j];
                    half4 weight = new half4((half)boneWeight.weight0, (half)boneWeight.weight1,
                        (half)boneWeight.weight2, (half)boneWeight.weight3);
                    
                    Debug.Assert(weight.x > 0.0f);
                    int remapBone0 = (boneWeight.boneIndex0 >= 0 && boneWeight.boneIndex0 < localToMerged.Length)
                        ? localToMerged[boneWeight.boneIndex0] : -1;
                    int remapBone1 = (boneWeight.boneIndex1 >= 0 && boneWeight.boneIndex1 < localToMerged.Length)
                        ? localToMerged[boneWeight.boneIndex1] : -1;
                    int remapBone2 = (boneWeight.boneIndex2 >= 0 && boneWeight.boneIndex2 < localToMerged.Length)
                        ? localToMerged[boneWeight.boneIndex2] : -1;
                    int remapBone3 = (boneWeight.boneIndex3 >= 0 && boneWeight.boneIndex3 < localToMerged.Length)
                        ? localToMerged[boneWeight.boneIndex3] : -1;
                    Debug.Assert(remapBone0 >= 0, "Failed to remap bone index for merged skinned meshes.");
                    if (remapBone0 < 0) remapBone0 = 0;
                    if (remapBone1 < 0) remapBone1 = remapBone0;
                    if (remapBone2 < 0) remapBone2 = remapBone0;
                    if (remapBone3 < 0) remapBone3 = remapBone0;
                    half4 boneIndex = new half4((half)remapBone0, (half)remapBone1, (half)remapBone2, (half)remapBone3);
                    Debug.Assert(boneIndex.x >= 0);
                    switch (bonePerVertex)
                    {
                        case 3:
                            half rate = (half)(1.0f / (weight.x + weight.y + weight.z));
                            weight.x *= rate;
                            weight.y *= rate;
                            weight.z *= rate;
                            weight.w = (half)(-0.1f);
                            break;
                        case 2:
                            rate = (half)(1.0f / (weight.x + weight.y));
                            weight.x *= rate;
                            weight.y *= rate;
                            weight.z = (half)(-0.1f);
                            weight.w = (half)(-0.1f);
                            break;
                        case 1:
                            weight.x = (half)(1.0f);
                            weight.y = (half)(-0.1f);
                            weight.z = (half)(-0.1f);
                            weight.w = (half)(-0.1f);
                            break;
                    }

                    data.TexCoord1 = boneIndex;
                    data.Color = weight;
                    vertexDataAry[j + vertexStart] = data;

                }
                for (int j = 0; j < m.triangles.Length; j++)
                {
                    indexDataAry[j + triangleStart] = (UInt16)(m.triangles[j] + vertexStart);
                }
                triangleStart += m.triangles.Length;
                vertexStart += m.vertexCount;
            }
            
            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, totalIndexCount));
            
            //Save Mesh To Disk

            var savePath = $"Assets{DataDefine.AnimationTextureDataPath}/ME_{_prefabToBake.name}.mesh";

            _currentMesh = AssetDatabase.LoadAssetAtPath<Mesh>(savePath);
            if (_currentMesh)
            {
                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, _currentMesh, MeshUpdateFlags.Default);
                _currentMesh.RecalculateBounds();
                EditorUtility.SetDirty(_currentMesh);
                AssetDatabase.SaveAssetIfDirty(_currentMesh);
            }
            else
            {
                _currentMesh = new Mesh();
                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, _currentMesh, MeshUpdateFlags.Default);
                _currentMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(_currentMesh, savePath);
            }
        }

        private void GenerateMaterial(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            var savePath = $"Assets{DataDefine.AnimationTextureDataPath}/M_{_prefabToBake.name}.mat";

            Material originMaterial = null;

            foreach (var mr in skinnedMeshRenderers)
            {
                if (mr.sharedMaterial)
                {
                    originMaterial = mr.sharedMaterial;
                    break;
                }
            }
            _currentMaterial = AssetDatabase.LoadAssetAtPath<Material>(savePath);
            if (_currentMaterial)
            {
                _currentMaterial.CopyPropertiesFromMaterial(originMaterial);
                _currentMaterial.shader = Shader.Find("FYC/Character/AnimationInstancingPBRNew");
                EditorUtility.SetDirty(_currentMaterial);
                AssetDatabase.SaveAssetIfDirty(_currentMaterial);
            }
            else
            {
                _currentMaterial = new Material(originMaterial);
                _currentMaterial.shader = Shader.Find("FYC/Character/AnimationInstancingPBRNew");
                AssetDatabase.CreateAsset(_currentMaterial, savePath);
            }
        }
        
        // ************ Animation Bake
        private void GenerateAnimation()
        {
            if (_workingInfoList.Count > 0 && _workingInfo == null)
            {
                _workingInfo = _workingInfoList[0];
                _workingInfoList.RemoveAt(0);

                _workingInfo.Animator.gameObject.SetActive(true);
                _workingInfo.Animator.Update(0);
                _workingInfo.Animator.Play(_workingInfo.Info.AnimationName, 0);
                _workingInfo.Animator.Update(0);
                _workingInfo.WorkingFrame = 0;
                return;
            }

            if (_workingInfo == null)
                return;
            
            for (int i = 0; i < _workingInfo.SkinnedMeshRenderers.Length; i++)
            {
                GenerateBoneMatrix(_workingInfo.SkinnedMeshRenderers[i].name.GetHashCode(), _workingInfo.Info.AnimationNameHash,
                    _workingInfo.WorkingFrame);
            }

            _workingInfo.Info.Velocities[_workingInfo.WorkingFrame] = _workingInfo.Animator.velocity;
            _workingInfo.Info.AngularVelocities[_workingInfo.WorkingFrame] = _workingInfo.Animator.angularVelocity;

            if (++_workingInfo.WorkingFrame >= _workingInfo.Info.TotalFrame)
            {
                //
                _aniInfoAry.Add(_workingInfo.Info);
                if (_workingInfoList.Count == 0)
                {
                    //export data
                    //reset transition
                    foreach (var obj in _cacheTransitions)
                    {
                        obj.Key.transitions = obj.Value;
                    }
                    _cacheTransitions.Clear();
                    
                    //TODO Clear animation Event
                    PrepareBoneTexture(_aniInfoAry);
                    SetupAnimationTexture(_aniInfoAry);
                    SaveAnimationInfo(_prefabToBake.name);
                    //Object.DestroyImmediate(_workingInfo.Animator.gameObject);
                    EditorUtility.ClearProgressBar();
                }

                if (_workingInfo.Animator != null)
                {
                    _workingInfo.Animator.gameObject.transform.position = Vector3.zero;
                    _workingInfo.Animator.transform.rotation = Quaternion.identity;
                }
                _workingInfo = null;
                return;
            }

            float deltaTime = _workingInfo.Length / (_workingInfo.Info.TotalFrame - 1);
            _workingInfo.Animator.Update(deltaTime);
        }

        private void GenerateBoneMatrix(int nameCode, int stateName, float stateTime)
        {
            VertexCache vertexCache = null;
            if (!_vertexCaches.TryGetValue(nameCode, out vertexCache))
                return;
            var matrixData = new GenerateObjectInfo();
            matrixData.NameCode = nameCode;
            matrixData.StateName = stateName;
            matrixData.AnimationTime = stateTime;
            matrixData.WorldMatrix = Matrix4x4.identity;
            matrixData.FrameIndex = -1;
            matrixData.BoneListIndex = -1;
            _generateObjectInfos.Add(matrixData);

            if (_generateMatrixDataCache.TryGetValue(stateName, out var aryList))
            {
                matrixData.BoneMatrix =
                    Utils.CalculateSkinMatrix(vertexCache.BoneTransformAry, vertexCache.BindPoseAry);

                var data = new GenerateObjectInfo();
                matrixData.CopyTo(data);
                aryList.Add(data);
            }
            else
            {
                matrixData.BoneMatrix =
                    Utils.CalculateSkinMatrix(vertexCache.BoneTransformAry, vertexCache.BindPoseAry);

                aryList = new ArrayList();
                var data = new GenerateObjectInfo();
                matrixData.CopyTo(data);
                aryList.Add(data);
                _generateMatrixDataCache[stateName] = aryList;
            }

            if (stateName == "attack".GetHashCode())
            {
                foreach (var ma in matrixData.BoneMatrix)
                {
                    Debug.LogError(ma);
                }
                
            }
        }

        private void SaveAnimationInfo(string animationName)
        {
            var path = $"Assets{DataDefine.AnimationTextureDataPath}";
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            
            var data = AnimationData.GetOrCreateAnimationData(path, animationName);

            data.mainMesh = _currentMesh;
            data.mainMaterial = _currentMaterial;
            data.animationData = new AnimationSaveData[_aniInfoAry.Count];
            for (int i = 0; i < _aniInfoAry.Count; i++)
            {
                var info = (AnimationInfo)_aniInfoAry[i];
                data.animationData[i].animationNameHash = info.AnimationName.GetHashCode();
                data.animationData[i].animationIndex = info.AnimationIndex;
                data.animationData[i].animationTexIndex = info.TextureIndex;
                data.animationData[i].totalFrame = info.TotalFrame;
                data.animationData[i].fps = info.Fps;
                data.animationData[i].rootMotion = info.RootMotion;
                data.animationData[i].wrapMode = (int)(info.WrapMode);
                // data.animationData[i].velocities = info.Velocities;
                // data.animationData[i].angularVelocities = info.AngularVelocities;
            }

            data.textureLength = _bakedBoneTexture.Length;
            data.textureBlockWidth = _textureBlockWidth;
            data.textureBlockHeight = _textureBlockHeight;
            data.textureData = new TextureSaveData[_bakedBoneTexture.Length];
            for (int i = 0; i < _bakedBoneTexture.Length; i++)
            {
                data.textureData[i].textureWidth = _bakedBoneTexture[i].width;
                data.textureData[i].textureHeight = _bakedBoneTexture[i].height;
                data.textureData[i].textureData = _bakedBoneTexture[i].GetRawTextureData();
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"生成Animation Texture Info 成功, 路径: {path}{animationName}.asset");
        }
        

        // ************Animator
        // Get Clips from controller
        private HashSet<AnimationClip> GetClips(Animator animator)
        {
            var controller = animator.runtimeAnimatorController as AnimatorController;

            if (!controller)
            {
                Debug.LogError($"[Error]当前对象没有找到AnimationController: Animator {animator.name}");
                return null;
            }
            var clips = new HashSet<AnimationClip>();
            GetClipsFromStateMachine(controller.layers[0].stateMachine, clips);

            return clips;
        }

        // Get Clips from state machine
        private void GetClipsFromStateMachine(AnimatorStateMachine stateMachine, HashSet<AnimationClip> clips)
        {
            for (int i = 0; i < stateMachine.states.Length; i++)
            {
                var state = stateMachine.states[i];
                var motion = state.state.motion;

                if (!motion)
                    continue;
                if (motion is BlendTree)
                {
                    var blendTree = motion as BlendTree;
                    var childMotions = blendTree.children;

                    for (int j = 0; j < childMotions.Length; j++)
                        clips.Add(childMotions[j].motion as AnimationClip);
                }
                else if (motion is AnimationClip)
                    clips.Add(motion as AnimationClip);
            }

            for (int i = 0; i < stateMachine.stateMachines.Length; i++)
            {
                var subStateMachine = stateMachine.stateMachines[i].stateMachine;
                if (subStateMachine)
                    GetClipsFromStateMachine(subStateMachine, clips);
            }
        }
        
        // Analyze state machine and get animation info
        private void AnalyzeStateMachine(UnityEditor.Animations.AnimatorStateMachine stateMachine, Animator animator,
            SkinnedMeshRenderer[] skinnedMeshRenderers, int layer, int bakeFPS, int animationIndex)
        {
            for (int i = 0; i < stateMachine.states.Length; ++i)
            {
                ChildAnimatorState state = stateMachine.states[i];
                AnimationClip clip = state.state.motion as AnimationClip;

                if (!clip)
                    continue;
                
                var needBake = true;
                foreach (var info in _workingInfoList)
                {
                    if (info.Info.AnimationName == clip.name)
                    {
                        //Has bake
                        needBake = false;
                        break;
                    }
                }

                if (!needBake)
                    continue;

                var bake = new AnimationBakeInfo();
                bake.Length = clip.averageDuration;
                bake.Animator = animator;
                bake.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                bake.SkinnedMeshRenderers = skinnedMeshRenderers;
                bake.Layer = layer;
                bake.Info = new AnimationInfo()
                {
                    AnimationName = clip.name,
                    
                    AnimationNameHash = state.state.GetHashCode(), //TODO
                    AnimationIndex = animationIndex,
                    TotalFrame = Mathf.Max((int)(bake.Length * bakeFPS + 0.5f) + 1, 1),
                    Fps = bakeFPS,
                    RootMotion = true,
                    WrapMode = clip.isLooping ? WrapMode.Loop : clip.wrapMode,
                };

                if (bake.Info.RootMotion)
                {
                    bake.Info.Velocities = new float3[bake.Info.TotalFrame];
                    bake.Info.AngularVelocities = new float3[bake.Info.TotalFrame];
                }
                _workingInfoList.Add(bake);
                animationIndex += bake.Info.TotalFrame;
                _totalFrame += bake.Info.TotalFrame;
                
                _cacheTransitions.Add(state.state, state.state.transitions);
                state.state.transitions = null;
                //TODO event if need
            }

            for (int i = 0; i < stateMachine.stateMachines.Length; ++i)
            {
                AnalyzeStateMachine(stateMachine.stateMachines[i].stateMachine, animator, skinnedMeshRenderers, layer, bakeFPS, animationIndex);
            }
        }
        
        // ******************** Animation Texture

        private void PrepareBoneTexture(ArrayList infoList)
        {
            var frames = new int[infoList.Count];

            for (int i = 0; i < infoList.Count; i++)
            {
                var info = infoList[i] as AnimationInfo;
                frames[i] = info.TotalFrame;
            }
            var textureWidth = CalculateTextureSize(out int count, frames);

            Debug.Assert(textureWidth > 0);

            _bakedBoneTexture = new Texture2D[count]; // Default to 1
            // var format = TextureFormat.RGBAHalf; // Test to RGBAFloat
            var format = TextureFormat.RGBAFloat;
            for (int i = 0; i < count; i++)
            {
                _bakedBoneTexture[i] = new Texture2D(textureWidth, textureWidth, format, false);
                _bakedBoneTexture[i].filterMode = FilterMode.Point;
            }
        }
        
        // Calculate the texture count and every size
        // force textureCount zero or one
        public int CalculateTextureSize(out int textureCount, int[] frames, Transform[] bone = null)
        {
            int textureWidth = -1;
            int blockWidth = 0;
            int blockHeight = 0;

            if (bone != null)
            {
                _boundCount = bone.Length;
                blockWidth = 1; //TODO Compress to 1
                blockHeight = _boundCount;
            }
            else
            {
                blockWidth = _textureBlockWidth; //TODO Compress to 1
                blockHeight = _textureBlockHeight;
            }

            int totalFrame = 0;
            //Calculate max frame 
            foreach (var t in frames)
            {
                totalFrame += t;
            }
            int totalPixelWidth = totalFrame * blockWidth;

            for (int i = 0; i < DataDefine.StandardTextureSize.Length; i++)
            {
                var tempSize = DataDefine.StandardTextureSize[i];

                if (tempSize < blockHeight)
                    continue;

                var heightNum = Mathf.CeilToInt((float)totalPixelWidth / (float)tempSize);

                if (heightNum * blockHeight <= tempSize)
                {
                    textureWidth = tempSize;
                    break;
                }
            }
            if (textureWidth < 0)
            {
                Debug.LogError("[Error] Animation Size Too Large, Can Not Find Property Resolution]");
            }
            textureCount = 1;
            return textureWidth;
        }

        public void SetupAnimationTexture(ArrayList infoList)
        {
            if (_generateObjectInfos == null ||  _generateObjectInfos.Count == 0)
                return;
            var preNameCode = _generateObjectInfos[0].StateName;
            var totalFrames = _generateObjectInfos.Count;
            var textureIndex = (int)0;
            var pixelx = (int)0;
            var pixely = (int)0;

            for (int i = 0; i < totalFrames; i++)
            {
                var matrixData = _generateObjectInfos[i];
                if (matrixData.BoneMatrix == null)
                    continue;

                if (preNameCode != matrixData.StateName)
                {
                    preNameCode = matrixData.StateName;
                    //Calculate frame count
                    int currentFrames = totalFrames - i; 
                    for (int j = i; j < totalFrames; j++)
                    {
                        if (preNameCode != _generateObjectInfos[j].StateName)
                        {
                            currentFrames = j - i;
                            break;
                        }
                    }
                    int width = _bakedBoneTexture[textureIndex].width;
                    int height = _bakedBoneTexture[textureIndex].height;
                    var y = pixely;
                    var currentLineBlockCount = (width - pixelx) / _textureBlockWidth % (width / _textureBlockWidth);
                    currentFrames -= currentLineBlockCount;
                    
                    //暂时不会超过一张texture的情况
                    if (currentFrames > 0)
                    {
                        var framesEachLine = width / _textureBlockWidth;
                        y += (currentFrames / framesEachLine) * _textureBlockHeight;
                        y += currentLineBlockCount > 0 ? _textureBlockHeight : 0;
                        if (height < y + _textureBlockHeight)
                        {
                            textureIndex++;
                            pixelx = 0;
                            pixely = 0;
                            Debug.Assert(textureIndex < _bakedBoneTexture.Length);
                        }
                    }

                    foreach (var obj in infoList)
                    {
                        var info = obj as AnimationInfo;
                        if (info.AnimationNameHash == matrixData.StateName)
                        {
                            info.AnimationIndex = pixelx / _textureBlockWidth + pixely / _textureBlockHeight * _bakedBoneTexture[textureIndex].width /  _textureBlockWidth;
                            info.TextureIndex = textureIndex; //Default to 0
                        }
                    }
                }
                Debug.Assert(pixely + _textureBlockHeight <= _bakedBoneTexture[textureIndex].height);
                // var colors = Utils.Convert2Color(matrixData.BoneMatrix);
                var colors = Utils.ConvertToPosRotColor(matrixData.BoneMatrix);
                _bakedBoneTexture[textureIndex].SetPixels(pixelx, pixely, _textureBlockWidth, _textureBlockHeight, colors);
                pixelx += _textureBlockWidth;

                if (pixelx + _textureBlockWidth > _bakedBoneTexture[textureIndex].width)
                {
                    pixelx = 0;
                    pixely += _textureBlockHeight;
                }

                if (pixely + _textureBlockHeight > _bakedBoneTexture[textureIndex].height)
                {
                    Debug.Assert(_generateObjectInfos[i + 1].StateName != matrixData.StateName);
                    textureIndex++;
                    pixelx = 0;
                    pixely = 0;
                    Debug.Assert(textureIndex < _bakedBoneTexture.Length);
                }
            }
        }

        public Texture2D GetBoneTexture()
        {
            return _bakedBoneTexture[0];
        }
        
        
        //test
        [MenuItem("Assets/Animation/Animation Generator")]
        public static void TestBaker()
        {
            var obj = Selection.objects[0];

            var go = obj as GameObject;
            if (!go)
                return;
            
            Instance.CallBake(go);
        }
    }
}
