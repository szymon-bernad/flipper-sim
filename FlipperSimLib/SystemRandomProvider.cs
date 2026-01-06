namespace FlipperSimLib;

public class SystemRandomProvider : IRandomProvider
{
    private readonly Random _random;

    public SystemRandomProvider(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random((int)DateTime.Now.Ticks);
    }

    public double NextDouble() => _random.NextDouble();
}