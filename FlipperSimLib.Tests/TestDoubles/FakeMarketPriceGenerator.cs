namespace FlipperSimLib.Tests.TestDoubles;

public sealed class FakeMarketPriceGenerator : FakeMarketPriceProvider, IMarketPriceGenerator
{
    public int RunCount { get; private set; }

    public Action? RunCallback { get; set; }

    public void RunGenerator()
    {
        RunCount++;
        RunCallback?.Invoke();
    }
}