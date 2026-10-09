using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FAMOT.Recording
{
    /// <summary>
    /// Plain-text log of raw UDP traffic, one line per datagram:
    /// <c>utc&lt;TAB&gt;unity_time&lt;TAB&gt;direction&lt;TAB&gt;endpoint&lt;TAB&gt;message</c>.
    /// Thread-safe: <see cref="Log"/> may be called from the UDP receive thread. Because Unity's <c>Time</c> API is
    /// main-thread only, <c>unity_time</c> is derived from a <see cref="System.Diagnostics.Stopwatch"/> anchored to
    /// <c>Time.realtimeSinceStartupAsDouble</c> when the logger is constructed (construct it on the main thread).
    /// Tabs and line breaks inside messages are escaped as \t, \n and \r so each entry stays on one line.
    /// </summary>
    public sealed class RawUdpLogger : IDisposable
    {
        /// <summary>Direction tag for received datagrams.</summary>
        public const string In = "IN";

        /// <summary>Direction tag for sent datagrams.</summary>
        public const string Out = "OUT";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly System.Diagnostics.Stopwatch _clock;
        private readonly double _unityTimeAtStart;
        private readonly int _flushEveryLines;
        private readonly StringBuilder _line = new StringBuilder(256);
        private StreamWriter _writer;
        private long _lineCount;
        private long _lastFlushTicks;

        /// <summary>
        /// Creates (overwrites) the log file and writes a commented header line. Call on the Unity main thread.
        /// </summary>
        /// <param name="path">Destination file, typically <c>{session}/udp_raw.log</c>.</param>
        /// <param name="flushEveryLines">Flush after this many lines (also flushed at least every 0.5 s of logging).</param>
        public RawUdpLogger(string path, int flushEveryLines = 50)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            Path = path;
            _flushEveryLines = Math.Max(1, flushEveryLines);

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096);
            _writer = new StreamWriter(stream, Utf8NoBom, 64 * 1024);
            _writer.Write("# utc\tunity_time\tdirection\tendpoint\tmessage\n");
            _writer.Flush();

            _unityTimeAtStart = Time.realtimeSinceStartupAsDouble;
            _clock = System.Diagnostics.Stopwatch.StartNew();
        }

        /// <summary>Path of the log file.</summary>
        public string Path { get; }

        /// <summary>Number of entries written.</summary>
        public long LineCount
        {
            get
            {
                lock (_sync)
                {
                    return _lineCount;
                }
            }
        }

        /// <summary>
        /// Appends one entry. Safe to call from any thread; ignored after <see cref="Dispose"/>.
        /// </summary>
        /// <param name="direction">"IN" or "OUT" (see <see cref="In"/>, <see cref="Out"/>).</param>
        /// <param name="endpoint">Remote endpoint, e.g. "192.168.1.10:5005".</param>
        /// <param name="message">Datagram payload as text.</param>
        public void Log(string direction, string endpoint, string message)
        {
            string utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            lock (_sync)
            {
                if (_writer == null)
                {
                    return;
                }

                double unityTime = _unityTimeAtStart + _clock.Elapsed.TotalSeconds;

                _line.Clear();
                _line.Append(utc).Append('\t');
                _line.Append(unityTime.ToString("0.000000", CultureInfo.InvariantCulture)).Append('\t');
                AppendEscaped(_line, direction);
                _line.Append('\t');
                AppendEscaped(_line, endpoint);
                _line.Append('\t');
                AppendEscaped(_line, message);
                _line.Append('\n');
                _writer.Write(_line);

                _lineCount++;
                long now = _clock.ElapsedTicks;
                if (_lineCount % _flushEveryLines == 0 ||
                    now - _lastFlushTicks > System.Diagnostics.Stopwatch.Frequency / 2)
                {
                    _writer.Flush();
                    _lastFlushTicks = now;
                }
            }
        }

        /// <summary>Flushes buffered lines to the operating system.</summary>
        public void Flush()
        {
            lock (_sync)
            {
                if (_writer != null)
                {
                    _writer.Flush();
                }
            }
        }

        /// <summary>Flushes and closes the file. Safe to call more than once and from any thread.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_writer == null)
                {
                    return;
                }

                try
                {
                    _writer.Flush();
                }
                finally
                {
                    _writer.Dispose();
                    _writer = null;
                    _clock.Stop();
                }
            }
        }

        private static void AppendEscaped(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return;
            }

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
        }
    }
}
