using System.Collections.Generic;
using System.Globalization;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Session;
using TMPro;
using UnityEngine;
using JointLimits = FAMOT.Session.JointLimits;
using UnityEngine.SceneManagement;

namespace FAMOT.UI
{
    /// <summary>
    /// Login screen: pick an existing subject or create a new one, choose hand, visualisation mode and
    /// input source, adjust joint limits and optional metadata, then launch the experiment scene.
    /// Replaces the original EnterFamot script.
    /// </summary>
    public class LoginController : MonoBehaviour
    {
        [Header("Original fields")]
        public TMP_InputField subNameInputField;
        public TMP_InputField subIDInputField;
        public TMP_Dropdown handPrefDropdown;   // 0 = placeholder, 1 = Right, 2 = Left
        public TMP_Dropdown vizTypeDropdown;    // 0 = placeholder, 1 = Desktop, 2 = VR
        public TMP_Text infoText;
        public TMP_InputField maxAnglePronation;
        public TMP_InputField maxAngleSupination;
        public TMP_InputField maxAngleFlexion;
        public TMP_InputField maxAngleExtension;
        public MaximumAngleSO maximumAnglesSettings;

        [Header("New fields (optional)")]
        public TMP_Dropdown existingSubjectDropdown; // 0 = "New subject", then one entry per subject
        public TMP_Dropdown inputSourceDropdown;     // 0 = UDP / external, 1 = Controller, 2 = Hand tracking, 3 = Auto
        public TMP_InputField ageInput;
        public TMP_Dropdown sexDropdown;             // 0 = unspecified, 1 Male, 2 Female, 3 Other
        public TMP_Dropdown affectedSideDropdown;    // 0 None, 1 Left, 2 Right, 3 Both
        public TMP_InputField notesInput;
        public TMP_Text dataPathText;

        [Header("Scenes")]
        public string desktopRightScene = "Scene1R";
        public string desktopLeftScene = "Scene1L";
        public string vrScene = "Scene1_VR";

        private List<SubjectInfo> subjects = new List<SubjectInfo>();
        private SubjectInfo selected;

        private void Start()
        {
            subjects = SubjectRegistry.LoadAll();
            var ids = new HashSet<string>();
            foreach (SubjectInfo s in subjects) ids.Add(s.subjectId);

            if (subNameInputField != null && string.IsNullOrEmpty(subNameInputField.text)) subNameInputField.text = "RISE_Lab";
            if (subIDInputField != null)
            {
                subIDInputField.text = SubjectRegistry.GenerateUniqueId(ids);
                subIDInputField.interactable = false;
            }
            if (handPrefDropdown != null && handPrefDropdown.value == 0) handPrefDropdown.value = 1;
            if (vizTypeDropdown != null && vizTypeDropdown.value == 0) vizTypeDropdown.value = 1;

            if (existingSubjectDropdown != null)
            {
                existingSubjectDropdown.ClearOptions();
                var opts = new List<string> { "New subject" };
                foreach (SubjectInfo s in subjects) opts.Add($"{s.subjectId}  {s.subjectName}  ({s.sessionCount} sessions)");
                existingSubjectDropdown.AddOptions(opts);
                existingSubjectDropdown.value = 0;
                existingSubjectDropdown.onValueChanged.AddListener(OnSubjectSelected);
            }

            if (maximumAnglesSettings != null) ApplyLimitsToFields(new JointLimits
            {
                maxFlexion = maximumAnglesSettings.maxAngleFlexion,
                maxExtension = maximumAnglesSettings.maxAngleExtension,
                maxPronation = maximumAnglesSettings.maxAnglePronation,
                maxSupination = maximumAnglesSettings.maxAngleSupination
            });
            if (dataPathText != null) dataPathText.text = "Data: " + DataPaths.RootDirectory;
            if (infoText != null) infoText.text = subjects.Count > 0 ? $"{subjects.Count} subject(s) on record" : "";
        }

        private void OnSubjectSelected(int index)
        {
            selected = index > 0 && index - 1 < subjects.Count ? subjects[index - 1] : null;
            bool isNew = selected == null;
            if (subNameInputField != null)
            {
                subNameInputField.interactable = isNew;
                if (!isNew) subNameInputField.text = selected.subjectName;
            }
            if (subIDInputField != null)
            {
                if (isNew)
                {
                    var ids = new HashSet<string>();
                    foreach (SubjectInfo s in subjects) ids.Add(s.subjectId);
                    subIDInputField.text = SubjectRegistry.GenerateUniqueId(ids);
                }
                else subIDInputField.text = selected.subjectId;
            }
            if (!isNew)
            {
                if (handPrefDropdown != null) handPrefDropdown.value = selected.HandednessSide() == ArmSide.Left ? 2 : 1;
                if (ageInput != null) ageInput.text = selected.age >= 0 ? selected.age.ToString(CultureInfo.InvariantCulture) : "";
                if (sexDropdown != null) sexDropdown.value = SexIndex(selected.sex);
                if (affectedSideDropdown != null) affectedSideDropdown.value = AffectedIndex(selected.affectedSide);
                if (notesInput != null) notesInput.text = selected.notes ?? "";
                if (selected.jointLimits != null) ApplyLimitsToFields(selected.jointLimits);
            }
        }

