using Unity.Netcode;
using UnityEngine;

enum CrumblingPlatformState
{
    Stable = 1,
    Warning,
    Collapsed,
}

public class CrumblingPlatform : NetworkBehaviour
{
    [SerializeField] private float collapseTime = 3f;

    [Header("Visuals Ref")]
    [SerializeField] private Transform visualTransform;
    [SerializeField] private SpriteRenderer visualSpriteRenderer;

    [Header("Warning State")]
    [SerializeField] private float shakeAmount = 0.05f;
    [SerializeField] private float shakeSpeed = 30f;

    private NetworkVariable<CrumblingPlatformState> _state = new(CrumblingPlatformState.Stable);
    private NetworkVariable<double> _collapseEndTime = new();
    private Collider2D _collider;
    private Vector3 _initialVisualPosition;
    private bool _isWarning;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _initialVisualPosition = visualTransform.localPosition;
    }

    private void Update()
    {
        ApplyWarningState();
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        PlatformCountdown();
    }

    public void TriggerCollapse()
    {
        if (!IsServer)
            return;

        if (_state.Value != CrumblingPlatformState.Stable) 
            return;

        _collapseEndTime.Value = NetworkManager.ServerTime.Time + collapseTime;
        _state.Value = CrumblingPlatformState.Warning;
    }

    public override void OnNetworkSpawn()
    {
        _state.OnValueChanged += OnStateChanged;

        ApplyState(_state.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= OnStateChanged;
    }

    private void ApplyWarningState()
    {
        if (!_isWarning)
            return;

        double remaining = _collapseEndTime.Value - NetworkManager.ServerTime.Time; // Calculate the remaining time until collapse
        float progress = 1f - Mathf.Clamp01((float)(remaining / collapseTime)); // Calculate the progress of the warning state (0 to 1)
        float currentShakeAmount = Mathf.Lerp(shakeAmount, shakeAmount * collapseTime, progress); // Increase the shake amount as the warning progresses
        float shakeOffset = Mathf.Sin(Time.time * shakeSpeed) * currentShakeAmount;

        visualTransform.localPosition = _initialVisualPosition + Vector3.right * shakeOffset;
    }

    private void OnStateChanged(CrumblingPlatformState previousState, CrumblingPlatformState newState)
    {
        ApplyState(newState);
    }

    private void ApplyState(CrumblingPlatformState state)
    {
        switch (state)
        {
            case CrumblingPlatformState.Stable:
                _collider.enabled = true;
                _isWarning = false;
                visualTransform.localPosition = _initialVisualPosition;
                visualSpriteRenderer.enabled = true;
                break;
            case CrumblingPlatformState.Warning:
                _isWarning = true;
                break;
            case CrumblingPlatformState.Collapsed:
                _isWarning = false;
                visualTransform.localPosition = _initialVisualPosition;
                _collider.enabled = false;
                visualSpriteRenderer.enabled = false;
                break;
        }
    }

    private void PlatformCountdown()
    {
        if (_state.Value != CrumblingPlatformState.Warning)
            return;

        if(NetworkManager.ServerTime.Time >= _collapseEndTime.Value)
        {
            _state.Value = CrumblingPlatformState.Collapsed;
        }
    }
}
