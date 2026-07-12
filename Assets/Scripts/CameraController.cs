using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float mouseSensitivity = 0.6f;
    public float minVerticalAngle = -90f;
    public float maxVerticalAngle = 90f;

    private float xRotation = 0f;

    public void Look(Vector2 lookInput)
    {
        float mouseX = lookInput.x * mouseSensitivity;
        float mouseY = lookInput.y * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, minVerticalAngle, maxVerticalAngle);

        transform.localRotation = Quaternion.Euler(xRotation, transform.localEulerAngles.y + mouseX, 0f);
    }

    public void ResetRotation()
    {
        xRotation = 0f;
        transform.localRotation = Quaternion.identity;
    }

    public float GetVerticalRotation()
    {
        return transform.rotation.eulerAngles.y;
    }
    public void UpdatePos(Vector3 pos)
    { transform.localPosition = pos; }
}
