using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerAnimationController : NetworkBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int ShootHash = Animator.StringToHash("Shoot");

    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;

    private PlayerMovement _movement;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        animator.SetFloat(SpeedHash, Mathf.Abs(_movement.HorizontalSpeed));
        animator.SetFloat(VerticalSpeedHash, _movement.VerticalSpeed);
        animator.SetBool(GroundedHash, _movement.IsGrounded);
    }

    public void PlayShoot()
    {
        if (!IsServer)
            return;

        networkAnimator.SetTrigger(ShootHash);
    }
}
