
using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Game;

[ExecuteInEditMode]
public class SplineExtrude : Script
{
    [ReadOnly]
    public int VerticlesCount;
    
    public SplineSampler SplineSampler{
        set
        {
            if (_splineSampler != null)
            {
                if (value == null)
                {
                    _splineSampler.SamplerUpdated -= OnSplineUpdated;
                }
                else
                if(_splineSampler != value)
                {
                    value.SamplerUpdated += OnSplineUpdated;
                }
            }
            _splineSampler = value;
        }
        get => _splineSampler;
    }

    [Tooltip("横截面配置（矩形、圆形等）")]
    public ExtrudeSetting Profile{
        set
        {
            _profile = value;
            _settingsUpdate?.Invoke();
        }
        get => _profile;
    }
    
    private ExtrudeSetting _profile = new()
    {
        PolygonSegments = 4,
        PolygonRadius = 8f,
    };
    private SplineSampler _splineSampler;
    private Float3[] _verticesBuffer;
    private Float3[] _normalsBuffer;
    private Float2[] _uvsBuffer;
    private uint[] _indicesBuffer;
    private StaticModel _staticModel;
    private MeshAccessor _accessor;
    private Action _settingsUpdate;

    public override void OnEnable()
    {
        _splineSampler?.SamplerUpdated += OnSplineUpdated;
        _settingsUpdate += OnSplineUpdated;
        OnSplineUpdated();
    }

    public override void OnDisable()
    {
        _splineSampler?.SamplerUpdated -= OnSplineUpdated;
        _settingsUpdate -= OnSplineUpdated;
        if (_staticModel != null)
        {
            Destroy(ref _staticModel);
        }
    }

