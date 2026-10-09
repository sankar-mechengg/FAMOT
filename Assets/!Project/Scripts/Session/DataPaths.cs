using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FAMOT.Session
{
    /// <summary>
    /// Central place for all on-disk locations used by FAMOT data recording.
    /// Layout: <c>{RootDirectory}/subjects.csv</c>, <c>{RootDirectory}/{id}_{name}/subject.json</c>,
    /// <c>{RootDirectory}/{id}_{name}/{yyyy-MM-dd_HHmmss}_{mode}_{hand}/*.csv|json|log</c>.
    /// All paths use forward slashes. Must be first accessed from the Unity main thread.
    /// </summary>
    public static class DataPaths
    {
        /// <summary>Name of the data root folder.</summary>
        public const string RootFolderName = "FAMOT_Data";

        /// <summary>Subject index file name.</summary>
        public const string SubjectsCsv = "subjects.csv";

        /// <summary>Per-subject metadata file name.</summary>
        public const string SubjectJson = "subject.json";

        /// <summary>Per-session metadata file name.</summary>
        public const string SessionJson = "session.json";

        /// <summary>Calibration log file name.</summary>
        public const string CalibrationCsv = "calibration.csv";

        /// <summary>Joint angle time series file name.</summary>
        public const string KinematicsCsv = "kinematics.csv";

        /// <summary>Raw tracking (head/controllers/hands) time series file name.</summary>
        public const string TrackingCsv = "tracking.csv";

        /// <summary>Articulated hand joint time series file name.</summary>
        public const string HandJointsCsv = "hand_joints.csv";

        /// <summary>Discrete event log file name.</summary>
        public const string EventsCsv = "events.csv";

        /// <summary>Raw UDP traffic log file name.</summary>
        public const string UdpRawLog = "udp_raw.log";

        /// <summary>Maximum length of a sanitized name.</summary>
        public const int MaxSanitizedLength = 40;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object Sync = new object();
        private static string _cachedRoot;
        private static string _overrideRoot;

        /// <summary>
        /// Absolute path of the data root. <c>StreamingAssets/FAMOT_Data</c> in the Editor and on Windows/macOS/Linux
        /// standalone builds; <c>persistentDataPath/FAMOT_Data</c> on Android (Quest), iOS, WebGL and any other platform,
        /// or when the StreamingAssets location turns out not to be writable. Resolved once and cached; the directory
        /// is created if needed. <see cref="OverrideRootDirectory"/> takes precedence (used by tests).
        /// </summary>
        public static string RootDirectory
        {
            get
            {
                lock (Sync)
                {
                    if (_overrideRoot != null)
                    {
                        Directory.CreateDirectory(_overrideRoot);
                        return _overrideRoot;
                    }

                    if (_cachedRoot == null)
                    {
                        _cachedRoot = ResolveRoot();
                    }
                    return _cachedRoot;
                }
            }
        }

        /// <summary>True when <see cref="RootDirectory"/> currently comes from <see cref="OverrideRootDirectory"/>.</summary>
        public static bool IsOverridden
        {
            get
            {
                lock (Sync)
                {
                    return _overrideRoot != null;
                }
            }
        }

        /// <summary>Absolute path of <c>subjects.csv</c>.</summary>
        public static string SubjectsIndexPath => Combine(RootDirectory, SubjectsCsv);

        /// <summary>
        /// Redirects <see cref="RootDirectory"/> to <paramref name="path"/> (created if missing) until
        /// <see cref="ClearOverride"/> is called. Intended for tests and tools.
        /// </summary>
        /// <param name="path">Directory to use as the data root.</param>
        public static void OverrideRootDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Override path must not be empty.", nameof(path));
            }

            string full = Normalize(Path.GetFullPath(path));
            Directory.CreateDirectory(full);
            lock (Sync)
            {
                _overrideRoot = full;
            }
        }

        /// <summary>Removes a root override set by <see cref="OverrideRootDirectory"/>.</summary>
        public static void ClearOverride()
        {
            lock (Sync)
            {
                _overrideRoot = null;
            }
        }

        /// <summary>
        /// Folder of a subject. Returns the canonical <c>{RootDirectory}/{FolderName}</c> if it exists; otherwise an
        /// existing folder starting with <c>{subjectId}_</c> (the subject was renamed after the folder was created,
        /// so data stays in one place); otherwise the canonical path. Does not create the directory.
        /// </summary>
        /// <param name="s">Subject.</param>
        public static string SubjectDirectory(SubjectInfo s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            string root = RootDirectory;
            string canonical = Combine(root, s.FolderName);
            if (Directory.Exists(canonical) || string.IsNullOrEmpty(s.subjectId))
            {
                return canonical;
            }

            string existing = FindExistingSubjectDirectory(s.subjectId);
            return existing ?? canonical;
        }

        /// <summary>
        /// Returns an existing folder in the root named <c>{subjectId}_*</c>, or null if none exists.
        /// </summary>
        /// <param name="subjectId">Subject id.</param>
        public static string FindExistingSubjectDirectory(string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId))
            {
                return null;
            }

            string root = RootDirectory;
            try
            {
                string prefix = subjectId + "_";
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string name = Path.GetFileName(dirs[i]);
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return Combine(root, name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FAMOT] Could not scan data root: " + e.Message);
            }
            return null;
        }

        /// <summary>Absolute path of the subject's <c>subject.json</c>.</summary>
        /// <param name="s">Subject.</param>
        public static string SubjectJsonPath(SubjectInfo s)
        {
            return Combine(SubjectDirectory(s), SubjectJson);
        }

        /// <summary>
        /// Creates a new, unique session folder inside the subject folder, named
        /// <c>yyyy-MM-dd_HHmmss_{mode}_{hand}</c> (local time). If it already exists, <c>_2</c>, <c>_3</c>... is appended.
        /// </summary>
        /// <param name="s">Subject owning the session.</param>
        /// <param name="mode">"Desktop" or "VR".</param>
        /// <param name="hand">"L" or "R".</param>
        /// <param name="sessionId">The created folder name.</param>
        /// <returns>Absolute path of the created folder.</returns>
        public static string CreateSessionDirectory(SubjectInfo s, string mode, string hand, out string sessionId)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            string subjectDir = SubjectDirectory(s);
            Directory.CreateDirectory(subjectDir);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
            string baseName = stamp + "_" + SanitizeCore(mode, "Mode") + "_" + SanitizeCore(hand, "X");
            string name = baseName;
            string path = Combine(subjectDir, name);
            int suffix = 2;
            while (Directory.Exists(path) || File.Exists(path))
            {
                name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                path = Combine(subjectDir, name);
                suffix++;
            }

            Directory.CreateDirectory(path);
            sessionId = name;
            return path;
        }

        /// <summary>
        /// Makes a string safe for use as a folder/file name on all target platforms: characters that are invalid in
        /// file names, whitespace and punctuation other than '-', '_' and '.' become '_' (runs collapsed), leading and
        /// trailing '_'/'.' are trimmed, the result is limited to 40 characters, and "Subject" is returned if empty.
        /// Unicode letters and digits are kept.
        /// </summary>
        /// <param name="name">Arbitrary text, may be null.</param>
        public static string Sanitize(string name)
        {
            return SanitizeCore(name, "Subject");
        }

        /// <summary>
        /// Writes <paramref name="contents"/> (UTF-8, no BOM) to a temporary file next to <paramref name="path"/> and then
        /// replaces the target, so readers never observe a half-written file.
        /// </summary>
        /// <param name="path">Destination file.</param>
        /// <param name="contents">Text to write.</param>
        public static void WriteAllTextAtomic(string path, string contents)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents ?? "", Utf8NoBom);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (Exception)
                {
                    // File.Replace is not supported on every platform/file system; fall back to delete + move.
                }

                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>Joins two path segments with a forward slash.</summary>
        /// <param name="a">Left segment.</param>
        /// <param name="b">Right segment.</param>
        public static string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return b;
            }
            if (string.IsNullOrEmpty(b))
            {
                return a;
            }
            a = a.TrimEnd('/', '\\');
            b = b.TrimStart('/', '\\');
            return a + "/" + b;
        }

        private static string ResolveRoot()
        {
            string persistent = Combine(Normalize(Application.persistentDataPath), RootFolderName);

            if (IsStreamingAssetsWritablePlatform(Application.platform))
            {
                string streaming = Combine(Normalize(Application.streamingAssetsPath), RootFolderName);
                try
                {
                    Directory.CreateDirectory(streaming);
                    string probe = Combine(streaming, ".write_test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return streaming;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[FAMOT] StreamingAssets is not writable (" + e.Message +
                                     "); using persistentDataPath instead.");
                }
            }

            Directory.CreateDirectory(persistent);
            return persistent;
        }

        private static bool IsStreamingAssetsWritablePlatform(RuntimePlatform p)
        {
            switch (p)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.LinuxPlayer:
                    return true;
                default:
                    // Android (Quest), iOS, WebGL and everything else: StreamingAssets is read-only or packed.
                    return false;
            }
        }

        private static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        private static string SanitizeCore(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            bool lastUnderscore = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool keep = (char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_') &&
                            Array.IndexOf(invalid, c) < 0;
                char outChar = keep ? c : '_';
                if (outChar == '_')
                {
                    if (lastUnderscore)
                    {
                        continue;
                    }
                    lastUnderscore = true;
                }
                else
                {
                    lastUnderscore = false;
                }
                sb.Append(outChar);
            }

            string r = sb.ToString().Trim('_', '.', ' ');
            if (r.Length > MaxSanitizedLength)
            {
                r = r.Substring(0, MaxSanitizedLength).TrimEnd('_', '.');
            }
            return r.Length == 0 ? fallback : r;
        }
    }
}
