using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class GenerateObjectInfo
    {
        public Matrix4x4 WorldMatrix;
        public int NameCode;
        public float AnimationTime;
        public int StateName;
        public int FrameIndex;
        public int BoneListIndex = -1;
        public Matrix4x4[] BoneMatrix;


        public void CopyTo(GenerateObjectInfo other)
        {
            other.AnimationTime = AnimationTime;
            other.BoneListIndex = BoneListIndex;
            other.FrameIndex = FrameIndex;
            other.NameCode = NameCode;
            other.StateName = StateName;
            other.WorldMatrix = WorldMatrix;
            other.BoneMatrix = BoneMatrix;
        }
    }
}