    private void OnSplineUpdated()
    {
        if (SplineSampler == null) return;

        SplineSampler.SplineSample[] samples = SplineSampler.CachedSamples;
        if (samples == null || samples.Length < 2 || !Profile.ModelParent) return;

        // 初始化 StaticModel
        _accessor ??= new();
        if (!_staticModel)
        {
            _staticModel = Profile.ModelParent.GetOrAddChild<StaticModel>();
            _staticModel.Model = Content.CreateVirtualAsset<Model>();
            _staticModel.Model.SetupLODs([1]);
            _staticModel.HideFlags = HideFlags.FullyHidden;
        }

        (Vector2[],float[]) pointAndU = Profile.GetProfilePointsAndUVu();
        Vector2[] profileVertices = pointAndU.Item1;
        float[] uVu = pointAndU.Item2;
        int profileCount = profileVertices.Length;
        int sampleCount = samples.Length;
        float totalLength = SplineSampler.TotalLength;

        // 确定阵列数量
        int arrayCount = Profile.UseArrayGenerate ? Math.Max(1, Profile.ArrayGenerateCount) : 1;
        Vector2 arrayOffset = Profile.ArrayGenerateInterval;

        bool IsGenerateCap = Profile.CapEnds && !SplineSampler.Spline.IsLoop;

        // 预估缓冲区大小
        int sideVertexCountPerArray = (sampleCount - 1) * profileCount * 4; 
        int capVertexCountPerArray = IsGenerateCap ? profileCount * 2 + 2 : 0; // +2 for centers
        
        int totalVertexCount = (sideVertexCountPerArray + capVertexCountPerArray) * arrayCount;
        
        int sideTriangleCountPerArray = (sampleCount - 1) * profileCount * 2;
        int capTriangleCountPerArray = IsGenerateCap ? profileCount * 2 : 0;
        int totalTriangleCount = (sideTriangleCountPerArray + capTriangleCountPerArray) * arrayCount;

        int estimatedIndices = totalTriangleCount * 3;

        _verticesBuffer = new Float3[totalVertexCount];
        _normalsBuffer = new Float3[totalVertexCount];
        _uvsBuffer = new Float2[totalVertexCount];
        _indicesBuffer = new uint[estimatedIndices];

        int vIndex = 0;
        int iIndex = 0;

        // 外层阵列循环
        Vector2 uvScale = Profile.UVScaleIncreasement + 1;
        for (int a = 0; a < arrayCount; a++)
        {
            // 计算当前阵列实例的2D偏移
            Vector2 currentOffset = arrayOffset * ((arrayCount - 1)*0.5f - a)*2f;

            // 计算采样点处轮廓的实际顶点法线和UV
            int CalculateRingVertices(SplineSampler.SplineSample sample, float vCoord, int startVIndex)
            {
                int actualVertexCount = 0;
                for (int j = 0; j < profileCount; j++)
                {
                    int prevJ = (j - 1 + profileCount) % profileCount;
                    int nextJ = (j + 1) % profileCount;

                    // 在局部空间应用阵列偏移
                    Vector2 pPrev = profileVertices[prevJ] + currentOffset;
                    Vector2 pCurr = profileVertices[j] + currentOffset;
                    Vector2 pNext = profileVertices[nextJ] + currentOffset;

                    Vector3 posPrev = sample.Position + (pPrev.X * sample.Tangent) + (pPrev.Y * sample.Normal);
                    Vector3 posCurr = sample.Position + (pCurr.X * sample.Tangent) + (pCurr.Y * sample.Normal);
                    Vector3 posNext = sample.Position + (pNext.X * sample.Tangent) + (pNext.Y * sample.Normal);

                    Vector3 dir1 = (posCurr - posPrev).Normalized;
                    Vector3 faceNormal1 = Vector3.Cross(dir1, sample.Direction).Normalized;
                    
                    Vector3 dir2 = (posNext - posCurr).Normalized;
                    Vector3 faceNormal2 = Vector3.Cross(sample.Direction, dir2).Normalized;

                    float angle = MathF.Acos(float.Clamp(Vector3.Dot(dir1, dir2), -1f, 1f)) * (180f / MathF.PI);

                    float u = uVu[j];
                    if (angle < Profile.HardEdgeAngleThreshold)
                    {
                        // 平滑法线应该基于偏移后的位置计算
                        Vector3 smoothNormal = (posCurr - (sample.Position + (currentOffset.X * sample.Tangent) + (currentOffset.Y * sample.Normal))).Normalized;
                        faceNormal1 = smoothNormal;
                        faceNormal2 = smoothNormal;
                    }
                    _verticesBuffer[startVIndex + actualVertexCount] = posCurr;
                    _normalsBuffer[startVIndex + actualVertexCount] = faceNormal1;
                    _uvsBuffer[startVIndex + actualVertexCount] = new Vector2(0, vCoord) * uvScale;
                    actualVertexCount++;

                    _verticesBuffer[startVIndex + actualVertexCount] = posCurr;
                    _normalsBuffer[startVIndex + actualVertexCount] = faceNormal2;
                    _uvsBuffer[startVIndex + actualVertexCount] = new Vector2(u, vCoord) * uvScale;
                    actualVertexCount++;
                }
                return actualVertexCount;
            }

            int GetVertexOffset(int j)
            {
                int offset = 0;
                for (int k = 0; k < j; k++)
                {
                    int prevJ = (k - 1 + profileCount) % profileCount;
                    int nextJ = (k + 1) % profileCount;
                    Vector2 pP = profileVertices[prevJ];
                    Vector2 pC = profileVertices[k];
                    Vector2 pN = profileVertices[nextJ];
                    
                    Vector2 dir1 = (pC - pP).Normalized;
                    Vector2 dir2 = (pN - pC).Normalized;
                    float angle = MathF.Acos(float.Clamp(Vector2.Dot(dir1, dir2), -1f, 1f)) * (180f / MathF.PI);
                    
                    if (angle > Profile.HardEdgeAngleThreshold) offset += 2; else offset += 1;
                }
                return offset;
            }

            // 生成侧面网格
            int prevRingStartIndex = -1;

            for (int i = 0; i < sampleCount - 1; i++)
            {
                ref var currentSample = ref samples[i];
                ref var nextSample = ref samples[i + 1];
                int currentRingStartIndex;
                int currentRingVertexCount;
                int prevRingVertexCount;

                if (prevRingStartIndex == -1)
                {
                    prevRingStartIndex = vIndex;
                    prevRingVertexCount = CalculateRingVertices(currentSample, currentSample.Distance / totalLength, vIndex);
                    vIndex += prevRingVertexCount;
                }

                currentRingStartIndex = vIndex;
                currentRingVertexCount = CalculateRingVertices(nextSample, nextSample.Distance / totalLength, vIndex);
                vIndex += currentRingVertexCount;

                for (int j = 0; j < profileCount; j++)
                {
                    int nextJ = (j + 1) % profileCount;
                    int currOffset = GetVertexOffset(j);
                    int nextOffset = GetVertexOffset(nextJ);

                    bool isCurrHardEdge = (MathF.Acos(float.Clamp(Vector2.Dot(
                        (profileVertices[j] - profileVertices[(j - 1 + profileCount) % profileCount]).Normalized,
                        (profileVertices[(j + 1) % profileCount] - profileVertices[j]).Normalized), -1f, 1f)) * (180f / MathF.PI)) > Profile.HardEdgeAngleThreshold;

                    uint currBottom = (uint)(prevRingStartIndex + currOffset + (isCurrHardEdge ? 1 : 0));
                    uint currTop = (uint)(currentRingStartIndex + currOffset + (isCurrHardEdge ? 1 : 0));
                    uint nextBottom = (uint)(prevRingStartIndex + nextOffset);
                    uint nextTop = (uint)(currentRingStartIndex + nextOffset);

                    _indicesBuffer[iIndex++] = currBottom;
                    _indicesBuffer[iIndex++] = currTop;
                    _indicesBuffer[iIndex++] = nextBottom;

                    _indicesBuffer[iIndex++] = nextBottom;
                    _indicesBuffer[iIndex++] = currTop;
                    _indicesBuffer[iIndex++] = nextTop;
                }

                prevRingStartIndex = currentRingStartIndex;
            }

            // 生成两端封顶
            if (IsGenerateCap)
            {
                // 起始端
                ref var startSample = ref samples[0];
                Vector3 startDir = Profile.InvertNormals ? startSample.Direction : -startSample.Direction;
                Vector3 startCenterPos = startSample.Position + (currentOffset.X * startSample.Tangent) + (currentOffset.Y * startSample.Normal);
                
                uint startCenterIdx = (uint)vIndex;
                _verticesBuffer[vIndex] = startCenterPos;
                _normalsBuffer[vIndex] = startDir;
                _uvsBuffer[vIndex] = new Vector2(0.5f, 0.5f);
                vIndex++;

                int startEdgeBase = vIndex;
                for (int j = 0; j < profileCount; j++)
                {
                    Vector2 p = profileVertices[j] + currentOffset; // 【修改】
                    Vector3 offset = (p.X * startSample.Tangent) + (p.Y * startSample.Normal);
                    _verticesBuffer[vIndex] = startSample.Position + offset;
                    _normalsBuffer[vIndex] = startDir;
                    _uvsBuffer[vIndex] = new Vector2((float)j / profileCount, 0);
                    vIndex++;
                }

                for (int j = 0; j < profileCount; j++)
                {
                    int nextJ = (j + 1) % profileCount;
                    _indicesBuffer[iIndex++] = startCenterIdx;
                    _indicesBuffer[iIndex++] = (uint)(startEdgeBase + j);
                    _indicesBuffer[iIndex++] = (uint)(startEdgeBase + nextJ);
                }

                // 结束端
                ref var endSample = ref samples[sampleCount - 1];
                Vector3 endDir = Profile.InvertNormals ? -endSample.Direction : endSample.Direction;
                Vector3 endCenterPos = endSample.Position + (currentOffset.X * endSample.Tangent) + (currentOffset.Y * endSample.Normal);
                
                uint endCenterIdx = (uint)vIndex;
                _verticesBuffer[vIndex] = endCenterPos;
                _normalsBuffer[vIndex] = endDir;
                _uvsBuffer[vIndex] = new Vector2(0.5f, 0.5f);
                vIndex++;

                int endEdgeBase = vIndex;
                for (int j = 0; j < profileCount; j++)
                {
                    Vector2 p = profileVertices[j] + currentOffset; // 【修改】
                    Vector3 offset = (p.X * endSample.Tangent) + (p.Y * endSample.Normal);
                    _verticesBuffer[vIndex] = endSample.Position + offset;
                    _normalsBuffer[vIndex] = endDir;
                    _uvsBuffer[vIndex] = new Vector2((float)j / profileCount, 1);
                    vIndex++;
                }

                for (int j = 0; j < profileCount; j++)
                {
                    int nextJ = (j + 1) % profileCount;
                    _indicesBuffer[iIndex++] = endCenterIdx;
                    _indicesBuffer[iIndex++] = (uint)(endEdgeBase + nextJ);
                    _indicesBuffer[iIndex++] = (uint)(endEdgeBase + j);
                }
            }
        }

        VerticlesCount = vIndex;
        UpdateMeshByAccessor(vIndex, iIndex);
    }

