using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Infrastructure;

/// <summary>
/// Bridges Spectre.Console.Cli's type resolution into the Microsoft DI container.
/// </summary>
internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        return _provider.GetService(type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }
}
