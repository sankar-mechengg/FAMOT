using System;
using System.Globalization;
using FAMOT.Core;
using UnityEngine;
using UnityEngine.XR;

namespace FAMOT.Session
{
    /// <summary>
    /// Owns the current subject and the current recording session. One instance lives for the whole application
    /// (DontDestroyOnLoad); get it with <see cref="Ensure"/>. Recorders subscribe to <see cref="SessionStarted"/> /
    /// <see cref="SessionEnded"/> and open/close their files in <see cref="SessionDirectory"/> (see <see cref="FilePath"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        /// <summary>The live instance, or null if none has been created yet.</summary>
        public static SessionManager Instance { get; private set; }

        /// <summary>Raised after a session folder and <c>session.json</c> have been created.</summary>
        public event Action<SessionInfo> SessionStarted;

        /// <summary>
        /// Raised when a session ends, after <c>endUtc</c> is written but before <see cref="SessionDirectory"/> is cleared,
        /// so handlers can still flush and close their files.
        /// </summary>
        public event Action<SessionInfo> SessionEnded;

        /// <summary>Subject selected for the next/current session, or null.</summary>
        public SubjectInfo CurrentSubject { get; private set; }

        /// <summary>The running session, or null when none is active.</summary>
        public SessionInfo CurrentSession { get; private set; }

        /// <summary>The most recently ended session, or null.</summary>
        public SessionInfo LastSession { get; private set; }

        /// <summary>Absolute path of the running session folder, or null when none is active.</summary>
        public string SessionDirectory { get; private set; }

        /// <summary>True while a session is running.</summary>
        public bool HasActiveSession => CurrentSession != null && SessionDirectory != null;

        /// <summary>
        /// Returns the existing instance or creates one on a hidden GameObject (DontDestroyOnLoad while playing).
        /// </summary>
        public static SessionManager Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("[FAMOT SessionManager]");
            go.hideFlags = HideFlags.HideInHierarchy;
            SessionManager m = go.AddComponent<SessionManager>();
            if (Instance == null)
            {
                // Awake does not run for AddComponent in edit mode.
                m.RegisterAsInstance();
            }
            return Instance;
        }

        /// <summary>
        /// Path of <paramref name="fileName"/> inside the running session folder, or null when no session is active.
        /// </summary>
        /// <param name="fileName">File name, e.g. <see cref="DataPaths.KinematicsCsv"/>.</param>
        public static string FilePath(string fileName)
        {
            SessionManager m = Instance;
            if (m == null || !m.HasActiveSession || string.IsNullOrEmpty(fileName))
            {
                return null;
            }
            return m.SessionDirectory + "/" + fileName;
        }

        /// <summary>
        /// Selects the subject for subsequent sessions. If a session for a different subject is running, it is ended first.
        /// </summary>
        /// <param name="s">Subject, or null to clear.</param>
        public void SetSubject(SubjectInfo s)
        {
            if (HasActiveSession && (s == null || !string.Equals(s.subjectId, CurrentSession.subjectId, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogWarning("[FAMOT] Subject changed during an active session; ending session " + CurrentSession.sessionId);
                EndSession();
            }

            if (s != null)
            {
                s.EnsureValid();
            }
            CurrentSubject = s;
        }

        /// <summary>
        /// Ends any running session, creates a new session folder for <see cref="CurrentSubject"/>, fills
        /// <see cref="SessionInfo"/> from Application/SystemInfo/XR settings, writes <c>session.json</c>, updates the
        /// subject's <c>sessionCount</c>/<c>lastSessionUtc</c> and raises <see cref="SessionStarted"/>.
        /// UDP fields, calibration records and column lists can be filled afterwards; call <see cref="UpdateSessionJson"/>.
        /// </summary>
        /// <param name="mode">"Desktop" or "VR".</param>
        /// <param name="hand">"L" or "R"; null/empty uses the subject's handedness.</param>
        /// <param name="inputSource">Description of the input source (e.g. "UDP", "XRHands").</param>
        /// <exception cref="InvalidOperationException">No subject selected.</exception>
        public SessionInfo StartSession(string mode, string hand, string inputSource)
        {
            if (CurrentSubject == null)
            {
                throw new InvalidOperationException("SessionManager.StartSession: no subject selected (call SetSubject first).");
            }

            if (HasActiveSession)
            {
                EndSession();
            }

            SubjectInfo subject = CurrentSubject;
            if (string.IsNullOrEmpty(hand))
            {
                hand = subject.HandednessSide().ShortName();
            }

            string dir = DataPaths.CreateSessionDirectory(subject, mode, hand, out string sessionId);
            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            var info = new SessionInfo
            {
                sessionId = sessionId,
                subjectId = subject.subjectId,
                subjectName = subject.subjectName,
                mode = mode ?? "",
                hand = hand,
                startUtc = now,
                endUtc = "",
                unityVersion = Application.unityVersion,
                appVersion = Application.version,
                platform = Application.platform.ToString(),
                deviceModel = SafeDeviceModel(),
                inputSource = inputSource ?? "",
                targetFrameRate = Application.targetFrameRate
            };
            FillXrInfo(info);

            CurrentSession = info;
            SessionDirectory = dir;
            UpdateSessionJson();

            subject.sessionCount++;
            subject.lastSessionUtc = now;
            try
            {
                SubjectRegistry.Save(subject);
            }
            catch (Exception e)
            {
                Debug.LogError("[FAMOT] Could not update subject " + subject.subjectId + ": " + e);
            }

            Debug.Log("[FAMOT] Session started: " + dir);
            SessionStarted?.Invoke(info);
            return info;
        }

        /// <summary>Rewrites <c>session.json</c> from <see cref="CurrentSession"/>. No-op when no session is active.</summary>
        public void UpdateSessionJson()
        {
            if (!HasActiveSession)
            {
                return;
            }

            try
            {
                string path = DataPaths.Combine(SessionDirectory, DataPaths.SessionJson);
                DataPaths.WriteAllTextAtomic(path, JsonUtility.ToJson(CurrentSession, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[FAMOT] Could not write session.json: " + e);
            }
        }

        /// <summary>
        /// Sets <c>endUtc</c>, writes <c>session.json</c>, raises <see cref="SessionEnded"/> and clears the active session.
        /// No-op when no session is active.
        /// </summary>
        public void EndSession()
        {
            if (!HasActiveSession)
            {
                return;
            }

            SessionInfo info = CurrentSession;
            info.endUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            UpdateSessionJson();

            try
            {
                SessionEnded?.Invoke(info);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            Debug.Log("[FAMOT] Session ended: " + SessionDirectory);
            LastSession = info;
            CurrentSession = null;
            SessionDirectory = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            RegisterAsInstance();
        }

        private void OnApplicationQuit()
        {
            EndSession();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                EndSession();
                Instance = null;
            }
        }

        private void RegisterAsInstance()
        {
            Instance = this;
            if (Application.isPlaying && transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode Options" with domain reload disabled.
            Instance = null;
        }

        private static string SafeDeviceModel()
        {
            try
            {
                return SystemInfo.deviceModel;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static void FillXrInfo(SessionInfo info)
        {
            try
            {
                bool active = XRSettings.isDeviceActive;
                string loaded = XRSettings.loadedDeviceName;
                info.xrRuntimeName = active && !string.IsNullOrEmpty(loaded) ? loaded : "None";
            }
            catch (Exception)
            {
                info.xrRuntimeName = "Unknown";
            }

            try
            {
                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                info.xrDeviceName = head.isValid ? head.name : "";
            }
            catch (Exception)
            {
                info.xrDeviceName = "";
            }
        }
    }
}
