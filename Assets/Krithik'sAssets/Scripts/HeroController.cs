using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class HeroController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Walking speed of the avatar.")]
    public float walkSpeed = 6f;
    [Tooltip("Sprinting speed of the avatar.")]
    public float sprintSpeed = 12f;
    [Tooltip("Rotation smoothing factor.")]
    public float rotationSpeed = 12f;
    [Tooltip("Jump height in Unity units.")]
    public float jumpHeight = 1.4f;
    [Tooltip("Gravity applied to the player.")]
    public float gravity = -9.81f;

    [Header("References")]
    [Tooltip("Reference to the main camera.")]
    public Transform cameraTransform;
    [Tooltip("Animator controlling the avatar animations.")]
    public Animator animator;
    private CharacterController controller;

    [Header("Projectile Settings (Power1)")]
    [Tooltip("Where projectiles spawn from.")]
    public Transform muzzle;
    [Tooltip("Projectile prefab to fire.")]
    public GameObject projectilePrefab;
    [Tooltip("Speed at which projectiles travel.")]
    public float projectileSpeed = 20f;
    [Tooltip("Delay to sync projectile spawn with animation.")]
    public float shootDelay = 0.3f;
    [Tooltip("offset when firing projectiles.")]
    public float angleOffset = 0f;

    [Header("Hover Settings (Power2)")]
    [Tooltip("How long hover lasts")]
    public float hoverDuration = 2f;
    [Tooltip("Hover height offset")]
    public float hoverHeight = 2f;
    [Tooltip("Smoke prefab while hovering.")]
    public GameObject hoverSmokePrefab;
    [Tooltip("glow effect 1.")]
    public ParticleSystem hoverGlowEffect1;
    [Tooltip("glow effect 2.")]
    public ParticleSystem hoverGlowEffect2;

    [Header("Jump Effects")]
    [Tooltip("Particle system plays on jump.")]
    public GameObject jumpBurstVFX;
    [Tooltip("Duration of jump burst.")]
    public float jumpBurstDuration = 1.5f;

    // private variables
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool sprintHeld;
    private float yVelocity;
    private bool isGrounded;
    private PlayerControls controls;

    private GameObject activeHoverSmoke;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Camera fallback
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Setup input bindings through new unity input system
        controls = new PlayerControls();

        controls.Gameplay.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Move.canceled += ctx => moveInput = Vector2.zero;

        controls.Gameplay.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Look.canceled += ctx => lookInput = Vector2.zero;

        controls.Gameplay.Sprint.performed += ctx => sprintHeld = ctx.ReadValue<float>() > 0.5f;
        controls.Gameplay.Sprint.canceled += ctx => sprintHeld = false;

        controls.Gameplay.Jump.performed += ctx => TryJump();
        controls.Gameplay.Power1.performed += ctx => DoPower1();
        controls.Gameplay.Power2.performed += ctx => DoPower2();
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Update()
    {
        HandleMovement();
        HandleAnimation();
    }

    #region Movement
    private void HandleMovement()
    {
        // Ground Check
        // Raycast to avoid floating precision issues
        isGrounded = controller.isGrounded; if (isGrounded && yVelocity < 0f) yVelocity = -2f;

        // Calculata desired direction
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 desired = forward * moveInput.y + right * moveInput.x;

        float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;
        Vector3 horizontal = desired.normalized * targetSpeed;

        
        if (desired.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(desired, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        
        yVelocity += gravity * Time.deltaTime;

        
        Vector3 velocity = new Vector3(horizontal.x, 0f, horizontal.z);
        velocity.y = yVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
    #endregion

    #region Animation
    private void HandleAnimation()
    {
        if (animator == null) return;

        float speedParam = new Vector2(moveInput.x, moveInput.y).magnitude;
        animator.SetFloat("Speed", speedParam);
        animator.SetBool("IsGrounded", isGrounded);
    }
    #endregion

    #region Jump
    private void TryJump() { 
        if (!controller.isGrounded) return; 
        yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity); 
        if (animator) animator.SetTrigger("Jump"); 
        if (jumpBurstVFX != null) { 
            jumpBurstVFX.SetActive(true); 
            ParticleSystem ps = jumpBurstVFX.GetComponent<ParticleSystem>(); 
            if (ps != null) 
            { 
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); 
                ps.Play();
            } 
            
            StartCoroutine(DisableJumpBurst());
        } 
    }
    private IEnumerator DisableJumpBurst()
    {
        yield return new WaitForSeconds(jumpBurstDuration);
        if (jumpBurstVFX != null)
            jumpBurstVFX.SetActive(false);
    }
    #endregion

    #region Power1: Projectile
    private void DoPower1()
    {
        if (animator) animator.SetTrigger("Power1");
        StartCoroutine(DelayedShoot());
    }

    private IEnumerator DelayedShoot()
    {
        yield return new WaitForSeconds(shootDelay);

        if (projectilePrefab == null || muzzle == null) yield break;

        Quaternion rotation = muzzle.rotation * Quaternion.Euler(0f, angleOffset, 0f);
        GameObject proj = Instantiate(projectilePrefab, muzzle.position, rotation);

        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = rotation * Vector3.forward * projectileSpeed;

        // Ignores collision with player’s CharacterController collider
        Collider projCol = proj.GetComponent<Collider>();
        Collider playerCol = controller != null ? controller.GetComponent<Collider>() : null;
        if (projCol != null && playerCol != null)
            Physics.IgnoreCollision(projCol, playerCol);
    }
    #endregion

    #region Power2: Hover
    private void DoPower2()
    {
        if (animator) animator.SetTrigger("Power2");
        StartCoroutine(HoverRoutine());
    }

    private IEnumerator HoverRoutine()
    {
        // Smokee
        if (hoverSmokePrefab)
        {
            activeHoverSmoke = Instantiate(hoverSmokePrefab, transform.position, Quaternion.identity, transform);
        }

        // Glow ball effects on either hand
        if (hoverGlowEffect1)
        {
            hoverGlowEffect1.gameObject.SetActive(true);
            hoverGlowEffect1.Play();
        }
        if (hoverGlowEffect2)
        {
            hoverGlowEffect2.gameObject.SetActive(true);
            hoverGlowEffect2.Play();
        }

        yield return new WaitForSeconds(hoverDuration);

        // Enablingg and disabling effects
        if (activeHoverSmoke) Destroy(activeHoverSmoke);

        if (hoverGlowEffect1)
        {
            hoverGlowEffect1.Stop();
            hoverGlowEffect1.gameObject.SetActive(false);
        }
        if (hoverGlowEffect2)
        {
            hoverGlowEffect2.Stop();
            hoverGlowEffect2.gameObject.SetActive(false);
        }
    }
    #endregion
}