        private void ApplyLimitsToFields(JointLimits jl)
        {
            if (maxAngleFlexion != null) maxAngleFlexion.text = jl.maxFlexion.ToString(CultureInfo.InvariantCulture);
            if (maxAngleExtension != null) maxAngleExtension.text = jl.maxExtension.ToString(CultureInfo.InvariantCulture);
            if (maxAnglePronation != null) maxAnglePronation.text = jl.maxPronation.ToString(CultureInfo.InvariantCulture);
            if (maxAngleSupination != null) maxAngleSupination.text = jl.maxSupination.ToString(CultureInfo.InvariantCulture);
        }

        private static float ParseOr(TMP_InputField f, float fallback)
        {
            if (f == null) return fallback;
            string s = (f.text ?? string.Empty).Trim().Replace(',', '.');
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static int SexIndex(string s) => s == "Male" ? 1 : s == "Female" ? 2 : s == "Other" ? 3 : 0;
        private static string SexName(int i) => i == 1 ? "Male" : i == 2 ? "Female" : i == 3 ? "Other" : "";
        private static int AffectedIndex(string s) => s == "Left" ? 1 : s == "Right" ? 2 : s == "Both" ? 3 : 0;
        private static string AffectedName(int i) => i == 1 ? "Left" : i == 2 ? "Right" : i == 3 ? "Both" : "None";

        /// <summary>Hooked to the Enter button.</summary>
        public void LoadScene()
        {
            string name = subNameInputField != null ? subNameInputField.text.Trim() : "Subject";
            string id = subIDInputField != null ? subIDInputField.text.Trim() : "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || handPrefDropdown == null || handPrefDropdown.value == 0 || vizTypeDropdown == null || vizTypeDropdown.value == 0)
            {
                if (infoText != null) infoText.text = "Please enter the details in all the fields";
                return;
            }

            ArmSide hand = handPrefDropdown.value == 2 ? ArmSide.Left : ArmSide.Right;
            VisualisationMode mode = vizTypeDropdown.value == 2 ? VisualisationMode.VR : VisualisationMode.Desktop;

            SubjectInfo subject = selected ?? SubjectInfo.Create(id, name);
            subject.handedness = hand.ToString();
            subject.age = ageInput != null && int.TryParse(ageInput.text.Trim(), out int age) ? age : subject.age;
            if (sexDropdown != null) subject.sex = SexName(sexDropdown.value);
            if (affectedSideDropdown != null) subject.affectedSide = AffectedName(affectedSideDropdown.value);
            if (notesInput != null) subject.notes = notesInput.text;
            if (subject.jointLimits == null) subject.jointLimits = new JointLimits();
            subject.jointLimits.maxFlexion = ParseOr(maxAngleFlexion, subject.jointLimits.maxFlexion);
            subject.jointLimits.maxExtension = ParseOr(maxAngleExtension, subject.jointLimits.maxExtension);
            subject.jointLimits.maxPronation = ParseOr(maxAnglePronation, subject.jointLimits.maxPronation);
            subject.jointLimits.maxSupination = ParseOr(maxAngleSupination, subject.jointLimits.maxSupination);
            // Keep the 7-DOF limits consistent with the 2-DOF fields.
            subject.jointLimits.EnsureValid();
            subject.jointLimits.minAngles[4] = subject.jointLimits.maxPronation;
            subject.jointLimits.maxAngles[4] = subject.jointLimits.maxSupination;
            subject.jointLimits.minAngles[5] = subject.jointLimits.maxFlexion;
            subject.jointLimits.maxAngles[5] = subject.jointLimits.maxExtension;
            SubjectRegistry.Save(subject);

            ArmInputSource source = ArmInputSource.ExternalAngles;
            if (inputSourceDropdown != null)
            {
                switch (inputSourceDropdown.value)
                {
                    case 1: source = ArmInputSource.Controller; break;
                    case 2: source = ArmInputSource.HandTracking; break;
                    case 3: source = ArmInputSource.Auto; break;
                }
            }
            else if (mode == VisualisationMode.VR)
            {
                source = ArmInputSource.Auto;
            }

            // Backwards compatibility with scripts that still read PlayerPrefs.
            PlayerPrefs.SetString("subName", subject.subjectName);
            PlayerPrefs.SetString("subID", subject.subjectId);

            LaunchConfig.Set(subject, mode, hand, source);
            SessionManager.Ensure().SetSubject(subject);

            string scene = mode == VisualisationMode.VR ? vrScene : hand == ArmSide.Left ? desktopLeftScene : desktopRightScene;
            if (Application.CanStreamedLevelBeLoaded(scene))
            {
                SceneManager.LoadScene(scene);
            }
            else
            {
                if (infoText != null) infoText.text = "Scene '" + scene + "' is not in the build settings";
                Debug.LogError("[FAMOT] Scene not found in build settings: " + scene);
            }
        }
    }
}
