using UnityEngine;
using GLTFast;
using System.Threading.Tasks;

public class MetaPersonLoader : MonoBehaviour
{
    [Header("Avatar Settings")]
    public string avatarUrl = "";

    private GameObject currentAvatar;
    private bool isLoading = false;

    public async Task LoadAvatar(string url)
    {
        if (isLoading) return;
        isLoading = true;

        Debug.Log($"Loading avatar from: {url}");

        if (currentAvatar != null)
            Destroy(currentAvatar);

        currentAvatar = new GameObject("MetaPersonAvatar");
        currentAvatar.transform.position = transform.position;

        var gltfImport = new GltfImport();
        bool success = await gltfImport.Load(url);

        if (success)
        {
            await gltfImport.InstantiateMainSceneAsync(currentAvatar.transform);
            Debug.Log("Avatar loaded and instantiated!");
            currentAvatar.AddComponent<SuperpowerController>();
        }
        else
        {
            Debug.LogError($"Failed to load avatar from: {url}");
        }

        isLoading = false;
    }

    public void LoadAvatarFromUrl(string url)
    {
        avatarUrl = url;
        _ = LoadAvatar(url);
    }
}