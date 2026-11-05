using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameplayPlayerSpawner : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Color32[] spawnColors =
    {
        new(96, 165, 250, 255),  // Blue
        new(248, 113, 113, 255), // Red
        new(74, 222, 128, 255),  // Green
        new(251, 191, 36, 255)   // Yellow
    };

    [Header("Spawn Points")]
    [SerializeField] private Transform[] spawnPoints;

    private readonly HashSet<ulong> _spawnedClients = new();

    private bool _ready;

    private void Start()
    {
        StartCoroutine(InitializeSpawner());
    }

    private void OnEnable()
    {
        PlayerSession.OnServerSessionSpawned += HandleSessionSpawned;
    }

    private void OnDisable()
    {
        PlayerSession.OnServerSessionSpawned -= HandleSessionSpawned;
    }

    private IEnumerator InitializeSpawner()
    {
        // Wait until NGO and GameManager are ready.
        yield return new WaitUntil(() =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            GameManager.Instance != null &&
            GameManager.Instance.IsSpawned
        );

        if (!NetworkManager.Singleton.IsServer)
            yield break;

        if (playerPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("GameplayPlayerSpawner is not configured.", this);
            yield break;
        }

        _ready = true;

        // Register existing session before the scene is loaded.
        PlayerSession[] sessions = FindObjectsByType<PlayerSession>(FindObjectsSortMode.None);

        Array.Sort(sessions, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        foreach (PlayerSession session in sessions)
        {
            TrySpawnPlayer(session);
        }
    }

    private void HandleSessionSpawned(PlayerSession session)
    {
        if (!_ready)
            return;

        TrySpawnPlayer(session);
    }

    private void TrySpawnPlayer(PlayerSession session)
    {
        Debug.Log(
            $"[Spawner] ClientId: {session.OwnerClientId}, " +
            $"GameState: {GameManager.Instance.CurrentState}, " +
            $"SessionSpawned: {session.IsSpawned}, " +
            $"HasAvatar: {session.HasSpawnedAvatar}"
        );

        if (!NetworkManager.Singleton.IsServer)
            return;

        if (GameManager.Instance.CurrentState != GameState.Waiting)
            return;

        if (!session.IsSpawned || session.HasSpawnedAvatar)
            return;

        ulong clientId = session.OwnerClientId;

        if (_spawnedClients.Contains(clientId))
            return;

        int spawnIndex = _spawnedClients.Count % spawnPoints.Length;
        Vector3 spawnPosition = spawnPoints[spawnIndex].position;

        NetworkObject avatar = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

        avatar.SpawnWithOwnership(clientId);

        PlayerColor playerColor = avatar.GetComponent<PlayerColor>();
        int colorIndex = spawnIndex % spawnColors.Length;
        playerColor.SetColor(spawnColors[colorIndex]);

        session.AssignAvatarServer(avatar);

        _spawnedClients.Add(clientId);

        Debug.Log($"Avatar spawned for Client {clientId}");
    }
}
