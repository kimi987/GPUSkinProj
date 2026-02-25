using UnityEngine;

namespace Fyc.AnimationInstancing
{
    // For Bake Info Data
    public class AnimationBakeInfo
    {
        public SkinnedMeshRenderer[] SkinnedMeshRenderers;
        public Animator Animator;
        public int WorkingFrame;
        public float Length;
        public int Layer;
        public AnimationInfo Info;
    }
}