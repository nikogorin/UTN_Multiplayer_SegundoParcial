using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_Text connectedPlayersInfoText;
    [SerializeField] private TMP_Text roomHostNameText;

    private MultiplayerConnectionManager _multiplayerConnectionManager;
    private Coroutine _refreshRoomInfoCoroutine;

    private void Start()
    {
        Debug.Log($"WaitingRoom started. ConnectionManager: {MultiplayerConnectionManager.Instance}");
        Debug.Log($"Manager null: {_multiplayerConnectionManager == null}");
        Debug.Log($"Session null: {_multiplayerConnectionManager?.Session == null}");

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        _multiplayerConnectionManager = MultiplayerConnectionManager.Instance;
        joinCodeText.text = _multiplayerConnectionManager.JoinCode;
        roomHostNameText.text = _multiplayerConnectionManager.RoomHostDisplayName;

        RefreshPlayers();
    }

    private void OnEnable()
    {
        RefreshRoomInfo();
    }

    //private void OnEnable()
    //{
    //    _multiplayerConnectionManager = MultiplayerConnectionManager.Instance;

    //    NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    //    NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

    //    _refreshRoomInfoCoroutine = StartCoroutine(WaitForRoomInfo());
    //    RefreshPlayers();
    //}

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    //private void OnDisable()
    //{
    //    if (_refreshRoomInfoCoroutine != null)
    //    {
    //        StopCoroutine(_refreshRoomInfoCoroutine);
    //        _refreshRoomInfoCoroutine = null;
    //    }

    //    if (NetworkManager.Singleton != null)
    //    {
    //        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    //        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    //    }
    //}

    private void RefreshRoomInfo()
    {
        _multiplayerConnectionManager = MultiplayerConnectionManager.Instance;
        if (_multiplayerConnectionManager == null)
            return;

        string joinCode = _multiplayerConnectionManager.JoinCode;

        //Debug.Log($"UI room code: '{_multiplayerConnectionManager.JoinCode}'");
        Debug.Log($"[WaitingRoom] Manager ID: {_multiplayerConnectionManager.GetInstanceID()} | " + $"Session null: {_multiplayerConnectionManager.Session == null} | " + $"Code: '{_multiplayerConnectionManager.JoinCode}'");

        joinCodeText.text = joinCode;
        roomHostNameText.text = _multiplayerConnectionManager.RoomHostDisplayName;
    }

    private void RefreshPlayers()
    {
        connectedPlayersInfoText.text = $"{_multiplayerConnectionManager.ConnectedPlayersCount} / {_multiplayerConnectionManager.MaxPlayers}";
    }

    private void OnClientConnected(ulong clientId)
    {
        RefreshPlayers();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        RefreshPlayers();
    }

    private IEnumerator WaitForRoomInfo()
    {
        while (_multiplayerConnectionManager == null || string.IsNullOrEmpty(_multiplayerConnectionManager.JoinCode))
        {
            _multiplayerConnectionManager = MultiplayerConnectionManager.Instance;

            yield return null;
        }

        RefreshRoomInfo();
        _refreshRoomInfoCoroutine = null;
    }

    public void OnStartGamePressed()
    {
        _multiplayerConnectionManager.LoadGameScene();
    }

    public async void OnLeaveLobbyPressed()
    {
        await MultiplayerConnectionManager.Instance.LeaveGame();
    }
}
