using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Game;

/// <summary>
/// 沿着Spline进行采样
/// TODO:每两控制点分段使用多线程进行采样
/// </summary>
#if FLAX_EDITOR
[ExecuteInEditMode, RequireActor(typeof(Spline))]
#endif
public class SplineSampler : Script
{
    private Action SettingsUpdate;

    [Tooltip("细分采样角度阈值"), Range(0.5f, 25f)]
    public float AngleThreshold
    {
        set
        {
            if (_angleThreshold != value)
            {
                _angleThreshold = value;
                SettingsUpdate?.Invoke();
            }
        }
        get => _angleThreshold;
    }

    [Tooltip("基础采样步长"), Range(0.001f, 0.5f)]
    public float BaseStepSize
    {
        set
        {
            if (_baseStepSize != value)
            {
                _baseStepSize = value;
                SettingsUpdate?.Invoke();
            }
        }
        get => _baseStepSize;
    }

    [ReadOnly]
    public float TotalLength;

    [NoSerialize, HideInEditor]
    public SplineSample[] CachedSamples;

    [NoSerialize, HideInEditor]
    public Action SamplerUpdated;

    private float _baseStepSize = 0.1f;
    private float _angleThreshold = 5f;
    public Spline Spline => _spline;
    private Spline _spline;
    private Transform _transform;

    public struct SplineSample
    {
        public float Time;
        public float Distance;
        public Vector3 Position;
        public Vector3 Direction; // Forward
        public Vector3 Normal;    // Up
        public Vector3 Tangent;   // Right
        public Transform Transform;
    }

    public override void OnEnable()
    {
        _spline = Actor as Spline;
        if (!_spline) return;

        _spline.SplineUpdated += OnSplineUpdated;
        SettingsUpdate += OnSplineUpdated;
        OnSplineUpdated();
    }

    public override void OnDisable()
    {
        _spline?.SplineUpdated -= OnSplineUpdated;
        SettingsUpdate -= OnSplineUpdated;
    }

