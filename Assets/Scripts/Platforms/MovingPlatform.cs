using Unity.Netcode;
using UnityEngine;

enum MovePlatformState
{
    Pause,
    Moving
}

[DefaultExecutionOrder(-100)]
public class MovingPlatform : NetworkBehaviour
{
    [SerializeField] private float pauseTime = 2f;
    [SerializeField] private float speed = 4f;
    [SerializeField] private Transform destination;

    private float _currentPauseTime = 0f;
    private MovePlatformState _currentState = MovePlatformState.Pause;
    private Vector3 _currentDestination;
    private Vector3 _initialPosition = Vector3.zero;

    public Vector3 MovePlatformDelta { get; private set; }

    private void OnValidate()
    {
        if (destination == null)
        {
            Debug.LogWarning("MovingPlatform: Destination must be assigned!", this);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (destination == null)
        {
            Debug.LogError("MovingPlatform: Destination is required!", this);

            enabled = false;
            return;
        }

        if (!IsServer)
            return;

        _initialPosition = transform.position;
        _currentDestination = destination.position;
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        Vector3 previousPosition = transform.position;

        MovePlatform();

        MovePlatformDelta = transform.position - previousPosition;

        if (MovePlatformDelta.sqrMagnitude > 0f)
        {
            Physics2D.SyncTransforms();
        }
    }

    private void MovePlatform()
    {
        Vector3 previousPosition = transform.position;

        if (_currentState == MovePlatformState.Moving)
            transform.position = Vector3.MoveTowards(transform.position, _currentDestination, speed * Time.fixedDeltaTime);

        if (_currentState == MovePlatformState.Pause)
            _currentPauseTime += Time.fixedDeltaTime;

        if (_currentState == MovePlatformState.Pause && _currentPauseTime >= pauseTime)
        {
            _currentState = MovePlatformState.Moving;
            _currentPauseTime = 0;
        }
        if (_currentState == MovePlatformState.Moving && transform.position == _currentDestination)
        {
            _currentState = MovePlatformState.Pause;
            _currentPauseTime = 0;
            _currentDestination = _currentDestination == _initialPosition ? destination.position : _initialPosition;
        }

        MovePlatformDelta = transform.position - previousPosition;
    }

    private void OnDrawGizmosSelected()
    {
        if (destination == null)
            return;

        if (!TryGetComponent<BoxCollider2D>(out var boxCollider))
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, destination.position);

        Gizmos.color = Color.green;

        var currentInitialPosition = _initialPosition == Vector3.zero ? transform.position : _initialPosition;
        Vector3 initialCenter = _initialPosition + transform.TransformVector(boxCollider.offset);
        Vector3 destinationCenter = destination.position + transform.TransformVector(boxCollider.offset);
        Vector3 destinationSize = Vector3.Scale(boxCollider.size, transform.lossyScale);

        Gizmos.DrawWireCube(initialCenter, destinationSize);
        Gizmos.DrawWireCube(destinationCenter, destinationSize);
    }
}
