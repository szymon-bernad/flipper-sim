namespace FlipperSimLib.Tests.TestDoubles;

public sealed class FakeRandomProvider : IRandomProvider
{
    private readonly Queue<double> _scheduledValues = new();

    public double DefaultValue { get; set; } = 0.5d;

    public void Enqueue(double value) => _scheduledValues.Enqueue(value);

    public void Enqueue(params double[] values)
    {
        foreach (var value in values)
        {
            _scheduledValues.Enqueue(value);
        }
    }

    public double NextDouble() => _scheduledValues.Count > 0 ? _scheduledValues.Dequeue() : DefaultValue;

    public void Clear() => _scheduledValues.Clear();
}