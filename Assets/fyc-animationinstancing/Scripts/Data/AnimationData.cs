using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    [Serializable]
    public struct AnimationSaveData
    {
        public int animationNameHash;
        public int animationIndex;
        public int animationTexIndex;
        public int totalFrame;
        public int fps;
        public int rootMotion;
        public int wrapMode;
        // public float3[] velocities;
        // public float3[] angularVelocities;
    }

    [Serializable]
    public struct TextureSaveData
    {
        public int textureWidth;
        public int textureHeight;
        public byte[] textureData;
    }
    [Serializable]
    public class AnimationData : SerializedScriptableObject
    {
        [SerializeField]
        public Mesh mainMesh;
        [SerializeField]
        public Material mainMaterial;
        [SerializeField]
        public AnimationSaveData[] animationData;
        [SerializeField]
        public int textureLength;
        [SerializeField]
        public int textureBlockWidth;
        [SerializeField]
        public int textureBlockHeight;
        [SerializeField]
        public TextureSaveData[] textureData;

        public ref AnimationSaveData GetAnimation(int index)
        {
            return ref animationData[index];
        }

        public (int, int) GetTextureWidthAndHeight()
        {
            if (textureData == null || textureData.Length == 0)
            {
                Debug.LogError("[Error] GetTextureWidthAndHeight fail, textureData is empty");
                return (0, 0);
            }

            return (textureData[0].textureWidth, textureData[0].textureHeight);
        }
        public Texture2D CreateBoneTexture()
        {
            if (textureData == null || textureData.Length == 0)
            {
                Debug.LogError("[Error] CreateBoneTexture fail, textureData is empty");
                return null;
            }
            //暂时只支持一张
            var texture = new Texture2D(textureData[0].textureWidth, textureData[0].textureHeight, TextureFormat.RGBAFloat, false);
            texture.name = "BoneTexture";
            texture.LoadRawTextureData(textureData[0].textureData);
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            return texture;
        }
#if UNITY_EDITOR
        /// <summary>
        /// 创建或者更新已有的数据
        /// </summary>
        /// <param name="path"></param>
        /// <param name="saveName"></param>
        /// <returns></returns>
        public static AnimationData GetOrCreateAnimationData(string path, string saveName)
        {
            var assetPath = $"{path}/{saveName}.asset";

            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationData>(assetPath);

            if (data)
                return data;

            data = CreateInstance<AnimationData>();
            UnityEditor.AssetDatabase.CreateAsset(data, assetPath);
            return data;
        }
#endif
    }
}