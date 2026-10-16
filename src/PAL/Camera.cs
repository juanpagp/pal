using System.Numerics;
using FilamentSharp;

namespace PAL;

/// <summary>
/// A camera that renders a <see cref="Scene"/>: perspective projection with
/// automatic aspect handling, and a look-at orientation helper.
/// </summary>
/// <remarks>
/// <para>
/// Create with <see cref="Scene.CreateCamera"/>. When the camera is
/// configured through <see cref="SetPerspective(float, float, float)"/>, PAL
/// re-applies the projection with the current viewport aspect on every
/// render, so window resizes keep geometry undistorted automatically.
/// </para>
/// <para>
/// The underlying FilamentSharp camera is available via <see cref="Native"/>;
/// a camera configured directly through it (e.g. orthographic projection)
/// is left untouched by PAL's aspect logic.
/// </para>
/// </remarks>
public sealed class Camera : IDisposable
{
    private readonly Graphics _graphics;
    private readonly Scene _scene;
    private readonly FilamentSharp.Camera _native;
    private readonly FilamentSharp.Entity _entity;
    private float? _fovDegrees;
    private double _near;
    private double _far;
    private double _lastAppliedAspect;
    private bool _disposed;

    internal Camera(Graphics graphics, Scene scene)
    {
        _graphics = graphics;
        _scene = scene;
        _entity = EntityManager.Create();
        _native = graphics.Engine.CreateCamera(_entity);

        // A sane default so rendering before SetPerspective still works.
        SetPerspective(60.0f, 0.1f, 1000.0f);
    }

    /// <summary>The underlying FilamentSharp camera (borrowed; do not dispose it yourself).</summary>
    public FilamentSharp.Camera Native => _disposed
        ? throw NewDisposedException()
        : _native;

    /// <summary>
    /// Sets a symmetric perspective projection. The aspect is managed by
    /// PAL: every render re-applies this projection with the current
    /// viewport's aspect ratio.
    /// </summary>
    /// <param name="fieldOfViewDegrees">Vertical field of view in degrees (0..180).</param>
    /// <param name="near">Near plane distance in world units (&gt; 0).</param>
    /// <param name="far">Far plane distance in world units (&gt; near).</param>
    public void SetPerspective(float fieldOfViewDegrees, float near, float far)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldOfViewDegrees);
        if (fieldOfViewDegrees >= 180.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldOfViewDegrees),
                "Field of view must be below 180 degrees.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(near);
        if (far <= near)
        {
            throw new ArgumentOutOfRangeException(nameof(far),
                "Far plane must be greater than the near plane.");
        }

        _fovDegrees = fieldOfViewDegrees;
        _near = near;
        _far = far;
        _lastAppliedAspect = -1; // force re-apply on next render
    }

    /// <summary>
    /// Positions and orients the camera: looks from <paramref name="eye"/>
    /// toward <paramref name="target"/>, world +Y up unless overridden.
    /// </summary>
    public void LookAt(Vector3 eye, Vector3 target)
    {
        LookAt(eye, target, new Vector3(0, 1, 0));
    }

    /// <summary>
    /// Positions and orients the camera with an explicit up direction.
    /// </summary>
    public void LookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        Native.LookAt(eye, target, up);
    }

    /// <summary>The projection matrix (column-major Filament layout as System.Numerics).</summary>
    public Matrix4x4 Projection => Native.ProjectionMatrix;

    /// <summary>
    /// Re-applies the perspective projection for the given aspect, if the
    /// aspect changed since the last application.
    /// </summary>
    internal void ApplyAspect(double aspect)
    {
        if (_fovDegrees is not float fov || Math.Abs(aspect - _lastAppliedAspect) < 1e-9)
        {
            return;
        }
        _native.SetProjection(fov, aspect, _near, _far);
        _lastAppliedAspect = aspect;
    }

    /// <summary>
    /// Disposes the camera and removes it from its scene. The FilamentSharp
    /// camera component and its entity are destroyed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _scene.RemoveCamera(this);
        _native.Dispose();
        EntityManager.Destroy(_entity);
    }

    private ObjectDisposedException NewDisposedException() =>
        new(nameof(Camera), "The PAL camera has been disposed.");
}
