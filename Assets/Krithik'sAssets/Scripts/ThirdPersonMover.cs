using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonMover : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3.5f;
    public float sprintSpeed = 6.0f;
    public float rotationSpeed = 12f;
    public float jumpHeight = 1.4f;
    public float gravity = -9.81f;

    [Header("References")]
    public Transform cameraTransform; // assign Camera.main.transform in Inspector
    public Animator animator;

    private CharacterController controller;
    private Vector2 moveInput;
    private Vector2 lookInput; // reserved if you build a custom camera
    private bool sprintHeld;
    private float yVelocity;
    private bool isGrounded;

    // Input System (if you generated the C# wrapper, else read via PlayerInput)
    private PlayerControls controls;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // Use generated wrapper for clarity
        controls = new PlayerControls();
        controls.Gameplay.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Move.canceled += ctx => moveInput = Vector2.zero;

        controls.Gameplay.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Look.canceled += ctx => lookInput = Vector2.zero;

        controls.Gameplay.Sprint.performed += ctx => sprintHeld = ctx.ReadValue<float>() > 0.5f;
        controls.Gameplay.Sprint.canceled += ctx => sprintHeld = false;

        controls.Gameplay.Jump.performed += ctx => TryJump();
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Update()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && yVelocity < 0f) yVelocity = -2f;

        // Camera-relative movement
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 desired = forward * moveInput.y + right * moveInput.x;

        float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;
        Vector3 horizontal = desired.normalized * targetSpeed;

        // Rotate toward movement
        if (desired.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(desired, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // Gravity
        yVelocity += gravity * Time.deltaTime;

        // Move
        Vector3 velocity = new Vector3(horizontal.x, 0f, horizontal.z);
        velocity.y = yVelocity;
        controller.Move(velocity * Time.deltaTime);

        // Animator params
        if (animator)
        {
            float speedParam = new Vector2(horizontal.x, horizontal.z).magnitude;
            animator.SetFloat("Speed", speedParam);
            animator.SetBool("IsGrounded", isGrounded);
        }
    }

    private void TryJump()
    {
        if (!controller.isGrounded) return;
        yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        if (animator) animator.SetTrigger("Jump");
    }
}
