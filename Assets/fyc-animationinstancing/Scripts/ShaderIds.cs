using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class ShaderIds
    {
        public static int FrameIndex = Shader.PropertyToID("_FrameIndex");
        public static int PreFrameIndex = Shader.PropertyToID("_PreFrameIndex");
        public static int TransitionProgress = Shader.PropertyToID("_TransitionProgress");
        public static int BoneTexture = Shader.PropertyToID("_BoneTexture");
        public static int BoneTextureWidth = Shader.PropertyToID("_BoneTextureWidth");
        public static int BoneTextureHeight = Shader.PropertyToID("_BoneTextureHeight");
        public static int BoneTextureBlockWidth = Shader.PropertyToID("_BoneTextureBlockWidth");
        public static int BoneTextureBlockHeight = Shader.PropertyToID("_BoneTextureBlockHeight");

        public static string InstanceKeyWord = "_INSTANCING";
        public static string IndirectKeyWord = "_INDIRECT";
    }

    public class ComputeShaderIds
    {
        public static string ComputeCullingAnimationKernel = "CSCullingAndAnimation";

        public static string TotalBufferName = "_FrameData";
        public static string AvaBufferName = "_OutDrawFrameData";
        public static string AniInfoBufferName = "_InfoData";
        public static string MotionBufferName = "_MotionData";
        public static string PathAryName = "_PathAry";

        public static string PlaneName = "_Planes";
        public static string DeltaTimeName = "_DeltaTime";
        public static string DrawLayerMask = "_LayerMask";
        
        //parent
        public static string ParentMotionBufferName = "_ParentMotionData";
        public static string CullingType = "_CullingType";
    }
}