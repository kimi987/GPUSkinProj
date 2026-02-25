using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class AnimationInfo
    {
        public string AnimationName;
        public int AnimationNameHash;
        public int TotalFrame;
        public int Fps;
        public int AnimationIndex;
        public int TextureIndex;
        public bool RootMotion;
        public WrapMode WrapMode;
        public float3[] Velocities;
        public float3[] AngularVelocities;
        //TODO Event not use here

    
        public bool Campare(AnimationInfo other)
        {
            return AnimationName == other.AnimationName;
        }
    }
}