using FlaxEngine;

namespace Game.Game;

#if FLAX_EDITOR
[ExecuteInEditMode]
#endif
public class Train : Script
{
    public SplineSampler Sampler;
    public Train FollowTarget;
    
    [Tooltip("火车总推力 单位kN")]
    public float ThrustForce = 100f;

    [Tooltip("火车刹车力 单位kN")]
    public float BrakeForce = 50f;
    
    [Tooltip("火车总质量 单位t")]
    public float Mass = 10f;
    
    public float DragFroceCoincidence = 10f;

    public float CurrentDistance;
    public float TrainLength = 100f;

    public bool IsForward;

    public bool IsBackward;

    [ReadOnly]
    public float Speed;

    public static bool GetTotaleDistance(SplineSampler splineSampler, out float length)
    {
        bool resual = splineSampler;
        length = resual?splineSampler.TotalLength:0f;
        return resual;
    }

    public override void OnUpdate()
    {
        if (GetTotaleDistance(Sampler, out float length))
        {
            if (Mathf.Abs(length) < 1f)
                return;
            float force = (IsForward?ThrustForce:0f) - (IsBackward?BrakeForce:0f);
            float dragForce = -DragFroceCoincidence * Speed;
#if FLAX_EDITOR
            float dt = 1f / Engine.FramesPerSecond;
#else
            float dt = Time.DeltaTime;
#endif

            float next = FollowTarget? FollowTarget.CurrentDistance - FollowTarget.TrainLength:CurrentDistance + Speed * dt;
            if (FollowTarget)
            {
                next = FollowTarget.CurrentDistance - FollowTarget.TrainLength * 1.5f;
            }
            else
            {
                next = CurrentDistance + Speed * dt;
                force += dragForce;
                Speed += force / Mass * dt;
            }
            CurrentDistance = Sampler.Spline.IsLoop? float.Clamp(next % length,0.0001f,length): next;
            Transform trans0 = SplineSampler.GetSplineTransformAtDistance(Sampler, CurrentDistance + TrainLength * 0.5f, Actor.Scale);
            Transform trans1 = SplineSampler.GetSplineTransformAtDistance(Sampler, CurrentDistance - TrainLength * 0.5f, Actor.Scale);
            Transform finalTrans = new()
            {
                Translation = Vector3.Lerp(trans0.Translation,trans1.Translation,0.5f),
                Orientation = Quaternion.LookRotation(trans0.Translation - trans1.Translation),
                Scale = Actor.Scale,
            };
            Actor.Transform = finalTrans;
        }
    }
}