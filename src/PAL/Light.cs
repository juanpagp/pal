using System.Numerics;
using FilamentSharp;

namespace PAL;

/// <summary>
/// A light in a <see cref="Scene"/>. Created through scene helpers such as
/// <see cref="Scene.AddDirectionalLight(Vector3, Color, float)"/>;
/// disposing it removes the light from the scene.
/// </summary>
public sealed class Light : IDisposable
{
    private readonly FilamentSharp.Light _native;
    private readonly Entity _entity;
    private Scene? _scene;
    private bool _disposed;

    private Light(FilamentSharp.Light native, Entity entity, Scene scene)
    {
        _native = native;
        _entity = entity;
        _scene = scene;
    }

    internal static Light CreateDirectional(Graphics graphics, Scene scene,
        Vector3 direction, Color color, float intensityLux)
    {
        var length = direction.Length();
        if (length < 1e-6f)
        {
            throw new ArgumentOutOfRangeException(nameof(direction),
                "Direction must be a non-zero vector.");
        }

        var entity = EntityManager.Create();
        var linear = color.ToLinear();
        var native = FilamentSharp.Light.Create(graphics.Engine, entity, new LightOptions
        {
            Type = LightType.Directional,
            Color = new Vector3(linear.R, linear.G, linear.B),
            Intensity = intensityLux,
            Direction = direction / length,
        });
        scene.Native.AddEntity(entity);
        return new Light(native, entity, scene);
    }

    /// <summary>Disposes the light: it leaves its scene and its entity is destroyed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _scene?.Native.RemoveEntity(_entity);
        _scene?.RemoveLight(this);
        _native.Dispose();
        EntityManager.Destroy(_entity);
        _scene = null;
    }
}
