using UnityEngine;

/// <summary>
/// Game zone placement driven by the radial menu.
/// Uses a teleport-style arc line from the right controller to show
/// where the zone will land, with a ring indicator at the target point.
///
/// Flow:
///   Menu "Place Zone" → arc preview follows controller
///   Right trigger → spawn zone at target
///   Menu "Place Zone" again → despawn
/// </summary>
public class XRGameZonePlacementManager : MonoBehaviour
{
    public enum PlacementState
    {
        Idle,
        Preview,
        Spawned
    }

    /// <summary>Fired after the zone is placed and active.</summary>
    public event System.Action OnZonePlaced;

    [Header("Zone Roots")]
    [SerializeField] private Transform environmentRoot;
    [SerializeField] private Transform blocksEngineRoot;

    [Header("Controller References")]
    [SerializeField] private Transform centerEyeAnchor;
    [SerializeField] private Transform rightControllerAnchor;

    [Header("Placement")]
    [SerializeField] private float fallbackDistance = 1.8f;
    [SerializeField] private float maxRayDistance = 8f;
    [SerializeField] private LayerMask placementMask = ~0;

    [Header("Scaling")]
    [SerializeField] private bool scaleToFootprint = true;
    [SerializeField] private float targetFootprintMeters = 0.9144f;
    [SerializeField] private bool scaleBlocksWithEnvironment = true;

