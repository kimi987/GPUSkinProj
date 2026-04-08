using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public enum AnimationBakeType
    {
        Pawn,
        Vehicle,
    }

    public static class AnimationExportTools
    {
        private static string SOLDIER_TEMPLATE_PATH = "Assets/Res/Army/Soldier_Template.controller";
        private static string VEHICLE_TEMPLATE_PATH = "Assets/Res/Army/Vehicle_Template.controller";
        private static string FYC_Character_PBR_PATH = "Assets/Res/Shaders/Character/FYC_Character_PBR.shader";
        
        private static readonly int Stencil = Shader.PropertyToID("_Stencil");
        private static readonly int StencilID = Shader.PropertyToID("_StencilID");
        private static readonly int StencilComp = Shader.PropertyToID("_StencilComp");
        private static readonly int StencilPass = Shader.PropertyToID("_StencilPass");
        private static readonly int StencilFail = Shader.PropertyToID("_StencilFail");

        private static Material _currentMat;
        public static void SetShowUnit(Material mat)
        {
            mat.SetFloat(Stencil, 1);
            mat.SetFloat(StencilID, 10);
            mat.SetFloat(StencilComp, 0f);
            mat.SetFloat(StencilPass, 2);
            mat.SetFloat(StencilFail, 0);
            mat.renderQueue = 1999;
        }
        private static bool CheckHasFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return true;
            Debug.Log($"找不到{path}文件夹！");
            return false;
        }
        
        private static object GetOrCreateFolder(string folderPath)
        {
            var pathPart = folderPath.Split('/');
            var folderName = pathPart[^1];
            var folder = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
            if (folder != null) return folder;
            return AssetDatabase.CreateFolder(string.Join('/',pathPart,0,pathPart.Length - 1), folderName);
        }

        public static (bool,string,string) CheckIsModelsFolderValid(string path,string armyTypeName)
        {
            path += "/Models";
            if (!CheckHasFolder(path))
            {
                return (false,null,null);
            }
            var modelWithSkinPath = $"{path}/{armyTypeName}@skin.FBX";
            var modelWithSkinAsset = AssetDatabase.LoadAssetAtPath<Object>(modelWithSkinPath);
            if (modelWithSkinAsset == null)
            {
                modelWithSkinPath = $"{path}/{armyTypeName}@skin.fbx";
                modelWithSkinAsset = AssetDatabase.LoadAssetAtPath<Object>(modelWithSkinPath);
                if (modelWithSkinAsset == null)
                    return (false,null,null);
            }
            return (true,path,modelWithSkinPath);
        }
        
        public static (bool,string) CheckIsMaterialsFolderValid(string path)
        {
            path += "/Materials";
            if (!CheckHasFolder(path))
            {
                return (false,path);
            }
            return (true,path);
        }

        public static (bool,string) CheckIsTexturesFolderValid(string path)
        {
            path += "/Textures";
            if (!CheckHasFolder(path))
            {
                return (false,path);
            }
            return (true,path);
        }
        
        public static bool HandleModelsFolder(string path,string modelsPath,string modelWithSkinPath,string armyTypeName)
        {
            var modelGUidList = AssetDatabase.FindAssets("t:Model", new []{modelsPath});
            
            //复制Mesh
            var meshFolderPath = $"{path}/Meshes";
            var meshFolder = GetOrCreateFolder(meshFolderPath);
            var modelWithSkinSubAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelWithSkinPath);
            foreach (var subAsset in modelWithSkinSubAssets)
            {
                if (subAsset is not Mesh mesh) continue;
                var targetMeshPath = $"{meshFolderPath}/{mesh.name}.mesh";
                if (AssetDatabase.LoadMainAssetAtPath(targetMeshPath) != null)
                {
                    AssetDatabase.DeleteAsset(targetMeshPath);
                }
                var newMesh = new Mesh();
                EditorUtility.CopySerialized(mesh, newMesh);
                AssetDatabase.CreateAsset(newMesh, targetMeshPath);
                break;
            }
            //复制Clip
            if (modelGUidList.Length <= 1) return true;
            var animationsFolderPath = $"{path}/Animation";
            var animationsFolder = GetOrCreateFolder(animationsFolderPath);
                
            foreach (var modelGUid in modelGUidList)
            {
                var modelPath = AssetDatabase.GUIDToAssetPath(modelGUid);
                if (!modelPath.Equals(modelWithSkinPath))
                {
                    var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath);
                    foreach (var subAsset in subAssets)
                    {
                        if (subAsset is not AnimationClip clip) continue;
                        var targetClipPath = $"{animationsFolderPath}/{clip.name.Split('@')[^1]}.anim";
                        if (AssetDatabase.LoadMainAssetAtPath(targetClipPath) != null)
                            AssetDatabase.DeleteAsset(targetClipPath);
                        var newClip = new AnimationClip();
                        EditorUtility.CopySerialized(clip, newClip);
                        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(newClip);
                        settings.loopTime = true;
                        AnimationUtility.SetAnimationClipSettings(newClip, settings);
                        AssetDatabase.CreateAsset(newClip, targetClipPath);
                    }
                }
            }
            
            //设置模型 read/write 为 true
            foreach (var modelGUid in modelGUidList)
            {
                var modelPath = AssetDatabase.GUIDToAssetPath(modelGUid);
                var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;

                if (modelImporter == null) continue;
                if (modelImporter.isReadable) continue;
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
            }
            return true;
        }
        
        public static bool HandleMaterialsFolder(string path,string armyTypeName)
        {
            try
            {
                var filter = "t:Material";
                var guids = AssetDatabase.FindAssets(filter, new[] { path });
                var shaderAnimationInstancingPbr = AssetDatabase.LoadAssetAtPath<Shader>(FYC_Character_PBR_PATH);
                if (shaderAnimationInstancingPbr == null)
                    return false;
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    mat.shader = shaderAnimationInstancingPbr;
                    SetShowUnit(mat);
                    mat.enableInstancing = true; // 启用 GPU Instancing
                    _currentMat = mat;
                    EditorUtility.SetDirty(mat);
                    AssetDatabase.SaveAssetIfDirty(mat);
                }
                // var materialPath = $"{path}/{armyTypeName}";
                // var materialForEditorViewPath = $"{materialPath}(EditorView)";
                // if (AssetDatabase.LoadMainAssetAtPath(materialForEditorViewPath) != null)
                //     AssetDatabase.DeleteAsset(materialForEditorViewPath);
        
                // if (!AssetDatabase.CopyAsset($"{materialPath}.mat", $"{materialForEditorViewPath}.mat"))
                //     return false;
        
                // var shaderForEditorView = AssetDatabase.LoadAssetAtPath<Shader>(FYC_Character_PBR_PATH);
                // if (shaderForEditorView == null)
                // {
                //     return false;
                // }
                //
                // var materialForEditorView = AssetDatabase.LoadAssetAtPath<Material>($"{materialForEditorViewPath}.mat");
                // if (materialForEditorView != null)
                // {
                //     materialForEditorView.shader = shaderForEditorView;
                //     EditorUtility.SetDirty(materialForEditorView);
                //     AssetDatabase.SaveAssets();
                //     return true;
                // }
        
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"HandleMaterialsFolder执行失败: {e.Message}");
                return false;
            }
        }
        
        private static AnimatorController CreateAnimationFsm(string animationsFolderPath,string armyTypeName, AnimationBakeType bakeType)
        {
            var animationControllerPath = $"{animationsFolderPath}/{armyTypeName}.controller";

            var templatePath = bakeType == AnimationBakeType.Vehicle ? VEHICLE_TEMPLATE_PATH : SOLDIER_TEMPLATE_PATH;
            var templateController = AssetDatabase.LoadAssetAtPath<AnimatorController>(templatePath);
            if (templateController == null)
            {
                return null;
            }
            
            // 删除已存在的文件
            if (File.Exists(animationControllerPath))
            {
                AssetDatabase.DeleteAsset(animationControllerPath);
            }
            AssetDatabase.Refresh();
                
            // 创建新的 Animator Controller
            AssetDatabase.CopyAsset(templatePath,animationControllerPath);
            var newController = AssetDatabase.LoadAssetAtPath<AnimatorController>(animationControllerPath);
            foreach (var newChildAnimatorState in newController.layers[0].stateMachine.states)
            {
                newChildAnimatorState.state.motion = GetArmyTypeTargetAnimationClip(newChildAnimatorState.state.name, animationsFolderPath);
            }
            
            //创建动画过渡配置文件
            // {
            //     var animationTransitionsPath = $"{animationsFolderPath}/transitions.asset";
            //
            //     var config = ScriptableObject.CreateInstance<StateTransitionConfig>();
            //     foreach (var newChildAnimatorState in newController.layers[0].stateMachine.states)
            //     {
            //         foreach (var animatorStateTransition in newChildAnimatorState.state.transitions)
            //         {
            //             config.AddTransition(newChildAnimatorState.state.name, animatorStateTransition.destinationState.name,
            //                 animatorStateTransition.duration);
            //         }
            //     }
            //
            //     // 删除已存在的文件
            //     if (File.Exists(animationTransitionsPath))
            //     {
            //         AssetDatabase.DeleteAsset(animationTransitionsPath);
            //     }
            //
            //     AssetDatabase.Refresh();
            //     AssetDatabase.CreateAsset(config, animationTransitionsPath);
            // }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return newController;
        }
        
        public static bool HandleTexturesFolder(string path)
        {
            return true;
        }

        //在兵种animation文件夹中查找该动作
        private static Motion GetArmyTypeTargetAnimationClip(string stateName,string animationFolderPath)
        {
            var stateMotionName = stateName;
            var armyTypeTargetAnimationClipName = $"{animationFolderPath}/{stateMotionName}.anim";
            var armyTypeTargetAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(armyTypeTargetAnimationClipName);
            if (armyTypeTargetAnimationClip)
            {
                return armyTypeTargetAnimationClip;
            }
            Debug.LogError($"找不到对应的{armyTypeTargetAnimationClipName}!");
            return null;
        }
        
        public static GameObject CreatePrefabFolder(string path,string modelWithSkinPath,string armyTypeName, AnimationBakeType bakeType = AnimationBakeType.Pawn)
        {
            var prefabFolderPath = $"{path}/Prefabs";
            string prefabPath = $"{prefabFolderPath}/{armyTypeName}";
            var prefabFolder = GetOrCreateFolder(prefabFolderPath);
            var createPrefab = CreatePrefab(path,prefabPath,modelWithSkinPath,armyTypeName, bakeType);
            // var isCreatePrefabForEditorViewPrefabSuccess = CreatePrefabForEditorView(path,prefabPath,armyTypeName);
            return createPrefab;
        }
        
        private static GameObject CreatePrefab(string path, string prefabPath, string modelWithSkinPath, string armyTypeName, AnimationBakeType bakeType)
        {
            var prefabName = armyTypeName;
            var animationsFolderPath = $"{path}/Animation";
            var initAnimationPath = $"{animationsFolderPath}/idle.anim";
            var modelWithSkinGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(modelWithSkinPath);
            var modelInstance = PrefabUtility.InstantiatePrefab(modelWithSkinGameObject) as GameObject;
            var animator = modelInstance.GetComponent<Animator>();
            if (animator == null)
                animator = modelInstance.AddComponent<Animator>();
            //创建controller
            animator.runtimeAnimatorController = CreateAnimationFsm(animationsFolderPath,armyTypeName, bakeType);
            // var animationInstancing = modelInstance.AddComponent<AnimationInstancing.AnimationInstancing>();
            // // animationInstancing.initAni = AssetDatabase.LoadAssetAtPath<AnimationClip>(initAnimationPath);
            // animationInstancing.prototype = modelInstance;
            // animationInstancing.shadowCastingMode = ShadowCastingMode.On;
            // animationInstancing.receiveShadow = true;
            
            var prefab = new GameObject(prefabName);
            modelInstance.name = prefab.name;
            modelInstance.transform.SetParent(prefab.transform, false);

            var prefabFullPath = $"{prefabPath}.prefab";
            // 保存为预制体
            if (AssetDatabase.LoadMainAssetAtPath(prefabFullPath) != null)
                AssetDatabase.DeleteAsset(prefabFullPath);
            
            //Material
            Debug.LogError("modelInstance.name " + modelInstance.name);
            var renderers = modelInstance.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                Debug.LogError("renderer name: " + renderer.name);
                renderer.sharedMaterial = _currentMat;
            }
            
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefab, prefabFullPath);

            if (savedPrefab)
                return savedPrefab;
            Debug.LogError($"预制体保存失败: {prefabFullPath}");
            return null;
        }
    }
}