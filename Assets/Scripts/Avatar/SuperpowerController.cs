using UnityEngine;
using UnityEngine.InputSystem;

public class SuperpowerController : MonoBehaviour
{
    [Header("Fireball Settings")]
    public float fireballSpeed = 20f;

    [Header("Teleport Settings")]
    public float teleportDistance = 10f;

    [Header("Shield Settings")]
    public float shieldDuration = 5f;

    private InputAction fireballAction;
    private InputAction teleportAction;
    private InputAction shieldAction;
    private bool shieldActive = false;

    void OnEnable()
    {
        fireballAction = new InputAction(binding: "<Keyboard>/1");
        teleportAction = new InputAction(binding: "<Keyboard>/2");
        shieldAction = new InputAction(binding: "<Keyboard>/3");

        fireballAction.performed += _ => CastFireball();
        teleportAction.performed += _ => Teleport();
        shieldAction.performed += _ => ToggleShield();

        fireballAction.Enable();
        teleportAction.Enable();
        shieldAction.Enable();
    }

    void OnDisable()
    {
        fireballAction?.Disable();
        teleportAction?.Disable();
        shieldAction?.Disable();
    }

    void CastFireball()
    {
        Debug.Log("Fireball cast!");
        GameObject fireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fireball.transform.position = transform.position + transform.forward * 2f + Vector3.up * 1.5f;
        fireball.transform.localScale = Vector3.one * 0.3f;

        var renderer = fireball.GetComponent<Renderer>();
        renderer.material.color = new Color(1f, 0.3f, 0f);

        Rigidbody rb = fireball.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearVelocity = transform.forward * fireballSpeed;

        var light = fireball.AddComponent<Light>();
        light.color = new Color(1f, 0.5f, 0f);
        light.intensity = 3f;
        light.range = 5f;

        Destroy(fireball, 3f);
    }

    void Teleport()
    {
        Debug.Log("Teleported!");
        CreateTeleportEffect(transform.position);
        transform.position += transform.forward * teleportDistance;
        CreateTeleportEffect(transform.position);
    }

    void ToggleShield()
    {
        shieldActive = !shieldActive;
        Debug.Log($"Shield {(shieldActive ? "activated" : "deactivated")}!");

        if (shieldActive)
        {
            GameObject shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shield.name = "ShieldBubble";
            shield.transform.SetParent(transform);
            shield.transform.localPosition = Vector3.up * 1f;
            shield.transform.localScale = Vector3.one * 3f;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_AlphaClip", 0);
            mat.color = new Color(0.2f, 0.5f, 1f, 0.3f);
            mat.renderQueue = 3000;
            shield.GetComponent<Renderer>().material = mat;

            Destroy(shield.GetComponent<Collider>());
            Invoke(nameof(DeactivateShield), shieldDuration);
        }
        else
        {
            DeactivateShield();
        }
    }

    void DeactivateShield()
    {
        shieldActive = false;
        Transform shield = transform.Find("ShieldBubble");
        if (shield != null) Destroy(shield.gameObject);
    }

    void CreateTeleportEffect(Vector3 position)
    {
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.transform.position = position + Vector3.up;
        flash.transform.localScale = Vector3.one * 0.5f;

        var renderer = flash.GetComponent<Renderer>();
        renderer.material.color = new Color(0.5f, 0f, 1f);

        var light = flash.AddComponent<Light>();
        light.color = new Color(0.5f, 0f, 1f);
        light.intensity = 5f;
        light.range = 8f;

        Destroy(flash.GetComponent<Collider>());
        Destroy(flash, 0.5f);
    }
}