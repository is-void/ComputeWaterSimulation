using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;
    [SerializeField] CameraController cameraController;
    public float speed = 2f;
    public float maxSpeed = 5f;
    float inputX;
    float inputZ;
    bool isMoving = false;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        rb = GetComponent<Rigidbody>();
        cameraController = FindAnyObjectByType<CameraController>();

       
    }
    private void FixedUpdate()
    {
        cameraController?.UpdatePos(transform.position);
        transform.localRotation = Quaternion.Euler(Vector3.up * cameraController.GetVerticalRotation());
        if(isMoving && rb.linearVelocity.magnitude < maxSpeed)
            rb.linearVelocity += (transform.forward * inputZ + transform.right * inputX).normalized * speed * Time.fixedDeltaTime;
    }


    public void OnMove(InputAction.CallbackContext value)
    {
        switch(value.phase)
        {
            case InputActionPhase.Started:
            case InputActionPhase.Performed:
                isMoving = true; 
                break;
            case InputActionPhase.Disabled:
            case InputActionPhase.Canceled:
                isMoving = false;
                break;
            default:
                isMoving = false;
                break;
        }
        Vector2 moveInput = value.ReadValue<Vector2>();
        inputX = moveInput.x;
        inputZ = moveInput.y * 0.5f;
    }

    public void OnLook(InputAction.CallbackContext value)
    {
        Vector2 lookInput = value.ReadValue<Vector2>();
        if (cameraController != null)
        {
            cameraController.Look(lookInput);
        }
        
    }

    public void OnInteract(InputAction.CallbackContext value)
    {
        if (value.performed)
        {
            // Handle interact
        }
    }
}
