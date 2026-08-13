namespace WallhavenService.Models;

public sealed record SearchRetry(string Message, int RetryNumber, int MaximumRetries, TimeSpan Delay);
