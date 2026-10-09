using System;
using System.Globalization;
using System.IO;
using System.Threading;
using FAMOT.Recording;
using NUnit.Framework;
using UnityEngine;

namespace FAMOT.Tests
{
    /// <summary>Edit-mode tests for <see cref="CsvWriter"/>.</summary>
    public class CsvWriterTests
    {
        private string _dir;

        /// <summary>Creates a fresh temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FAMOT_CsvTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup of the temp folder.
            }
        }

        private string[] ReadLines(string path)
        {
            string text = File.ReadAllText(path);
            return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The header is written on construction, even with no rows.</summary>
        [Test]
        public void Header_IsWritten()
        {
            string path = Path.Combine(_dir, "h.csv");
            using (new CsvWriter(path, new[] { "a", "b", "c" }))
            {
            }

            string[] lines = ReadLines(path);
            Assert.AreEqual(1, lines.Length);
            Assert.AreEqual("a,b,c", lines[0]);
        }

        /// <summary>No UTF-8 byte order mark is written.</summary>
        [Test]
        public void File_HasNoBom()
        {
            string path = Path.Combine(_dir, "bom.csv");
            using (new CsvWriter(path, new[] { "x" }))
            {
            }

            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual((byte)'x', bytes[0]);
        }

        /// <summary>Fields containing commas, quotes or newlines are quoted per RFC 4180.</summary>
        [Test]
        public void Strings_WithCommasAndQuotes_AreQuoted()
        {
            string path = Path.Combine(_dir, "q.csv");
            using (var w = new CsvWriter(path, new[] { "s1", "s2", "s3" }))
            {
                w.BeginRow();
                w.Append("a,b");
                w.Append("say \"hi\"");
                w.Append("plain");
                w.EndRow();
            }

            string[] lines = ReadLines(path);
            Assert.AreEqual("\"a,b\",\"say \"\"hi\"\"\",plain", lines[1]);
        }

        /// <summary>Numbers use '.' as decimal separator even when the current culture uses ','.</summary>
        [Test]
        public void Numbers_UseInvariantCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string path = Path.Combine(_dir, "c.csv");
                using (var w = new CsvWriter(path, new[] { "f", "d", "v_x", "v_y", "v_z" }))
                {
                    w.BeginRow();
                    w.Append(1.5f);
                    w.Append(2.25);
                    w.Append(new Vector3(0.5f, -1.25f, 3f));
                    w.EndRow();
                }

                string[] lines = ReadLines(path);
                Assert.AreEqual("1.5,2.25,0.5,-1.25,3", lines[1]);
                StringAssert.DoesNotContain("1,5", lines[1]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        /// <summary>Integers, booleans, quaternions and empty fields are formatted as documented.</summary>
        [Test]
        public void MixedTypes_AreFormatted()
        {
            string path = Path.Combine(_dir, "m.csv");
            using (var w = new CsvWriter(path, null))
            {
                w.BeginRow();
                w.Append(42);
                w.Append(1234567890123L);
                w.Append(true);
                w.Append(false);
                w.AppendEmpty();
                w.Append(Quaternion.identity);
                w.EndRow();
            }

            string[] lines = ReadLines(path);
            Assert.AreEqual(1, lines.Length);
            Assert.AreEqual("42,1234567890123,1,0,,0,0,0,1", lines[0]);
        }

        /// <summary>RowCount counts data rows only, and every row ends up in the file.</summary>
        [Test]
        public void RowCount_CountsDataRows()
        {
            string path = Path.Combine(_dir, "r.csv");
            using (var w = new CsvWriter(path, new[] { "i" }))
            {
                for (int i = 0; i < 7; i++)
                {
                    w.BeginRow();
                    w.Append(i);
                    w.EndRow();
                }
                w.WriteRow(7);
                Assert.AreEqual(8, w.RowCount);
            }

            Assert.AreEqual(9, ReadLines(path).Length);
        }

        /// <summary>Buffered rows are written when the writer is disposed, even if periodic flushing never triggered.</summary>
        [Test]
        public void Dispose_FlushesBufferedRows()
        {
            string path = Path.Combine(_dir, "f.csv");
            var w = new CsvWriter(path, new[] { "a", "b" }, 100000);
            for (int i = 0; i < 50; i++)
            {
                w.WriteRow(i, i * 0.5f);
            }
            w.Dispose();

            string[] lines = ReadLines(path);
            Assert.AreEqual(51, lines.Length);
            Assert.AreEqual("49,24.5", lines[50]);
        }

        /// <summary>Disposing twice is harmless; writing after dispose throws.</summary>
        [Test]
        public void Dispose_Twice_IsSafe()
        {
            string path = Path.Combine(_dir, "d.csv");
            var w = new CsvWriter(path, new[] { "a" });
            w.Dispose();
            Assert.DoesNotThrow(() => w.Dispose());
            Assert.IsTrue(w.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => w.Append(1));
        }

        /// <summary>Column helpers produce the documented suffixes.</summary>
        [Test]
        public void ColumnHelpers_ProduceSuffixes()
        {
            CollectionAssert.AreEqual(new[] { "p_x", "p_y", "p_z" }, CsvWriter.Vector3Columns("p"));
            CollectionAssert.AreEqual(new[] { "q_x", "q_y", "q_z", "q_w" }, CsvWriter.QuaternionColumns("q"));
        }
    }
}
