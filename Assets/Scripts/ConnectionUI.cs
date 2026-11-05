using TMPro;
using UnityEngine;

public class ConnectionUI : MonoBehaviour
{
    [SerializeField] private MultiplayerConnectionManager connectionManager;
    [SerializeField] private TMP_InputField joinCodeInput;

    public async void OnCreateGamePressed()
    {
        await connectionManager.CreateGame();
    }

    public async void OnJoinGamePressed()
    {
        string code = joinCodeInput.text.Trim();

        await connectionManager.JoinGame(code);
    }

    public void OnQuitGamePressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
