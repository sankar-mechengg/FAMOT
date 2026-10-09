using UnityEngine;
using UnityEngine.SceneManagement;

public class HomePosition : MonoBehaviour
{
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    void Awake()
    {
        // Save the initial position and rotation of the Main Camera
        initialPosition = Camera.main.transform.position;
        initialRotation = Camera.main.transform.rotation;
    }

    // This method will reset the Main Camera to its original position and rotation
    public void GoToHome()
    {
        Camera.main.transform.position = initialPosition;
        Camera.main.transform.rotation = initialRotation;
    }

    public void ReturnToLogin()
    {
        // Close the data session cleanly, then load the Login scene
        if (FAMOT.Session.SessionManager.Instance != null)
        {
            FAMOT.Session.SessionManager.Instance.EndSession();
        }
        FAMOT.Session.LaunchConfig.Clear();
        SceneManager.LoadScene("Scene0");
    }
}
