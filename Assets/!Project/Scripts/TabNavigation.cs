using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TabNavigation : MonoBehaviour
{
    public TMP_InputField firstInputField; // Reference to the first input field
    public TMP_InputField secondInputField; // Reference to the second input field
    public TMP_Dropdown firstDropdown; // Reference to the first dropdown
    public TMP_Dropdown secondDropdown; // Reference to the second dropdown
    public Button targetButton; // Reference to the button

    private void Update()
    {
        // Check if Tab key is pressed
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // Check if an InputField or Dropdown is currently selected
            if (EventSystem.current.currentSelectedGameObject != null)
            {
                GameObject selected = EventSystem.current.currentSelectedGameObject;

                // Move focus from the first input field to the second input field
                if (selected == firstInputField.gameObject)
                {
                    secondInputField.Select();
                }
                // Move focus from the second input field to the first dropdown
                else if (selected == secondInputField.gameObject)
                {
                    firstDropdown.Select();
                }
                // Move focus from the first dropdown to the second dropdown
                else if (selected == firstDropdown.gameObject)
                {
                    secondDropdown.Select();
                }
                // Move focus from the second dropdown to the button
                else if (selected == secondDropdown.gameObject)
                {
                    targetButton.Select();
                }
                // Optionally, loop back to the first input field if focus is on the button
                else if (selected == targetButton.gameObject)
                {
                    firstInputField.Select();
                }
            }
        }

        // Check if Enter key is pressed while the button is selected
        if (EventSystem.current.currentSelectedGameObject == targetButton.gameObject && Input.GetKeyDown(KeyCode.Return))
        {
            targetButton.onClick.Invoke();
        }
    }
}
