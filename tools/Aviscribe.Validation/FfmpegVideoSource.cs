// Copyright (C) 2026 SMOVision contributors. SPDX-License-Identifier: AGPL-3.0-or-later
// Vendored from SMOVision v0.1.0-beta.1 (ea286dd22c0ba7b70e2dc1d6d42ca76ad7467193).
// Only the namespace is changed. See SMOVision-LICENSE.txt and README.md.
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SMOVision.Core;
using SMOVision.Core.Capture;

namespace Aviscribe.Validation;

public sealed record FfmpegOptions(string FfmpegExecutable = "ffmpeg", string FfprobeExecutable = "ffprobe",
    string SourceId = "video", TimeSpan? End = null, bool ComputeContentHash = false);

/// <summary>Application-side example of sequential FFmpeg decoding. Used by samples and tests, not shipped as a library package.</summary>
public sealed partial class FfmpegVideoSource : ISeekableFrameSource
{
    private readonly string _file;
    private readonly FfmpegOptions _options;
    private TimeSpan _start, _reportFrom;
    private int _active, _disposed;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private (TimeSpan StartTime, TimeSpan? Duration)? _probeInfo;
    public FrameSourceInfo Info { get; private set; }
    public TimeSpan ReportingFrom => _reportFrom;
    public TimeSpan DecodingFrom => _start;
    public long DecodedFrames { get; private set; }
    public TimeSpan Position { get; private set; }
    public FfmpegVideoSource(string file, FfmpegOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        _file = Path.GetFullPath(file); _options = options ?? new();
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SourceId);
        if (_options.End < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        Info = new(_options.SourceId, false, true, Decoder: "FFmpeg CLI");
    }
    public void Seek(TimeSpan position, TimeSpan leadIn = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Volatile.Read(ref _active) != 0) throw new InvalidOperationException("Stop the reader before seeking.");
        if (position < TimeSpan.Zero || leadIn < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        _reportFrom = position; _start = position > leadIn ? position - leadIn : TimeSpan.Zero;
    }
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_probeInfo is not null) return;
        var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        string? hash = null;
        if (_options.ComputeContentHash)
        {
            await using var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        Info = Info with { Duration = probe.Duration, ContentHash = hash }; _probeInfo = probe;
    }
    public async IAsyncEnumerable<FrameBuffer> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _active, 1) != 0) throw new InvalidOperationException("A file source permits only one active reader.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var token = lifetime.Token;
        Process? decoder = null;
        Task? metadataReader = null;
        var metadata = Channel.CreateBounded<FrameMetadata>(new BoundedChannelOptions(4) { SingleReader = true, SingleWriter = true });
        try
        {
            await InitializeAsync(token).ConfigureAwait(false);
            var probe = _probeInfo!.Value;
            if (_options.End is { } end && end <= _start) yield break;
            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "info", "-nostats", "-xerror", "-copyts" };
            if (_start > TimeSpan.Zero) args.AddRange(["-ss", Seconds(_start)]);
            args.AddRange(["-i", _file, "-map", "0:v:0", "-an", "-sn", "-dn",
                "-vf", "format=bgr24,showinfo=checksum=0", "-fps_mode", "passthrough", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"]);
            decoder = Start(_options.FfmpegExecutable, args);
            using var cancellation = token.Register(() => Kill(decoder));
            metadataReader = ReadMetadataAsync(decoder, metadata.Writer, token);
            DecodedFrames = 0; Position = _start;
            TimeSpan? previous = null;
            var reachedEnd = false;
            await foreach (var item in metadata.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var mediaTime = item.Timestamp - probe.StartTime;
                if (_options.End is { } stop && mediaTime >= stop) { reachedEnd = true; break; }
                if (previous is { } last && mediaTime < last) throw new InvalidDataException("Non-monotonic media presentation timestamps.");
                previous = mediaTime;
                var stride = checked(item.Width * 3);
                var length = checked(stride * item.Height);
                if (length <= 0 || length > 512 * 1024 * 1024) throw new InvalidDataException("Unsupported decoded frame dimensions.");
                var owner = MemoryPool<byte>.Shared.Rent(length);
                FrameBuffer frame;
                try
                {
                    await decoder.StandardOutput.BaseStream.ReadExactlyAsync(owner.Memory[..length], token).ConfigureAwait(false);
                    frame = new(owner, item.Width, item.Height, stride, PixelFormat.Bgr24,
                        new(Info.Id, Guid.Empty, 0, DecodedFrames++, mediaTime, TimestampOrigin.MediaPresentation, mediaTime < _reportFrom));
                }
                catch { owner.Dispose(); throw; }
                Position = mediaTime;
                yield return frame;
            }
            if (!reachedEnd)
            {
                await decoder.WaitForExitAsync(token).ConfigureAwait(false);
                await metadataReader.ConfigureAwait(false);
                if (decoder.ExitCode != 0) throw new InvalidDataException("FFmpeg could not decode the complete video stream.");
            }
        }
        finally
        {
            lifetime.Cancel();
            if (decoder is not null)
            {
                Kill(decoder);
                if (metadataReader is not null) try { await metadataReader.ConfigureAwait(false); } catch (OperationCanceledException) { }
                await decoder.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                decoder.Dispose();
            }
            Volatile.Write(ref _active, 0);
        }
    }
    private async Task<(TimeSpan StartTime, TimeSpan? Duration)> ProbeAsync(CancellationToken token)
    {
        if (!File.Exists(_file)) throw new FileNotFoundException("Video input is unavailable.");
        using var process = Start(_options.FfprobeExecutable,
            ["-v", "error", "-select_streams", "v:0", "-show_program_version", "-show_entries", "stream=width,height,start_time:format=duration", "-of", "json", _file]);
        using var cancellation = token.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync(token);
        var json = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false); await errors.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException("Video metadata could not be decoded.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("program_version", out var program) && program.TryGetProperty("version", out var version))
            Info = Info with { DecoderVersion = "ffprobe " + version.GetString() };
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidDataException("Input has no supported video stream.");
        var stream = streams[0];
        var start = stream.TryGetProperty("start_time", out var startValue) && ParseSeconds(startValue.GetString(), out var startTime) ? startTime : TimeSpan.Zero;
        TimeSpan? duration = document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var value) &&
            ParseSeconds(value.GetString(), out var length) ? length : null;
        return (start, duration);
    }
    private static async Task ReadMetadataAsync(Process process, ChannelWriter<FrameMetadata> output, CancellationToken token)
    {
        Exception? failure = null;
        long timeBaseNumerator = 0, timeBaseDenominator = 0;
        try
        {
            while (await process.StandardError.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                var configuration = TimeBase().Match(line);
                if (configuration.Success)
                {
                    timeBaseNumerator = long.Parse(configuration.Groups[1].Value, CultureInfo.InvariantCulture);
                    timeBaseDenominator = long.Parse(configuration.Groups[2].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                var match = ShowInfo().Match(line);
                if (!match.Success) continue;
                if (timeBaseNumerator <= 0 || timeBaseDenominator <= 0 || !long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pts))
                    throw new InvalidDataException("Missing media presentation time base.");
                var time = TimeSpan.FromTicks(checked((long)decimal.Round((decimal)pts * timeBaseNumerator * TimeSpan.TicksPerSecond / timeBaseDenominator)));
                await output.WriteAsync(new(time, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)), token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { failure = error; }
        finally { output.TryComplete(failure); }
    }
    private sealed record FrameMetadata(TimeSpan Timestamp, int Width, int Height);
    [GeneratedRegex(@"\[.*showinfo.*\].*\bn:\s*\d+.*\bpts:\s*(-?\d+).*\bs:(\d+)x(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ShowInfo();
    [GeneratedRegex(@"\[.*showinfo.*\].*config in time_base:\s*(\d+)/(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TimeBase();
    private static bool ParseSeconds(string? value, out TimeSpan time)
    {
        time = default;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds)) return false;
        time = TimeSpan.FromSeconds(seconds); return true;
    }
    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("R", CultureInfo.InvariantCulture);
    private static Process Start(string executable, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try { return Process.Start(info) ?? throw new InvalidOperationException("Decoder process did not start."); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("FFmpeg tools are unavailable. Install FFmpeg and make ffmpeg/ffprobe available on PATH, or supply explicit FfmpegOptions in your application."); }
    }
    private static void Kill(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _disposeCancellation.Cancel();
        return ValueTask.CompletedTask;
    }
}
