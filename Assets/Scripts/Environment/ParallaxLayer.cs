using UnityEngine;

[DefaultExecutionOrder(1000)]
public class ParallaxLayer : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;

    [Tooltip("1 follows the camera completely. Lower values produce more movement.")]
    [SerializeField] private Vector2 cameraFollow = new(0.9f, 0.8f);

    private Vector3 initialPosition;
    private Vector3 initialCameraPosition;

    private void Start()
    {
        Rebase();
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
            return;

        Vector3 cameraDelta = cameraTransform.position - initialCameraPosition;
        transform.position = new Vector3(initialPosition.x + cameraDelta.x * cameraFollow.x, initialPosition.y + cameraDelta.y * cameraFollow.y, initialPosition.z);
    }

    public void Rebase(bool snapToCamera = false)
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == null)
            return;

        if (snapToCamera)
        {
            transform.position = new Vector3(cameraTransform.position.x, cameraTransform.position.y, transform.position.z);
        }

        initialPosition = transform.position;
        initialCameraPosition = cameraTransform.position;
    }
}
