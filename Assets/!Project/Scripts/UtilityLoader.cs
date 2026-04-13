using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UtilityLoader : MonoBehaviour
{
    public TMP_Text welcomeText;
    public Button exitButton;

    // Start is called before the first frame update
    void Start()
    {
        // Get the subject name and ID from the PlayerPrefs
        string subName = PlayerPrefs.GetString("subName");
        string subID = PlayerPrefs.GetString("subID");

        // Display the welcome message
        welcomeText.text = "Welcome " + subName;
    }

    // Update is called once per frame
    void Update() { }

    // Exit the application
    public void ExitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
