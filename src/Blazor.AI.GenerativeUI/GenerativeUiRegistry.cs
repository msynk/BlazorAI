using System.Collections.Concurrent;
using System.ComponentModel;
using IComponent = Microsoft.AspNetCore.Components.IComponent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.AI.GenerativeUI;

/// <summary>
/// The components model output is allowed to render.
/// </summary>
public interface IGenerativeUiRegistry
{
    /// <summary>Every registered component.</summary>
    IReadOnlyCollection<GenerativeComponent> Components { get; }

    /// <summary>
    /// Finds a component by the name the model used.
    /// </summary>
    /// <param name="name">The component name.</param>
    /// <returns>
    /// The registration, or <see langword="null"/> when nothing is registered under that name. A
    /// null result must render a fallback: it is the case where a model named something it should
    /// not have.
    /// </returns>
    GenerativeComponent? Find(string name);
}

/// <summary>
/// The default registry, populated at startup and safe to read from any thread.
/// </summary>
/// <remarks>
/// Registration derives the bindable property list from the component public
/// <see cref="ParameterAttribute"/> properties, minus the ones no model should ever set:
/// callbacks, render fragments, and unmatched-attribute catch-alls. That exclusion is what stops a
/// generated payload from wiring up event handlers or injecting attributes.
/// </remarks>
public sealed class GenerativeUiRegistry : IGenerativeUiRegistry
{
    private readonly ConcurrentDictionary<string, GenerativeComponent> _components =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyCollection<GenerativeComponent> Components => _components.Values.ToArray();

    /// <inheritdoc />
    public GenerativeComponent? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _components.TryGetValue(name, out var component) ? component : null;
    }

    /// <summary>
    /// Registers a component under a name the model can use.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="name">The name the model uses. Keep it descriptive: the model reads it.</param>
    /// <param name="description">What the component is for, included in the schema.</param>
    /// <param name="required">Property names the model must supply.</param>
    /// <returns>The registration.</returns>
    public GenerativeComponent Register<TComponent>(
        string name,
        string? description = null,
        params string[] required)
        where TComponent : IComponent =>
        Register(typeof(TComponent), name, description, required);

    /// <summary>
    /// Registers a component under a name the model can use.
    /// </summary>
    /// <param name="componentType">The component type.</param>
    /// <param name="name">The name the model uses.</param>
    /// <param name="description">What the component is for.</param>
    /// <param name="required">Property names the model must supply.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentException">The type is not a Blazor component.</exception>
    public GenerativeComponent Register(
        Type componentType,
        string name,
        string? description = null,
        params string[] required)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"{componentType.Name} is not a Blazor component.", nameof(componentType));
        }

        var properties = DiscoverProperties(componentType, required);
        var component = new GenerativeComponent(name, componentType, properties, description);
        _components[name] = component;
        return component;
    }

    /// <summary>Removes a registration.</summary>
    /// <param name="name">The name to remove.</param>
    /// <returns><see langword="true"/> when a registration was removed.</returns>
    public bool Unregister(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _components.TryRemove(name, out _);
    }

    /// <summary>
    /// Produces a JSON Schema describing every registered component, for use in a system prompt or
    /// as the schema of a render tool.
    /// </summary>
    /// <returns>The schema.</returns>
    /// <remarks>
    /// Handing this to the model is what makes generative UI reliable. Without it the model guesses
    /// component names, and every guess renders a fallback.
    /// </remarks>
    public JsonElement DescribeAll()
    {
        var variants = _components.Values
            .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["description"] = c.Description,
                ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["component"] = new Dictionary<string, object?> { ["const"] = c.Name },
                    ["props"] = c.Schema,
                },
                ["required"] = new[] { "component", "props" },
            })
            .ToArray();

        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["oneOf"] = variants,
        });
    }

    private static IReadOnlyList<GenerativeProperty> DiscoverProperties(Type componentType, string[] required)
    {
        var results = new List<GenerativeProperty>();

        foreach (var property in componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameter = property.GetCustomAttribute<ParameterAttribute>();
            if (parameter is null || parameter.CaptureUnmatchedValues) continue;
            if (property.SetMethod is null || !property.SetMethod.IsPublic) continue;
            if (!IsBindable(property.PropertyType)) continue;

            var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var isRequired = required.Contains(property.Name, StringComparer.OrdinalIgnoreCase);
            results.Add(new GenerativeProperty(property.Name, property.PropertyType, description, isRequired));
        }

        return results;
    }

    private static bool IsBindable(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type)) return false;
        if (type == typeof(RenderFragment) || type == typeof(EventCallback)) return false;
        if (!type.IsGenericType) return true;

        var definition = type.GetGenericTypeDefinition();
        return definition != typeof(RenderFragment<>) && definition != typeof(EventCallback<>);
    }
}
