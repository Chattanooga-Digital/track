using Track.Services.Mail;

namespace Track.Website.HostedServices;

/// <summary>Central: sends queued email every minute, or as soon as some is queued.</summary>
public sealed class EmailDeliveryHostedService(
    ILogger<EmailDeliveryHostedService> logger,
    IServiceScopeFactory scopeFactory,
    EmailDeliverySignal signal) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ILogger<EmailDeliveryHostedService> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly EmailDeliverySignal _signal = signal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<EmailDelivery>().DeliverDueAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Email delivery pass failed");
                }

                await _signal.WaitAsync(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
