using UnityEngine;
using System.Collections;

public class PowerController : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public CharacterController controller;
    public Transform muzzle; 

    [Header("Projectile Settings")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 20f;
    public float shootDelay = 0.3f; // Delay to sync with animation
    public float angleOffset = 0f;   

 


    [Header("Hover Settings")]
    public float hoverDuration = 2f;
    public float hoverHeight = 2f;
    public GameObject hoverSmokePrefab; 
    public ParticleSystem hoverGlowEffect1;  
    public ParticleSystem hoverGlowEffect2;

    private GameObject activeHoverSmoke;
    private GameObject activeHoverGlow;

    void Update()
    {
        
        if (Input.GetKeyDown(KeyCode.E))
        {
            animator.SetTrigger("Power1");
            StartCoroutine(DelayedShoot());
        }

        
        if (Input.GetKeyDown(KeyCode.R))
        {
            animator.SetTrigger("Power2");
            StartCoroutine(HoverRoutine());
        }
    }

    // Delayed projectile spawn
    IEnumerator DelayedShoot()
    {
        yield return new WaitForSeconds(shootDelay);

        if (projectilePrefab == null || muzzle == null) yield break;

        
        Quaternion rotation = muzzle.rotation * Quaternion.Euler(0f, angleOffset, 0f);

        GameObject proj = Instantiate(projectilePrefab, muzzle.position, rotation);

        
        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = rotation * Vector3.forward * projectileSpeed;
        }

        // Ignores collision with player
        Collider projCol = proj.GetComponent<Collider>();
        Collider playerCol = GetComponent<Collider>();
        if (projCol != null && playerCol != null)
        {
            Physics.IgnoreCollision(projCol, playerCol);
        }
    }

    
    IEnumerator HoverRoutine()
    {
        // Spawn smoke
        if (hoverSmokePrefab)
        {
            activeHoverSmoke = Instantiate(hoverSmokePrefab, transform.position, Quaternion.identity);
            activeHoverSmoke.transform.parent = transform;
        }

        if (hoverGlowEffect1)
        {
            hoverGlowEffect1.gameObject.SetActive(true);
            hoverGlowEffect1.Play();
        }

        // Enable second glow effect
        if (hoverGlowEffect2)
        {
            hoverGlowEffect2.gameObject.SetActive(true);
            hoverGlowEffect2.Play();
        }

        yield return new WaitForSeconds(hoverDuration);

        
        if (activeHoverSmoke)
            Destroy(activeHoverSmoke);

        if (hoverGlowEffect1)
        {
            hoverGlowEffect1.Stop();
            hoverGlowEffect1.gameObject.SetActive(false);
        }

        // Disable second glow effect
        if (hoverGlowEffect2)
        {
            hoverGlowEffect2.Stop();
            hoverGlowEffect2.gameObject.SetActive(false);
        }
    }



}
