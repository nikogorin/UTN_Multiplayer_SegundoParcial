using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private GameObject visual;

    public bool IsDead => _currentHealth.Value <= 0;
    public Collider2D PlayerCollider { get; private set; }
    public int CurrentHealth => _currentHealth.Value;
    public int MaxHealth => maxHealth;
    public event Action<int, int> OnHealthUpdated;

    private NetworkVariable<int> _currentHealth = new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Collider2D _playerCollider;

    private void Awake()
    {
        PlayerCollider = GetComponent<Collider2D>();
    }

    public override void OnNetworkSpawn()
    {
        _currentHealth.OnValueChanged += OnHealthChanged;

        _playerCollider = GetComponent<Collider2D>();

        if (IsServer)
        {
            _currentHealth.Value = maxHealth;
            GameManager.Instance.RegisterPlayer(this);
        }
        
        UpdateDeathState();
    }

    public override void OnNetworkDespawn()
    {
        _currentHealth.OnValueChanged -= OnHealthChanged;

        if (IsServer && GameManager.Instance != null)
            GameManager.Instance.UnregisterPlayer(this);
    }

    public void TakeDamage(int damage)
    {
        if (!IsServer)
            return;

        if (damage <= 0 || _currentHealth.Value <= 0)
            return;

        _currentHealth.Value = Mathf.Max(_currentHealth.Value - damage, 0);

        if (_currentHealth.Value == 0)
            Die();
    }

    private void Die()
    {
        if(!IsServer) 
            return;

        GameManager.Instance.RegisterPlayerDeath(OwnerClientId, this);
    }

    private void OnHealthChanged(int previousHealth, int currentHealth)
    {
        UpdateDeathState();
        OnHealthUpdated?.Invoke(currentHealth, maxHealth);
    }

    private void UpdateDeathState()
    {
        bool isAlive = !IsDead;

        visual.SetActive(isAlive);
        _playerCollider.enabled = isAlive;
    }
}
