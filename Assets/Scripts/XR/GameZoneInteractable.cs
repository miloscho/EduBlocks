using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

/// <summary>
/// Companion script for the 3D Environment. Provides ISDK grab-to-reposition
/// and spatial anchoring on release. Sets up its own grab stack at runtime
/// if no Grabbable component is already present (e.g. from a building block).
/// </summary>
public class GameZoneInteractable : MonoBehaviour
{
    [Header("Spatial Anchoring")]
    [SerializeField] private bool enableSpatialAnchorOnRelease = true;

    private Grabbable _grabbable;
    private bool _wasGrabbed;

    public bool IsGrabbed { get; private set; }

    /// <summary>Fired when the user releases the environment after grabbing.</summary>
    public event System.Action OnReleased;

    private void Start()
    {
        _grabbable = GetComponent<Grabbable>();
        if (_grabbable == null)
            SetupGrabStack();

        AutoFitBoxCollider();
    }

    private void SetupGrabStack()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        _grabbable = gameObject.AddComponent<Grabbable>();
        _grabbable.InjectOptionalRigidbody(rb);
        _grabbable.InjectOptionalTargetTransform(transform);
        _grabbable.InjectOptionalKinematicWhileSelected(true);
        _grabbable.InjectOptionalThrowWhenUnselected(false);

        GameObject childGO = new GameObject("ZoneGrabInteractables");
        childGO.transform.SetParent(transform, false);

        var grabInteractable = childGO.AddComponent<GrabInteractable>();
        grabInteractable.InjectAllGrabInteractable(rb);
        grabInteractable.InjectOptionalPointableElement(_grabbable);

        var handGrab = childGO.AddComponent<HandGrabInteractable>();
        handGrab.InjectRigidbody(rb);

        Debug.Log("[GameZone] Runtime grab stack created.");
    }

    private void AutoFitBoxCollider()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        if (!hasBounds) return;

        box.center = transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            Mathf.Abs(transform.InverseTransformVector(bounds.size).x),
            Mathf.Abs(transform.InverseTransformVector(bounds.size).y),
            Mathf.Abs(transform.InverseTransformVector(bounds.size).z)
        );
    }

    private void Update()
    {
        if (_grabbable == null) return;

        bool currentlyGrabbed = _grabbable.SelectingPointsCount > 0;

        if (_wasGrabbed && !currentlyGrabbed)
        {
            HandleRelease();
        }

        _wasGrabbed = currentlyGrabbed;
        IsGrabbed = currentlyGrabbed;
    }

    private void HandleRelease()
    {
        if (enableSpatialAnchorOnRelease)
        {
            OVRSpatialAnchor existing = GetComponent<OVRSpatialAnchor>();
            if (existing != null)
            {
                Destroy(existing);
            }
            gameObject.AddComponent<OVRSpatialAnchor>();
        }

        OnReleased?.Invoke();
    }
}
