using UnityEngine;

/// <summary>
/// Smooth locomotion using the right thumbstick.
/// Moves the Camera Rig in the direction the player is looking (head-relative).
/// Attach to the Camera Rig or VR Systems GameObject.
/// </summary>
public class SmoothLocomotion : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The OVRCameraRig transform (the root that moves the player)")]
    [SerializeField] private Transform cameraRig;
    [Tooltip("CenterEyeAnchor — used for movement direction")]
    [SerializeField] private Transform centerEyeAnchor;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float deadzone = 0.15f;
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("Options")]
    [Tooltip("If true, movement is relative to head direction. If false, relative to controller.")]
    [SerializeField] private bool headRelativeMovement = true;

    private void Update()
    {
        if (cameraRig == null || centerEyeAnchor == null) return;

        Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);

        if (thumbstick.magnitude < deadzone) return;

        // Get forward and right vectors projected onto the ground plane
        Transform directionSource = headRelativeMovement ? centerEyeAnchor : cameraRig;
        Vector3 forward = Vector3.ProjectOnPlane(directionSource.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(directionSource.right, Vector3.up).normalized;

        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;

        Vector3 movement = (forward * thumbstick.y + right * thumbstick.x) * moveSpeed * Time.deltaTime;
        cameraRig.position += movement;
    }
}
