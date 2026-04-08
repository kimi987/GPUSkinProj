using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class AnimationBakeWindow : OdinEditorWindow
    {
        public List<GameObject> bakeObjs = new();

        public string savePath = "Assets/Res/Army/Export";

        [FolderPath]
        public string modelPath;

        public AnimationBakeType bakeType = AnimationBakeType.Pawn;

        public Texture2D debugTexture;
        [MenuItem("Tools/AnimationTools")]
        public static void OnOpenWindow()
        {
            var window = GetWindow<AnimationBakeWindow>();
            window.titleContent = new GUIContent("Bake GPU Animation");
            window.Show();
        }
        [Button]
        public void BakeFold()
        {
            var path = modelPath;
            var armyTypeName = path.Split('/')[^1];
            
            var (isModelsFolderValid, modelsPath,modelWithSkinPath) = AnimationExportTools.CheckIsModelsFolderValid(path,armyTypeName);
            var (isMaterialsFolderValid, materialsPath) = AnimationExportTools.CheckIsMaterialsFolderValid(path);
            var (isTexturesFolderValid, texturesPath) = AnimationExportTools.CheckIsTexturesFolderValid(path);
            if (!isModelsFolderValid || !isMaterialsFolderValid || !isTexturesFolderValid)
            {
                if (!isModelsFolderValid) Debug.LogError($"[BakeFold] Models文件夹校验失败, path: {modelsPath}, modelWithSkinPath: {modelWithSkinPath}");
                if (!isMaterialsFolderValid) Debug.LogError($"[BakeFold] Materials文件夹校验失败, path: {materialsPath}");
                if (!isTexturesFolderValid) Debug.LogError($"[BakeFold] Textures文件夹校验失败, path: {texturesPath}");
                return;
            }
            
            var isHandleModelsSuccess = AnimationExportTools.HandleModelsFolder(path,modelsPath,modelWithSkinPath,armyTypeName);
            var isHandleMaterialsSuccess = AnimationExportTools.HandleMaterialsFolder(materialsPath,armyTypeName);
            var isHandleTexturesSuccess = AnimationExportTools.HandleTexturesFolder(texturesPath);
            if (!isHandleModelsSuccess || !isHandleMaterialsSuccess || !isHandleTexturesSuccess)
            {
                if (!isHandleModelsSuccess) Debug.LogError($"[BakeFold] HandleModelsFolder处理失败, path: {modelsPath}");
                if (!isHandleMaterialsSuccess) Debug.LogError($"[BakeFold] HandleMaterialsFolder处理失败, path: {materialsPath}");
                if (!isHandleTexturesSuccess) Debug.LogError($"[BakeFold] HandleTexturesFolder处理失败, path: {texturesPath}");
                return;
            }
            
            var createPrefab = AnimationExportTools.CreatePrefabFolder(path,modelWithSkinPath,armyTypeName,bakeType);
            if (!createPrefab)
            {
                Debug.LogError($"[BakeFold] CreatePrefabFolder失败, path: {path}, modelWithSkinPath: {modelWithSkinPath}, armyTypeName: {armyTypeName}");
                return;
            }
            
            AnimationGenerator.Instance.SavePath = savePath;
            AnimationGenerator.Instance.CallBake(createPrefab);
            debugTexture = AnimationGenerator.Instance.GetBoneTexture();
            AssetDatabase.SaveAssets();
            Debug.Log($"{armyTypeName}全部生成完毕!");
        }
        
        
        [Button]
        public void Bake()
        {
            AnimationGenerator.Instance.SavePath = savePath;
            foreach (var obj in bakeObjs)
            {
                if (obj)
                {
                    AnimationGenerator.Instance.CallBake(obj);
                    debugTexture = AnimationGenerator.Instance.GetBoneTexture();
                }
            }
        }
    }
}