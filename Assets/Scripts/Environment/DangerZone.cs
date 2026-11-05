using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum DangerZoneState { Idle, Moving };

public class DangerZone : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 2.0f;
    [SerializeField, Min(0f)] private float initialDelayTime = 10.0f;
    [SerializeField, Min(0.1f)] private float damageInterval = 1f;
    [SerializeField, Min(1)] private int damageAmount = 10;
    [SerializeField] private Transform dangerBoundary;

    private float _currentDelayTime;
    private readonly Dictionary<ulong, float> _damageTimers = new();
    private NetworkVariable<DangerZoneState> _currentState = new(DangerZoneState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public DangerZoneState CurrentState => _currentState.Value;
    public event Action<DangerZoneState> OnStateUpdated;

    public override void OnNetworkSpawn()
    {
        _currentDelayTime = 0f;

        _currentState.OnValueChanged += HandleCurrentStateValueChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentState.OnValueChanged -= HandleCurrentStateValueChanged;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !GameManager.Instance.IsPlaying)
            return;

        //should be set state to idle when the game is finished? (no need for the moment)

        ApplyDelayTime();
        ApplyMovement();
        DetectPlayersInDangerZone();
    }

    private void ApplyMovement()
    {
        if (_currentState.Value != DangerZoneState.Moving)
            return;

        this.transform.position += (Vector3)(moveSpeed * Time.fixedDeltaTime * Vector2.up);
    }

    private void ApplyDelayTime()
    {
        if (_currentState.Value == DangerZoneState.Moving)
            return;

        _currentDelayTime += Time.fixedDeltaTime;

        if (_currentDelayTime >= initialDelayTime)
        {
            _currentState.Value = DangerZoneState.Moving;
        }
    }

    private void DetectPlayersInDangerZone()
    {
        float dangerHeight = dangerBoundary.position.y;
        foreach (var player in GameManager.Instance.Players)
        {
            PlayerHealth playerHealth = player.Value;
            ulong clientId = player.Key;

            if (playerHealth.IsDead)
            {
                _damageTimers.Remove(clientId);
                continue;
            }

            float playerBasePosition = playerHealth.PlayerCollider.bounds.min.y;
            if (playerBasePosition <= dangerHeight)
            {
                ApplyPlayerDamage(playerHealth);
            }
            else
            {
                _damageTimers.Remove(clientId);
            }
        }
    }

    private void ApplyPlayerDamage(PlayerHealth playerHealth)
    {
        ulong clientId = playerHealth.OwnerClientId;

        if(!_damageTimers.TryGetValue(clientId, out float remainingTime))
        {
            playerHealth.TakeDamage(damageAmount);
            _damageTimers[clientId] = damageInterval;
            return;
        }

        remainingTime -= Time.fixedDeltaTime;

        if (remainingTime <= 0f)
        {
            playerHealth.TakeDamage(damageAmount);
            remainingTime = damageInterval;
        }

        _damageTimers[clientId] = remainingTime;
    }

    private void HandleCurrentStateValueChanged(DangerZoneState previousValue, DangerZoneState newValue)
    {
        OnStateUpdated?.Invoke(newValue);
    }

    private void OnDrawGizmos()
    {
        if (dangerBoundary == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(-100f, dangerBoundary.position.y, 0f), new Vector3(100f, dangerBoundary.position.y, 0f));
    }
}
