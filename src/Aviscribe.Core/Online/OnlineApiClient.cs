using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace Aviscribe.Core.Online;

public sealed class OnlineApiClient(string host, int port)
{
    public string Host { get; } = host;
    public int Port { get; } = port;

    public Task<OnlineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        SendAsync<OnlineCapabilities>(new OnlineRequest { Operation = "capabilities" }, cancellationToken);

    public async Task<T> SendAsync<T>(OnlineRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535)
            throw new OnlineApiException("invalidRequest", "The server address or port is invalid.");

        var json = JsonSerializer.SerializeToUtf8Bytes(request, OnlineProtocol.JsonOptions);
        if (json.Length > OnlineProtocol.MaximumRequestSize)
            throw new OnlineApiException("invalidRequest", "The request exceeds the protocol limit.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Operation == "waitForChanges"
            ? TimeSpan.FromSeconds(35)
            : TimeSpan.FromSeconds(10));
        var requestCancellation = timeout.Token;

        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(Host, Port, requestCancellation).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await stream.WriteAsync(OnlineProtocol.Magic, requestCancellation).ConfigureAwait(false);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, json.Length);
        await stream.WriteAsync(length, requestCancellation).ConfigureAwait(false);
        await stream.WriteAsync(json, requestCancellation).ConfigureAwait(false);

        await ReadExactAsync(stream, length, requestCancellation).ConfigureAwait(false);
        var responseLength = BinaryPrimitives.ReadInt32BigEndian(length);
        if (responseLength is <= 0 or > OnlineProtocol.MaximumResponseSize)
            throw new OnlineApiException("invalidResponse", "The server returned an invalid response size.");
        var payload = new byte[responseLength];
        await ReadExactAsync(stream, payload, requestCancellation).ConfigureAwait(false);

        OnlineResponse response;
        try
        {
            response = JsonSerializer.Deserialize<OnlineResponse>(payload, OnlineProtocol.JsonOptions) ??
                       throw new JsonException("Empty response.");
        }
        catch (JsonException ex)
        {
            throw new OnlineApiException("invalidResponse", $"The server response was malformed: {ex.Message}");
        }
        if (response.Version != OnlineProtocol.Version || response.RequestId != request.RequestId)
            throw new OnlineApiException("invalidResponse", "The server response did not match the request.");
        if (!response.Ok)
            throw new OnlineApiException(
                response.Error?.Code ?? "invalidResponse",
                response.Error?.Message ?? "The server rejected the request.");
        if (response.Error != null || response.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new OnlineApiException("invalidResponse", "The successful response was incomplete.");
        try
        {
            return response.Data.Deserialize<T>(OnlineProtocol.JsonOptions) ??
                   throw new JsonException("Missing response data.");
        }
        catch (JsonException ex)
        {
            throw new OnlineApiException("invalidResponse", $"The response data was invalid: {ex.Message}");
        }
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new OnlineApiException("invalidResponse", "The server closed the response early.");
            offset += read;
        }
    }
}
