using UnityEngine;

[CreateAssetMenu(fileName = "MaximumAnglesSettings", menuName = "FAMOT/Maximum Angles Settings")]
public class MaximumAngleSO : ScriptableObject
{
    public float maxAngleFlexion = -70f;
    public float maxAngleExtension = 70f;
    public float maxAnglePronation = -20f;
    public float maxAngleSupination = 90f;
}
