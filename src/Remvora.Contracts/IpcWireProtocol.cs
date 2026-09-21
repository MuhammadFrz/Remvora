using System.Text.Json;

namespace Remvora.Contracts;

/// <summary>
/// Framed JSON-line reader and writer for bidirectional named-pipe IPC communication.
/// </summary>
public static class IpcWireProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
            return default;

        return JsonSerializer.Deserialize<T>(line, JsonOptions);
    }
}
