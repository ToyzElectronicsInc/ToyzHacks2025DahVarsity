using UnityEngine;

public class EnergyProjectile : MonoBehaviour
{
    public float lifeTime = 5f;
    public float hitForce = 5f;
    public string playerTag = "Player"; 

    void Start()
    {
        
        Destroy(gameObject, lifeTime);

        // Ignores collision with the player
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player != null)
        {
            Collider projCol = GetComponent<Collider>();
            Collider playerCol = player.GetComponent<Collider>();
            if (projCol != null && playerCol != null)
            {
                Physics.IgnoreCollision(projCol, playerCol);
            }
        }
    }

    void OnCollisionEnter(Collision other)
    {
        Rigidbody rb = other.rigidbody;
        if (rb != null)
        {
            // Applies knockback force
            rb.AddForce(other.contacts[0].normal * -hitForce, ForceMode.Impulse);
        }

        
        Destroy(gameObject);
    }
}
