using System.Collections.Generic;
using System.IO;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Experiment;
using FAMOT.Input;
using FAMOT.Kinematics;
using FAMOT.Protocol;
using FAMOT.Recording;
using FAMOT.Session;
using FAMOT.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FAMOT.Editor
{
    /// <summary>
    /// One-shot scene and project configuration. Every command is idempotent: running it again repairs
    /// missing pieces without duplicating objects. Menu: FAMOT ▸ Setup.
    /// </summary>
    public static class FamotSetupTools
    {
        private const string ScenesDir = "Assets/!Project/Scenes/";
        private const string LoginScene = ScenesDir + "Scene0.unity";
        private const string DesktopRightScene = ScenesDir + "Scene1R.unity";
        private const string DesktopLeftScene = ScenesDir + "Scene1L.unity";
        private const string VrSceneOld = ScenesDir + "Scene1R_VR.unity";
        private const string VrScene = ScenesDir + "Scene1_VR.unity";
        private const string RightPrefab = "Assets/!Project/Media/Prefabs/Male_Right_Hand.prefab";
        private const string RightGhostPrefab = "Assets/!Project/Media/Prefabs/Male_Right_Hand_Ghost.prefab";
        private const string LeftPrefab = "Assets/!Project/Media/Prefabs/Male_Left_Hand.prefab";
        private const string LeftGhostPrefab = "Assets/!Project/Media/Prefabs/Male_Left_Hand_Ghost.prefab";

        // ------------------------------------------------------------------ menu entries

        [MenuItem("FAMOT/Setup/Configure Everything (all scenes + project)", priority = 0)]
        public static void ConfigureEverything()
        {
            ConfigureProject();
            ConfigureLoginSceneAsset();
            ConfigureSceneAsset(DesktopRightScene, VisualisationMode.Desktop, ArmSide.Right);
            ConfigureSceneAsset(DesktopLeftScene, VisualisationMode.Desktop, ArmSide.Left);
            RenameVrSceneIfNeeded();
            ConfigureSceneAsset(VrScene, VisualisationMode.VR, ArmSide.Right);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[FAMOT Setup] Everything configured.");
        }

        [MenuItem("FAMOT/Setup/Configure Login Scene (Scene0)", priority = 10)]
        public static void ConfigureLoginSceneAsset()
        {
            Scene s = EditorSceneManager.OpenScene(LoginScene, OpenSceneMode.Single);
            ConfigureLoginScene();
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Debug.Log("[FAMOT Setup] Login scene configured.");
        }

        [MenuItem("FAMOT/Setup/Configure Open Scene as Desktop (Right)", priority = 20)]
        public static void ConfigureOpenDesktopRight() => ConfigureOpenScene(VisualisationMode.Desktop, ArmSide.Right);

        [MenuItem("FAMOT/Setup/Configure Open Scene as Desktop (Left)", priority = 21)]
        public static void ConfigureOpenDesktopLeft() => ConfigureOpenScene(VisualisationMode.Desktop, ArmSide.Left);

        [MenuItem("FAMOT/Setup/Configure Open Scene as VR (both arms)", priority = 22)]
        public static void ConfigureOpenVr() => ConfigureOpenScene(VisualisationMode.VR, ArmSide.Right);

        [MenuItem("FAMOT/Setup/Project: OpenXR features + build scenes", priority = 40)]
        public static void ConfigureProjectMenu()
        {
            ConfigureProject();
            RenameVrSceneIfNeeded();
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureOpenScene(VisualisationMode mode, ArmSide hand)
        {
            Scene s = SceneManager.GetActiveScene();
            ConfigureExperimentScene(mode, hand);
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Debug.Log($"[FAMOT Setup] {s.name} configured as {mode} ({hand}).");
        }

        public static void ConfigureSceneAsset(string path, VisualisationMode mode, ArmSide hand)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[FAMOT Setup] Scene not found: " + path);
                return;
            }
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            ConfigureExperimentScene(mode, hand);
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Debug.Log($"[FAMOT Setup] {s.name} configured as {mode} ({hand}).");
        }

        private static void RenameVrSceneIfNeeded()
        {
            if (File.Exists(VrSceneOld) && !File.Exists(VrScene))
            {
                string err = AssetDatabase.RenameAsset(VrSceneOld, "Scene1_VR");
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[FAMOT Setup] Could not rename VR scene: " + err);
                string oldFolder = ScenesDir + "Scene1R_VR";
                if (AssetDatabase.IsValidFolder(oldFolder)) AssetDatabase.RenameAsset(oldFolder, "Scene1_VR");
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------ project

        public static void ConfigureProject()
        {
            EnableOpenXrHandTracking();
        }

        private static void EnableOpenXrHandTracking()
        {
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null) continue;
                var hand = settings.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>();
                if (hand != null && !hand.enabled)
                {
                    hand.enabled = true;
                    EditorUtility.SetDirty(hand);
                    Debug.Log($"[FAMOT Setup] Enabled OpenXR Hand Tracking Subsystem for {group}.");
                }
                var gaze = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.EyeGazeInteraction>();
                if (gaze != null && !gaze.enabled && group == BuildTargetGroup.Standalone)
                {
                    gaze.enabled = true;
                    EditorUtility.SetDirty(gaze);
                    Debug.Log("[FAMOT Setup] Enabled OpenXR Eye Gaze Interaction for Standalone.");
                }
                EditorUtility.SetDirty(settings);
            }
        }

        public static void ConfigureBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (string p in new[] { LoginScene, DesktopLeftScene, DesktopRightScene, VrScene })
            {
                if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[FAMOT Setup] Build scenes: " + string.Join(", ", list.ConvertAll(x => Path.GetFileNameWithoutExtension(x.path))));
        }

        // ------------------------------------------------------------------ login scene

        public static void ConfigureLoginScene()
        {
            GameObject manager = GameObject.Find("Enter Famot Manager");
            if (manager == null)
            {
                Debug.LogError("[FAMOT Setup] 'Enter Famot Manager' not found in the login scene.");
                return;
            }
            LoginController login = manager.GetComponent<LoginController>();
            if (login == null) login = Undo.AddComponent<LoginController>(manager);

            // Migrate references from the legacy EnterFamot component when present.
            var legacy = manager.GetComponent("EnterFamot") as MonoBehaviour;
            if (legacy != null)
            {
                var src = new SerializedObject(legacy);
                var dst = new SerializedObject(login);
                foreach (string f in new[] { "subNameInputField", "subIDInputField", "handPrefDropdown", "vizTypeDropdown", "infoText", "maxAnglePronation", "maxAngleSupination", "maxAngleFlexion", "maxAngleExtension", "maximumAnglesSettings" })
                {
                    SerializedProperty sp = src.FindProperty(f);
                    SerializedProperty dp = dst.FindProperty(f);
                    if (sp != null && dp != null) dp.objectReferenceValue = sp.objectReferenceValue;
                }
                dst.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(legacy);
            }

            // Fill any missing references by name.
            if (login.subNameInputField == null) login.subNameInputField = FindComponent<TMP_InputField>("Subject Name InputField (TMP)");
            if (login.subIDInputField == null) login.subIDInputField = FindComponent<TMP_InputField>("Subject ID InputField (TMP)");
            if (login.handPrefDropdown == null) login.handPrefDropdown = FindComponent<TMP_Dropdown>("Hand Preference Dropdown");
            if (login.vizTypeDropdown == null) login.vizTypeDropdown = FindComponent<TMP_Dropdown>("Visualization Dropdown");
            if (login.infoText == null) login.infoText = FindComponent<TMP_Text>("Instructions Text");
            if (login.maxAnglePronation == null) login.maxAnglePronation = FindComponent<TMP_InputField>("Pronation");
            if (login.maxAngleSupination == null) login.maxAngleSupination = FindComponent<TMP_InputField>("Supination");
            if (login.maxAngleFlexion == null) login.maxAngleFlexion = FindComponent<TMP_InputField>("Flexion");
            if (login.maxAngleExtension == null) login.maxAngleExtension = FindComponent<TMP_InputField>("Extension");
            if (login.maximumAnglesSettings == null) login.maximumAnglesSettings = AssetDatabase.LoadAssetAtPath<MaximumAngleSO>("Assets/Resources/MaximumAnglesSettings.asset");

            // Enter button → LoginController.LoadScene
            GameObject buttonGo = GameObject.Find("Enter Button");
            if (buttonGo != null)
            {
                Button button = buttonGo.GetComponent<Button>();
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                {
                    if (button.onClick.GetPersistentTarget(i) == null || button.onClick.GetPersistentMethodName(i) == "LoadScene")
                    {
                        UnityEventTools.RemovePersistentListener(button.onClick, i);
                    }
                }
                UnityEventTools.AddPersistentListener(button.onClick, new UnityAction(login.LoadScene));
                EditorUtility.SetDirty(button);
            }

            // Details panel with the new fields.
            GameObject loginPanel = GameObject.Find("Login Panel");
            Transform canvas = loginPanel != null ? loginPanel.transform.parent : null;
            if (loginPanel == null || canvas == null)
            {
                Debug.LogWarning("[FAMOT Setup] Login Panel not found; skipping the details panel.");
                EditorUtility.SetDirty(login);
                return;
            }

            GameObject details = FindChild(canvas, "Details Panel");
            if (details == null)
            {
                details = new GameObject("Details Panel", typeof(RectTransform), typeof(Image));
                details.transform.SetParent(canvas, false);
                var img = details.GetComponent<Image>();
                Image panelImg = loginPanel.GetComponent<Image>();
                if (panelImg != null)
                {
                    UnityEditorInternal.ComponentUtility.CopyComponent(panelImg);
                    UnityEditorInternal.ComponentUtility.PasteComponentValues(img);
                }
                var rt = details.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(260f, 470f);
                rt.anchoredPosition = new Vector2(330f, -190f);
                details.transform.SetSiblingIndex(loginPanel.transform.GetSiblingIndex() + 1);
            }

            float y = -14f;
            TMP_Text title = EnsureText(details.transform, "Details Title", "Details", 22, ref y, 36f, login.infoText);
            login.existingSubjectDropdown = EnsureDropdown(details.transform, "Existing Subject Dropdown", login.handPrefDropdown, new[] { "New subject" }, ref y);
            login.inputSourceDropdown = EnsureDropdown(details.transform, "Input Source Dropdown", login.handPrefDropdown, new[] { "Input: UDP (MATLAB/Python)", "Input: VR controller", "Input: Hand tracking", "Input: Auto (hand ▸ controller ▸ UDP)" }, ref y);
            login.ageInput = EnsureInput(details.transform, "Age Input", login.subNameInputField, "Age (optional)", TMP_InputField.ContentType.IntegerNumber, ref y, 44f);
            login.sexDropdown = EnsureDropdown(details.transform, "Sex Dropdown", login.handPrefDropdown, new[] { "Sex: not specified", "Sex: male", "Sex: female", "Sex: other" }, ref y);
            login.affectedSideDropdown = EnsureDropdown(details.transform, "Affected Side Dropdown", login.handPrefDropdown, new[] { "Affected side: none", "Affected side: left", "Affected side: right", "Affected side: both" }, ref y);
            login.notesInput = EnsureInput(details.transform, "Notes Input", login.subNameInputField, "Notes (optional)", TMP_InputField.ContentType.Standard, ref y, 80f);
            login.dataPathText = EnsureText(details.transform, "Data Path Text", "Data: …", 11, ref y, 60f, login.infoText);
            if (title != null) title.alignment = TextAlignmentOptions.Center;

            EditorUtility.SetDirty(login);
        }

        private static TMP_Text EnsureText(Transform parent, string name, string text, float size, ref float y, float height, TMP_Text styleSource)
        {
            GameObject go = FindChild(parent, name);
            if (go == null)
            {
                go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
            }
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.TopLeft;
            if (styleSource != null)
            {
                t.font = styleSource.font;
                t.color = styleSource.color;
            }
            Place(go.GetComponent<RectTransform>(), ref y, height);
            return t;
        }

        private static TMP_Dropdown EnsureDropdown(Transform parent, string name, TMP_Dropdown template, string[] options, ref float y)
        {
            GameObject go = FindChild(parent, name);
            if (go == null)
            {
                if (template == null) return null;
                go = Object.Instantiate(template.gameObject, parent, false);
                go.name = name;
            }
            var dd = go.GetComponent<TMP_Dropdown>();
            dd.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dd.ClearOptions();
            dd.AddOptions(new List<string>(options));
            dd.value = 0;
            dd.RefreshShownValue();
            Place(go.GetComponent<RectTransform>(), ref y, 44f);
            return dd;
        }

        private static TMP_InputField EnsureInput(Transform parent, string name, TMP_InputField template, string placeholder, TMP_InputField.ContentType type, ref float y, float height)
        {
            GameObject go = FindChild(parent, name);
            if (go == null)
            {
                if (template == null) return null;
                go = Object.Instantiate(template.gameObject, parent, false);
                go.name = name;
            }
            var input = go.GetComponent<TMP_InputField>();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.contentType = type;
            input.lineType = height > 60f ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.text = string.Empty;
            input.interactable = true;
            if (input.placeholder is TMP_Text ph) ph.text = placeholder;
            Place(go.GetComponent<RectTransform>(), ref y, height);
            return input;
        }

        private static void Place(RectTransform rt, ref float y, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(236f, height);
            rt.anchoredPosition = new Vector2(0f, y);
            y -= height + 8f;
        }

        // ------------------------------------------------------------------ experiment scenes

        public static void ConfigureExperimentScene(VisualisationMode mode, ArmSide defaultHand)
        {
            bool vr = mode == VisualisationMode.VR;

            Transform managersHolder = FindRoot("----MANAGERS----");
            GameObject mgr = GameObject.Find("FAMOT Managers");
            if (mgr == null)
            {
                mgr = new GameObject("FAMOT Managers");
                if (managersHolder != null) mgr.transform.SetParent(managersHolder, false);
            }

            var legacyReceiver = Object.FindFirstObjectByType<UDPReceiver>(FindObjectsInactive.Include);

            var server = Ensure<UdpCommandServer>(mgr);
            var sender = Ensure<UdpStreamSender>(mgr);
            Ensure<XrTrackingService>(mgr);
            var gaze = Ensure<GazeRaycaster>(mgr);
            var ctrl = Ensure<ExperimentController>(mgr);
            var runner = Ensure<ProtocolRunner>(mgr);
            var recorder = Ensure<SessionRecorder>(mgr);
            var boot = Ensure<ExperimentSceneBootstrap>(mgr);

            if (legacyReceiver != null)
            {
                var so = new SerializedObject(server);
                so.FindProperty("listenPort").intValue = legacyReceiver.Port > 0 ? legacyReceiver.Port : 8400;
                so.ApplyModifiedPropertiesWithoutUndo();
                ctrl.infoText = legacyReceiver.infoText;
                ctrl.headerText = legacyReceiver.headerText;
                ctrl.ledImage = legacyReceiver.ledImage;
                ctrl.trialText = legacyReceiver.infoTrialText;
                ctrl.targetText = legacyReceiver.infoTargetText;
                ctrl.legacyLimits = legacyReceiver.maximumAnglesSettings;
                Object.DestroyImmediate(legacyReceiver);
            }
            if (ctrl.legacyLimits == null) ctrl.legacyLimits = AssetDatabase.LoadAssetAtPath<MaximumAngleSO>("Assets/Resources/MaximumAnglesSettings.asset");
            if (ctrl.infoText == null) ctrl.infoText = FindComponent<TMP_Text>("Info Text (TMP)");
            if (ctrl.headerText == null) ctrl.headerText = FindComponent<TMP_Text>("Header Text (TMP)");
            if (ctrl.trialText == null) ctrl.trialText = FindComponent<TMP_Text>("Trial Info Text (TMP)");
            if (ctrl.targetText == null) ctrl.targetText = FindComponent<TMP_Text>("Target Info Text (TMP)");
            if (ctrl.ledImage == null) ctrl.ledImage = FindComponent<Image>("Light Image");
            ctrl.server = server;
            ctrl.streamSender = sender;

            runner.controller = ctrl;
            recorder.controller = ctrl;
            recorder.server = server;
            recorder.streamSender = sender;
            boot.controller = ctrl;
            boot.protocolRunner = runner;
            boot.gaze = gaze;
            boot.sceneMode = mode;
            boot.defaultHand = defaultHand;
            boot.defaultLiveSource = vr ? ArmInputSource.Auto : ArmInputSource.ExternalAngles;
            boot.showOtherArm = vr;

            foreach (var st in Object.FindObjectsByType<SetTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(st);
            }
            // Remove the now-empty legacy holder objects.
            foreach (string legacyName in new[] { "UDP_Receiver", "Set Target" })
            {
                GameObject g = FindSceneObject(legacyName);
                if (g != null && g.GetComponents<Component>().Length == 1 && g.transform.childCount == 0)
                {
                    Object.DestroyImmediate(g);
                }
            }

            // Arms
            Transform objectsHolder = FindRoot("----GAMEOBJECTS----");
            bool needRight = vr || defaultHand == ArmSide.Right;
            bool needLeft = vr || defaultHand == ArmSide.Left;
            if (needRight)
            {
                SetupArm(ArmSide.Right, "Male_Right_Hand", "Male_Right_Hand_Ghost", RightPrefab, RightGhostPrefab, objectsHolder, vr,
                    out boot.rightArm, out boot.rightGhost, out boot.rightFingers);
            }
            if (needLeft)
            {
                SetupArm(ArmSide.Left, "Male_Left_Hand", "Male_Left_Hand_Ghost", LeftPrefab, LeftGhostPrefab, objectsHolder, vr,
                    out boot.leftArm, out boot.leftGhost, out boot.leftFingers);
            }
            ctrl.liveArm = defaultHand == ArmSide.Right ? boot.rightArm : boot.leftArm;
            ctrl.ghostArm = defaultHand == ArmSide.Right ? boot.rightGhost : boot.leftGhost;
            ctrl.liveFingers = defaultHand == ArmSide.Right ? boot.rightFingers : boot.leftFingers;
            ctrl.otherArm = vr ? (defaultHand == ArmSide.Right ? boot.leftArm : boot.rightArm) : null;

            // Home / return button: end the session before leaving.
            var home = Object.FindFirstObjectByType<HomePosition>(FindObjectsInactive.Include);
            if (home != null) EditorUtility.SetDirty(home);

            EnsureExperimenterCanvas();

            foreach (Object o in new Object[] { mgr, ctrl, runner, recorder, boot, server, sender })
            {
                if (o != null) EditorUtility.SetDirty(o);
            }
        }

        private static void SetupArm(ArmSide side, string liveName, string ghostName, string livePrefab, string ghostPrefab,
            Transform holder, bool vr, out ArmDriver driver, out ArmRig ghostRig, out FingerRetargeter fingers)
        {
            GameObject live = FindSceneObject(liveName);
            GameObject ghost = FindSceneObject(ghostName);
            // Place a missing arm mirrored (about x = 0) relative to the opposite arm when that one exists.
            GameObject opposite = FindSceneObject(side == ArmSide.Right ? "Male_Left_Hand" : "Male_Right_Hand");
            Vector3 defaultPos = opposite != null
                ? new Vector3(-opposite.transform.position.x, opposite.transform.position.y, opposite.transform.position.z)
                : new Vector3(side == ArmSide.Right ? 0.35f : -0.35f, 1.3f, 0.1f);
            if (live == null)
            {
                live = InstantiatePrefab(livePrefab, holder, defaultPos);
                live.name = liveName;
            }
            if (ghost == null)
            {
                ghost = InstantiatePrefab(ghostPrefab, holder, defaultPos);
                ghost.name = ghostName;
            }
            // The NatureManufacture prefabs carry an Animator; it would fight the solver, so keep it off.
            foreach (GameObject g in new[] { live, ghost })
            {
                var anim = g.GetComponent<Animator>();
                if (anim != null && anim.enabled)
                {
                    anim.enabled = false;
                    EditorUtility.SetDirty(anim);
                }
            }

            string anchorName = "ArmAnchor_" + side.ShortName();
            GameObject anchorGo = FindSceneObject(anchorName);
            if (anchorGo == null)
            {
                anchorGo = new GameObject(anchorName);
                Transform parent = live.transform.parent;
                anchorGo.transform.SetParent(parent, false);
                Transform shoulderBone = FindDeep(live.transform, "arm_up_deform");
                anchorGo.transform.position = shoulderBone != null ? shoulderBone.position : live.transform.position;
                anchorGo.transform.rotation = Quaternion.identity;
                anchorGo.transform.SetSiblingIndex(live.transform.GetSiblingIndex());
            }
            if (live.transform.parent != anchorGo.transform) live.transform.SetParent(anchorGo.transform, true);
            if (ghost.transform.parent != anchorGo.transform) ghost.transform.SetParent(anchorGo.transform, true);

            var anchor = Ensure<ShoulderAnchor>(anchorGo);
            anchor.side = side;
            anchor.followHead = vr;
            anchor.offsetFromHead = new Vector3(0.17f * side.LateralSign(), -0.22f, -0.06f);

            ArmRig liveRig = Ensure<ArmRig>(live);
            liveRig.side = side;
            liveRig.anchor = anchorGo.transform;
            liveRig.ResolveBones();

            ghostRig = Ensure<ArmRig>(ghost);
            ghostRig.side = side;
            ghostRig.anchor = anchorGo.transform;
            ghostRig.ResolveBones();

            driver = Ensure<ArmDriver>(anchorGo);
            driver.side = side;
            driver.rig = liveRig;
            driver.shoulderAnchor = anchor;
            driver.requestedSource = vr ? ArmInputSource.Auto : ArmInputSource.ExternalAngles;

            fingers = Ensure<FingerRetargeter>(anchorGo);
            fingers.side = side;
            fingers.rig = liveRig;
            fingers.driver = driver;
            fingers.ResolveBones();

            var liveCol = Ensure<ArmGazeColliders>(live);
            liveCol.rig = liveRig;
            liveCol.kind = GazeTargetKind.LiveArm;
            var ghostCol = Ensure<ArmGazeColliders>(ghost);
            ghostCol.rig = ghostRig;
            ghostCol.kind = GazeTargetKind.GhostArm;

            foreach (Object o in new Object[] { anchorGo, anchor, liveRig, ghostRig, driver, fingers, liveCol, ghostCol, live, ghost })
            {
                EditorUtility.SetDirty(o);
            }
        }

        private static void EnsureExperimenterCanvas()
        {
            GameObject canvasGo = GameObject.Find("Experimenter Canvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("Experimenter Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Transform holder = FindRoot("----UI----");
                if (holder != null) canvasGo.transform.SetParent(holder, false);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                var bg = new GameObject("Panel Background", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(canvasGo.transform, false);
                bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
                var bgRt = bg.GetComponent<RectTransform>();
                bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 0f);
                bgRt.pivot = new Vector2(0f, 0f);
                bgRt.anchoredPosition = new Vector2(12f, 12f);
                bgRt.sizeDelta = new Vector2(760f, 400f);

                var txt = new GameObject("Panel Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                txt.transform.SetParent(bg.transform, false);
                var t = txt.GetComponent<TextMeshProUGUI>();
                t.fontSize = 19f;
                t.color = Color.white;
                t.richText = true;
                t.alignment = TextAlignmentOptions.TopLeft;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                var tRt = txt.GetComponent<RectTransform>();
                tRt.anchorMin = Vector2.zero;
                tRt.anchorMax = Vector2.one;
                tRt.offsetMin = new Vector2(12f, 10f);
                tRt.offsetMax = new Vector2(-12f, -10f);

                var rec = new GameObject("REC Indicator", typeof(RectTransform), typeof(TextMeshProUGUI));
                rec.transform.SetParent(canvasGo.transform, false);
                var r = rec.GetComponent<TextMeshProUGUI>();
                r.fontSize = 42f;
                r.color = Color.red;
                r.alignment = TextAlignmentOptions.TopRight;
                r.text = "";
                var rRt = rec.GetComponent<RectTransform>();
                rRt.anchorMin = rRt.anchorMax = new Vector2(1f, 1f);
                rRt.pivot = new Vector2(1f, 1f);
                rRt.anchoredPosition = new Vector2(-20f, -16f);
                rRt.sizeDelta = new Vector2(240f, 60f);

                var panel = canvasGo.AddComponent<ExperimenterPanel>();
                panel.text = t;
                panel.recIndicator = r;
            }
            EditorUtility.SetDirty(canvasGo);
        }

        // ------------------------------------------------------------------ helpers

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static T FindComponent<T>(string objectName) where T : Component
        {
            GameObject go = FindSceneObject(objectName);
            return go != null ? go.GetComponent<T>() : null;
        }

        private static GameObject FindSceneObject(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root;
                Transform t = FindDeep(root.transform, name);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static Transform FindRoot(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root.transform;
            }
            return null;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent)
            {
                if (t.name == name) return t.gameObject;
            }
            return null;
        }

        private static GameObject InstantiatePrefab(string path, Transform parent, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[FAMOT Setup] Prefab not found: " + path);
                return new GameObject(Path.GetFileNameWithoutExtension(path));
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            return go;
        }
    }
}