    /// <summary>
    /// 使用 MeshAccessor 更新网格
    /// </summary>
    private void UpdateMeshByAccessor(int vertexCount, int indexCount)
    {
        // 定义顶点布局：包含位置、法线和切线（切线对光照很重要）
        var vertexLayout = new VertexElement[]
        {
            new(VertexElement.Types.Position, PixelFormat.R32G32B32_Float),
            new(VertexElement.Types.Normal, PixelFormat.R10G10B10A2_UNorm),
            new(VertexElement.Types.TexCoord, PixelFormat.R16G16B16A16_Float),
        };

        // 分配索引缓冲区和顶点缓冲区
        _accessor.AllocateBuffer(MeshBufferType.Index, indexCount, PixelFormat.R32_UInt);
        _accessor.AllocateBuffer(MeshBufferType.Vertex0, vertexCount, GPUVertexLayout.Get(vertexLayout));

        // 填入数据
        _accessor.Positions = _verticesBuffer;
        _accessor.Triangles = _indicesBuffer;
        _accessor.TexCoords = _uvsBuffer;

        // 自动计算法线切线
        _accessor.ComputeNormals();

        // 将更改提交到 GPU 的指定网格中
        if (_accessor.UpdateMesh(_staticModel.Model.LODs[0].Meshes[0], false))
        {
            Debug.Log("UpdateMesh Failed!");
            return;
        }
        BoundingBox box = SplineSampler.Spline.Box;
        _staticModel.Model.LODs[0].Meshes[0].SetBounds(ref box);
        _staticModel.SetMaterial(0,Profile.ModelMaterial);
    }
}

