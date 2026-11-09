using System.Diagnostics;

namespace RobloxModManager.Core;

/// <summary>Starts the user's custom integrations alongside Roblox and closes the ones marked auto-close.</summary>
public sealed class IntegrationRunner
{
    private readonly List<(CustomIntegration Integration, Process Process)> _running = new();

    public bool HasAutoClose => _running.Any(r => r.Integration.AutoClose);

    public void StartAll(IEnumerable<CustomIntegration> integrations)
    {
        foreach (var integration in integrations)
        {
            if (string.IsNullOrWhiteSpace(integration.Location) || !File.Exists(integration.Location))
            {
                Logger.WriteLine("Integrations", $"Skipping '{integration.Name}': file not found ({integration.Location})");
                continue;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = integration.Location,
                    Arguments = integration.LaunchArgs,
                    WorkingDirectory = Path.GetDirectoryName(integration.Location) ?? "",
                    UseShellExecute = true
                });

                if (process is not null)
                    _running.Add((integration, process));

                Logger.WriteLine("Integrations", $"Started '{integration.Name}'");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Integrations", $"Failed to start '{integration.Name}': {ex.Message}");
            }
        }
    }

    public void CloseAutoClose()
    {
        foreach (var (integration, process) in _running)
        {
            if (!integration.AutoClose)
                continue;

            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Integrations", $"Could not close '{integration.Name}': {ex.Message}");
            }
        }
    }
}