    [Header("Arc Settings")]
    [SerializeField] private int arcSegments = 30;
    [SerializeField] private float arcVelocity = 3.5f;
    [SerializeField] private float arcGravity = 9.8f;
    [SerializeField] private float arcLineWidth = 0.008f;
    [SerializeField] private Color arcColorValid = new Color(0.2f, 0.8f, 1f, 0.8f);
    [SerializeField] private Color arcColorInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);
    [SerializeField] private float ringRadius = 0.15f;

    [Header("Spatial Anchoring")]
    [SerializeField] private bool addSpatialAnchorOnPlace = true;

    [Header("Vehicle Reset")]
    [SerializeField] private Transform[] targetObjects;

    private PlacementState _state = PlacementState.Idle;
    public PlacementState State => _state;

    private LineRenderer _arcLine;
    private GameObject _arcObject;
    private GameObject _ringIndicator;
    private float _initialFootprint = 1f;
    private Vector3 _blocksPosOffset;
    private Quaternion _blocksRotOffset;
    private bool _hasValidTarget;
    private Pose _currentTargetPose;

    // Prevent trigger press on the same frame as menu selection
    private int _previewStartFrame;

    private Vector3[] _homeLocalPositions;
    private Quaternion[] _homeLocalRotations;

    private void Awake()
    {
        if (environmentRoot == null)
        {
            Debug.LogError("XRGameZonePlacementManager: Environment Root is required.", this);
            enabled = false;
            return;
        }

        if (blocksEngineRoot != null)
        {
            _blocksPosOffset = Quaternion.Inverse(environmentRoot.rotation)
                * (blocksEngineRoot.position - environmentRoot.position);
            _blocksRotOffset = Quaternion.Inverse(environmentRoot.rotation)
                * blocksEngineRoot.rotation;
        }

        _initialFootprint = Mathf.Max(0.01f, MeasureFootprint());
        CaptureVehicleHomes();
        SetZoneActive(false);
        CreateArcVisuals();
        SetPreviewActive(false);
    }

    private void Update()
    {
        if (_state != PlacementState.Preview) return;

        UpdateArc();

        // Don't accept trigger on the frame we entered preview
        if (Time.frameCount <= _previewStartFrame) return;

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            if (_hasValidTarget)
                PlaceZone();
        }
    }

    /// <summary>Called from radial menu.</summary>
    public void ToggleFromMenu()
    {
        switch (_state)
        {
            case PlacementState.Idle:
                EnterPreview();
                break;
            case PlacementState.Preview:
                CancelPreview();
                break;
            case PlacementState.Spawned:
                DespawnZone();
                break;
        }
    }

    private void EnterPreview()
    {
        _state = PlacementState.Preview;
        _previewStartFrame = Time.frameCount;
        SetPreviewActive(true);
    }

    private void CancelPreview()
    {
        _state = PlacementState.Idle;
        SetPreviewActive(false);
    }

    public void PlaceZone()
    {
        _state = PlacementState.Spawned;
        SetPreviewActive(false);

        if (scaleToFootprint)
        {
            float scale = Mathf.Clamp(targetFootprintMeters / _initialFootprint, 0.001f, 100f);
            environmentRoot.localScale = Vector3.one * scale;
            if (scaleBlocksWithEnvironment && blocksEngineRoot != null)
                blocksEngineRoot.localScale = Vector3.one * scale;
        }

        environmentRoot.SetPositionAndRotation(_currentTargetPose.position, _currentTargetPose.rotation);

        if (blocksEngineRoot != null)
        {
            Vector3 worldPos = environmentRoot.position
                + (environmentRoot.rotation * _blocksPosOffset);
            Quaternion worldRot = environmentRoot.rotation * _blocksRotOffset;
            blocksEngineRoot.SetPositionAndRotation(worldPos, worldRot);
        }

        SetZoneActive(true);
        ResetVehicles();

        if (addSpatialAnchorOnPlace)
            EnsureSpatialAnchor(environmentRoot.gameObject);

        OnZonePlaced?.Invoke();
    }

    private void DespawnZone()
    {
        _state = PlacementState.Idle;
        SetZoneActive(false);
    }

    public void ResetVehicles()
    {
        if (targetObjects == null || _homeLocalPositions == null) return;
        for (int i = 0; i < targetObjects.Length; i++)
        {
            if (targetObjects[i] == null) continue;
            targetObjects[i].localPosition = _homeLocalPositions[i];
            targetObjects[i].localRotation = _homeLocalRotations[i];
            Rigidbody rb = targetObjects[i].GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    // ── Arc Visuals ─────────────────────────────────────────────────

    private void CreateArcVisuals()
    {
        // Arc line
        _arcObject = new GameObject("PlacementArc");
        _arcObject.transform.SetParent(transform, false);
        _arcLine = _arcObject.AddComponent<LineRenderer>();
        _arcLine.positionCount = arcSegments;
        _arcLine.startWidth = arcLineWidth;
        _arcLine.endWidth = arcLineWidth * 0.5f;
        _arcLine.material = new Material(Shader.Find("Sprites/Default"));
        _arcLine.startColor = arcColorValid;
        _arcLine.endColor = arcColorValid;
        _arcLine.useWorldSpace = true;
        _arcLine.receiveShadows = false;
        _arcLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Landing ring indicator
        _ringIndicator = new GameObject("LandingRing");
        LineRenderer ringLine = _ringIndicator.AddComponent<LineRenderer>();
        int ringSegs = 32;
        ringLine.positionCount = ringSegs + 1;
        ringLine.startWidth = 0.005f;
        ringLine.endWidth = 0.005f;
        ringLine.material = new Material(Shader.Find("Sprites/Default"));
        ringLine.startColor = arcColorValid;
        ringLine.endColor = arcColorValid;
        ringLine.useWorldSpace = true;
        ringLine.loop = true;
        ringLine.receiveShadows = false;
        ringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Pre-calculate ring positions (unit circle, will be scaled/positioned in UpdateArc)
        for (int i = 0; i <= ringSegs; i++)
        {
            float angle = (float)i / ringSegs * Mathf.PI * 2f;
            ringLine.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }
    }

    private void SetPreviewActive(bool active)
    {
        if (_arcObject != null) _arcObject.SetActive(active);
        if (_ringIndicator != null) _ringIndicator.SetActive(active);
    }

    private void UpdateArc()
    {
        if (rightControllerAnchor == null) return;

        Vector3 origin = rightControllerAnchor.position;
        Vector3 direction = rightControllerAnchor.forward;

        _hasValidTarget = false;
        Vector3 hitPoint = Vector3.zero;

        // 1. Raycast — hits any physics surface (virtual ground, passthrough mesh, etc.)
        if (Physics.Raycast(origin, direction, out RaycastHit hit,
            maxRayDistance, placementMask, QueryTriggerInteraction.Ignore))
        {
            _hasValidTarget = true;
            hitPoint = hit.point;
        }
        // 2. Fallback — intersect horizontal plane slightly below head height
        else if (centerEyeAnchor != null && direction.y < -0.01f)
        {
            float planeY = centerEyeAnchor.position.y - 0.8f;
            float t = (planeY - origin.y) / direction.y;
            if (t > 0f && t < maxRayDistance)
            {
                hitPoint = origin + direction * t;
                _hasValidTarget = true;
            }
        }

        // 3. Last resort — short distance in front of controller
        if (!_hasValidTarget)
        {
            hitPoint = origin + direction * fallbackDistance;
            _hasValidTarget = true;
        }

        // Update target pose
        Vector3 fwd = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        _currentTargetPose = new Pose(hitPoint, Quaternion.LookRotation(fwd, Vector3.up));

        // Straight line from controller to target
        _arcLine.positionCount = 2;
        _arcLine.SetPosition(0, origin);
        _arcLine.SetPosition(1, hitPoint);

        Color c = arcColorValid;
        _arcLine.startColor = c;
        _arcLine.endColor = new Color(c.r, c.g, c.b, c.a * 0.3f);

        // Landing ring
        if (_ringIndicator != null)
        {
            _ringIndicator.SetActive(true);
            _ringIndicator.transform.position = hitPoint + Vector3.up * 0.005f;
            _ringIndicator.transform.rotation = Quaternion.LookRotation(Vector3.up, fwd);
            _ringIndicator.transform.localScale = Vector3.one * ringRadius;

            LineRenderer ringLine = _ringIndicator.GetComponent<LineRenderer>();
            if (ringLine != null)
            {
                ringLine.startColor = c;
                ringLine.endColor = c;
            }
        }
    }

    // ── Utility ─────────────────────────────────────────────────────

    private void SetZoneActive(bool active)
    {
        if (environmentRoot != null)
            environmentRoot.gameObject.SetActive(active);
        if (blocksEngineRoot != null)
            blocksEngineRoot.gameObject.SetActive(active);
    }

    private void EnsureSpatialAnchor(GameObject target)
    {
        OVRSpatialAnchor existing = target.GetComponent<OVRSpatialAnchor>();
        if (existing != null) Destroy(existing);
        target.AddComponent<OVRSpatialAnchor>();
    }

    public void CaptureVehicleHomes()
    {
        if (targetObjects == null || targetObjects.Length == 0) return;
        _homeLocalPositions = new Vector3[targetObjects.Length];
        _homeLocalRotations = new Quaternion[targetObjects.Length];
        for (int i = 0; i < targetObjects.Length; i++)
        {
            if (targetObjects[i] == null) continue;
            _homeLocalPositions[i] = targetObjects[i].localPosition;
            _homeLocalRotations[i] = targetObjects[i].localRotation;
        }
    }

    private float MeasureFootprint()
    {
        Renderer[] renderers = environmentRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return 1f;
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return hasBounds ? Mathf.Max(bounds.size.x, bounds.size.z) : 1f;
    }
}
