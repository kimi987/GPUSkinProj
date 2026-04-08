using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    [BurstCompile]
    public struct ParentCullingMotionJob : IJobParallelFor
    {
        private const float Radius = 1;
        [ReadOnly]
        public NativeArray<Plane> Planes;
        [ReadOnly] 
        public float DeltaTime;
        [ReadOnly] 
        public uint LayerMask;
        
        public NativeArray<MotionData> MotionData;
        [ReadOnly] 
        public NativeArray<float3> PathAry;
        
        public void Execute(int index)
        {
            //Motion
            MotionData motion = MotionData[index];

            if (motion.Enable == 0)
                return;

            JobUtils.CalMotionMove(ref motion, ref PathAry, ref DeltaTime);
            
            JobUtils.CalMotionRotateY(ref motion, ref DeltaTime);

            float3 position = motion.Position;
            if ((LayerMask & motion.LayerMask) == 0 || motion.Visible == 0)
            {
                // Layer 不满足
                motion.IsCull = 1;
                MotionData[index] = motion;
                return;
            }
            for (int i = 0; i < 6; i++) {
                // 计算点积：法向量 * 球心 + 距离
                float dist = Vector3.Dot(Planes[i].normal, position) + Planes[i].distance;

                // 如果距离小于负半径，球体完全在平面外侧，立即剔除
                if (dist < -Radius)
                {
                    motion.IsCull = 1;
                    MotionData[index] = motion;
                    return;
                }
            }
            
            motion.IsCull = 0;
            MotionData[index] = motion;
        }
    }
    
    [BurstCompile]
    public struct CullingUnitJob : IJobParallelFor
    {
        private const float Radius = 1;
        [ReadOnly]
        public NativeArray<Plane> Planes;
        [ReadOnly] 
        public float DeltaTime;
        [ReadOnly] 
        public uint LayerMask;

        public NativeArray<MotionData> MotionData;
        
        [ReadOnly]
        public NativeArray<MotionData> ParentMotionData;

        [ReadOnly] 
        public NativeArray<float3> PathAry;
   
        // 并行写入器，允许多个线程同时向 List 添加元素
        public NativeList<int>.ParallelWriter VisibleIndices;
        
        public void Execute(int index)
        {
            //Motion
            MotionData motion = MotionData[index];

            if (motion.Enable == 0)
                return;
            
            JobUtils.CalMotionMove(ref motion, ref PathAry, ref DeltaTime);
            JobUtils.CalMotionRotateY(ref motion, ref DeltaTime);


            float3 position = motion.Position;
            if (motion.ParentIndex >= 0)
            {
                //has parent
                var motionParent = ParentMotionData[motion.ParentIndex];
                if ((LayerMask & motionParent.LayerMask) == 0 || motionParent.Visible == 0 || motionParent.IsCull == 1)
                {
                    //not visible
                    motion.IsCull = 1;
                    MotionData[index] = motion;
                    return;
                }
                //add parent position
                position += motionParent.Position;
            }
            
            if ((LayerMask & motion.LayerMask) == 0 || motion.Visible == 0)
            {
                // Layer 不满足
                motion.IsCull = 1;
                MotionData[index] = motion;
                return;
            }
            
            for (int i = 0; i < 6; i++) {
                // 计算点积：法向量 * 球心 + 距离
                float dist = Vector3.Dot(Planes[i].normal, position) + Planes[i].distance;

                // 如果距离小于负半径，球体完全在平面外侧，立即剔除
                if (dist < -Radius)
                {
                    motion.IsCull = 1;
                    MotionData[index] = motion;
                    return;
                }
            }
            
            motion.IsCull = 0;
            MotionData[index] = motion;
            VisibleIndices.AddNoResize(index);
        }
    }
    
    [BurstCompile]
    public struct AnimationJob : IJobParallelFor
    {
        [ReadOnly]
        public float DeltaTime;
        [NativeDisableParallelForRestriction]
        public NativeArray<FrameData> FrameData;
        [NativeDisableParallelForRestriction]
        public NativeArray<MotionData> MotionData;
        [ReadOnly]
        public NativeArray<MotionData> ParentMotionData;
        [ReadOnly]
        public NativeList<int> VisibleIndices;
        [ReadOnly] 
        public NativeArray<AnimationSaveData> InfoData;
        [WriteOnly]
        public NativeArray<Matrix4x4> WorldMatrix;
        [WriteOnly]
        public NativeArray<float> FrameIndexes;
        [WriteOnly]
        public NativeArray<float> PreFrameIndexes;
        [WriteOnly]
        public NativeArray<float> TransitionProgress;
        public void Execute(int index)
        {
            var visibleIndex = VisibleIndices[index];
            var data = FrameData[visibleIndex];
            var motion = MotionData[visibleIndex];
            var currentInfo = InfoData[data.CurAniIndex];

            float3 positon = motion.Position;
            float3 rotation = motion.Rotation;
            float scale = motion.Scale;

            if (motion.ParentIndex >= 0)
            {
                var parentMotion = ParentMotionData[motion.ParentIndex];
                positon += parentMotion.Position;
                rotation += parentMotion.Rotation;
                scale *= parentMotion.Scale;
            }
            
            if (motion.MoveSpeed < 0.001 && motion.RotateYSpeed < 0.001)
            {
                //Stop move
                if (data.ResetAniIndex >= 0)
                {
                    //reset animation
                    currentInfo = InfoData[data.ResetAniIndex];
                    data.CurAniIndex = data.ResetAniIndex;
                    data.FrameIndex = currentInfo.animationIndex;
                    data.TransitionDuration = 0;
                    data.ResetAniIndex = -1;
                    motion.RotateYSpeed = 0;
                    motion.Rotation.y = 0;
                    motion.RotateYTarget = float.PositiveInfinity;
                    motion.RotateYFinal = float.PositiveInfinity;
                    MotionData[visibleIndex] = motion;
                }
            }
            WorldMatrix[index] = Matrix4x4.TRS(positon, Quaternion.Euler(rotation),  new float3(scale));

            if (data.Pause == 1)
            {
                //暂停 不再执行动画更新逻辑
                FrameIndexes[index] = data.FrameIndex;
                PreFrameIndexes[index] = data.PreFrameIndex;
                TransitionProgress[index] = data.TransitionProgress;
                return;
            }

            float framePercent = DeltaTime * currentInfo.fps * data.AniSpeed;
            data.FrameIndex += framePercent;
            float totalFrame = currentInfo.totalFrame - 1;
            float endFrame = totalFrame + currentInfo.animationIndex;
            if (data.FrameIndex > endFrame)
            {
                //播放完毕 切换
                if (currentInfo.wrapMode == (int)WrapMode.Loop)
                    data.FrameIndex -= totalFrame;
                else
                {
                    //切换到Idle
                    var firstInfo = InfoData[0];
                    data.CurAniIndex = 0;
                    data.FrameIndex = firstInfo.animationIndex; 
                }
            }
           
            if (data.PreAniIndex >= 0)
            {
                var preInfo = InfoData[data.PreAniIndex];
                data.PreFrameIndex += framePercent;
                totalFrame = preInfo.totalFrame - 1;
                endFrame = totalFrame+ preInfo.animationIndex;
                
                if (data.PreFrameIndex > endFrame)
                {
                    //End
                    data.PreAniIndex = -1;
                    data.TransitionProgress = 1;
                }
                else
                {
                    if (data.TransitionDuration > 0)
                    {
                        float progress = DeltaTime / data.TransitionDuration;
                        data.TransitionProgress += progress;

                        if (data.TransitionProgress > 1)
                        {
                            //end
                            data.PreAniIndex = -1;
                            data.TransitionProgress = 1;
                        }
                    }
                }
            }
            //写回
            FrameData[visibleIndex] = data;
            FrameIndexes[index] = data.FrameIndex;
            PreFrameIndexes[index] = data.PreFrameIndex;
            TransitionProgress[index] = data.TransitionProgress;
        }
    }

}