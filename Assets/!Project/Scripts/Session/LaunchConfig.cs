using FAMOT.Avatar;
using FAMOT.Core;

namespace FAMOT.Session
{
    /// <summary>Visualisation mode chosen on the login screen.</summary>
    public enum VisualisationMode
    {
        Desktop = 0,
        VR = 1
    }

    /// <summary>
    /// Choices made on the login screen, handed to the experiment scene. Static so it survives scene loads
    /// without a persistent object; the experiment scene falls back to editor-friendly defaults when it is
    /// opened directly.
    /// </summary>
    public static class LaunchConfig
    {
        public static bool IsSet { get; private set; }
        public static SubjectInfo Subject { get; private set; }
        public static VisualisationMode Mode { get; private set; } = VisualisationMode.Desktop;
        public static ArmSide Hand { get; private set; } = ArmSide.Right;
        public static ArmInputSource LiveArmSource { get; private set; } = ArmInputSource.ExternalAngles;

        public static void Set(SubjectInfo subject, VisualisationMode mode, ArmSide hand, ArmInputSource source)
        {
            Subject = subject;
            Mode = mode;
            Hand = hand;
            LiveArmSource = source;
            IsSet = true;
        }

        public static void Clear()
        {
            IsSet = false;
            Subject = null;
        }

        public static string ModeName => Mode == VisualisationMode.VR ? "VR" : "Desktop";
    }
}
