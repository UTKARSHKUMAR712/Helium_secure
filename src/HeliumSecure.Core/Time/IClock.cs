namespace HeliumSecure.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class TestClock : IClock
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset initialTime)
    {
        _now = initialTime;
    }

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan duration)
    {
        _now = _now.Add(duration);
    }

    public void Set(DateTimeOffset time)
    {
        _now = time;
    }
}
