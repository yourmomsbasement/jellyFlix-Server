namespace JellyFlix.Profiles;

/// <summary>
/// The device a profile switch happens on. The new session is issued for the same device.
/// </summary>
/// <param name="DeviceId">The device id.</param>
/// <param name="DeviceName">The device name.</param>
/// <param name="Client">The client app name.</param>
/// <param name="Version">The client app version.</param>
/// <param name="CurrentToken">The access token used for the request; it is revoked after the switch.</param>
/// <param name="RemoteEndPoint">The caller's address.</param>
public record SwitchContext(string DeviceId, string DeviceName, string Client, string Version, string? CurrentToken, string? RemoteEndPoint);
