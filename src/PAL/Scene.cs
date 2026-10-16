using System.Numerics;
using FilamentSharp;

namespace PAL;

/// <summary>
/// A rendering scene: a container of renderable entities, cameras, lights
/// and models, convenient to create from <see cref="Graphics"/> and dispose
/// deterministically.
/// </summary>
/// <remarks>
/// <para>
/// This is a convenience wrapper over FilamentSharp's scene, not a world
/// model: there is no hierarchy, no components and no lifecycle the
/// application must follow. Models and lights created from the scene detach
/// themselves when disposed; raw FilamentSharp entities added through
/// <see cref="AddEntity(FilamentSharp.Entity)"/> remain managed by you.
/// </para>
/// <para>
/// Ownership: the scene is owned by PAL until disposed; the underlying
/// FilamentSharp scene is available via <see cref="Native"/> for anything
/// PAL has not wrapped.
/// </para>
/// </remarks>
public sealed class Scene : IDisposable
{
    private readonly Graphics _graphics;
    private readonly FilamentSharp.Scene _native;
    private readonly List<Camera> _cameras = new();
    private readonly List<Model> _models = new();
    private readonly List<Light> _lights = new();
    private bool _disposed;

    internal Scene(Graphics graphics)
    {
        _graphics = graphics;
        _native = graphics.Engine.CreateScene();
    }

    /// <summary>The underlying FilamentSharp scene (borrowed; do not dispose it yourself).</summary>
    public FilamentSharp.Scene Native => _disposed
        ? throw NewDisposedException()
        : _native;

    /// <summary>Number of entities currently in the scene.</summary>
    public int EntityCount => (int)Native.EntityCount;

    /// <summary>Creates a camera that renders this scene.</summary>
    public Camera CreateCamera()
    {
        var camera = new Camera(_graphics, this);
        _cameras.Add(camera);
        return camera;
    }

    /// <summary>
    /// Loads a glTF/GLB model into this scene from a file.
    /// </summary>
    /// <param name="path">Path to a self-contained GLB (or glTF with embedded data URIs).</param>
    /// <returns>The loaded model, already added to this scene.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="PALException">The content is not valid, self-contained glTF.</exception>
    /// <remarks>
    /// The glTF loader performs no filesystem access: the file must embed
    /// its buffers and textures (GLB, or JSON with data URIs). glTF files
    /// referencing external .bin/image files are rejected by the loader;
    /// wrap such content in a GLB first.
    /// </remarks>
    public Model LoadModel(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Model file not found: '{path}'. The glTF loader needs a self-contained " +
                "GLB file (embedded buffers and textures).", path);
        }
        return LoadModel(File.ReadAllBytes(path));
    }

    /// <summary>Loads a glTF/GLB model into this scene from memory.</summary>
    /// <param name="glb">Self-contained GLB (or glTF with embedded data URIs) bytes.</param>
    public Model LoadModel(byte[] glb)
    {
        var model = new Model(_graphics, this, glb);
        _models.Add(model);
        return model;
    }

    /// <summary>
    /// Adds a directional light to the scene: cheap, uniform lighting for
    /// lit materials (the glTF ubershader, lit procedural geometry).
    /// </summary>
    /// <param name="direction">
    /// The direction the light travels (normalized by PAL); for a "sun at
    /// noon" look pass something like <c>new Vector3(-0.4f, -1, -0.6f)</c>.
    /// </param>
    /// <param name="color">Light color (sRGB authored).</param>
    /// <param name="intensityLux">Illuminance in lux (e.g. 100_000 for daylight).</param>
    /// <returns>The light; dispose to remove it from the scene.</returns>
    public Light AddDirectionalLight(Vector3 direction, Color color, float intensityLux)
    {
        var light = Light.CreateDirectional(_graphics, this, direction, color, intensityLux);
        _lights.Add(light);
        return light;
    }

    /// <summary>
    /// Adds a raw FilamentSharp entity to the scene (escape hatch). The
    /// entity's lifetime stays yours: remove it with
    /// <see cref="RemoveEntity(FilamentSharp.Entity)"/> or dispose whatever
    /// wrapper owns it.
    /// </summary>
    public void AddEntity(FilamentSharp.Entity entity) => Native.AddEntity(entity);

    /// <summary>Removes a raw FilamentSharp entity from the scene.</summary>
    public void RemoveEntity(FilamentSharp.Entity entity) => Native.RemoveEntity(entity);

    internal void RemoveCamera(Camera camera) => _cameras.Remove(camera);

    internal void RemoveModel(Model model) => _models.Remove(model);

    internal void RemoveLight(Light light) => _lights.Remove(light);

    /// <summary>
    /// Disposes the scene. Models, cameras and lights created from it stay
    /// alive (their entities simply leave the scene) and must be disposed by
    /// their owner — or by the engine's teardown safety net if leaked.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var model in _models)
        {
            model.SceneDisposed();
        }
        _models.Clear();

        _native.Dispose();
    }

    private ObjectDisposedException NewDisposedException() =>
        new(nameof(Scene), "The PAL scene has been disposed.");
}
