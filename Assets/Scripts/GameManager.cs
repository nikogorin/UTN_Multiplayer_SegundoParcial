using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum GameState { Waiting, Playing, Finished }

public class GameManager : NetworkBehaviour
{
#if UNITY_EDITOR
    [Header("Development")]
    [SerializeField] private bool autoStartForTesting = false;
    [SerializeField, Min(2)] private int testPlayerCount = 2;
#endif
    private NetworkVariable<GameState> _gameState = new (GameState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> _winnerClientId = new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _alivePlayerCount = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly Dictionary<ulong, PlayerHealth> _players = new();
    private int _expectedPlayerCount;
    private bool _sceneLoaded = false;

    public static GameManager Instance { get; private set; }
    public ulong WinnerClientId => _winnerClientId.Value;
    public GameState CurrentState => _gameState.Value;
    public bool IsPlaying => _gameState.Value == GameState.Playing;
    public IReadOnlyDictionary<ulong, PlayerHealth> Players => _players;
    public int AlivePlayersCount => _alivePlayerCount.Value;
    
    public event Action<GameState> OnGameStateChanged;
    public event Action<int> OnAlivePlayersCountChanged;
    public event Action<ulong> OnWinnerChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Multiple GameManager instances detected.");
            return;
        }
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        _gameState.OnValueChanged += HandleGameStateChanged;
        _alivePlayerCount.OnValueChanged += HandleAlivePlayersCountChanged;
        _winnerClientId.OnValueChanged += HandleWinnerChanged;
    }

    public override void OnNetworkDespawn()
    {
        _gameState.OnValueChanged -= HandleGameStateChanged;
        _alivePlayerCount.OnValueChanged -= HandleAlivePlayersCountChanged;
        _winnerClientId.OnValueChanged -= HandleWinnerChanged;
    }

    private void UpdateAlivePlayerCount()
    {
        if(!IsServer) 
            return;

        int playerCount = 0;

        foreach(var player in _players.Values)
        {
            if(!player.IsDead)
                playerCount++;
        }

        _alivePlayerCount.Value = playerCount;

    }

    public void RegisterPlayer(PlayerHealth playerHealth)
    {
        if (!IsServer) 
            return;

        if(_players.TryAdd(playerHealth.OwnerClientId, playerHealth))
        {
            Debug.Log($"Register Player: {playerHealth.OwnerClientId}");
        }

        UpdateAlivePlayerCount();
        CheckAllPlayersReady();
    }

    public void UnregisterPlayer(PlayerHealth playerHealth)
    {
        if (!IsServer)
            return;

        if(_players.Remove(playerHealth.OwnerClientId))
        {
            UpdateAlivePlayerCount();
            CheckGameOver();
        }
    }

    public void RegisterPlayerDeath(ulong clientId, PlayerHealth player)
    {
        if (!IsServer)
            return;

        if (!_players.ContainsKey(clientId))
            return;

        Debug.Log($"Player {clientId} died");

        UpdateAlivePlayerCount();
        CheckGameOver();
    }

    public void SetSceneLoaded(int expectedPlayerCount)
    {
        if (!IsServer)
            return;

        _expectedPlayerCount = expectedPlayerCount;
        _sceneLoaded = true;

        CheckAllPlayersReady();
    }

    private void CheckGameOver()
    {
        if(!IsServer || _gameState.Value != GameState.Playing)
            return;

        PlayerHealth lastAlive = null;
        int aliveCount = 0;

        foreach (var player in _players.Values)
        {
            if(player.IsDead)
                continue;

            aliveCount++;
            lastAlive = player;
        }

        if(aliveCount == 1)
        {
            EndGame(lastAlive.OwnerClientId);
        }
        else if(aliveCount == 0)
        {
            EndGame(null);
        }
    }

    private void EndGame(ulong? winnerClientId)
    {
        if (!IsServer || _gameState.Value != GameState.Playing)
            return;

        _winnerClientId.Value = winnerClientId ?? ulong.MaxValue;
        _gameState.Value = GameState.Finished;
    }

    private void StartGame()
    {
        _gameState.Value = GameState.Playing;
        Debug.Log("Game State: Playing");
    }

    private void CheckAllPlayersReady()
    {
        if (!IsServer || _gameState.Value != GameState.Waiting)
            return;

#if UNITY_EDITOR
        if (autoStartForTesting)
        {
            if (_players.Count < testPlayerCount)
                return;

            StartGame();
            return;
        }
#endif

        if (!_sceneLoaded || _expectedPlayerCount < 2)
            return;

        if (_players.Count != _expectedPlayerCount)
            return;

        StartGame();
    }

    private void HandleGameStateChanged(GameState previous, GameState current)
    {
        OnGameStateChanged?.Invoke(current);
    }

    private void HandleAlivePlayersCountChanged(int previous, int newValue)
    {
        OnAlivePlayersCountChanged?.Invoke(newValue);
    }

    private void HandleWinnerChanged(ulong previous, ulong newValue)
    {
            Debug.Log($"[GameManager] Client: {NetworkManager.LocalClientId}, " + $"WinnerId: {newValue}");

        OnWinnerChanged?.Invoke(newValue);
    }

    /// <summary>
    /// For testing purposes
    /// </summary>
    /// <returns></returns>
    IEnumerator WaitForTenSeconds()
    {
        Debug.Log("Inicio de la corrutina...");
        yield return new WaitForSeconds(5f);

        _gameState.Value = GameState.Finished;
        Debug.Log("Fin de la corrutina...");
    }
}