public struct ExtrudeSetting
{
    [Tooltip("模型的父Actor")]
    public Actor ModelParent;

    [Tooltip("模型的材质")]
    public Material ModelMaterial;

    public bool UseCostomProfile;

    [Tooltip("多边形的半径"),Range(1f,256f),VisibleIf("UseCostomProfile",true)]
    public float PolygonRadius;

    [Tooltip("多边形的分段数（段数越多越圆滑）"),Range(2,256),VisibleIf("UseCostomProfile",true)]
    public int PolygonSegments;

    [Tooltip("自定义轮廓"),VisibleIf("UseCostomProfile")]
    public List<Vector2> CostomProfilePoints;

    [Tooltip("旋转角度"),Range(0f,360f)]
    public float Degree;

    [Tooltip("缩放增量")]
    public Vector2 ScaleIncreasement;

    [Tooltip("位置偏移")]
    public Vector2 Offset;

    [Tooltip("UV缩放增量")]
    public Vector2 UVScaleIncreasement;

    [Tooltip("按角度平滑法线"), Range(0f,180f)]
    public float HardEdgeAngleThreshold; 

    [Tooltip("是否生成封顶面")]
    public bool CapEnds;

    [Tooltip("是否翻转法线")]
    public bool InvertNormals;

    [Tooltip("是否启用阵列生成")]
    public bool UseArrayGenerate;

    [Tooltip("阵列生成数量"), Range(1,64), VisibleIf(nameof(UseArrayGenerate))]
    public int ArrayGenerateCount;

    [Tooltip("阵列生成间距"), VisibleIf(nameof(UseArrayGenerate))]
    public Vector2 ArrayGenerateInterval;

    public readonly (Vector2[],float[])GetProfilePointsAndUVu()
    {
        Vector2[] profile;
        float[] u;
        int count = 0;
        if (UseCostomProfile)
        {
            if (CostomProfilePoints != null && CostomProfilePoints.Count > 0)
            {
                count = CostomProfilePoints.Count;
                profile = new Vector2[count];

                float rad = Mathf.DegreesToRadians * Degree;
                float cosA = Mathf.Cos(rad);
                float sinA = Mathf.Sin(rad);

                // 预计算缩放因子（与内置模式语义一致：1+Scale）
                float sx = 1f + ScaleIncreasement.X;
                float sy = 1f + ScaleIncreasement.Y;

                for (int i = 0; i < count; i++)
                {
                    Vector2 p = CostomProfilePoints[i];

                    // 1. 缩放（相对于原点）
                    p.X *= sx;
                    p.Y *= sy;

                    // 2. 旋转（绕原点）
                    float rx = p.X * cosA - p.Y * sinA;
                    float ry = p.X * sinA + p.Y * cosA;
                    p.X = rx;
                    p.Y = ry;

                    // 3. 平移
                    p += Offset;

                    profile[InvertNormals?(count - 1 - i):i] = p;
                }
            }
            else
            {
                profile = [];
            }
        }
        else
        {
            count = PolygonSegments<2?2:PolygonSegments;
            profile = new Vector2[count];
            float angleStep = Mathf.TwoPi / count;
            float rad = Mathf.DegreesToRadians * Degree;
            float sx = 1f + ScaleIncreasement.X;
            float sy = 1f + ScaleIncreasement.Y;

            for (int i = 0; i < count; i++)
            {
                float a = -(i + 0.5f) * angleStep * (InvertNormals ? -1f : 1f) + rad;
                profile[i] = new Vector2(
                    Mathf.Cos(a) * sx,
                    Mathf.Sin(a) * sy
                ) * PolygonRadius + Offset;
            }
        }

        u = new float[count];
        if (count > 0)
        {
            float cumLen = 0f;
            for (int j = 0; j < count; j++)
            {
                int nextJ = (j + 1) % count;
                float length =  Vector2.Distance(profile[j], profile[nextJ]);
                cumLen += length;
                u[j] = length;
            }
            float factor = cumLen>0f?1f/cumLen:1f;
            for (int j = 0; j < count; j++)
            {
                u[j] *= factor;
            }
        }
        return (profile,u);
    }
}