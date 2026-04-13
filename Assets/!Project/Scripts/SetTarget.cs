using UnityEngine;

public class SetTarget : MonoBehaviour
{
    // Assign these in the Unity Inspector
    public Transform wristTransform;
    public Transform elbowTransform;

    // Define the ranges for the wrist and elbow angles
    public Vector2 wristAngleRange = new Vector2(-80, 80); // Example range for wrist X angle
    public Vector2 elbowAngleRange = new Vector2(-80, 80); // Example range for elbow Z angle

    // Define the key to trigger the change
    public KeyCode changeKey = KeyCode.Space;

    void Update()
    {
        // Check if the specified key has been pressed
        if (Input.GetKeyDown(changeKey))
        {
            SetRandomAngles();
        }
    }

    void SetRandomAngles()
    {
        // Randomly select an angle for the wrist within the specified range
        float wristXAngle = Random.Range(wristAngleRange.x, wristAngleRange.y);

        // Randomly select an angle for the elbow within the specified range
        float elbowZAngle = Random.Range(elbowAngleRange.x, elbowAngleRange.y);

        // Set the wrist angle locally (only change the X angle)
        Vector3 wristLocalEulerAngles = wristTransform.localEulerAngles;
        wristLocalEulerAngles.x = wristXAngle;
        wristTransform.localEulerAngles = wristLocalEulerAngles;

        // Set the elbow angle globally (only change the Z angle)
        Vector3 elbowLocalEulerAngles = elbowTransform.localEulerAngles;
        elbowLocalEulerAngles.y = elbowZAngle;
        elbowTransform.localEulerAngles = elbowLocalEulerAngles;
    }
}
