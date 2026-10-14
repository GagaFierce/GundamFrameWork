using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace WFrameWork.Diagnostics
{
    /// <summary>Appends diagnostic events as UTF-8 JSON lines. File failures never escape Report.</summary>
    public sealed class JsonLineDiagnosticSink : IDiagnosticSink
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private readonly object _gate = new object();
        private readonly string _path;
        private long _failedWrites;

        public JsonLineDiagnosticSink(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A diagnostic file path is required.", nameof(path));
            _path = path;
        }

        public long FailedWriteCount => Interlocked.Read(ref _failedWrites);

        public void Report(in DiagnosticEvent diagnostic)
        {
            try
            {
                var text = new StringBuilder(256);
                text.Append("{\"timestampUtc\":").Append(JsonString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
                text.Append(",\"level\":").Append(JsonString(diagnostic.Level.ToString()));
                text.Append(",\"module\":").Append(JsonString(diagnostic.Module));
                text.Append(",\"message\":").Append(JsonString(diagnostic.Message));
                text.Append(",\"sequence\":").Append(diagnostic.Sequence.ToString(CultureInfo.InvariantCulture));
                if (diagnostic.Exception != null)
                {
                    text.Append(",\"exceptionType\":").Append(JsonString(diagnostic.Exception.GetType().FullName));
                    text.Append(",\"exceptionMessage\":").Append(JsonString(diagnostic.Exception.Message));
                    text.Append(",\"exceptionStackTrace\":").Append(JsonString(diagnostic.Exception.StackTrace));
                }
                text.Append('}').Append('\n');

                lock (_gate)
                {
                    string directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.AppendAllText(_path, text.ToString(), Utf8WithoutBom);
                }
            }
            catch
            {
                Interlocked.Increment(ref _failedWrites);
            }
        }

        private static string JsonString(string value)
        {
            var text = new StringBuilder((value == null ? 0 : value.Length) + 2);
            text.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char ch = value[i];
                    switch (ch)
                    {
                        case '"': text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\b': text.Append("\\b"); break;
                        case '\f': text.Append("\\f"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        default:
                            if (ch < 0x20) text.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            else text.Append(ch);
                            break;
                    }
                }
            }
            return text.Append('"').ToString();
        }
    }
}
