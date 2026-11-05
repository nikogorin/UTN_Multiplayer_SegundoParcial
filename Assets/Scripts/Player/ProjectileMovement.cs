using Unity.Netcode;
using UnityEngine;

public struct ProjectileSpawnData : INetworkSerializable
{
    public bool Initialized;
    public Vector2 SpawnPosition;
    public Vector2 Direction;
    public double SpawnTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Initialized);
        serializer.SerializeValue(ref SpawnPosition);
        serializer.SerializeValue(ref Direction);
        serializer.SerializeValue(ref SpawnTime);
    }
}

public class ProjectileMovement : NetworkBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private float lifetime = 2f;
    [SerializeField] private int damage = 20;
    [SerializeField, Min(0f)] private float knockbackForce = 16f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask environmentLayer;

    [Header("Network Compensation")]
    [SerializeField, Min(0f)] private float maxCompensationTime = 0.3f;

    private readonly NetworkVariable<ProjectileSpawnData> _spawnData = new(new ProjectileSpawnData(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Rigidbody2D _rigidbody;
    private bool _movementInitialized;
    private Vector2 _spawnPosition;
    private float _currentLifeTime = 0;
    private ulong _shooterClientId = 0;
    private SpriteRenderer[] _renderers;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();

        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        SetProjectileVisible(false);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !_movementInitialized)
            return;

        Vector2 direction = _spawnData.Value.Direction;
        _rigidbody.MovePosition(_rigidbody.position + speed * Time.fixedDeltaTime * direction);

        // life time is managed only by the server
        if (!IsServer)
            return;

        _currentLifeTime += Time.fixedDeltaTime;

        if (_currentLifeTime >= lifetime)
            NetworkObject.Despawn();
    }

    public override void OnNetworkSpawn()
    {
        _spawnPosition = _rigidbody.position;

        _spawnData.OnValueChanged += OnSpawnDataChanged;

        // apply values that are already present when the OnNetworkSpawn is executed
        if (!IsServer && _spawnData.Value.Initialized)
        {
            InitializeClientMovement(_spawnData.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _spawnData.OnValueChanged -= OnSpawnDataChanged;
    }

    private void OnSpawnDataChanged(ProjectileSpawnData previous, ProjectileSpawnData current)
    {
        if (IsServer || !current.Initialized)
            return;

        InitializeClientMovement(current);
    }

    public void Initialize(ulong shooterClientId, Vector2 direction)
    {
        if (!IsServer)
            return;

        _shooterClientId = shooterClientId;

        _spawnData.Value = new ProjectileSpawnData
        {
            Initialized = true,
            SpawnPosition = _rigidbody.position,
            Direction = direction.normalized,
            SpawnTime = NetworkManager.ServerTime.Time
        };

        _movementInitialized = true;
        SetProjectileVisible(true);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer)
            return;

        if (IsInLayerMask(other.gameObject, playerLayer))
        {
            HandlePlayerCollision(other);
        }
        else if (IsInLayerMask(other.gameObject, environmentLayer))
        {
            NetworkObject.Despawn();
            //call clients to apply other effects if it is needed
        }
    }

    private void HandlePlayerCollision(Collider2D other)
    {
        var networkObject = other.GetComponentInParent<NetworkObject>();

        if (networkObject == null)
            return;

        // Ignores the player who fired
        if (networkObject.OwnerClientId == _shooterClientId)
            return;

        ApplyPlayerCollisionKnockback(other);
        ApplyPlayerCollisionDamage(other);
        
        NetworkObject.Despawn();
    }

    /// <summary>
    /// Return true when GO has the LayerMask
    /// </summary>
    /// <param name="go"></param>
    /// <param name="mask"></param>
    /// <returns></returns>
    private bool IsInLayerMask(GameObject go, LayerMask mask)
    {
        return (mask.value & (1 << go.layer)) != 0;
    }

    private void ApplyPlayerCollisionKnockback(Collider2D other)
    {
        var playerMovement = other.GetComponentInParent<PlayerMovement>();
        if (playerMovement == null)
            return;

        playerMovement.ApplyKnockback(_spawnData.Value.Direction, knockbackForce);
    }

    private void ApplyPlayerCollisionDamage(Collider2D other)
    {
        var playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            return;

        playerHealth.TakeDamage(damage);
    }

    private void InitializeClientMovement(ProjectileSpawnData data)
    {
        if (_movementInitialized || !data.Initialized)
            return;

        double currentTime = NetworkManager.ServerTime.Time;
        double elapsedTime = System.Math.Max(0.0, currentTime - data.SpawnTime);
        float compensationTime = Mathf.Min((float)elapsedTime, maxCompensationTime);

        Vector2 compensatedPosition = data.SpawnPosition + data.Direction * speed * compensationTime;
        _rigidbody.position = compensatedPosition;
        _movementInitialized = true;

        // show when the position is known
        SetProjectileVisible(true);

        Debug.Log($"[Projectile] Compensation: {compensationTime:F3}s, " + $"Position: {compensatedPosition}");
    }

    private void SetProjectileVisible(bool visible)
    {
        foreach (SpriteRenderer renderer in _renderers)
            renderer.enabled = visible;
    }
}
