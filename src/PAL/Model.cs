using System.Numerics;
using FilamentSharp;

namespace PAL;

/// <summary>
/// A glTF model loaded into a <see cref="Scene"/>: owns the asset's
/// entities and resources, exposes transform manipulation, bounds and
/// animation playback.
/// </summary>
/// <remarks>
/// <para>
/// There is no PAL base class to inherit and no lifecycle: create with
/// <see cref="Scene.LoadModel(string)"/>, move it by setting
/// <see cref="Transform"/>, dispose to remove it. The entity hierarchy and
/// materials come from gltfio's ubershader provider — no runtime shader
/// generation is involved.
/// </para>
/// <para>
/// Transform convention: <see cref="System.Numerics.Matrix4x4"/> (row-vector
/// convention; PAL translates to Filament's layout). Setting
/// <see cref="Position"/> only replaces the translation of the current
/// transform, preserving rotation and scale.
/// </para>
/// </remarks>
public sealed class Model : IDisposable
{
    private readonly Graphics _graphics;
    private readonly GltfLoader _loader;
    private readonly GltfAsset _asset;
    private readonly Entity[] _entities;
    private Scene? _scene;
    private bool _disposed;

    internal Model(Graphics graphics, Scene scene, byte[] glb)
    {
        ArgumentNullException.ThrowIfNull(glb);
        if (glb.Length == 0)
        {
            throw new ArgumentException("glTF content must not be empty.", nameof(glb));
        }

        _graphics = graphics;
        _scene = scene;

        _loader = graphics.LeaseGltfLoader();
        try
        {
            _asset = _loader.Load(glb);
        }
        catch (FilamentSharpException ex)
        {
            throw new PALException(
                "Could not load the glTF model. The loader supports self-contained GLB " +
                "files (embedded buffers and textures); JSON glTF referencing external " +
                ".bin or image files is not fetched. " +
                "See the original error: " + ex.Message, ex);
        }

        _entities = _asset.GetEntities();
        foreach (var entity in _entities)
        {
            scene.Native.AddEntity(entity);
        }
    }

    /// <summary>The underlying FilamentSharp glTF asset (borrowed; do not dispose it yourself).</summary>
    public GltfAsset Native => _disposed
        ? throw NewDisposedException()
        : _asset;

    /// <summary>The entities backing this model (borrowed).</summary>
    public IReadOnlyList<Entity> Entities => _entities;

    /// <summary>The asset's scene-graph root entity.</summary>
    public Entity Root => Native.Root;

    /// <summary>
    /// The model's transform in world space (row-vector
    /// <see cref="Matrix4x4"/> convention). Setting it moves the whole model.
    /// </summary>
    public Matrix4x4 Transform
    {
        get => _graphics.Engine.GetWorldTransform(Root);
        set => _graphics.Engine.SetTransform(Root, value);
    }

    /// <summary>
    /// The model's position (translation of <see cref="Transform"/>).
    /// Setting it preserves rotation and scale.
    /// </summary>
    public Vector3 Position
    {
        get => Transform.Translation;
        set
        {
            var transform = Transform;
            transform.M41 = value.X;
            transform.M42 = value.Y;
            transform.M43 = value.Z;
            Transform = transform;
        }
    }

    /// <summary>
    /// Scales the model uniformly (multiplies the current transform's
    /// linear part). Useful after loading models authored in unexpected
    /// units.
    /// </summary>
    public void Scale(float uniformScale)
    {
        var scale = Matrix4x4.CreateScale(uniformScale);
        Transform = scale * Transform;
    }

    /// <summary>
    /// Rotates the model by <paramref name="radians"/> around the axis
    /// (applied to the current transform).
    /// </summary>
    public void Rotate(Vector3 axis, float radians)
    {
        Transform = Matrix4x4.CreateFromAxisAngle(axis, radians) * Transform;
    }

    /// <summary>
    /// The asset's axis-aligned bounding box (before any transform).
    /// </summary>
    public (Vector3 Min, Vector3 Max) Bounds => Native.BoundingBox;

    /// <summary>Number of animations in the asset (0 for static models).</summary>
    public int AnimationCount => Native.AnimationCount;

    /// <summary>
    /// Applies an animation at the given time and updates bone matrices.
    /// Call once per frame with advancing time; invalid indices throw.
    /// </summary>
    public void ApplyAnimation(int animationIndex, double timeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(animationIndex);
        if (animationIndex >= AnimationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(animationIndex),
                $"The asset has {AnimationCount} animation(s); index {animationIndex} is out of range.");
        }
        Native.ApplyAnimation(animationIndex, timeSeconds);
    }

    /// <summary>
    /// Disposes the model: entities leave their scene and the glTF asset
    /// (its renderables, materials and textures) is released.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_scene is { } scene)
        {
            foreach (var entity in _entities)
            {
                scene.Native.RemoveEntity(entity);
            }
            scene.RemoveModel(this);
        }
        _asset.Dispose();
        _graphics.ReleaseGltfLoader();
        _scene = null;
    }

    /// <summary>Called by Scene.Dispose to stop detaching on its behalf.</summary>
    internal void SceneDisposed()
    {
        _scene = null;
    }

    private ObjectDisposedException NewDisposedException() =>
        new(nameof(Model), "The PAL model has been disposed.");
}
