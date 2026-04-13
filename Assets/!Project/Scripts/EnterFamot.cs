using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EnterFamot : MonoBehaviour
{
    public TMP_InputField subNameInputField;
    public TMP_InputField subIDInputField;
    public TMP_Dropdown handPrefDropdown;
    public TMP_Dropdown vizTypeDropdown;
    public TMP_Text infoText;

    public TMP_InputField maxAnglePronation;
    public TMP_InputField maxAngleSupination;
    public TMP_InputField maxAngleFlexion;
    public TMP_InputField maxAngleExtension;

    public MaximumAngleSO maximumAnglesSettings;

    public static string subName;
    public static string subID;

    // Start is called before the first frame update
    void Start()
    {
        // Set default values
        subNameInputField.text = "RISE_Lab";
        subIDInputField.text = GenerateUUID();
        subIDInputField.interactable = false; // Auto-generated, no manual editing
        handPrefDropdown.value = 1; // Right handed
        vizTypeDropdown.value = 1; // Desktop Screen

        // Load maximum angles settings from ScriptableObject
        if (maximumAnglesSettings != null)
        {
            maxAngleFlexion.text = maximumAnglesSettings.maxAngleFlexion.ToString();
            maxAngleExtension.text = maximumAnglesSettings.maxAngleExtension.ToString();
            maxAnglePronation.text = maximumAnglesSettings.maxAnglePronation.ToString();
            maxAngleSupination.text = maximumAnglesSettings.maxAngleSupination.ToString();
        }

        // Add listeners to input fields to update ScriptableObject on change
        maxAngleFlexion.onValueChanged.AddListener(delegate { UpdateMaxAngleFlexion(); });
        maxAngleExtension.onValueChanged.AddListener(delegate { UpdateMaxAngleExtension(); });
        maxAnglePronation.onValueChanged.AddListener(delegate { UpdateMaxAnglePronation(); });
        maxAngleSupination.onValueChanged.AddListener(delegate { UpdateMaxAngleSupination(); });
    }

    // Update is called once per frame
    void Update() { }

    // On click Load Scene
    public void LoadScene()
    {
        //Check if the input field is empty
        if (subNameInputField.text == "" || subIDInputField.text == "" || handPrefDropdown.value == 0 || vizTypeDropdown.value == 0)
        {
            infoText.text = "Please enter the details in all the fields";
            return;
        }
        else
        {
            //Wait for 5 seconds
            StartCoroutine(WaitAndExecute());
            //Get the string in the input field and store it in a player pref
            PlayerPrefs.SetString("subName", subNameInputField.text);
            subName = subNameInputField.text;
            subID = subIDInputField.text;

            // If subject folder already exists, regenerate UUID until unique
            string folderPath = Application.streamingAssetsPath + "/" + subID + "_" + subName;
            while (System.IO.Directory.Exists(folderPath))
            {
                subID = GenerateUUID();
                subIDInputField.text = subID;
                folderPath = Application.streamingAssetsPath + "/" + subID + "_" + subName;
            }

            PlayerPrefs.SetString("subID", subID);
            System.IO.Directory.CreateDirectory(folderPath);

            //Load the next scene
            if (handPrefDropdown.value == 1 && vizTypeDropdown.value == 1)
            {
                SceneManager.LoadScene("Scene1R");
            }
            else if (handPrefDropdown.value == 2 && vizTypeDropdown.value == 1)
            {
                SceneManager.LoadScene("Scene1L");
            }
            else if (handPrefDropdown.value == 1 && vizTypeDropdown.value == 2)
            {
                SceneManager.LoadScene("Scene1R_VR");
            }
            else if (handPrefDropdown.value == 2 && vizTypeDropdown.value == 2)
            {
                SceneManager.LoadScene("Scene1L_VR");
            }
        }
    }

    // Wait for 5 seconds
                IEnumerator WaitAndExecute()
    {
        yield return new WaitForSecondsRealtime(3f);
    }

    // Generate a random 4-digit alphanumeric UUID
    private string GenerateUUID()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        char[] code = new char[4];
        for (int i = 0; i < 4; i++)
        {
            code[i] = chars[Random.Range(0, chars.Length)];
        }
        return new string(code);
    }

    // Update ScriptableObject when input fields change
    private void UpdateMaxAngleFlexion()
    {
        if (maximumAnglesSettings != null && float.TryParse(maxAngleFlexion.text, out float value))
        {
            maximumAnglesSettings.maxAngleFlexion = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(maximumAnglesSettings);
#endif
        }
    }

    private void UpdateMaxAngleExtension()
    {
        if (maximumAnglesSettings != null && float.TryParse(maxAngleExtension.text, out float value))
        {
            maximumAnglesSettings.maxAngleExtension = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(maximumAnglesSettings);
#endif
        }
    }

    private void UpdateMaxAnglePronation()
    {
        if (maximumAnglesSettings != null && float.TryParse(maxAnglePronation.text, out float value))
        {
            maximumAnglesSettings.maxAnglePronation = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(maximumAnglesSettings);
#endif
        }
    }

    private void UpdateMaxAngleSupination()
    {
        if (maximumAnglesSettings != null && float.TryParse(maxAngleSupination.text, out float value))
        {
            maximumAnglesSettings.maxAngleSupination = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(maximumAnglesSettings);
#endif
        }
    }
}