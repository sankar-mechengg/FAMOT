using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    //Header
    [Header("Camera Movement")]
    public float movementSpeed;
    public float normalSpeed;
    public float fastSpeed;

    [Header("Camera Rotation")]
    public float rotationSpeed;

    [Header("Camera Zoom")]
    public float zoomSpeed;
    public float minZoomDistance; // Minimum distance to zoom in
    public float maxZoomDistance; // Maximum distance to zoom out

    //====================================================================================================

    // Start is called before the first frame update
    void Start()
    {
        //Set the variables
        movementSpeed = 2.0f;
        normalSpeed = 2.0f;
        fastSpeed = 10.0f;

        rotationSpeed = 5.0f;

        zoomSpeed = 5.0f;

        minZoomDistance = 1.0f;
        maxZoomDistance = 200.0f;
    }

    // Update is called once per frame
    void Update()
    {
        HandleMovementKeyInput();
        ZoomTowardsMouseCursor();
    }

    //====================================================================================================

    //Handles the camera movement using the keyboard
    void HandleMovementKeyInput()
    {
        // Camera Movement
        // Set the movement speed based on the current speed
        if (Input.GetKey(KeyCode.LeftShift))
        {
            movementSpeed = fastSpeed;
        }
        else
        {
            movementSpeed = normalSpeed;
        }

        // When arrow keys are pressed, move the camera in the global direction of X and Z axis
        // Global movement with arrow keys
        if (Input.GetKey(KeyCode.UpArrow))
        {
            transform.position += Vector3.forward * movementSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            transform.position += Vector3.back * movementSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            transform.position += Vector3.left * movementSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            transform.position += Vector3.right * movementSpeed * Time.deltaTime;
        }

        // Local movement with WASD keys
        if (Input.GetKey(KeyCode.W))
        {
            transform.Translate(Vector3.forward * movementSpeed * Time.deltaTime);
        }
        if (Input.GetKey(KeyCode.S))
        {
            transform.Translate(Vector3.back * movementSpeed * Time.deltaTime);
        }
        if (Input.GetKey(KeyCode.A))
        {
            transform.Translate(Vector3.left * movementSpeed * Time.deltaTime);
        }
        if (Input.GetKey(KeyCode.D))
        {
            transform.Translate(Vector3.right * movementSpeed * Time.deltaTime);
        }

        //-----------------------------------------------------------------------------------

        // Camera Rotation
        // Rotate around Global Y axis with Q and E keys
        if (Input.GetKey(KeyCode.Q))
        {
            transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.World);
        }
        if (Input.GetKey(KeyCode.E))
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        }

        // Rotate around Local Y axis with Z and C keys
        if (Input.GetKey(KeyCode.Z))
        {
            transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.Self);
        }
        if (Input.GetKey(KeyCode.C))
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }

        //-----------------------------------------------------------------------------------

        // Camera Zoom
        // Zoom in with R key
        if (Input.GetKey(KeyCode.R))
        {
            transform.Translate(Vector3.forward * zoomSpeed * Time.deltaTime);
        }
        // Zoom out with F key
        if (Input.GetKey(KeyCode.F))
        {
            transform.Translate(Vector3.back * zoomSpeed * Time.deltaTime);
        }
    }

    //====================================================================================================


    //Handles the camera zoom using the mouse scroll to zoom towards the mouse cursor
    void ZoomTowardsMouseCursor()
    {
        // Get the mouse scroll wheel input
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            // Cast a ray from the camera to the mouse cursor position
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // Check if the ray hits an object
            if (Physics.Raycast(ray, out hit, maxZoomDistance))
            {
                // Calculate the direction and distance to zoom
                Vector3 direction = hit.point - transform.position; // Direction from camera to hit point
                float zoomDistance = zoomSpeed * scroll * Time.deltaTime * 100.0f; // Distance to zoom
                Vector3 newPosition = transform.position + direction.normalized * zoomDistance; // New camera position

                // Check if the new position is within the min and max zoom distance
                if (
                    (newPosition - hit.point).magnitude > minZoomDistance // Check min zoom distance
                    && (newPosition - hit.point).magnitude < maxZoomDistance // Check max zoom distance
                )
                {
                    transform.position = newPosition; //
                }
            }
            else
            {
                // If the ray does not hit anything, zoom based on the camera's forward direction
                Vector3 newPosition =
                    transform.position
                    + transform.forward * zoomSpeed * scroll * Time.deltaTime * 100.0f;

                // Optional: You can add checks here to limit the forward and backward zoom
                transform.position = newPosition;
            }
        }
    }

    //====================================================================================================
}
