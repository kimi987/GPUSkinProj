using Unity.Collections;
using Unity.Mathematics;

namespace Fyc.AnimationInstancing
{
    public static class JobUtils
    {
        // Cal unit move
        public static void CalMotionMove(ref MotionData motion, ref NativeArray<float3> pathAry, ref float deltaTime)
        {
            if (motion.MoveSpeed > 0.01)
            {
                var targetPos = motion.TargetPos;
                if (motion.PathIndex >= 0)
                    targetPos = pathAry[motion.CurPathIndex];
                var step = deltaTime * motion.MoveSpeed;
                targetPos.y = motion.Position.y;
                var distance = math.length(targetPos - motion.Position);
                if (step >= distance)
                {
                    if (motion.CurPathIndex >= motion.PathIndex)
                    {
                        //Done Finish Move
                        motion.Position = targetPos;
                        motion.MoveSpeed = 0;
                    }
                    else
                    {
                        float remainStep = step - distance;
                        motion.CurPathIndex += 1;
                        var newPos = pathAry[motion.CurPathIndex];
                        motion.MoveSpeed = newPos.y;
                        newPos.y = motion.Position.y;
                        motion.MoveDirection = math.normalize(newPos - targetPos);
                        motion.Position = targetPos + motion.MoveDirection * remainStep;
                        if (motion.RotateYSpeedSetting > 0.01)
                        {
                            motion.RotateYTarget = Utils.DirectionToEulerAngles(motion.MoveDirection).y;
                            motion.RotateYSpeed = motion.RotateYSpeedSetting;
                        }
                    }
                }
                else
                    motion.Position += step * motion.MoveDirection; 
            }
        }

        public static void CalMotionRotateY(ref MotionData motion, ref float deltaTime)
        {
            if (math.abs(motion.RotateYSpeed) > 0.01)
            {
                float targetY = 0;
                bool useFinal = false;
               
                if (float.IsFinite(motion.RotateYTarget))
                    targetY = motion.RotateYTarget;
                else if (float.IsFinite(motion.RotateYFinal))
                {
                    targetY = motion.RotateYFinal;
                    useFinal = true;
                }
                else
                    motion.RotateYSpeed = 0;

                if (math.abs(motion.RotateYSpeed) > 0.01)
                {
                    float deltaAngle = Utils.DeltaAngle(motion.Rotation.y, targetY);
                    float step = motion.RotateYSpeed * deltaTime;
                    if (step > math.abs(deltaAngle))
                    {
                        if (useFinal)
                        {
                            motion.RotateYSpeed = 0;
                            motion.RotateYFinal = float.PositiveInfinity;
                        }
                        else
                            motion.RotateYTarget = float.PositiveInfinity;
                        motion.Rotation.y = targetY;
                    }
                    else
                        motion.Rotation.y += math.sign(deltaAngle) * step;
                }
            }
        }
    }
}