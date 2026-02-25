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

        public string savePath = "Assets/DataExport/AnimationInstancingAnimationTextureData";

        public Texture2D debugTexture;
        [MenuItem("Tools/AnimationTools")]
        public static void OnOpenWindow()
        {
            var window = GetWindow<AnimationBakeWindow>();
            window.titleContent = new GUIContent("Bake GPU Animation");
            window.Show();
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