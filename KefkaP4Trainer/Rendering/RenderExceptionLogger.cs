using Dalamud.Plugin.Services;

namespace KefkaP4Trainer.Rendering;

internal sealed class RenderExceptionLogger
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(10);

    private readonly IPluginLog log;
    private readonly string component;
    private DateTime lastLoggedAtUtc = DateTime.MinValue;
    private string? lastSignature;

    public RenderExceptionLogger(IPluginLog log, string component)
    {
        this.log = log;
        this.component = component;
    }

    public void Report(Exception exception)
    {
        var signature = $"{exception.GetType().FullName}:{exception.Message}";
        var now = DateTime.UtcNow;
        if (signature == lastSignature && now - lastLoggedAtUtc < RepeatInterval)
        {
            return;
        }

        lastSignature = signature;
        lastLoggedAtUtc = now;
        log.Error(exception, "{Component} rendering failed; the affected draw was skipped.", component);
    }
}
