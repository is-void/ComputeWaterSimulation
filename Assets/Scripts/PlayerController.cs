using System.Data.Common;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;

    [SerializeField] private CameraController cameraController;

    [Header("Movement")]
    public float maxSpeed = 6f;
    public float acceleration = 25f;
    public float deceleration = 20f;
    public float airAcceleration = 8f;

    [Header("Jump")]
    public float jumpForce = 7f;

    [Header("Grounding")]
    public float groundCheckDistance = 0.5f;
    public float groundCheckRadius = 0.5f;
    public LayerMask groundMask;

    private float inputX;
    private float inputZ;

    private bool isMoving;
    public bool isGrounded;
    private Vector3 groundNormal = Vector3.up;

    private bool isFreeMovementMode = false;

    void Start()
    {
        var action = GetComponent<PlayerInput>().actions["Look"];

        Debug.Log(action.actionMap.enabled);
        Debug.Log(action.enabled);

        action.performed += ctx =>
        {
            Debug.Log("Performed: " + ctx.ReadValue<Vector2>());
        };

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

    }

    private void FixedUpdate()
    {
        cameraController?.UpdatePos(transform.position);

        transform.rotation =
            Quaternion.Euler(0f, cameraController.GetVerticalRotation(), 0f);

        UpdateGrounding();
        MovePlayer();
    }

    private void OnDrawGizmos()
    {
        Vector3 origin = transform.position
                       + Vector3.down * 0.5f * transform.lossyScale.y
                       + Vector3.up * 0.1f;

        Gizmos.color = isGrounded ? Color.green : Color.red;

        // Draw the starting sphere
        Gizmos.DrawWireSphere(origin, groundCheckRadius);

        // Draw the ending sphere
        Vector3 end = origin + Vector3.down * groundCheckDistance;
        Gizmos.DrawWireSphere(end, groundCheckRadius);

        // Draw lines connecting the spheres
        Vector3 right = Vector3.right * groundCheckRadius;
        Vector3 left = Vector3.left * groundCheckRadius;
        Vector3 forward = Vector3.forward * groundCheckRadius;
        Vector3 back = Vector3.back * groundCheckRadius;

        Gizmos.DrawLine(origin + right, end + right);
        Gizmos.DrawLine(origin + left, end + left);
        Gizmos.DrawLine(origin + forward, end + forward);
        Gizmos.DrawLine(origin + back, end + back);

        // Draw the center line
        Gizmos.DrawLine(origin, end);
    }

    private void UpdateGrounding()
    {
        Vector3 origin = transform.position + Vector3.down * 0.85f * transform.lossyScale.y + Vector3.up * 0.1f;
        RaycastHit hit;
        if (Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out hit, 0.2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            isGrounded = true;
            
            groundNormal = hit.normal;
        }
        else
        {
            isGrounded = false;
            groundNormal = Vector3.up;
        }
    }

    private void MovePlayer()
    {
        Vector3 inputDirection;

        if (isFreeMovementMode)
        {
            // Free 3D movement based on camera forward
            Vector3 cameraForward = cameraController.transform.forward;
            Vector3 cameraRight = cameraController.transform.right;
            
            inputDirection = (cameraForward * inputZ + cameraRight * inputX);
        }
        else
        {
            // Ground-based movement
            inputDirection = (transform.forward * inputZ + transform.right * inputX);
        }

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        if (!isFreeMovementMode && isGrounded)
        {
            // Makes movement follow slopes and stairs (ground mode only)
            inputDirection = Vector3.ProjectOnPlane(inputDirection, groundNormal).normalized;
        }

        Vector3 currentVelocity = isFreeMovementMode 
            ? rb.linearVelocity 
            : Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

        Vector3 targetVelocity = inputDirection * maxSpeed;

        float accel;

        if (inputDirection.sqrMagnitude > 0.01f)
            accel = isGrounded ? acceleration : airAcceleration;
        else
            accel = deceleration;

        Vector3 newVelocity = Vector3.MoveTowards(
            currentVelocity,
            targetVelocity,
            accel * Time.fixedDeltaTime);

        if (isFreeMovementMode)
        {
            rb.linearVelocity = newVelocity;
        }
        else
        {
            rb.linearVelocity = new Vector3(
                newVelocity.x,
                rb.linearVelocity.y,
                newVelocity.z);
        }
    }

    public void OnMove(InputValue value)
    {
        Debug.Log("Moving");
        Vector2 moveInput = value.Get<Vector2>();

        inputX = moveInput.x;
        inputZ = moveInput.y;

        isMoving = moveInput.sqrMagnitude > 0.001f;
    }

    public void OnLook(InputValue value)
    {
        Debug.Log("Check");
        Vector2 lookInput = value.Get<Vector2>();

        if (cameraController != null)
            cameraController.Look(lookInput);
    }

    public void OnJump(InputValue value)
    {
        Debug.Log("Jump");
        /*if (value.canceled)
            return;
        */
        if (!isGrounded)
            return;
        

        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
    }

    public void OnInteract(InputValue value)
    {
        Debug.Log("Interact");
        ToggleFreeMovement();
    }

    private void ToggleFreeMovement()
    {
        isFreeMovementMode = !isFreeMovementMode;
        rb.useGravity = !isFreeMovementMode;
        Debug.Log(isFreeMovementMode ? "Free Movement Mode: ON" : "Free Movement Mode: OFF");
    }
}
