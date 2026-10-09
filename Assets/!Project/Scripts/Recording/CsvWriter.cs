using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FAMOT.Recording
{
    /// <summary>
    /// Minimal, allocation-light CSV writer for per-frame data. Numbers are always formatted with
    /// <see cref="CultureInfo.InvariantCulture"/> ('.' decimal separator), fields are separated by ',' and rows end
    /// with '\n'. Typical use per frame: <see cref="BeginRow"/>, several <c>Append</c> calls, <see cref="EndRow"/>.
    /// Not thread-safe; use from one thread (or lock externally).
    /// </summary>
    public sealed class CsvWriter : IDisposable
    {
        /// <summary>Size of the underlying StreamWriter buffer, in bytes/chars.</summary>
        public const int BufferSize = 64 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly char[] CharsNeedingQuotes = { ',', '"', '\n', '\r' };

        private readonly char[] _numberBuffer = new char[64];
        private readonly int _flushEveryRows;
        private readonly string[] _header;
        private StreamWriter _writer;
        private int _fieldIndex;
        private bool _rowOpen;

        /// <summary>
        /// Creates (overwrites) <paramref name="path"/> and writes the header row immediately.
        /// The parent directory is created if needed.
        /// </summary>
        /// <param name="path">Destination file.</param>
        /// <param name="header">Column names; null or empty for no header row.</param>
        /// <param name="flushEveryRows">Flush to disk every N data rows; 0 or less disables periodic flushing.</param>
        public CsvWriter(string path, IList<string> header, int flushEveryRows = 200)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            Path = path;
            _flushEveryRows = flushEveryRows;

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096);
            _writer = new StreamWriter(stream, Utf8NoBom, BufferSize);
            _writer.NewLine = "\n";

            if (header != null && header.Count > 0)
            {
                _header = new string[header.Count];
                for (int i = 0; i < header.Count; i++)
                {
                    _header[i] = header[i] ?? "";
                    if (i > 0)
                    {
                        _writer.Write(',');
                    }
                    _writer.Write(Escape(_header[i]));
                }
                _writer.Write('\n');
                _writer.Flush();
            }
            else
            {
                _header = new string[0];
            }
        }

        /// <summary>Absolute or relative path of the file being written.</summary>
        public string Path { get; }

        /// <summary>Number of data rows completed with <see cref="EndRow"/> (header not included).</summary>
        public long RowCount { get; private set; }

        /// <summary>Number of header columns (0 when no header was written).</summary>
        public int ColumnCount => _header.Length;

        /// <summary>Copy of the header column names.</summary>
        public string[] Header => (string[])_header.Clone();

        /// <summary>True after <see cref="Dispose"/>.</summary>
        public bool IsDisposed => _writer == null;

        /// <summary>Numeric format used by <see cref="Append(float)"/>. Default "0.####".</summary>
        public string FloatFormat { get; set; } = "0.####";

        /// <summary>Numeric format used by <see cref="Append(double)"/>. Default "0.######" (microsecond timestamps).</summary>
        public string DoubleFormat { get; set; } = "0.######";

        /// <summary>Returns {prefix_x, prefix_y, prefix_z}.</summary>
        /// <param name="prefix">Column name prefix.</param>
        public static string[] Vector3Columns(string prefix)
        {
            return new[] { prefix + "_x", prefix + "_y", prefix + "_z" };
        }

        /// <summary>Returns {prefix_x, prefix_y, prefix_z, prefix_w}.</summary>
        /// <param name="prefix">Column name prefix.</param>
        public static string[] QuaternionColumns(string prefix)
        {
            return new[] { prefix + "_x", prefix + "_y", prefix + "_z", prefix + "_w" };
        }

        /// <summary>
        /// Returns <paramref name="value"/> quoted per RFC 4180 if it contains a comma, quote or newline;
        /// otherwise returns it unchanged. Null becomes an empty string.
        /// </summary>
        /// <param name="value">Field text.</param>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            if (value.IndexOfAny(CharsNeedingQuotes) < 0)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Starts a new row. If the previous row was not ended, it is ended first so the file stays well-formed.
        /// Calling an <c>Append</c> method without <see cref="BeginRow"/> also starts a row implicitly.
        /// </summary>
        public void BeginRow()
        {
            EnsureOpen();
            if (_rowOpen)
            {
                EndRow();
            }
            _rowOpen = true;
            _fieldIndex = 0;
        }

        /// <summary>Appends a float field formatted with <see cref="FloatFormat"/> (invariant culture).</summary>
        /// <param name="v">Value.</param>
        public void Append(float v)
        {
            Separator();
            if (v.TryFormat(_numberBuffer, out int n, FloatFormat, CultureInfo.InvariantCulture))
            {
                _writer.Write(_numberBuffer, 0, n);
            }
            else
            {
                _writer.Write(v.ToString(FloatFormat, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Appends a double field formatted with <see cref="DoubleFormat"/> (invariant culture).</summary>
        /// <param name="v">Value.</param>
        public void Append(double v)
        {
            Separator();
            if (v.TryFormat(_numberBuffer, out int n, DoubleFormat, CultureInfo.InvariantCulture))
            {
                _writer.Write(_numberBuffer, 0, n);
            }
            else
            {
                _writer.Write(v.ToString(DoubleFormat, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Appends an integer field.</summary>
        /// <param name="v">Value.</param>
        public void Append(int v)
        {
            Separator();
            if (v.TryFormat(_numberBuffer, out int n, default, CultureInfo.InvariantCulture))
            {
                _writer.Write(_numberBuffer, 0, n);
            }
            else
            {
                _writer.Write(v.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Appends a 64-bit integer field.</summary>
        /// <param name="v">Value.</param>
        public void Append(long v)
        {
            Separator();
            if (v.TryFormat(_numberBuffer, out int n, default, CultureInfo.InvariantCulture))
            {
                _writer.Write(_numberBuffer, 0, n);
            }
            else
            {
                _writer.Write(v.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Appends a boolean field as 1 (true) or 0 (false).</summary>
        /// <param name="v">Value.</param>
        public void Append(bool v)
        {
            Separator();
            _writer.Write(v ? '1' : '0');
        }

        /// <summary>Appends a text field, quoting it if it contains a comma, quote or newline. Null writes an empty field.</summary>
        /// <param name="v">Value.</param>
        public void Append(string v)
        {
            Separator();
            if (string.IsNullOrEmpty(v))
            {
                return;
            }
            if (v.IndexOfAny(CharsNeedingQuotes) < 0)
            {
                _writer.Write(v);
            }
            else
            {
                _writer.Write(Escape(v));
            }
        }

        /// <summary>Appends three float fields: x, y, z.</summary>
        /// <param name="v">Value.</param>
        public void Append(Vector3 v)
        {
            Append(v.x);
            Append(v.y);
            Append(v.z);
        }

        /// <summary>Appends four float fields: x, y, z, w.</summary>
        /// <param name="q">Value.</param>
        public void Append(Quaternion q)
        {
            Append(q.x);
            Append(q.y);
            Append(q.z);
            Append(q.w);
        }

        /// <summary>Appends an empty field (e.g. for missing values).</summary>
        public void AppendEmpty()
        {
            Separator();
        }

        /// <summary>Appends <paramref name="count"/> empty fields.</summary>
        /// <param name="count">Number of empty fields.</param>
        public void AppendEmpty(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Separator();
            }
        }

        /// <summary>Terminates the current row with '\n', increments <see cref="RowCount"/> and flushes every N rows.</summary>
        public void EndRow()
        {
            EnsureOpen();
            _writer.Write('\n');
            _rowOpen = false;
            _fieldIndex = 0;
            RowCount++;
            if (_flushEveryRows > 0 && RowCount % _flushEveryRows == 0)
            {
                _writer.Flush();
            }
        }

        /// <summary>
        /// Convenience: writes a complete row from boxed values (allocates; avoid in per-frame code).
        /// Supports float, double, int, long, bool, string, Vector3, Quaternion, null (empty field) and any
        /// <see cref="IFormattable"/> (formatted with the invariant culture).
        /// </summary>
        /// <param name="values">Field values.</param>
        public void WriteRow(params object[] values)
        {
            BeginRow();
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    AppendObject(values[i]);
                }
            }
            EndRow();
        }

        /// <summary>Flushes buffered data to the operating system.</summary>
        public void Flush()
        {
            if (_writer != null)
            {
                _writer.Flush();
            }
        }

        /// <summary>Ends an open row, flushes and closes the file. Safe to call more than once.</summary>
        public void Dispose()
        {
            StreamWriter w = _writer;
            if (w == null)
            {
                return;
            }

            try
            {
                if (_rowOpen)
                {
                    EndRow();
                }
                w.Flush();
            }
            finally
            {
                _writer = null;
                w.Dispose();
            }
        }

        private void AppendObject(object o)
        {
            switch (o)
            {
                case null:
                    AppendEmpty();
                    break;
                case float f:
                    Append(f);
                    break;
                case double d:
                    Append(d);
                    break;
                case int i:
                    Append(i);
                    break;
                case long l:
                    Append(l);
                    break;
                case bool b:
                    Append(b);
                    break;
                case string s:
                    Append(s);
                    break;
                case Vector3 v:
                    Append(v);
                    break;
                case Quaternion q:
                    Append(q);
                    break;
                case IFormattable fmt:
                    Append(fmt.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    Append(o.ToString());
                    break;
            }
        }

        private void Separator()
        {
            EnsureOpen();
            if (!_rowOpen)
            {
                _rowOpen = true;
                _fieldIndex = 0;
            }
            if (_fieldIndex > 0)
            {
                _writer.Write(',');
            }
            _fieldIndex++;
        }

        private void EnsureOpen()
        {
            if (_writer == null)
            {
                throw new ObjectDisposedException(nameof(CsvWriter), "CSV file already closed: " + Path);
            }
        }
    }
}
