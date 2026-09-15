namespace xyz.Drivers.Robot;

public sealed class RobotResponse
{
    public bool IsSuccess { get; init; }

    public string Error { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public bool? ServoOn { get; init; }

    public double? Speed { get; init; }

    public double? Position { get; init; }

    public string? DeviceError { get; init; }
}
