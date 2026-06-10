using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

/// <summary>
/// Manages the floating BE2 block editor display panel. Receives toggle events
/// from the radial menu, positions the RT display quad at chest height in front
/// of the user, and adds ISDK Grabbable so the panel can be repositioned.
/// </summary>
public class FloatingBE2Panel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RadialMenuController radialMenu;
    [SerializeField] private Transform centerEyeAnchor;

    [Header("Placement")]
    [SerializeField] private float distanceFromHead = 0.55f;
    [SerializeField] private float heightOffset = -0.2f;

    [Header("Wrist Tap")]
    [SerializeField] private float wristTapDistance = 0.15f;
    [SerializeField] private float wristTapCooldown = 0.5f;

    private bool _isFloating;
    private Grabbable _panelGrabbable;
    private float _tapCooldownTimer;

    /// <summary>
    /// True on frames where a wrist-tap toggle fired. BE2_VRInputManager checks
    /// this to avoid also starting a block drag from the same trigger pull.
    /// </summary>
    public static bool WristTapFiredThisFrame { get; private set; }

    private void Update()
    {
        WristTapFiredThisFrame = false;
        _tapCooldownTimer -= Time.deltaTime;

        if (_tapCooldownTimer > 0) return;

        Vector3 rightPos = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Vector3 leftPos = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch);
        float dist = Vector3.Distance(rightPos, leftPos);

        if (dist < wristTapDistance && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            TogglePanel();
            WristTapFiredThisFrame = true;
            _tapCooldownTimer = wristTapCooldown;
        }
    }

    private void OnEnable()
    {
        if (radialMenu != null)
            radialMenu.OnOptionSelectedByName += HandleRadialSelection;
    }

    private void OnDisable()
    {
        if (radialMenu != null)
            radialMenu.OnOptionSelectedByName -= HandleRadialSelection;
    }

    private void HandleRadialSelection(string optionName)
    {
        if (optionName == "Block Editor")
            TogglePanel();
    }

    public void TogglePanel()
    {
        _isFloating = !_isFloating;

        if (_isFloating)
            ShowPanel();
        else
            HidePanel();
    }

    private void ShowPanel()
    {
        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if (setup == null || setup.PanelTransform == null) return;

        Transform eye = FindCenterEye();
        if (eye != null)
        {
            setup.ShowInFrontOf(eye);
            Debug.Log($"[BE2_VR] ShowPanel: eye={eye.name} pos={eye.position} panel={setup.PanelTransform.position}");
        }
        else
        {
            Debug.LogWarning("[BE2_VR] ShowPanel: FindCenterEye returned null! Panel may appear at wrong position.");
            setup.SetVisible(true);
        }
    }

    private Transform FindCenterEye()
    {
        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
            return rig.centerEyeAnchor;
        Camera main = Camera.main;
        return main != null ? main.transform : null;
    }

    private void HidePanel()
    {
        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if (setup != null)
            setup.SetVisible(false);
    }

    private void SetupPanelGrab(Transform panelTransform)
    {
        if (_panelGrabbable != null) return;

        // Rigidbody for ISDK grab system
        Rigidbody rb = panelTransform.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = panelTransform.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // ISDK Grabbable on the panel
        _panelGrabbable = panelTransform.GetComponent<Grabbable>();
        if (_panelGrabbable == null)
        {
            _panelGrabbable = panelTransform.gameObject.AddComponent<Grabbable>();
            _panelGrabbable.InjectOptionalRigidbody(rb);
            _panelGrabbable.InjectOptionalTargetTransform(panelTransform);
            _panelGrabbable.InjectOptionalKinematicWhileSelected(true);
            _panelGrabbable.InjectOptionalThrowWhenUnselected(false);
        }

        // GrabInteractable + HandGrabInteractable on a child for controller/hand grab
        Transform grabChild = panelTransform.Find("PanelGrabInteractables");
        if (grabChild == null)
        {
            GameObject childGO = new GameObject("PanelGrabInteractables");
            childGO.transform.SetParent(panelTransform, false);

            var grabInteractable = childGO.AddComponent<GrabInteractable>();
            grabInteractable.InjectAllGrabInteractable(rb);
            grabInteractable.InjectOptionalPointableElement(_panelGrabbable);

            var handGrab = childGO.AddComponent<HandGrabInteractable>();
            handGrab.InjectRigidbody(rb);
        }
    }
}
