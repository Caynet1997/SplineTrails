using System;
using FlaxEngine;
using FlaxEngine.Utilities;

namespace Game.Game;

[ExecuteInEditMode,RequireActor(typeof(Spline))]
public class TrailGenerator : Script
{
    private Action SettingsUpdate;
    public TrailGenerateConfig Config{
        set
        {
            ref TrailGenerateConfig profile = ref _settings;
            _settings = value;
            SettingsUpdate?.Invoke();
        }
        get => _settings;
    }
    private TrailGenerateConfig _settings;

    public Spline Spline => _spline;
    private Spline _spline;

    public override void OnEnable()
    {
        SettingsUpdate += GenerateTrail;
        GenerateTrail();
    }
    public override void OnDisable()
    {
        SettingsUpdate -= GenerateTrail;
    }

    public void GenerateTrail()
    {
        _spline ??= Actor as Spline;
        if (!_spline)
        {
            return;
        }

        Spline.ClearSpline();
        Vector3[] Points = new Vector3[Config.Count];
        for (int i = 0; i < Config.Count ; i++)
        {
            float randomScaleX = 1f + RandomUtil.Random.NextFloat(Config.ScaleX);
            float randomScaleY = 1f + RandomUtil.Random.NextFloat(Config.ScaleY);
            Vector3 pos = new()
            {
                X = Mathf.Sin((float)i/Config.Count*Mathf.TwoPi) * randomScaleX,
                Y = 0f,
                Z = Mathf.Cos((float)i/Config.Count*Mathf.TwoPi) * randomScaleY,
            };
            pos *=  Config.Radius;
            Points[i] = pos;
            Spline.AddSplinePoint(pos,false);
        }
        Spline.SetTangentsSmooth();
        //Spline.UpdateSpline();
    }

    public struct TrailGenerateConfig
    {
        [Range(1024f,65534f)]
        public float Radius;
        [Range(8,256)]
        public int Count;
        [Range(0.1f, 2f)]
        public float ScaleX;

        [Range(0.1f, 2f)]
        public float ScaleY;
    }
}