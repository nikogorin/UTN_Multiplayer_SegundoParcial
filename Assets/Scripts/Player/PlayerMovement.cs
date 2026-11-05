using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode.Components;
using System.Collections;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0)] private float maxSpeed = 5f;
    [SerializeField, Min(0)] private float acceleration = 20f;
    [SerializeField, Min(0)] private float deceleration = 30f;
    [SerializeField, Min(0)] private float airAcceleration = 8f;
    [SerializeField, Min(0)] private float airDeceleration = 5f;
    [SerializeField, Min(0)] private float jumpForce = 10f;
    [SerializeField, Min(0)] private float gravityForce = 20f;
    [SerializeField, Min(0f)] private float knockbackDeceleration = 15f;

    [Header("Jump")]
    [SerializeField] private LayerMask collisionLayer;
    [SerializeField, Min(0f)] private float groundCheckDistance = 0.1f;
    [SerializeField] private Transform groundCheck;
    [SerializeField, Min(0f)] private float skinWidth = 0.02f;
    [SerializeField, Min(0f)] private float coyoteTime = 0.15f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.1f;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.5f;

    [Header("Client Anticipation")]
    [SerializeField] private bool enableClientAnticipation = false;
    [SerializeField, Min(0f)] private float maxAnticipationDistance = 0.3f;
    [SerializeField, Min(0f)] private float correctionSmoothTime = 0.06f;
    [SerializeField, Min(0f)] private float maxSmoothCorrection = 0.15f;

    public bool IsGrounded => _isGrounded;
    public float HorizontalSpeed => _horizontalVelocity;
    public float VerticalSpeed => _verticalVelocity;

    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private Vector2 _serverMoveInput = Vector2.zero;
    private float _verticalVelocity;
    private float _horizontalVelocity;
    private Vector2 _knockbackVelocity;
    private bool _isGrounded;
    private float _coyoteTimeCounter;
    private float _jumpBufferCounter;
    private bool _jumpReleased;
    private Collider2D _collider;
    private MovingPlatform _currentPlatform;
    private MovingPlatform _previousPlatform;
    private PlayerHealth _playerHealth;
    // Anticipated movement variables
    private AnticipatedNetworkTransform _anticipatedTransform;
    private float _anticipatedHorizontalVelocity;
    private float _smoothingUntil;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _playerInput = GetComponent<PlayerInput>();
        _moveAction = _playerInput.actions["Move"];
        _jumpAction = _playerInput.actions["Jump"];

        _anticipatedTransform = GetComponent<AnticipatedNetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        _playerInput.enabled = IsOwner;
        _playerHealth = GetComponent<PlayerHealth>();

        if (_anticipatedTransform != null)
        {
            _anticipatedTransform.StaleDataHandling = StaleDataHandling.Reanticipate;
        }

        if (!IsOwner)
            return;

        // Set the Cinemachine camera to follow and look at this player (create a manager to pass the camera to the player instead of finding it in the scene)
        var cinemachineCamera = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();

        cinemachineCamera.Follow = transform;
        cinemachineCamera.LookAt = transform;
        cinemachineCamera.PreviousStateIsValid = false;
        StartCoroutine(RebaseParallaxNextFrame());
    }

    public override void OnReanticipate(double lastRoundTripTime)
    {
        if (!enableClientAnticipation || !IsOwner || IsServer)
            return;

        if (_anticipatedTransform == null || !_anticipatedTransform.ShouldReanticipate)
            return;

        if (!CanAnticipateMovement())
            return;

        var previousState = _anticipatedTransform.PreviousAnticipatedState;

        var authoritativeState = _anticipatedTransform.AuthoritativeState;

        float horizontalError = Mathf.Abs(previousState.Position.x - authoritativeState.Position.x);

        float verticalError = Mathf.Abs(previousState.Position.y - authoritativeState.Position.y);

        // Do not smooth jumps or large corrections.
        if (horizontalError < 0.01f || horizontalError > maxSmoothCorrection || verticalError > 0.03f)
            return;

        // Keep the authoritative vertical position.
        previousState.Position.y = authoritativeState.Position.y;

        _anticipatedTransform.Smooth(previousState, authoritativeState, correctionSmoothTime);
        _smoothingUntil = Time.time + correctionSmoothTime;
    }

    private void Update()
    {
        if (!IsOwner || _playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        Vector2 moveInput = _moveAction.ReadValue<Vector2>();

        SendMoveInputRpc(moveInput);

        AnticipateHorizontalMovement(moveInput.x);

        if (_jumpAction.WasPressedThisFrame())
        {
            SendJumpInputRpc();
        }
        if(_jumpAction.WasReleasedThisFrame())
        {
            SendJumpReleaseRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || _playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        _previousPlatform = _currentPlatform;

        CheckGround();

        UpdateCoyoteTime();
        UpdateJumpBuffer();

        Jump();
        ApplyJumpCut();

        ApplyHorizontalAcceleration();
        ApplyGravity();

        ApplyPlatformMovement();
        ApplyMovement();

        UpdateKnockback();
    }



    private void CheckGround()
    {
        Bounds bounds = _collider.bounds;
        Vector2 origin = new(bounds.center.x, bounds.min.y);
        Vector2 size = new(bounds.size.x - skinWidth * 2f, skinWidth);

        RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, Vector2.down, groundCheckDistance, collisionLayer);

        _isGrounded = hit.collider != null;
        _currentPlatform = null;

        if (!_isGrounded)
            return;

        if(hit.collider.TryGetComponent<CrumblingPlatform>(out var crumblingPlatform))
        {
            crumblingPlatform.TriggerCollapse();
        }

        if(hit.collider.TryGetComponent<MovingPlatform>(out var movingPlatform))
        {
            _currentPlatform = movingPlatform;
        }
    }

    private void Jump()
    {
        if (_jumpBufferCounter <= 0f)
            return;

        if (_coyoteTimeCounter <= 0f)
            return;

        _verticalVelocity = jumpForce;
        _jumpBufferCounter = 0f;
        _coyoteTimeCounter = 0f;
    }

    private void UpdateJumpBuffer()
    {
        if (_jumpBufferCounter > 0f)
            _jumpBufferCounter -= Time.fixedDeltaTime;
    }

    private void ApplyJumpCut()
    {
        if (!_jumpReleased)
            return;

        _jumpReleased = false;

        if (_verticalVelocity <= 0f)
            return;

        _verticalVelocity *= jumpCutMultiplier;
    }

    private void UpdateCoyoteTime()
    {
        if(_isGrounded)
        {
            _coyoteTimeCounter = coyoteTime;
        }
        else
        {
            _coyoteTimeCounter -= Time.fixedDeltaTime;
        }
    }

    private void ApplyGravity()
    {
        if (_isGrounded && _verticalVelocity <= 0f)
        {
            _verticalVelocity = 0f;
            return;
        }

        _verticalVelocity -= gravityForce * Time.fixedDeltaTime;
    }

    private void ApplyHorizontalAcceleration()
    {
        var targetSpeed = _serverMoveInput.x * maxSpeed;
        float rate;
        if (_isGrounded)
        {
            rate = Mathf.Abs(targetSpeed) > 0f ? acceleration : deceleration;
        }
        else
        {
            rate = Mathf.Abs(targetSpeed) > 0f ? airAcceleration : airDeceleration;
        }
        

        _horizontalVelocity = Mathf.MoveTowards(_horizontalVelocity, targetSpeed, rate * Time.fixedDeltaTime);
    }

    private void ApplyMovement()
    {
        Vector2 movementVelocity = new(_horizontalVelocity, _verticalVelocity);

        Vector2 totalVelocity = movementVelocity + _knockbackVelocity;
        Vector2 displacement = totalVelocity * Time.fixedDeltaTime;

        bool horizontalBlocked = ApplyHorizontalMovement(displacement.x);
        bool verticalBlocked = ApplyVerticalMovement(displacement.y);

        if (horizontalBlocked)
            _knockbackVelocity.x = 0f;

        if (verticalBlocked)
            _knockbackVelocity.y = 0f;
    }

    private bool ApplyHorizontalMovement(float displacement)
    {
        float distance = Mathf.Abs(displacement);
        if (distance == 0f)
            return false;

        float allowedDistance = CalculateAllowedHorizontalDistance(displacement);

        // Determine the direction of movement (left or right)
        Vector2 direction = displacement > 0f ? Vector2.right : Vector2.left;

        transform.position += (Vector3)(direction * allowedDistance);

        return allowedDistance < distance - 0.0001f;
    }

    private float CalculateAllowedHorizontalDistance(float displacement)
    {
        float distance = Mathf.Abs(displacement);

        if (distance == 0f)
            return 0f;

        Bounds bounds = _collider.bounds;

        Vector2 castSize = new (bounds.size.x - skinWidth * 2f, bounds.size.y - skinWidth * 2f);
        Vector2 castOrigin = new (bounds.center.x, bounds.center.y + skinWidth);

        Vector2 direction = displacement > 0f ? Vector2.right : Vector2.left;

        RaycastHit2D[] hits = Physics2D.BoxCastAll(castOrigin, castSize, 0f, direction, distance, collisionLayer);

        float allowedDistance = distance;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == _collider)
                continue;

            // Ignore the platform carrying the player.
            if (_previousPlatform != null && hit.collider == _previousPlatform.GetComponent<Collider2D>())
                continue;

            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(hit.distance - skinWidth, 0f));
        }

        return allowedDistance;
    }

    private bool ApplyVerticalMovement(float displacement)
    {
        float distance = Mathf.Abs(displacement);

        if (distance == 0f)
            return false;

        // Determine the direction of movement (up or down)
        Vector2 direction = displacement > 0f ? Vector2.up : Vector2.down;
        RaycastHit2D[] hits = Physics2D.BoxCastAll(_collider.bounds.center, _collider.bounds.size, 0f, direction, distance, collisionLayer);

        float allowedDistance = distance;
        //bool collisionDetected = false;

        foreach(var hit in hits)
        {
            if (hit.collider == _collider)
                continue;

            // Ignore collisions that do not oppose movement.
            if (Vector2.Dot(hit.normal, direction) >= 0f)
                continue;

            // Ignore the supporting moving platform while jumping.
            if (direction == Vector2.up && _previousPlatform != null && hit.collider == _previousPlatform.GetComponent<Collider2D>())
                continue;

            //collisionDetected = true;

            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(hit.distance - skinWidth, 0f));
        }

        //if (collisionDetected)
        //    _verticalVelocity = 0f;

        transform.position += (Vector3)(direction * allowedDistance);

        return allowedDistance < distance - 0.0001f;
    }

    private void ApplyPlatformMovement()
    {
        if (_previousPlatform == null || _verticalVelocity > 0f)
            return;

        transform.position += _previousPlatform.MovePlatformDelta;
    }

    private void UpdateKnockback()
    {
        _knockbackVelocity = Vector2.MoveTowards(_knockbackVelocity, Vector2.zero, knockbackDeceleration * Time.fixedDeltaTime);
    }

    public void ApplyKnockback(Vector2 direction, float knockbackForce)
    {
        if (!IsServer)
            return;

        if (direction.sqrMagnitude <= 0f)
            return;

        _knockbackVelocity += direction.normalized * knockbackForce;
    }

    private void ResetMovementInput()
    {
        _serverMoveInput = Vector2.zero;
        _horizontalVelocity = 0f;
        _verticalVelocity = 0f;
        _knockbackVelocity = Vector2.zero;

        _jumpBufferCounter = 0f;
        _coyoteTimeCounter = 0f;
        _jumpReleased = false;
    }

    private void AnticipateHorizontalMovement(float horizontalInput)
    {
        if (Time.time < _smoothingUntil)
            return;

        if (!CanAnticipateMovement())
        {
            _anticipatedHorizontalVelocity = 0f;
            return;
        }

        // Do not anticipate when the player stops providing input.
        if (Mathf.Abs(horizontalInput) < 0.01f)
        {
            _anticipatedHorizontalVelocity = 0f;
            return;
        }

        float targetSpeed = horizontalInput * maxSpeed;

        _anticipatedHorizontalVelocity = Mathf.MoveTowards(_anticipatedHorizontalVelocity, targetSpeed, acceleration * Time.deltaTime);

        float displacement = _anticipatedHorizontalVelocity * Time.deltaTime;

        float allowedDistance = CalculateAllowedHorizontalDistance(displacement);

        Vector3 anticipatedPosition = _anticipatedTransform.AnticipatedState.Position;

        anticipatedPosition.x += Mathf.Sign(displacement) * allowedDistance;

        float authoritativeX = _anticipatedTransform.AuthoritativeState.Position.x;

        anticipatedPosition.x = Mathf.Clamp(anticipatedPosition.x, authoritativeX - maxAnticipationDistance, authoritativeX + maxAnticipationDistance);

        // Keep the server's vertical position for this limited approach.
        anticipatedPosition.y = _anticipatedTransform.AuthoritativeState.Position.y;

        _anticipatedTransform.AnticipateMove(anticipatedPosition);
    }

    private bool CanAnticipateMovement()
    {
        if (!enableClientAnticipation || !IsOwner || IsServer)
            return false;

        if (_anticipatedTransform == null)
            return false;

        // Do not anticipate while jumping.
        if (_jumpAction.IsPressed())
            return false;

        Bounds bounds = _collider.bounds;

        Vector2 origin = new(bounds.center.x, bounds.min.y);
        Vector2 size = new(bounds.size.x - skinWidth * 2f, skinWidth);

        RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, Vector2.down, groundCheckDistance, collisionLayer);

        if (hit.collider == null)
            return false;

        // Only anticipate when standing on top of a surface.
        if (hit.normal.y < 0.5f)
            return false;

        // Do not anticipate movement on moving platforms.
        if (hit.collider.GetComponentInParent<MovingPlatform>() != null)
            return false;

        return true;
    }

    private IEnumerator RebaseParallaxNextFrame()
    {
        yield return null;

        ParallaxLayer[] layers = FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None);
        foreach (ParallaxLayer layer in layers)
            layer.Rebase(true);
    }

    [Rpc(SendTo.Server)]
    private void SendMoveInputRpc(Vector2 input)
    {
        if(_playerHealth.IsDead || !GameManager.Instance.IsPlaying) 
            return;

        _serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    [Rpc(SendTo.Server)]
    private void SendJumpInputRpc()
    {
        if (_playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        _jumpBufferCounter = jumpBufferTime;
    }

    [Rpc(SendTo.Server)]
    private void SendJumpReleaseRpc()
    {
        if (_playerHealth.IsDead || !GameManager.Instance.IsPlaying)
            return;

        _jumpReleased = true;
    }

    private void OnDrawGizmosSelected()
    {
        
        if (!TryGetComponent<Collider2D>(out var col))
            return;

        Bounds bounds = col.bounds;

        Vector2 origin = new(bounds.center.x, bounds.min.y);

        Vector2 size = new(bounds.size.x - skinWidth * 2f, skinWidth);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(origin + Vector2.down * groundCheckDistance, size);
    }
}
