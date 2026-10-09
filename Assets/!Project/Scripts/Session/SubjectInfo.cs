using System;
using System.Globalization;
using FAMOT.Core;

namespace FAMOT.Session
{
    /// <summary>
    /// Persistent description of one study participant. Stored as <c>subject.json</c> in the subject folder
    /// and summarized in <c>subjects.csv</c> (see <see cref="SubjectRegistry"/>).
    /// Flat [Serializable] class with public fields so that <c>UnityEngine.JsonUtility</c> can round-trip it.
    /// </summary>
    [Serializable]
    public class SubjectInfo
    {
        /// <summary>Short unique identifier, normally 4 uppercase alphanumeric characters (e.g. "K7QM").</summary>
        public string subjectId = "";

        /// <summary>Display name of the subject (free text; sanitized for folder names).</summary>
        public string subjectName = "";

        /// <summary>Dominant hand: "Left" or "Right".</summary>
        public string handedness = "Right";

        /// <summary>Age in years, or -1 when unknown.</summary>
        public int age = -1;

        /// <summary>"", "Male", "Female", "Other" or "PreferNotToSay".</summary>
        public string sex = "";

        /// <summary>Clinically affected side: "None", "Left", "Right" or "Both".</summary>
        public string affectedSide = "None";

        /// <summary>Free-text notes.</summary>
        public string notes = "";

        /// <summary>Creation time, UTC, ISO 8601 round-trip format ("o").</summary>
        public string createdUtc = "";

        /// <summary>Start time of the most recent session, UTC, ISO 8601 ("o"); empty if none.</summary>
        public string lastSessionUtc = "";

        /// <summary>Number of sessions recorded for this subject.</summary>
        public int sessionCount;

        /// <summary>Per-subject range-of-motion limits.</summary>
        public JointLimits jointLimits = new JointLimits();

        /// <summary>Canonical folder name: <c>{subjectId}_{sanitizedName}</c>. Not serialized.</summary>
        public string FolderName => subjectId + "_" + DataPaths.Sanitize(subjectName);

        /// <summary>Creates a new subject with the given id and name and <see cref="createdUtc"/> set to now.</summary>
        /// <param name="id">Subject id (see <see cref="SubjectRegistry.GenerateUniqueId"/>).</param>
        /// <param name="name">Display name.</param>
        public static SubjectInfo Create(string id, string name)
        {
            return new SubjectInfo
            {
                subjectId = id ?? "",
                subjectName = name ?? "",
                createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>Dominant hand as an <see cref="ArmSide"/>; anything other than "Left"/"L" maps to Right.</summary>
        public ArmSide HandednessSide()
        {
            return ParseSide(handedness, ArmSide.Right);
        }

        /// <summary>
        /// Parses "Left"/"L"/"Right"/"R" (case-insensitive) into an <see cref="ArmSide"/>.
        /// </summary>
        /// <param name="value">Text to parse.</param>
        /// <param name="fallback">Value returned when <paramref name="value"/> is not recognized.</param>
        public static ArmSide ParseSide(string value, ArmSide fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            string v = value.Trim();
            if (string.Equals(v, "Left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "L", StringComparison.OrdinalIgnoreCase))
            {
                return ArmSide.Left;
            }

            if (string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "R", StringComparison.OrdinalIgnoreCase))
            {
                return ArmSide.Right;
            }

            return fallback;
        }

        /// <summary>Fills null fields with defaults (useful after JSON deserialization of old files).</summary>
        public void EnsureValid()
        {
            subjectId = subjectId ?? "";
            subjectName = subjectName ?? "";
            handedness = string.IsNullOrEmpty(handedness) ? "Right" : handedness;
            sex = sex ?? "";
            affectedSide = string.IsNullOrEmpty(affectedSide) ? "None" : affectedSide;
            notes = notes ?? "";
            createdUtc = createdUtc ?? "";
            lastSessionUtc = lastSessionUtc ?? "";
            if (sessionCount < 0)
            {
                sessionCount = 0;
            }
            if (jointLimits == null)
            {
                jointLimits = new JointLimits();
            }
            jointLimits.EnsureValid();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return subjectId + " " + subjectName;
        }
    }
}
