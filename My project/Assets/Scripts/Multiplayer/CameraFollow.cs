using UnityEngine;

// Caméra qui suit le perso local avec un décalage fixe.
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 5f, -7f);
    public float smoothing = 8f;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 wanted = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, wanted, smoothing * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 1.2f);
    }
}
