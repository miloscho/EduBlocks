using UnityEngine;
using MG_BlocksEngine2.EditorScript;

/// <summary>
/// Direct WorldSpace display — no RenderTexture, no UICamera.
/// Scales the BE2 canvas hierarchy down to VR tablet size and lets
/// the VR camera see it directly. Controller ray hits the panel collider
/// and BE2_VRInputManager maps to screen coords via the VR camera.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BE2_VRRenderTextureSetup : MonoBehaviour
{
    public static BE2_VRRenderTextureSetup Instance { get; private set; }

    [Header("Scene References")]
    [SerializeField] private Transform blocksEngineRoot;
    [SerializeField] private Transform environmentRoot;

    [Header("Display")]
    [SerializeField] private float vrScale = 0.0005f;

    private Camera _vrCamera;
    private BoxCollider _panelCollider;
    private bool _isVisible;

    // --- Public API ---
    public Camera VRCamera => _vrCamera;
    public BoxCollider PanelCollider => _panelCollider;
    public Transform PanelTransform => blocksEngineRoot;
    public bool IsVisible => _isVisible;
    public float VRScale => vrScale;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // try/catch wrapper so any init exception is visible in logcat instead of
        // silently swallowing and leaving the scene empty on device.
        try
        {
            Debug.Log("[BE2_VR] Start begin");

            _vrCamera = FindVRCamera();
            Debug.Log($"[BE2_VR] VR camera = {(_vrCamera != null ? _vrCamera.name : "NULL")}");
            if (_vrCamera == null)
            {
                Debug.LogError("[BE2_VR] Could not find VR camera!");
                return;
            }

            if (blocksEngineRoot == null)
            {
                Debug.LogError("[BE2_VR] blocksEngineRoot not assigned!");
                return;
            }

            // Reparent ships to 3D Environment BEFORE scaling the BE2 root.
            // Ships are children of Blocks Engine 2 (y=10000) but need to be
            // on the 3D Environment surface. Must happen before scale to 0.0005.
            ReparentShipsToEnvironment();
            Debug.Log("[BE2_VR] ReparentShipsToEnvironment done");

            ConfigureCanvasesForVR();
            Debug.Log("[BE2_VR] ConfigureCanvasesForVR done");

            SetupCollider();
            Debug.Log("[BE2_VR] SetupCollider done");

            // Scale to VR tablet size
            blocksEngineRoot.localScale = Vector3.one * vrScale;

            // Start hidden
            SetVisible(false);

            Debug.Log($"[BE2_VR] Direct display ready. VRCamera={_vrCamera.name}, scale={vrScale}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BE2_VR] Start failed with exception: {e}");
        }
    }

    private void ReparentShipsToEnvironment()
    {
        if (environmentRoot == null || blocksEngineRoot == null) return;

        // Calculate ground surface level from the Ground child
        Transform ground = environmentRoot.Find("Ground");
        float groundY = -23f;
        float centerX = 0f, centerZ = 0f;
        if (ground != null)
        {
            groundY = ground.localPosition.y + ground.localScale.y / 2f + 0.5f;
            centerX = ground.localPosition.x;
            centerZ = ground.localPosition.z;
        }

        string[] shipNames = { "Target Object - craft_speederA", "Target Object - craft_miner" };
        Vector3[] startPositions = {
            new Vector3(centerX - 2, groundY, centerZ),
            new Vector3(centerX + 2, groundY, centerZ)
        };

        for (int i = 0; i < shipNames.Length; i++)
        {
            Transform ship = FindInChildren(blocksEngineRoot, shipNames[i]);
            if (ship != null)
            {
                ship.SetParent(environmentRoot, false);
                ship.localPosition = startPositions[i];
                ship.localRotation = Quaternion.identity;
                ship.localScale = Vector3.one;
                Debug.Log($"[BE2_VR] Reparented {shipNames[i]} to {environmentRoot.name} at {startPositions[i]}");
            }
        }

        // Re-capture home positions now that ships are children of 3D Environment
        var placementMgr = FindObjectOfType<XRGameZonePlacementManager>();
        if (placementMgr != null)
            placementMgr.CaptureVehicleHomes();

        // Add grab-to-reposition on the environment
        if (environmentRoot.GetComponent<GameZoneInteractable>() == null)
            environmentRoot.gameObject.AddComponent<GameZoneInteractable>();
    }

    private void ConfigureCanvasesForVR()
    {
        // Point BE2_Inspector at the VR camera
        BE2_Inspector inspector = FindObjectOfType<BE2_Inspector>();
        if (inspector == null)
        {
            var go = new GameObject("BE2_Inspector_Runtime");
            inspector = go.AddComponent<BE2_Inspector>();
        }
        BE2_Inspector.Instance = inspector;
        inspector.Camera = _vrCamera;
        // This setter iterates all BE2_Canvas and sets worldCamera + renderMode
        inspector.CanvasRenderMode = RenderMode.WorldSpace;

        // Also set worldCamera on ALL canvases and align them to the same position.
        // Programming Env canvases are under intermediate transforms with non-zero offsets
        // which causes misalignment in WorldSpace. Snapping all canvases to the root
        // position fixes this without affecting internal RectTransform layout.
        Canvas[] allCanvases = blocksEngineRoot.GetComponentsInChildren<Canvas>(true);
        foreach (Canvas c in allCanvases)
        {
            c.worldCamera = _vrCamera;
            c.transform.position = blocksEngineRoot.position;
        }

        // Reparent Start/Stop buttons and DPAD under a Programming Env so they
        // shift with the programming area when the sidebar opens/closes.
        ReparentControlsToCraftSelector();
    }

    private void ReparentControlsToCraftSelector()
    {
        // Find the craft selector canvas (named "Canvas" — the one with Speeder/Miner)
        Transform craftCanvas = null;
        foreach (Canvas c in blocksEngineRoot.GetComponentsInChildren<Canvas>(true))
        {
            if (c.name == "Canvas" && c.transform.childCount > 0)
            {
                craftCanvas = c.transform;
                break;
            }
        }
        if (craftCanvas == null) return;
        RectTransform craftRect = craftCanvas as RectTransform;

        // Place Play/Stop buttons above the craft selector panel
        // Craft panel is at ~(1852, 110), top edge ~Y=220
        Transform playBtn = FindInChildren(blocksEngineRoot, "Button Play");
        Transform stopBtn = FindInChildren(blocksEngineRoot, "Button Stop");

        if (playBtn != null)
            PlaceInParent(playBtn, craftRect, new Vector2(0, 0), new Vector2(1720, 280), new Vector2(70, 70));
        if (stopBtn != null)
            PlaceInParent(stopBtn, craftRect, new Vector2(0, 0), new Vector2(1800, 280), new Vector2(70, 70));

        // Place DPAD above the craft selector, to the right of Play/Stop
        Transform dpadCanvas = FindInChildren(blocksEngineRoot, "Canvas Virtual Joystick");
        if (dpadCanvas != null && dpadCanvas.childCount > 0)
        {
            Transform dpadContainer = dpadCanvas.GetChild(0);
            PlaceInParent(dpadContainer, craftRect, new Vector2(0, 0), new Vector2(2100, 370), new Vector2(220, 220));
        }
    }

    private void PlaceInParent(Transform element, RectTransform newParent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        element.SetParent(newParent, false);
        RectTransform rt = element as RectTransform;
        if (rt == null) return;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    private Transform FindInChildren(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    private void SetupCollider()
    {
        _panelCollider = blocksEngineRoot.GetComponent<BoxCollider>();
        if (_panelCollider == null)
            _panelCollider = blocksEngineRoot.gameObject.AddComponent<BoxCollider>();

        // Canvases have pivot (0,0), extending from (0,0) to (2560,1440) in local space.
        // Center the collider on the canvas area. Thin in Z for raycast precision.
        _panelCollider.center = new Vector3(1280, 720, 0);
        _panelCollider.size = new Vector3(2560, 1440, 10);
    }

    private Camera FindVRCamera()
    {
        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            Camera cam = rig.centerEyeAnchor.GetComponent<Camera>();
            if (cam != null) return cam;
        }
        return Camera.main;
    }

    // ------------------------------------------------------------------ Visibility
    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (blocksEngineRoot != null)
            blocksEngineRoot.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Positions the panel in front of the given eye transform, facing the user.
    /// </summary>
    public void ShowInFrontOf(Transform eye)
    {
        if (blocksEngineRoot == null || eye == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        // Canvas content faces local -Z, so LookRotation(forward) makes content face the user
        blocksEngineRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        blocksEngineRoot.localScale = Vector3.one * vrScale;

        // Offset so the canvas CENTER is at the target position (not the pivot corner)
        Vector3 centerOffset = blocksEngineRoot.rotation * new Vector3(1280 * vrScale, 720 * vrScale, 0);
        Vector3 targetPos = eye.position + forward * 0.6f;
        targetPos.y -= 0.15f;
        blocksEngineRoot.position = targetPos - centerOffset;

        SetVisible(true);
    }
}
