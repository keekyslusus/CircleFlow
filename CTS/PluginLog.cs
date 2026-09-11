using System.IO;
using System.Text;

namespace CircleToSearch;

public sealed class PluginLog
{
    private const string FileName = "plugin.log";
    private const string PreviousFileName = "plugin.log.old";
    private const long MaxBytes = 50 * 1024;
    private static readonly Encoding FileEncoding = new UTF8Encoding(false);

    private readonly string _path;
    private readonly string _previousPath;
    private readonly Lock _gate = new();

    public PluginLog(string pluginDirectory)
    {
        _path = Path.Combine(pluginDirectory, FileName);
        _previousPath = Path.Combine(pluginDirectory, PreviousFileName);
    }

    public void Info(string source, string message) => Write("INFO", source, message);

    public void Warn(string source, string message) => Write("WARN", source, message);

    public void Error(string source, string message, Exception exception)
        => Write("ERROR", source, $"{message}{Environment.NewLine}{exception}");

    public void SafeError(string source, string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        Write(
            "ERROR",
            source,
            $"operation={operation} exceptionType={exception.GetType().FullName} " +
            $"hresult=0x{exception.HResult:X8}{Environment.NewLine}{exception.StackTrace ?? "<no stack trace>"}");
    }

    private void Write(string level, string source, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {source} {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                Roll();
                File.AppendAllText(_path, line, FileEncoding);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Roll()
    {
        var current = new FileInfo(_path);
        if (!current.Exists || current.Length < MaxBytes) return;

        File.Move(_path, _previousPath, overwrite: true);
    }
}
