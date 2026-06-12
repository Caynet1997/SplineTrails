using System;
using FlaxEngine;

namespace Game.Game;

/// <summary>
/// 沿着Spline生成和形变给定数量的Mesh
/// </summary>
[ExecuteInEditMode]
public class SplineMesh : Script
{
    [ReadOnly]
    public int VerticlesCount;
    private Action SettingsUpdate;
    public SplineMeshSettings Settings{
        set
        {
            ref SplineMeshSettings profile = ref _settings;
            if (value.Scale.X <= -1f)
            {
                value.Scale.X += 0.01f;
            }
            if (value.Scale.Y <= -1f)
            {
                value.Scale.Y += 0.01f;
            }
            _settings = value;
            SettingsUpdate?.Invoke();
        }
        get => _settings;
    }
    private SplineMeshSettings _settings;

    public SplineSampler SplineSampler;
    public Actor ModelHolder;
    private StaticModel _staticModel;

    // 预分配缓冲区
    private Float3[] _verticesBuffer;
    private Float3[] _normalsBuffer;
    private Float2[] _uvsBuffer;
    private uint[] _indicesBuffer;
    private Float3[] _srcVertices;
    private Float3[] _srcNormals;
    private Float2[] _srcUVs;
    private uint[] _srcIndices;
    private ulong _lastUpdateFrame;

    public override void OnEnable()
    {
        SplineSampler?.SamplerUpdated += OnSplineUpdated;
        SettingsUpdate += OnSplineUpdated;
        OnSplineUpdated();
    }

    public override void OnDisable()
    {
        SplineSampler?.SamplerUpdated -= OnSplineUpdated;

        if (_staticModel != null)
            Destroy(ref _staticModel);
        
        SettingsUpdate -= OnSplineUpdated;
        _lastUpdateFrame = 0;
    }

    private void OnSplineUpdated()
    {
        if (SplineSampler == null || Settings.BaseModel == null || _lastUpdateFrame == Engine.FrameCount) return;
        
        var samples = SplineSampler.CachedSamples;
        if (samples == null || samples.Length < 2) return;

        // 同步样条线长度到设置中
        _settings.Distance = SplineSampler.TotalLength/Settings.Count;

        // 初始化 StaticModel
        if (!_staticModel)
        {
            _staticModel = ModelHolder.GetOrAddChild<StaticModel>();
            _staticModel.Model = Content.CreateVirtualAsset<Model>();
            _staticModel.Model.SetupLODs([1]);
            _staticModel.HideFlags = HideFlags.FullyHidden;
        }

        LoadSourceMeshData();

        if (_srcVertices == null || _srcVertices.Length == 0) return;

        int instanceCount = Settings.Count;
        if (instanceCount <= 0) return;

        int vertCountPerInst = _srcVertices.Length;
        int idxCountPerInst = _srcIndices.Length;

        // 安全上限检查
        long totalVertsLong = (long)instanceCount * vertCountPerInst;
        long totalIndicesLong = (long)instanceCount * idxCountPerInst;

        if (totalVertsLong > int.MaxValue)
        {
            Debug.LogWarning($"SplineMultiMesh: 顶点数过多 ({totalVertsLong})，已自动截断 Count。", this);
            instanceCount = Mathf.Max(1, int.MaxValue / vertCountPerInst);
            totalVertsLong = (long)instanceCount * vertCountPerInst;
            totalIndicesLong = (long)instanceCount * idxCountPerInst;
        }

        int totalVerts = (int)totalVertsLong;
        int totalIndices = (int)totalIndicesLong;

        // 按需扩容缓冲区
        _verticesBuffer = new Float3[totalVerts];
        _normalsBuffer = new Float3[totalVerts];
        _uvsBuffer = new Float2[totalVerts];
        _indicesBuffer = new uint[totalIndices];

        // 计算每个实例在 Spline 上的距离
        float totalLength = SplineSampler.TotalLength;
        bool ensureEndpoints = Settings.EnsureEndpointGenerate;
        bool enableDeform = Settings.DeformAlongSpline;

        for (int i = 0; i < instanceCount; i++)
        {
            float distance;
            if (ensureEndpoints && instanceCount > 1)
            {
                distance = (float)i / (instanceCount - 1)* totalLength;
            }
            else
            {
                distance = Mathf.Min(i * Settings.Distance, totalLength);
            }
            int vOffset = i * vertCountPerInst;
            int iOffset = i * idxCountPerInst;
            
            if (enableDeform)
            {
                for (int v = 0; v < vertCountPerInst; v++)
                {
                    Transform transform = SplineSampler.GetSplineTransformAtDistance(SplineSampler, distance + _srcVertices[v].Z, Vector3.One);
                    Matrix worldMatrix = transform.GetWorld();
                    Float3.TransformNormal(ref _srcNormals[v], ref worldMatrix, out _normalsBuffer[vOffset + v]);
                    _uvsBuffer[vOffset + v] = _srcUVs != null && _srcUVs.Length > v ? _srcUVs[v] : Vector2.Zero;
                    _verticesBuffer[vOffset + v] = transform.Translation + (_srcVertices[v].X * transform.Right) + (_srcVertices[v].Y * transform.Up);
                }
            }
            else
            {
                Transform transform = SplineSampler.GetSplineTransformAtDistance(SplineSampler, distance, Vector3.One);
                Matrix worldMatrix = transform.GetWorld();
                for (int v = 0; v < vertCountPerInst; v++)
                {
                    Float3.Transform(ref _srcVertices[v], ref worldMatrix, out _verticesBuffer[vOffset + v]);
                    Float3.TransformNormal(ref _srcNormals[v], ref worldMatrix, out _normalsBuffer[vOffset + v]);
                    _uvsBuffer[vOffset + v] = _srcUVs != null && _srcUVs.Length > v ? _srcUVs[v] : Vector2.Zero;
                }
                
            }
            for (int idx = 0; idx < idxCountPerInst; idx++)
            {
                _indicesBuffer[iOffset + idx] = _srcIndices[idx] + (uint)vOffset;
            }
        }
        VerticlesCount = totalVerts;
        UpdateMesh(totalVerts, totalIndices);

        // 应用材质覆盖
        if (Settings.Material != null)
            _staticModel.SetMaterial(0, Settings.Material);
        _lastUpdateFrame = Engine.FrameCount;
    }

