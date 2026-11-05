using System.Collections;
using System.Linq;
using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class LocalNetworkBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject networkRootPrefab;
    [SerializeField] private bool startHostWithoutPlayModeTag = true;

#if UNITY_EDITOR
    private IEnumerator Start()
    {
        if (NetworkManager.Singleton == null)
        {
            if (networkRootPrefab == null)
            {
                Debug.LogError("LocalMainSceneBootstrap requires NetworkRoot.prefab.", this);
                yield break;
            }

            Instantiate(networkRootPrefab);
            yield return null;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.IsListening)
            yield break;

        var tags = CurrentPlayer.Tags;
        bool started;

        if (tags.Contains("Server"))
            started = manager.StartServer();
        else if (tags.Contains("Client"))
            started = manager.StartClient();
        else if (tags.Contains("Host") || startHostWithoutPlayModeTag)
            started = manager.StartHost();
        else
            yield break;

        if (!started)
            Debug.LogError("LocalMainSceneBootstrap could not start NGO.", this);
    }
#endif
}
