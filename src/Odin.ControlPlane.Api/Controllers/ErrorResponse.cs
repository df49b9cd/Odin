namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Standard error response for all API endpoints.
/// </summary>
public sealed record ErrorResponse
{
    public required string Message { get; init; }
    public required string Code { get; init; }
}
