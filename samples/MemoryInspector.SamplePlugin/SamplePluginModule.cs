using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Common;
using MemoryInspector.Plugin;

namespace MemoryInspector.SamplePlugin;

public sealed class SamplePluginModule :
    IMemoryInspectorPlugin
{
    private IPluginUiContribution[] _contributions = [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SampleAnalyzerService>();
    }

    public ValueTask InitializeAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var analyzer = context.Services
            .GetRequiredService<SampleAnalyzerService>();
        _contributions =
        [
            new SampleAnalyzerContribution(analyzer),
        ];
        _ = context.Logger.Log(
            PluginLogLevel.Information,
            "Sample analyzer service initialized.");
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<IPluginUiContribution>
        GetUiContributions() => _contributions;

    public ValueTask ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        _contributions = [];
        return ValueTask.CompletedTask;
    }
}

internal sealed class SampleAnalyzerService
{
    public PluginUiResult Analyze()
    {
        return new PluginUiResult(
            "Sample analyzer completed.",
            "The isolated sample service was resolved from its " +
            "plugin DI provider.");
    }
}

internal sealed class SampleAnalyzerContribution(
    SampleAnalyzerService analyzer) : IPluginUiContribution
{
    public string Id => "sample-analyzer";

    public string Title => "Run Sample Analyzer";

    public string Description =>
        "Verifies the sample plugin service and UI contract.";

    public PluginKind Kind => PluginKind.Analyzer;

    public ValueTask<Result<PluginUiResult>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            Result<PluginUiResult>.Success(analyzer.Analyze()));
    }
}
