using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FAMOT.Recording;
using UnityEngine;

namespace FAMOT.Session
{
    /// <summary>
    /// Reads and writes the subject index (<c>subjects.csv</c>) and the per-subject <c>subject.json</c> files under
    /// <see cref="DataPaths.RootDirectory"/>. <c>subject.json</c> is authoritative; the CSV is a convenience index for
    /// humans and analysis scripts. Not intended for per-frame use.
    /// </summary>
    public static class SubjectRegistry
    {
        /// <summary>Column names of <c>subjects.csv</c>.</summary>
        public static readonly string[] IndexHeader =
        {
            "subject_id", "subject_name", "handedness", "created_utc", "last_session_utc", "session_count"
        };

        /// <summary>Characters used for generated ids (uppercase alphanumerics without 0/O/1/I).</summary>
        public const string IdAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Length of generated ids.</summary>
        public const int IdLength = 4;

        private static readonly Regex SubjectFolderRegex =
            new Regex("^([A-Z0-9]{4})_(.+)$", RegexOptions.CultureInvariant);

        private static readonly System.Random Rng = new System.Random();
        private static readonly object RngLock = new object();

        /// <summary>
        /// Loads all known subjects: every row of <c>subjects.csv</c> (using the subject's <c>subject.json</c> when
        /// present, otherwise the row itself), plus any <c>XXXX_Name</c> folder in the root that is missing from the
        /// index. Missing, malformed or duplicate rows are skipped. Sorted by subject id.
        /// </summary>
        public static List<SubjectInfo> LoadAll()
        {
            var result = new List<SubjectInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string[]> rows = ReadIndexRows();
            for (int r = 0; r < rows.Count; r++)
            {
                SubjectInfo fromRow = FromIndexRow(rows[r]);
                if (fromRow == null || seen.Contains(fromRow.subjectId))
                {
                    continue;
                }

                SubjectInfo s = fromRow;
                string jsonPath = DataPaths.SubjectJsonPath(fromRow);
                SubjectInfo fromJson = TryLoadJson(jsonPath);
                if (fromJson != null)
                {
                    if (string.IsNullOrEmpty(fromJson.subjectId))
                    {
                        fromJson.subjectId = fromRow.subjectId;
                    }
                    if (string.Equals(fromJson.subjectId, fromRow.subjectId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(fromJson.subjectName))
                        {
                            fromJson.subjectName = fromRow.subjectName;
                        }
                        s = fromJson;
                    }
                    else
                    {
                        Debug.LogWarning("[FAMOT] subject.json id mismatch in " + jsonPath + "; using index row.");
                    }
                }

                s.EnsureValid();
                seen.Add(s.subjectId);
                result.Add(s);
            }

            ScanUnindexedFolders(result, seen);

            result.Sort((a, b) => string.CompareOrdinal(a.subjectId, b.subjectId));
            return result;
        }

