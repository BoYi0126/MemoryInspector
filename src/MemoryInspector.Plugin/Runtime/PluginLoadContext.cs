using System.Reflection;
using System.Runtime.Loader;

namespace MemoryInspector.Plugin.Runtime;

internal sealed class PluginLoadContext(
    string entryAssemblyPath) :
    AssemblyLoadContext(
        $"MemoryInspector.Plugin:{Path.GetFileNameWithoutExtension(
            entryAssemblyPath)}:{Guid.NewGuid():N}",
        isCollectible: true)
{
    private static readonly HashSet<string> SharedAssemblies =
    [
        typeof(IPluginManager).Assembly.GetName().Name!,
        typeof(MemoryInspector.Common.Result).Assembly.GetName().Name!,
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
    ];
    private readonly AssemblyDependencyResolver _resolver =
        new(entryAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (SharedAssemblies.Contains(assemblyName.Name!))
        {
            return Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(
                    assembly.GetName(),
                    assemblyName));
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null
            ? null
            : LoadManagedAssembly(path);
    }

    protected override nint LoadUnmanagedDll(
        string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(
            unmanagedDllName);
        return path is null
            ? nint.Zero
            : LoadUnmanagedDllFromPath(path);
    }

    public Assembly LoadEntryAssembly(string path)
    {
        return LoadManagedAssembly(path);
    }

    private Assembly LoadManagedAssembly(string path)
    {
        using var assemblyStream = new MemoryStream(
            File.ReadAllBytes(path),
            writable: false);
        var symbolsPath = Path.ChangeExtension(path, ".pdb");

        if (!File.Exists(symbolsPath))
        {
            return LoadFromStream(assemblyStream);
        }

        using var symbolsStream = new MemoryStream(
            File.ReadAllBytes(symbolsPath),
            writable: false);
        return LoadFromStream(
            assemblyStream,
            symbolsStream);
    }
}
