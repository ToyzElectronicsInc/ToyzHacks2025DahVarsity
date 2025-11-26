using UnityEngine;
using UnityEngine.UI;

public class EnterChat : MonoBehaviour
{
    public InputField UserIdInput;
    public Button EnterChatButton;
    public Text StatusText;

    private void Start()
    {
        EnterChatButton.onClick.AddListener(OnEnterChatClicked);
    }

    private void OnEnterChatClicked()
    {
        SceneManager.LoadScene("ChatScene");
    }
}