    private void LoadSourceMeshData()
    {

        MeshAccessor accessor = new();
        var mesh = Settings.BaseModel.GetMesh(0);

        if (mesh == null || accessor.LoadMesh(mesh))
        {
            Debug.LogError($"SplineMesh: Failed to load mesh from BaseModel '{Settings.BaseModel}'", this);
            return;
        }

        _srcVertices = accessor.Positions;
        _srcIndices = accessor.Triangles;
        

        int vertCount = _srcVertices.Length;

        for (int i = 0; i < vertCount; i++)
        {
            _srcVertices[i] *= new Float3(Settings.Scale.X+1f,Settings.Scale.Y+1f,Settings.Scale.Z + 1f);
            _srcVertices[i] += new Float3(Settings.Offset.X ,Settings.Offset.Y ,0f );
        }

        if (accessor.Normals != null && accessor.Normals.Length >= vertCount)
        {
            _srcNormals = accessor.Normals;
        }
        else
        {
            Debug.LogWarning($"SplineMesh: BaseModel '{Settings.BaseModel}' 缺少法线数据或法线数量不匹配，已自动计算法线。", this);
            accessor.ComputeNormals();
            _srcNormals = accessor.Normals;
        }
        if (accessor.TexCoords != null && accessor.TexCoords.Length >= vertCount)
        {
            _srcUVs = accessor.TexCoords;
        }
        else
        {
            _srcUVs = new Float2[vertCount];
        }
    }

    private void UpdateMesh(int vertexCount, int indexCount)
    {
        var accessor = new MeshAccessor();
        
        var vertexLayout = new VertexElement[]
        {
            new(VertexElement.Types.Position, PixelFormat.R32G32B32_Float),
            new(VertexElement.Types.Normal, PixelFormat.R10G10B10A2_UNorm),
            new(VertexElement.Types.TexCoord, PixelFormat.R16G16B16A16_Float),
        };

        accessor.AllocateBuffer(MeshBufferType.Index, indexCount, PixelFormat.R32_UInt);
        accessor.AllocateBuffer(MeshBufferType.Vertex0, vertexCount, GPUVertexLayout.Get(vertexLayout));

        accessor.Positions = _verticesBuffer;
        accessor.Triangles = _indicesBuffer;
        accessor.Normals = _normalsBuffer;
        accessor.TexCoords = _uvsBuffer;

        if (accessor.UpdateMesh(_staticModel.Model.LODs[0].Meshes[0]))
        {
            Debug.LogError("SplineMesh: Failed to update mesh data!", this);
        }
    }
}

[Serializable]
public struct SplineMeshSettings
{
    [Tooltip("要重复生成的基础模型")]
    public Model BaseModel;

    [Tooltip("覆盖材质（留空则使用模型自带材质）")]
    public MaterialBase Material;

    [Tooltip("相邻实例之间的间隔距离"),ReadOnly]
    public float Distance;

    [Range(2, 8192)]
    [Tooltip("沿样条线生成的实例总数")]
    public int Count;

    [Tooltip("缩放增量")]
    public Vector3 Scale;
    
    [Tooltip("纵横位置偏移增量")]
    public Vector2 Offset;

    [Tooltip("保证在Spline两端精确生成网格")]
    public bool EnsureEndpointGenerate;

    // --- 新增选项 ---
    [Tooltip("如果启用，网格将逐顶点沿着样条线形变（挤出模式），否则为重复实例模式")]
    public bool DeformAlongSpline; 

    public static bool Equals(ref SplineMeshSettings a, ref SplineMeshSettings b)
    {
        return a.BaseModel == b.BaseModel
            && a.Material == b.Material
            && a.Count == b.Count
            && a.Offset == b.Offset
            && a.Scale == b.Scale
            && a.EnsureEndpointGenerate == b.EnsureEndpointGenerate
            && a.DeformAlongSpline == b.DeformAlongSpline;
    }
}