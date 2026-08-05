using System.Collections.ObjectModel;
using System.Text;

namespace Aviscribe.Core.Diagnostics;

public enum DiagnosticLevel
{
    Debug,
    Information,
    Error
}

public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    string Message);

public interface IAppDiagnostics : IDisposable
{
    string LogDirectory { get; }
    IReadOnlyList<DiagnosticEntry> RecentEntries { get; }

    void Debug(string message);
    void Information(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class FileAppDiagnostics : IAppDiagnostics
{
    public const long MaximumFileBytes = 5L * 1024 * 1024;
    public const int RetainedFileCount = 10;

    private readonly object _sync = new();
    private readonly Queue<DiagnosticEntry> _recentEntries = new();
    private readonly string _currentLogPath;
    private StreamWriter? _writer;
    private bool _disposed;

    public FileAppDiagnostics(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        LogDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        _currentLogPath = Path.Combine(logDirectory, "aviscribe.log");
        _writer = OpenWriter(_currentLogPath);
    }

    public string LogDirectory { get; }

    public IReadOnlyList<DiagnosticEntry> RecentEntries
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyCollection<DiagnosticEntry>(
                    _recentEntries.ToArray());
            }
        }
    }

    public void Debug(string message)
    {
        Write(DiagnosticLevel.Debug, message);
    }

    public void Information(string message)
    {
        Write(DiagnosticLevel.Information, message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception == null
            ? message
            : $"{message} ({exception.GetType().Name}: {exception.Message})";
        Write(DiagnosticLevel.Error, detail);
    }

    private void Write(DiagnosticLevel level, string message)
    {
        var entry = new DiagnosticEntry(
            DateTimeOffset.Now,
            level,
            NormalizeMessage(message));

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var line = Format(entry);
            RotateIfRequired(Encoding.UTF8.GetByteCount(line) + 2);
            _writer!.WriteLine(line);
            _writer.Flush();

            _recentEntries.Enqueue(entry);
            while (_recentEntries.Count > 200)
                _recentEntries.Dequeue();
        }
    }

    private void RotateIfRequired(int nextLineBytes)
    {
        if (!_currentLogPath.Equals(
                (_writer?.BaseStream as FileStream)?.Name,
                StringComparison.OrdinalIgnoreCase) ||
            _writer!.BaseStream.Length + nextLineBytes <= MaximumFileBytes)
        {
            return;
        }

        _writer.Dispose();
        _writer = null;
        for (var index = RetainedFileCount - 1; index >= 1; index--)
        {
            var destination = ArchivePath(index);
            var source = index == 1
                ? _currentLogPath
                : ArchivePath(index - 1);
            if (File.Exists(source))
                File.Move(source, destination, overwrite: true);
        }

        _writer = OpenWriter(_currentLogPath);
    }

    private string ArchivePath(int index)
    {
        return Path.Combine(LogDirectory, $"aviscribe.{index}.log");
    }

    private static StreamWriter OpenWriter(string path)
    {
        return new StreamWriter(
            new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Format(DiagnosticEntry entry)
    {
        return $"{entry.Timestamp:O} [{entry.Level}] {entry.Message}";
    }

    private static string NormalizeMessage(string message)
    {
        return message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

public sealed class NullAppDiagnostics : IAppDiagnostics
{
    public static NullAppDiagnostics Instance { get; } = new();

    private NullAppDiagnostics()
    {
    }

    public string LogDirectory => string.Empty;
    public IReadOnlyList<DiagnosticEntry> RecentEntries => [];
    public void Debug(string message) { }
    public void Information(string message) { }
    public void Error(string message, Exception? exception = null) { }
    public void Dispose() { }
}
