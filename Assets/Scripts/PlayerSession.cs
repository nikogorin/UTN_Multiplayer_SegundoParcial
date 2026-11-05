using System;
using Unity.Netcode;

public class PlayerSession : NetworkBehaviour
{
    public static PlayerSession Local { get; private set; }

    public readonly NetworkVariable<NetworkObjectReference> CurrentAvatar = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static event Action<PlayerSession> OnServerSessionSpawned;

    public string DisplayName => $"Jugador {OwnerClientId}";

    public bool HasSpawnedAvatar => CurrentAvatar.Value.TryGet(out NetworkObject avatar) && avatar != null && avatar.IsSpawned;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            Local = this;

        DontDestroyOnLoad(gameObject);

        if (IsServer)
            OnServerSessionSpawned?.Invoke(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && Local == this)
            Local = null;
    }

    public void AssignAvatarServer(NetworkObject avatar)
    {
        if (!IsServer)
            return;

        CurrentAvatar.Value = avatar != null ? new NetworkObjectReference(avatar) : default;
    }
}
