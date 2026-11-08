namespace AionMeter.App.Services;

public sealed record ActivityTimer(Guid Id, string Name, DateTimeOffset DueUtc, bool Notified = false)
{
    public string Countdown(DateTimeOffset now)
    {
        var seconds = Math.Max(0L, (long)Math.Ceiling((DueUtc - now).TotalSeconds));
        return $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
    }
}
