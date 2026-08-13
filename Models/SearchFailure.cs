namespace WallhavenService.Models;

public sealed record SearchFailure(string Message, int ConsecutiveFailures, bool AutoRotationDisabled);
