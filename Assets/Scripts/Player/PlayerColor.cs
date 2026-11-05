using Unity.Netcode;
using UnityEngine;

public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private SpriteRenderer[] spriteRenderers;

    private readonly NetworkVariable<Color32> _color = new(new Color32(255, 255, 255, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _color.OnValueChanged += OnColorChanged;

        ApplyColor(_color.Value);
    }

    public override void OnNetworkDespawn()
    {
        _color.OnValueChanged -= OnColorChanged;
    }

    public void SetColor(Color32 color)
    {
        if (!IsServer)
            return;

        _color.Value = color;
        ApplyColor(color);
    }

    private void OnColorChanged(Color32 previous, Color32 current)
    {
        ApplyColor(current);
    }

    private void ApplyColor(Color32 color)
    {
        foreach (SpriteRenderer spriteRenderer in spriteRenderers)
        {
            if (spriteRenderer != null)
                spriteRenderer.color = color;
        }
    }
}
