using System;
using System.Globalization;
using UnityEngine;

namespace FAMOT.Recording
{
    /// <summary>
    /// Writes discrete, rare events (calibration done, trial start/stop, UI actions...) to <c>events.csv</c> with columns
    /// <c>unity_time,utc,frame,category,name,value,detail</c>. Every event is flushed immediately so nothing is lost
    /// on a crash. <see cref="Log"/> reads <see cref="Time"/> and must therefore be called from the Unity main thread.
    /// </summary>
    public sealed class EventLogger : IDisposable
    {
        /// <summary>Column names of the events file.</summary>
        public static readonly string[] Header = { "unity_time", "utc", "frame", "category", "name", "value", "detail" };

        private readonly object _sync = new object();
        private CsvWriter _csv;

        /// <summary>Creates (overwrites) the events file at <paramref name="path"/> and writes the header.</summary>
        /// <param name="path">Destination file, typically <c>{session}/events.csv</c>.</param>
        public EventLogger(string path)
        {
            Path = path;
            _csv = new CsvWriter(path, Header, 0);
        }

        /// <summary>Path of the events file.</summary>
        public string Path { get; }

        /// <summary>Number of events written.</summary>
        public long Count
        {
            get
            {
                lock (_sync)
                {
                    return _csv != null ? _csv.RowCount : 0;
                }
            }
        }

        /// <summary>True after <see cref="Dispose"/>.</summary>
        public bool IsDisposed
        {
            get
            {
                lock (_sync)
                {
                    return _csv == null;
                }
            }
        }

        /// <summary>
        /// Records one event with the current <c>Time.realtimeSinceStartupAsDouble</c>, UTC time ("o") and
        /// <c>Time.frameCount</c>, then flushes. NaN values are written as an empty field.
        /// Calls after <see cref="Dispose"/> are ignored.
        /// </summary>
        /// <param name="category">Event group, e.g. "Session", "Calibration", "Trial".</param>
        /// <param name="name">Event name, e.g. "Start".</param>
        /// <param name="value">Optional numeric payload; NaN for none.</param>
        /// <param name="detail">Optional free text (quoted automatically when needed).</param>
        public void Log(string category, string name, double value = double.NaN, string detail = "")
        {
            double unityTime = Time.realtimeSinceStartupAsDouble;
            int frame = Time.frameCount;
            string utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            lock (_sync)
            {
                if (_csv == null)
                {
                    return;
                }

                _csv.BeginRow();
                _csv.Append(unityTime);
                _csv.Append(utc);
                _csv.Append(frame);
                _csv.Append(category);
                _csv.Append(name);
                if (double.IsNaN(value))
                {
                    _csv.AppendEmpty();
                }
                else
                {
                    _csv.Append(value);
                }
                _csv.Append(detail);
                _csv.EndRow();
                _csv.Flush();
            }
        }

        /// <summary>Flushes and closes the file. Safe to call more than once.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_csv == null)
                {
                    return;
                }
                _csv.Dispose();
                _csv = null;
            }
        }
    }
}
