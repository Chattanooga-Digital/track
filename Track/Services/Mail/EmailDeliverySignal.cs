namespace Track.Services.Mail;

public sealed class EmailDeliverySignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        await _wake.WaitAsync(timeout, cancellationToken);
}
