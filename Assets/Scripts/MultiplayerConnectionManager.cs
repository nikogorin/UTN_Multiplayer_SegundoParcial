using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;

public enum ConnectionMode
{
    Local,
    Relay
}

public class MultiplayerConnectionManager : MonoBehaviour
{
    [SerializeField] private ConnectionMode editorConnectionMode = ConnectionMode.Local;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private int minPlayers = 2;

    private ISession _session;
    private bool _isConnecting;
    private bool _isLeaving;

    public ISession Session => _session;
    public string JoinCode => _session?.Code ?? string.Empty;
    public ConnectionMode ConnectionMode => Application.isEditor ? editorConnectionMode : ConnectionMode.Relay;
    public static MultiplayerConnectionManager Instance { get; private set; }
    public int ConnectedPlayersCount
    {
        get
        {
            if (_session != null)
                return _session.Players.Count;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                return NetworkManager.Singleton.ConnectedClientsIds.Count;
            }

            return 0;
        }
    }

    public int MaxPlayers
    {
        get
        {
            if (_session != null)
                return _session.MaxPlayers;

            return maxPlayers;
        }
    }

    public string RoomHostDisplayName
    {
        get
        {
            if (_session != null)
                return _session.Host;

            return "Local Host";
        }
    }

    public int ExpectedPlayerCount { get; private set; }

    private void Awake()
    {
        Debug.Log($"MultiplayerConnectionManager Awake - Instance: {Instance}, Object: {gameObject.name}");

        if (Instance != null && Instance != this)
        {
            Debug.Log("Duplicate MultiplayerConnectionManager destroyed");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("MultiplayerConnectionManager Instance assigned");
    }

    private void Start()
    {
        if(NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        Debug.Log($"[ConnectionManager] Destroy ID: {GetInstanceID()} | " + $"Session null: {_session == null} | " + $"Scene: {gameObject.scene.name}");

        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null)
        {
            manager.OnClientDisconnectCallback -= HandleClientDisconnected;

            if (manager.SceneManager != null)
            {
                manager.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        if (Instance == this)
            Instance = null;
    }

    public async Task CreateGame()
    {
        if (_isConnecting || _isLeaving || NetworkManager.Singleton.IsListening)
            return;

        _isConnecting = true;

        try
        {
            switch (ConnectionMode)
            {
                case ConnectionMode.Local:
                    StartLocalHost();
                    break;

                case ConnectionMode.Relay:
                    await CreateRelayGame();
                    break;
            }
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public async Task JoinGame(string joinCode)
    {
        if (_isConnecting || _isLeaving || NetworkManager.Singleton.IsListening)
            return;

        _isConnecting = true;

        try
        {

            switch (ConnectionMode)
            {
                case ConnectionMode.Local:
                    StartLocalClient();
                    break;

                case ConnectionMode.Relay:
                    await JoinRelayGame(joinCode);
                    break;
            }
        }
        finally 
        { 
            _isConnecting = false; 
        }
    }

    public async Task LeaveGame()
    {
        if (_isLeaving)
            return;

        NetworkManager manager = NetworkManager.Singleton;

        _isLeaving = true;

        try
        {
            // leave unity service session
            if (_session != null)
            {
                ISession session = _session;
                _session = null;

                try
                {
                    await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // close NGO if is still active
            if (manager != null)
            {
                if (manager.IsListening && !manager.ShutdownInProgress)
                    manager.Shutdown();

                // wait until close.
                while (manager != null && manager.ShutdownInProgress)
                    await Task.Yield();

                // left pass though an aditional update.
                await Task.Yield();
            }

            ExpectedPlayerCount = 0;

            // NGO has already completed its shutdown at this point.
            Debug.Log($"Destroying NetworkRootManager ID: {GetInstanceID()}");
            Destroy(gameObject);
            // Allow Unity to process the destruction before loading
            // a scene containing a new NetworkRootManager.
            await Task.Yield();

            UnityEngine.SceneManagement.SceneManager.LoadScene("ConnectionScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            _isLeaving = false;
        }
    }

    private void StartLocalHost()
    {
        bool started = NetworkManager.Singleton.StartHost();
        if (!started)
        {
            Debug.LogError("Failed to start local host.");
            return;
        }

        LoadWaitingRoomScene();
    }

    private void StartLocalClient()
    {
        bool started = NetworkManager.Singleton.StartClient();
        if (!started)
        {
            Debug.LogError("Failed to start local client.");
        }
    }

    private async Task CreateRelayGame()
    {
        try
        {
            var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();

            _session = await MultiplayerService.Instance.CreateSessionAsync(options);

            Debug.Log("Session created...");
            Debug.Log($"[CreateRelayGame] Manager ID: {GetInstanceID()} | " + $"Session null: {_session == null} | " + $"Code: '{JoinCode}'");

            LoadWaitingRoomScene();
        }
        catch (System.Exception e)
        {

            Debug.LogException(e);
        }
    }

    private async Task JoinRelayGame(string joinCode)
    {
        try
        {
            _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void LoadWaitingRoomScene()
    {
        if(!NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("Only the Host can change network scenes.");
            return;
        }

        var status = NetworkManager.Singleton.SceneManager.LoadScene("WaitingRoomScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            Debug.LogError($"Failed to load WaitingRoomScene: {status}");
    }

    public void LoadGameScene()
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("Only the Host can change network scenes.");
            return;
        }

        int sessionPlayers = ConnectedPlayersCount;
        int ngoPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

        if (sessionPlayers != ngoPlayers)
        {
            Debug.LogWarning($"Players not synchronized. Sessions: {sessionPlayers}, NGO: {ngoPlayers}");
            return;
        }

        if (sessionPlayers < minPlayers || sessionPlayers > MaxPlayers)
        {
            Debug.LogWarning($"Cannot start game with {sessionPlayers} players." );
            return;
        }

        NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
        NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;

        ExpectedPlayerCount = sessionPlayers;

        var status = NetworkManager.Singleton.SceneManager.LoadScene("GameplayScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;

            Debug.LogError($"Failed to load GameplayScene: {status}");
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        if (sceneEvent.SceneEventType != SceneEventType.LoadEventCompleted)
            return;

        if (sceneEvent.SceneName != "GameplayScene")
            return;

        NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;

        if (sceneEvent.ClientsThatTimedOut.Count > 0)
        {
            Debug.LogError($"GameplayScene load failed. " + $"Timed out clients: {string.Join(", ", sceneEvent.ClientsThatTimedOut)}");
            return;
        }

        if (GameManager.Instance == null || !GameManager.Instance.IsSpawned)
        {
            Debug.LogError("GameManager is not ready.");
            return;
        }

        Debug.Log($"GameplayScene loaded. Expected players: {ExpectedPlayerCount}");

        GameManager.Instance.SetSceneLoaded(ExpectedPlayerCount);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || manager.IsServer)
            return;

        if (clientId != manager.LocalClientId)
            return;

        if (_isLeaving)
            return;

        Debug.LogWarning("Connection to the server lost.");

        _ = HandleUnexpectedDisconnectAsync();
    }

    private async Task HandleUnexpectedDisconnectAsync()
    {
        try
        {
            await LeaveGame();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
