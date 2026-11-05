using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShooting : NetworkBehaviour
{
    [Header("Orientation")]
    [SerializeField] private Transform visualTransform;

    [Header("Fire")]
    [SerializeField, Min(1)] private float fireCooldown = 2f;
    [SerializeField, Min(0)] private float projectileSpawnDistance = 0.75f;
    [SerializeField] GameObject projectilePrefab;

    private NetworkVariable<bool> _isFacingLeft = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server );
    private readonly NetworkVariable<double> _cooldownEndTime = new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Camera _playerCamera;
    private PlayerInput _playerInput;
    private InputAction _fireAction;
    private bool _canFire;
    private float _currentFireCooldown = 0;
    private Vector2 _aimDirection;
    private PlayerHealth _playerHealth;
    private bool _lastFacingLeft;
    private PlayerAnimationController _animationController;

    public float CooldownProgress
    {
        get
        {
            if (!IsSpawned || NetworkManager == null || fireCooldown <= 0f)
                return 1f;

            double remaining = _cooldownEndTime.Value - NetworkManager.ServerTime.Time;

            return 1f - Mathf.Clamp01((float)(remaining / fireCooldown));
        }
    }

    private void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        _animationController = GetComponent<PlayerAnimationController>();

        _fireAction = _playerInput.actions["Attack"];
        _canFire = true;
    }

    public override void OnNetworkSpawn()
    {
        _playerHealth = GetComponent<PlayerHealth>();

        _isFacingLeft.OnValueChanged += OnFacingDirectionChanged;
        ApplyFacingDirection(_isFacingLeft.Value);

        if (!IsOwner)
            return;

        _playerCamera = Camera.main;
    }

    public override void OnNetworkDespawn()
    {
        _isFacingLeft.OnValueChanged -= OnFacingDirectionChanged;
    }

    private void Update()
    {
        if (!IsOwner || _playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        UpdateAimDirection();
        UpdateFacingDirection();

        if (_fireAction.WasPressedThisFrame())
        {
            SendFireInputRpc(_aimDirection);
        }    
    }

    private void FixedUpdate()
    {
        if (!IsServer || _playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        ApplyFireCooldown();
    }

    private void ApplyFireCooldown()
    {
        if (_canFire)
            return;

        _currentFireCooldown += Time.fixedDeltaTime;

        if (_currentFireCooldown >= fireCooldown)
        {
            _canFire = true;
        }
    }

    private void SpawnProjectile(Vector2 direction)
    {
        Vector2 spawnPosition = (Vector2)transform.position + direction * projectileSpawnDistance;

        GameObject projectileObject = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);

        NetworkObject networkObject = projectileObject.GetComponent<NetworkObject>();
        ProjectileMovement projectile = projectileObject.GetComponent<ProjectileMovement>();

        networkObject.Spawn();
        projectile.Initialize(OwnerClientId, direction);
        _animationController.PlayShoot();

        _currentFireCooldown = 0;
        _canFire = false;
        _cooldownEndTime.Value = NetworkManager.ServerTime.Time + fireCooldown;
    }

    private void UpdateAimDirection()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPosition = _playerCamera.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, -_playerCamera.transform.position.z));

        Vector2 direction = mouseWorldPosition - transform.position;

        if(direction.sqrMagnitude > 0.0001f)
        {
            _aimDirection = direction.normalized;
        }
    }

    private void UpdateFacingDirection()
    {
        if (Mathf.Abs(_aimDirection.x) < 0.01f)
            return;

        bool facingLeft = _aimDirection.x < 0f;

        if (facingLeft == _lastFacingLeft)
            return;

        _lastFacingLeft = facingLeft;

        ApplyFacingDirection(facingLeft);
        SendFacingDirectionRpc(facingLeft);
    }

    private void OnFacingDirectionChanged(bool previous, bool current)
    {
        ApplyFacingDirection(current);
    }

    private void ApplyFacingDirection(bool facingLeft)
    {
        Vector3 scale = visualTransform.localScale;
        scale.x = Mathf.Abs(scale.x) * (facingLeft ? -1f : 1f);

        visualTransform.localScale = scale;
    }

    [Rpc(SendTo.Server)]
    private void SendFireInputRpc(Vector2 aimDirection)
    {
        if (!_canFire || _playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        if(aimDirection.sqrMagnitude < 0.0001f || !float.IsFinite(aimDirection.sqrMagnitude))
            return;

        SpawnProjectile(aimDirection.normalized);
    }

    [Rpc(SendTo.Server)]
    private void SendFacingDirectionRpc(bool facingLeft)
    {
        if (!Application.isPlaying || _playerHealth.IsDead)
            return;

        _isFacingLeft.Value = facingLeft;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !IsOwner)
            return;

        Gizmos.color = Color.aquamarine;

        Gizmos.DrawLine(transform.position, transform.position + (Vector3)_aimDirection * 2f);
    }
}