    /// <summary>
    /// 平行传输算法：计算将 prevTangent 旋转到 newTangent 时，prevNormal 应该跟随旋转后的新法线
    /// </summary>
    private Vector3 ParallelTransport(Vector3 prevNormal, Vector3 prevTangent, Vector3 newTangent)
    {
        prevTangent.Normalize();
        newTangent.Normalize();
        
        float dot = Vector3.Dot(prevTangent, newTangent);
        
        // 如果切线方向几乎没有变化，直接返回原法线
        if (dot > 0.9999f) 
            return prevNormal;

        // 计算旋转轴和角度
        Vector3 axis = Vector3.Cross(prevTangent, newTangent);
        float axisLen = axis.Length;
        
        // 处理反向情况（虽然样条线中极少出现）
        if (axisLen < 0.0001f) 
            return -prevNormal;

        axis /= axisLen;
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));

        // 将前一个法线绕轴旋转相应角度
        Quaternion q = Quaternion.RotationAxis(axis, angle);
        Vector3 rotated = Vector3.Transform(prevNormal,q);
        
        // 确保新法线与新切线严格正交，消除累积误差
        rotated -= newTangent * Vector3.Dot(rotated, newTangent);
        return rotated.Normalized;
    }

    private void OnSplineUpdated()
    {
        if (!Spline || Spline.SplinePointsCount <2) return;

        int pointCount = Spline.SplinePointsCount;
        float time = Spline.GetSplineTime(pointCount - 1);
        if (pointCount <= 2 || time <= float.Epsilon) return;

        List<SplineSample> samples = new List<SplineSample>();
        _transform = Transform.Identity;
        float t = 0f;
        float endTime = Spline.GetSplineTime(pointCount - 1);
        float dt = BaseStepSize;
        float dist = 0f;

        // 初始采样
        Vector3 startPos = Spline.GetSplinePoint(0);
        Vector3 startDir = Spline.GetSplineDirection(0);
        
        // 初始法线使用世界向上向量投影
        Vector3 currentNormal = (Float3.Up - startDir * Vector3.Dot(Float3.Up, startDir)).Normalized;
        if (currentNormal.LengthSquared < 0.001f) currentNormal = Float3.Forward; 

        SplineSample GetSample(float tVal, Vector3 lastPos, bool calcDistance = true, bool isEnd = false)
        {
            tVal = isEnd ? 0f : tVal;
            Vector3 dir = Spline.GetSplineDirection(tVal);
            Vector3 pos = Spline.GetSplinePoint(tVal);

            if (calcDistance)
                dist += Vector3.Distance(lastPos, pos);

            _transform.Translation = pos;
            // 使用修改后的叉乘顺序
            Vector3 right = Vector3.Cross(currentNormal, dir).Normalized;
            _transform.Orientation = Quaternion.LookRotation(dir, currentNormal);

            return new SplineSample
            {
                Time = tVal,
                Distance = dist,
                Position = pos,
                Direction = dir,
                Transform = _transform,
                Normal = currentNormal,
                Tangent = right,
            };
        }

        SplineSample lastSample = GetSample(0, startPos, false);
        samples.Add(lastSample);

        int i = 0;
        while (true)
        {
            float next = Mathf.Clamp(t + dt, 0f, endTime);
            bool nextLoop = false;

            for (int j = i; j < pointCount; j++)
            {
                float curPointTime = Spline.GetSplineTime(j);
                if (next >= curPointTime)
                {
                    float sampleTime = j == pointCount - 1 ? 0f :curPointTime;
                    Vector3 newDir = Spline.GetSplineDirection(sampleTime);
                    
                    // 核心：通过平行传输更新 currentNormal
                    currentNormal = ParallelTransport(currentNormal, lastSample.Direction, newDir);

                    SplineSample newSample = GetSample(sampleTime, lastSample.Position,true, j == pointCount - 1);
                    lastSample = newSample;
                    samples.Add(newSample);
                    t = next;
                    i++;
                    nextLoop = true;
                    break; 
                }
            }

            if (t >= endTime) break;
            if (nextLoop) continue;

            Vector3 nextDir = Spline.GetSplineDirection(next);
            if (Vector3.Angle(lastSample.Direction, nextDir) >= AngleThreshold)
            {
                currentNormal = ParallelTransport(currentNormal, lastSample.Direction, nextDir);
                SplineSample nextSample = GetSample(next, lastSample.Position);
                samples.Add(nextSample);
                lastSample = nextSample;
            }
            t = next;
        }

        SplineSample[] finalSamples =  [..samples];
        // 闭合样条线扭转补偿
        if (Spline.IsLoop && samples.Count > 2)
        {
            Vector3 startNormal = samples[0].Normal;
            Vector3 endNormal = samples[samples.Count - 1].Normal;
            
            // 计算起点和终点法线之间的夹角
            float twistAngle = Vector3.Angle(startNormal, endNormal);
            
            // 确定旋转方向（通过叉乘和点乘判断）
            Vector3 cross = Vector3.Cross(startNormal, endNormal);
            float dot = Vector3.Dot(cross, samples[0].Direction);
            if (dot < 0f) twistAngle = -twistAngle;

            // 将总扭转角度均匀地分配到每个采样点上
            int count = samples.Count;

            Span<SplineSample> splineSamples = finalSamples;
            for (int k = 0; k < count; k++)
            {
                // 计算当前点的插值比例 (0.0 到 1.0)
                float ratio = (float)k / (count - 1);
                
                // 计算当前点需要补偿的角度
                float compensationAngle = twistAngle * ratio;
                
                // 绕着该点的切线方向（Direction）反向旋转法线进行补偿
                Quaternion twistQuat = Quaternion.RotationAxis(samples[k].Direction, -compensationAngle * Mathf.DegreesToRadians);
                
                // 应用补偿后的法线
                Vector3 correctedNormal = Vector3.Transform(samples[k].Normal,twistQuat).Normalized;
                
                // 更新采样数据
                ref SplineSample sample = ref splineSamples[k];
                sample.Normal = correctedNormal;
                sample.Tangent = Vector3.Cross(correctedNormal, samples[k].Direction).Normalized;
                sample.Transform.Orientation = Quaternion.LookRotation(samples[k].Direction, correctedNormal);
                samples[k] = sample;
            }
        }

        TotalLength = dist;
        CachedSamples = finalSamples;
        SamplerUpdated?.Invoke();
    }

    public override void OnDebugDrawSelected()
    {
        if (CachedSamples == null || _spline == null) return;
        float length = 32f;
        foreach (var sample in CachedSamples)
        {
            DebugDraw.DrawLine(sample.Position, sample.Position + sample.Normal * length, Color.Green);   // Up / Normal
            DebugDraw.DrawLine(sample.Position, sample.Position + sample.Tangent * length, Color.Red); // Right / Tangent
            DebugDraw.DrawLine(sample.Position, sample.Position + sample.Direction * length, Color.Blue); // Forward / Direction
        }
    }

    /// <summary>
    /// 根据距离获取样条线时间（始终正向插值）
    /// </summary>
    public static float GetSplineTimeAtDistance(SplineSampler sampler, float targetDist)
    {
        if (sampler == null || sampler.CachedSamples == null || sampler.CachedSamples.Length < 2) return 0f;

        int index = FindSegmentIndex(sampler, targetDist);
        ref var currentSample = ref sampler.CachedSamples[index];
        ref var nextSample = ref sampler.CachedSamples[index + 1]; // 始终取下一个点

        float segmentLength = nextSample.Distance - currentSample.Distance;
        if (Mathf.IsZero(segmentLength)) return currentSample.Time;

        float alpha = float.Clamp((targetDist - currentSample.Distance) / segmentLength, 0f, 1f);
        return float.Lerp(currentSample.Time, nextSample.Time, alpha);
    }

    /// <summary>
    /// 根据距离获取样条线 Transform（始终正向插值，避免反向插值导致的顿挫）
    /// </summary>
    public static Transform GetSplineTransformAtDistance(SplineSampler sampler, float targetDist, Vector3 scale)
    {
        if (sampler == null || sampler.CachedSamples == null || sampler.CachedSamples.Length < 2 ) return Transform.Identity;
        float length = sampler.TotalLength;
        if (length <= 0.0001f)
            return Transform.Identity;
        if (sampler.Spline.IsLoop)
            while (targetDist < 0f || targetDist > length)
            {
                if (targetDist < 0f)
                {
                    targetDist += length;
                }
                else if (targetDist > length)
                {
                    targetDist -= length;
                }
            }
        else
        {
            targetDist = Mathf.Clamp(targetDist,0f, length);
        }
        int index = FindSegmentIndex(sampler, targetDist);
        ref var currentSample = ref sampler.CachedSamples[index];
        ref var nextSample = ref sampler.CachedSamples[index + 1]; // 始终取下一个点

        float segmentLength = nextSample.Distance - currentSample.Distance;
        if (Mathf.IsZero(segmentLength)) return currentSample.Transform;

        float alpha = float.Clamp((targetDist - currentSample.Distance) / segmentLength, 0f, 1f);

        return new Transform
        {
            Translation = Vector3.Lerp(currentSample.Position, nextSample.Position, alpha),
            Orientation = Quaternion.Slerp(currentSample.Transform.Orientation, nextSample.Transform.Orientation, alpha),
            Scale = scale
        };
    }

    private static int FindSegmentIndex(SplineSampler sampler, float targetDist)
    {
        int left = 0;
        int right = sampler.CachedSamples.Length - 2;

        while (left <= right)
        {
            int mid = (left + right) / 2;
            float midDist = sampler.CachedSamples[mid].Distance;
            float nextDist = sampler.CachedSamples[mid + 1].Distance;

            if (targetDist >= midDist && targetDist <= nextDist)
                return mid;

            if (targetDist < midDist)
                right = mid - 1;
            else
                left = mid + 1;
        }

        return Mathf.Clamp(left, 0, sampler.CachedSamples.Length - 2);
    }
}