        /// <summary>Returns the subject with the given id (case-insensitive), or null.</summary>
        /// <param name="id">Subject id.</param>
        public static SubjectInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            List<SubjectInfo> all = LoadAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].subjectId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }
            return null;
        }

        /// <summary>True if a subject with the given id exists in the index or as a folder.</summary>
        /// <param name="id">Subject id.</param>
        public static bool Exists(string id)
        {
            return Find(id) != null;
        }

        /// <summary>
        /// Writes <c>subject.json</c> (creating the subject folder if needed) and inserts or updates the subject's row in
        /// <c>subjects.csv</c>. The index is rewritten atomically through a temporary file. Sets
        /// <see cref="SubjectInfo.createdUtc"/> if empty.
        /// </summary>
        /// <param name="s">Subject to save; <see cref="SubjectInfo.subjectId"/> must be set.</param>
        public static void Save(SubjectInfo s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }
            if (!IsValidId(s.subjectId))
            {
                throw new ArgumentException(
                    "SubjectInfo.subjectId must be 1-32 letters, digits, '-' or '_' (got '" + s.subjectId + "').", nameof(s));
            }

            s.EnsureValid();
            if (string.IsNullOrEmpty(s.createdUtc))
            {
                s.createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }

            string dir = DataPaths.SubjectDirectory(s);
            Directory.CreateDirectory(dir);
            DataPaths.WriteAllTextAtomic(DataPaths.Combine(dir, DataPaths.SubjectJson), JsonUtility.ToJson(s, true));

            List<string[]> rows = ReadIndexRows();
            string[] newRow = ToIndexRow(s);
            bool replaced = false;
            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length > 0 && string.Equals(row[0].Trim(), s.subjectId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!replaced)
                    {
                        rows[i] = newRow;
                        replaced = true;
                    }
                    else
                    {
                        rows.RemoveAt(i);
                        i--;
                    }
                }
            }
            if (!replaced)
            {
                rows.Add(newRow);
            }

            WriteIndex(rows);
        }

        /// <summary>
        /// Generates a random id of <see cref="IdLength"/> characters from <see cref="IdAlphabet"/> that is not contained
        /// in <paramref name="existingIds"/> (pass the ids of <see cref="LoadAll"/>).
        /// </summary>
        /// <param name="existingIds">Ids already in use; may be null.</param>
        public static string GenerateUniqueId(ICollection<string> existingIds)
        {
            var buffer = new char[IdLength];
            lock (RngLock)
            {
                for (int attempt = 0; attempt < 10000; attempt++)
                {
                    for (int i = 0; i < IdLength; i++)
                    {
                        buffer[i] = IdAlphabet[Rng.Next(IdAlphabet.Length)];
                    }
                    string id = new string(buffer);
                    if (!IsTaken(existingIds, id))
                    {
                        return id;
                    }
                }
            }

            // Extremely crowded id space: fall back to an exhaustive search.
            int total = 1;
            for (int i = 0; i < IdLength; i++)
            {
                total *= IdAlphabet.Length;
            }
            for (int n = 0; n < total; n++)
            {
                int v = n;
                for (int i = IdLength - 1; i >= 0; i--)
                {
                    buffer[i] = IdAlphabet[v % IdAlphabet.Length];
                    v /= IdAlphabet.Length;
                }
                string id = new string(buffer);
                if (!IsTaken(existingIds, id))
                {
                    return id;
                }
            }

            throw new InvalidOperationException("No free subject id left.");
        }

        /// <summary>
        /// Parses CSV text (RFC 4180: quoted fields may contain commas, doubled quotes and line breaks).
        /// Blank lines are skipped. Accepts '\n' and '\r\n' line endings.
        /// </summary>
        /// <param name="text">Whole file contents.</param>
        public static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text))
            {
                return rows;
            }

            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool fieldWasQuoted = false;
            int i = 0;
            if (text[0] == '﻿')
            {
                i = 1;
            }

            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        fieldWasQuoted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        fieldWasQuoted = false;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        FinishRow(rows, fields, field, fieldWasQuoted);
                        fieldWasQuoted = false;
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (!inQuotes)
            {
                FinishRow(rows, fields, field, fieldWasQuoted);
            }
            // else: unterminated quoted field at end of file (truncated write) - drop the partial row.
            return rows;
        }

        private static void FinishRow(List<string[]> rows, List<string> fields, StringBuilder field, bool fieldWasQuoted)
        {
            bool blank = fields.Count == 0 && field.Length == 0 && !fieldWasQuoted;
            if (!blank)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }
            fields.Clear();
            field.Clear();
        }

        private static bool IsTaken(ICollection<string> existingIds, string id)
        {
            if (existingIds == null)
            {
                return false;
            }
            if (existingIds.Contains(id))
            {
                return true;
            }
            foreach (string e in existingIds)
            {
                if (string.Equals(e, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Data rows of subjects.csv (header removed), or empty when the file is missing/unreadable.</summary>
        private static List<string[]> ReadIndexRows()
        {
            string path = DataPaths.SubjectsIndexPath;
            if (!File.Exists(path))
            {
                return new List<string[]>();
            }

            List<string[]> rows;
            try
            {
                rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FAMOT] Could not read " + path + ": " + e.Message);
                return new List<string[]>();
            }

            if (rows.Count > 0 && rows[0].Length > 0 &&
                string.Equals(rows[0][0].Trim(), IndexHeader[0], StringComparison.OrdinalIgnoreCase))
            {
                rows.RemoveAt(0);
            }
            return rows;
        }

        private static void WriteIndex(List<string[]> rows)
        {
            var sb = new StringBuilder(256 + rows.Count * 96);
            AppendCsvLine(sb, IndexHeader);
            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row == null || row.Length == 0 || string.IsNullOrEmpty(row[0].Trim()))
                {
                    continue;
                }

                var normalized = new string[IndexHeader.Length];
                for (int c = 0; c < normalized.Length; c++)
                {
                    normalized[c] = c < row.Length ? row[c] : "";
                }
                AppendCsvLine(sb, normalized);
            }

            DataPaths.WriteAllTextAtomic(DataPaths.SubjectsIndexPath, sb.ToString());
        }

        private static void AppendCsvLine(StringBuilder sb, string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(CsvWriter.Escape(fields[i]));
            }
            sb.Append('\n');
        }

        private static string[] ToIndexRow(SubjectInfo s)
        {
            return new[]
            {
                s.subjectId,
                s.subjectName ?? "",
                s.handedness ?? "",
                s.createdUtc ?? "",
                s.lastSessionUtc ?? "",
                s.sessionCount.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SubjectInfo FromIndexRow(string[] row)
        {
            if (row == null || row.Length == 0)
            {
                return null;
            }

            string id = row[0].Trim();
            if (!IsValidId(id))
            {
                return null;
            }

            var s = new SubjectInfo
            {
                subjectId = id,
                subjectName = Get(row, 1),
                handedness = Get(row, 2),
                createdUtc = Get(row, 3),
                lastSessionUtc = Get(row, 4)
            };

            if (int.TryParse(Get(row, 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                s.sessionCount = count;
            }

            s.EnsureValid();
            return s;
        }

        /// <summary>Ids read from the index must be 1-32 chars of letters, digits, '-' or '_'.</summary>
        private static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32)
            {
                return false;
            }
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Get(string[] row, int index)
        {
            return index < row.Length && row[index] != null ? row[index].Trim() : "";
        }

        private static SubjectInfo TryLoadJson(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }
                SubjectInfo s = JsonUtility.FromJson<SubjectInfo>(json);
                if (s != null)
                {
                    s.EnsureValid();
                }
                return s;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FAMOT] Corrupt subject file " + path + ": " + e.Message);
                return null;
            }
        }

        private static void ScanUnindexedFolders(List<SubjectInfo> result, HashSet<string> seen)
        {
            string root = DataPaths.RootDirectory;
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(root);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FAMOT] Could not scan " + root + ": " + e.Message);
                return;
            }

            for (int i = 0; i < dirs.Length; i++)
            {
                string folder = Path.GetFileName(dirs[i]);
                Match m = SubjectFolderRegex.Match(folder);
                if (!m.Success)
                {
                    continue;
                }

                string id = m.Groups[1].Value;
                if (seen.Contains(id))
                {
                    continue;
                }

                string dir = DataPaths.Combine(root, folder);
                SubjectInfo s = TryLoadJson(DataPaths.Combine(dir, DataPaths.SubjectJson));
                if (s == null || !string.Equals(s.subjectId, id, StringComparison.OrdinalIgnoreCase))
                {
                    if (s != null)
                    {
                        Debug.LogWarning("[FAMOT] subject.json in " + dir + " has a different id; using folder name.");
                    }

                    s = new SubjectInfo
                    {
                        subjectId = id,
                        subjectName = m.Groups[2].Value.Replace('_', ' ')
                    };
                    try
                    {
                        s.createdUtc = Directory.GetCreationTimeUtc(dir).ToString("o", CultureInfo.InvariantCulture);
                    }
                    catch (Exception)
                    {
                        s.createdUtc = "";
                    }
                }

                s.EnsureValid();
                seen.Add(s.subjectId);
                result.Add(s);
            }
        }
    }
}
