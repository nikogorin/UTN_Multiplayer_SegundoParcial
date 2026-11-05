using System.Linq;
using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    void Start()
    {
        var tags = CurrentPlayer.Tags;
        if (tags.Contains("Host"))
        {
            NetworkManager.Singleton.StartHost();
        }
        else if (tags.Contains("Server"))
        {
            NetworkManager.Singleton.StartServer();
        }
        else if (tags.Contains("Client"))
        {
            NetworkManager.Singleton.StartClient();
        }
        else
        {
            Debug.Log("No configuration detected...");
        }
    }

}
