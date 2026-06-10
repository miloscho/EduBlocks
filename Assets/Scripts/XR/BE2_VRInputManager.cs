using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.UI;

/// <summary>
/// VR controller input for direct WorldSpace canvas display.
/// Feeds BOTH BE2's custom event pipeline (block dragging) AND Unity's
/// EventSystem (Button.onClick) so all UI elements work in VR.
/// </summary>
public class BE2_VRInputManager : MonoBehaviour, I_BE2_InputManager
{
    [Header("References")]
    [SerializeField] private Transform rightControllerAnchor;

    [Header("Raycast")]
    [SerializeField] private float maxRayDistance = 10f;

    [Header("Controller")]
    [SerializeField] private OVRInput.Controller activeController = OVRInput.Controller.RTouch;

    private BE2_EventsManager _mainEventsManager;
    private BE2_DragDropManager _dragDropManager;

    private bool _primaryDown;
    private float _holdCounter;
    private Vector2 _lastScreenPos;

    // Per-frame raycast
    private bool _hasPanelHit;    // controller hit the BE2 panel collider
    private bool _hasHit;         // controller is pointing at any interactive UI
    private Vector3 _hitScreenPos;
    private Vector3 _lastValidHitScreenPos; // fallback for block drop when not pointing at panel

    // Unity UI event injection state
    private PointerEventData _pointerEventData;
    private GameObject _pointerEnterTarget;
    private GameObject _pointerPressTarget;
    private bool _pointerWasDragged;
    private bool _overDropdownList; // cursor is over an open TMP_Dropdown list
    private Toggle _hoveredDropdownOption; // the specific dropdown option under the cursor
    private List<RaycastResult> _raycastResults = new List<RaycastResult>();
    private readonly Vector3[] _rtCorners = new Vector3[4]; // reused for RectTransform corner queries

    // Visual cursor dot at ray hit point
    private GameObject _cursorDot;


    public Vector3 ScreenPointerPosition
    {
        get
        {
            if (!_hasPanelHit)
                return new Vector3(-9999, -9999, 0);
            return _hitScreenPos;
        }
    }

    public Vector3 CanvasPointerPosition
    {
        get { return GetCanvasPointerPosition(); }
    }

    private void OnEnable()
    {
        BE2_InputManager.Instance = this;
        _mainEventsManager = BE2_MainEventsManager.Instance;
        _dragDropManager = BE2_DragDropManager.Instance;

        BE2_InputManager defaultManager = FindObjectOfType<BE2_InputManager>();
        if (defaultManager != null)
            defaultManager.enabled = false;

        if (EventSystem.current != null)
            _pointerEventData = new PointerEventData(EventSystem.current);
    }

    private void LateUpdate()
    {
        UpdateRaycast();

        // Safety: detect "block stuck to pointer" bug. If BE2's drag state is active
        // but the trigger is NOT held, BE2 failed to release the drag — typically
        // happens when a snap-only block (like an operation/condition block) is
        // dropped on empty space with no valid parent. Force-destroy the stuck block.
        if (!_primaryDown
            && _dragDropManager != null && _dragDropManager.isDragging
            && _dragDropManager.CurrentDrag != null
            && _dragDropManager.CurrentDrag.Transform != null)
        {
            Destroy(_dragDropManager.CurrentDrag.Transform.gameObject);
        }

        // When BE2 panel is hidden, BE2's event system won't call OnUpdate().
        // Drive UI input directly so tutorial/challenge buttons remain clickable.
        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if ((setup == null || !setup.IsVisible) && _hasHit)
            ProcessNonPanelInput();
    }

    private void UpdateRaycast()
    {
        _hasPanelHit = false;
        _hasHit = false;
        _overDropdownList = false;
        _hoveredDropdownOption = null;

        if (rightControllerAnchor == null) return;

        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if (setup == null || setup.VRCamera == null) return;

        Ray ray = new Ray(rightControllerAnchor.position, rightControllerAnchor.forward);

        // 0. If a TMP_Dropdown list is open, check it FIRST via direct ray-plane test.
        //    This bypasses the panel collider entirely — the dropdown list may be at a
        //    slightly different world position and our panel-collider-based screen
        //    coords don't reliably map to it. Direct plane intersection always works.
        Toggle hoveredOption;
        Vector3 dropdownHitPoint;
        if (TryRaycastDropdownOption(ray, setup.VRCamera, out hoveredOption, out dropdownHitPoint))
        {
            _hoveredDropdownOption = hoveredOption;
            _overDropdownList = true;
            _hasHit = true;
            _hitScreenPos = setup.VRCamera.WorldToScreenPoint(dropdownHitPoint);

            EnsureCursor();
            _cursorDot.SetActive(true);
            _cursorDot.transform.position = dropdownHitPoint + ray.direction * -0.01f;
            return;
        }

        // If a dropdown is OPEN but the ray isn't over a specific option, still set
        // the flag so BE2 block events are suppressed (prevents phantom block drags
        // while interacting anywhere near the dropdown).
        if (FindOpenDropdownListRect() != null)
            _overDropdownList = true;

        // 1. Check BE2 panel collider (only when panel is visible)
        if (setup.PanelCollider != null && setup.IsVisible)
        {
            RaycastHit hit;
            if (setup.PanelCollider.Raycast(ray, out hit, maxRayDistance))
            {
                _hitScreenPos = setup.VRCamera.WorldToScreenPoint(hit.point);
                _lastValidHitScreenPos = _hitScreenPos;
                _hasPanelHit = true;
                _hasHit = true;

                EnsureCursor();
                _cursorDot.SetActive(true);
                _cursorDot.transform.position = hit.point + ray.direction * -0.01f;
                return;
            }
        }

        // 2. Check each tutorial button collider directly — each button is its own
        //    3D GameObject, so we iterate the list and test them individually.
        //    No Canvas, no EventSystem, no screen-space projection.
        var buttons = TutorialMenuController.ActiveButtons;
        if (buttons != null)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null || btn.collider == null || !btn.collider.enabled) continue;

                RaycastHit hit;
                if (btn.collider.Raycast(ray, out hit, maxRayDistance))
                {
                    _hasHit = true;

                    EnsureCursor();
                    _cursorDot.SetActive(true);
                    _cursorDot.transform.position = hit.point + ray.direction * -0.01f;

                    // Trigger-down → fire button's onClick directly
                    if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, activeController)
                        && !FloatingBE2Panel.WristTapFiredThisFrame)
                    {
                        btn.onClick?.Invoke();
                    }
                    return;
                }
            }
        }

        if (_cursorDot != null)
            _cursorDot.SetActive(false);
    }

    /// <summary>
    /// Scans active Canvases for the runtime-created TMP_Dropdown list.
    /// TMP_Dropdown.Show() creates a GameObject named "Dropdown List" with a
    /// new Canvas (overrideSorting = true) when the user opens a dropdown.
    /// Returns the list's RectTransform, or null if no dropdown is open.
    /// </summary>
    private RectTransform FindOpenDropdownListRect()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null || !c.overrideSorting) continue;
            if (c.gameObject.name != "Dropdown List") continue;
            if (!c.gameObject.activeInHierarchy) continue;
            return c.transform as RectTransform;
        }
        return null;
    }

    /// <summary>
    /// Raycasts the controller ray against the open dropdown list's plane and
    /// checks if the hit point is within any of the Toggle options' RectTransforms.
    /// Returns true if a hit was found, with the Toggle and world hit point set.
    ///
    /// This bypasses Unity's EventSystem entirely: we compute ray intersection
    /// directly against the dropdown list's plane (built from its world corners)
    /// and use RectTransformUtility.RectangleContainsScreenPoint with our VR
    /// camera to test each option. No dependency on GraphicRaycaster, blocker
    /// elements, or Camera.main.
    /// </summary>
    private bool TryRaycastDropdownOption(Ray ray, Camera vrCamera, out Toggle option, out Vector3 hitPoint)
    {
        option = null;
        hitPoint = Vector3.zero;

        RectTransform listRect = FindOpenDropdownListRect();
        if (listRect == null) return false;

        // Build a plane from 3 of the dropdown list's world-space corners
        listRect.GetWorldCorners(_rtCorners);
        Plane plane = new Plane(_rtCorners[0], _rtCorners[1], _rtCorners[2]);

        float enter;
        if (!plane.Raycast(ray, out enter)) return false;
        if (enter < 0f || enter > maxRayDistance) return false;

        hitPoint = ray.GetPoint(enter);
        Vector2 screenPos = vrCamera.WorldToScreenPoint(hitPoint);

        // Iterate Toggle options inside the dropdown list and test each rect.
        // IMPORTANT: includeInactive = false — TMP_Dropdown keeps a "template" Toggle
        // as a child, which is typically inactive while the list is shown. Including
        // it causes iteration to find the template before real options (especially
        // when the template's rect overlaps a middle option's screen area), making
        // middlemost options appear "unclickable."
        Toggle[] toggles = listRect.GetComponentsInChildren<Toggle>(false);
        for (int i = 0; i < toggles.Length; i++)
        {
            Toggle toggle = toggles[i];
            if (toggle == null) continue;
            if (!toggle.gameObject.activeInHierarchy) continue;
            RectTransform rt = toggle.transform as RectTransform;
            if (rt == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, vrCamera))
            {
                option = toggle;
                return true;
            }
        }
        return false;
    }

    private bool HasUIAtScreenPos(Vector3 screenPos)
    {
        if (_pointerEventData == null && EventSystem.current != null)
            _pointerEventData = new PointerEventData(EventSystem.current);
        if (_pointerEventData == null || EventSystem.current == null) return false;
        _raycastResults.Clear();
        _pointerEventData.position = new Vector2(screenPos.x, screenPos.y);
        EventSystem.current.RaycastAll(_pointerEventData, _raycastResults);
        return _raycastResults.Count > 0;
    }

    /// <summary>
    /// Handles trigger→click for non-BE2 canvases (tutorial, challenges) when
    /// the block editor panel is hidden and BE2 isn't calling OnUpdate().
    /// </summary>
    private void ProcessNonPanelInput()
    {
        if (_pointerEventData == null && EventSystem.current != null)
            _pointerEventData = new PointerEventData(EventSystem.current);

        if (FloatingBE2Panel.WristTapFiredThisFrame) return;

        bool triggerDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, activeController);
        bool triggerUp = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, activeController);

        if (triggerDown)
        {
            _primaryDown = true;
            InjectPointerDown();
        }

        if (triggerUp && _primaryDown)
        {
            InjectPointerUp();
            _primaryDown = false;
        }

        UpdateUIHover();
    }

    private void EnsureCursor()
    {
        if (_cursorDot != null) return;
        _cursorDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _cursorDot.name = "VRCursorDot";
        Destroy(_cursorDot.GetComponent<Collider>());
        var rend = _cursorDot.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.black;
        // Render queue above everything (TMP_Dropdown list Canvas is at sortingOrder 30000).
        // Setting the material's renderQueue ensures the cursor dot draws on top of any
        // transparent canvas elements, so it remains visible over open dropdowns.
        mat.renderQueue = 5000;
        rend.material = mat;
        rend.sortingOrder = 32000;
        _cursorDot.transform.localScale = Vector3.one * 0.008f;
    }

    public void OnUpdate()
    {
        // Ensure PointerEventData exists
        if (_pointerEventData == null && EventSystem.current != null)
            _pointerEventData = new PointerEventData(EventSystem.current);

        // Skip all input if the wrist-tap gesture consumed this trigger pull
        if (FloatingBE2Panel.WristTapFiredThisFrame) return;

        bool triggerDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, activeController);
        bool triggerUp = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, activeController);
        bool triggerHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, activeController);
        bool gripDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, activeController);
        bool gripUp = OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, activeController);

        // While cursor is over a TMP_Dropdown list, bypass BE2 block drag/hold events
        // entirely. Otherwise, the underlying block (visually behind the dropdown list)
        // gets picked up and dragged, which both corrupts the program and suppresses
        // the dropdown option click.
        if (_overDropdownList)
        {
            // If hovering a specific option, a trigger click fires the Toggle directly —
            // TMP_Dropdown listens to Toggle.onValueChanged and handles selection+close.
            if (triggerDown && _hoveredDropdownOption != null)
            {
                _hoveredDropdownOption.isOn = true;
                _primaryDown = false;
                _holdCounter = 0;
                return;
            }
            // Otherwise, no click / no block-drag events while the dropdown is open
            return;
        }

        // --- Trigger down ---
        if (triggerDown)
        {
            _primaryDown = true;
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnPrimaryKeyDown);
            InjectPointerDown();
        }

        // --- Grip ---
        if (gripDown)
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnSecondaryKeyDown);

        // --- Hold detection ---
        if (_dragDropManager.CurrentDrag != null && !_dragDropManager.isDragging)
        {
            _holdCounter += Time.deltaTime;
            if (_holdCounter > 0.6f)
            {
                _mainEventsManager.TriggerEvent(BE2EventTypes.OnPrimaryKeyHold);
                _holdCounter = 0;
            }
        }

        // --- Drag detection ---
        if (triggerHeld && _primaryDown)
        {
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnPrimaryKey);
            Vector2 currentScreenPos = ScreenPointerPosition;
            float distance = Vector2.Distance(_lastScreenPos, currentScreenPos);
            if (distance > 0.5f && !BE2_UI_ContextMenuManager.instance.isActive)
                _mainEventsManager.TriggerEvent(BE2EventTypes.OnDrag);

            // Track if BE2 started a block drag — suppresses button click on release
            if (_dragDropManager.isDragging)
                _pointerWasDragged = true;
        }

        // --- Trigger up ---
        if (triggerUp)
        {
            // Off-panel while dragging → destroy block (deferred; BE2 still cleans up state)
            if (!_hasPanelHit && _dragDropManager.isDragging && _dragDropManager.CurrentDrag != null)
                Destroy(_dragDropManager.CurrentDrag.Transform.gameObject);

            InjectPointerUp();
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnPrimaryKeyUp);
            _holdCounter = 0;
            _primaryDown = false;
            // NOTE: the "block stuck to pointer" safety check lives in LateUpdate
            // because it has to run every frame to catch the bug regardless of when
            // BE2 updates its state — see LateUpdate() for details.
        }

        // --- Grip up ---
        if (gripUp)
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnSecondaryKeyUp);

        // --- Delete ---
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, activeController))
        {
            // If a block is currently being dragged, destroy it directly and clean
            // up state — this prevents the "block stuck to pointer" bug where BE2
            // sometimes doesn't clear the drag reference after a delete.
            if (_dragDropManager != null && _dragDropManager.isDragging
                && _dragDropManager.CurrentDrag != null
                && _dragDropManager.CurrentDrag.Transform != null)
            {
                Destroy(_dragDropManager.CurrentDrag.Transform.gameObject);
            }

            _mainEventsManager.TriggerEvent(BE2EventTypes.OnDeleteKeyDown);

            // Force release the drag/pointer state so BE2 doesn't keep any stale refs
            _mainEventsManager.TriggerEvent(BE2EventTypes.OnPrimaryKeyUp);
            _primaryDown = false;
            _holdCounter = 0;
            _pointerPressTarget = null;
            _pointerWasDragged = false;
        }

        _lastScreenPos = ScreenPointerPosition;

        // --- Hover tracking (every frame) ---
        UpdateUIHover();
    }

    // ------------------------------------------------------------------ Unity UI injection

    private GameObject RaycastUI()
    {
        _raycastResults.Clear();
        if (_pointerEventData == null || EventSystem.current == null || !_hasHit)
            return null;

        _pointerEventData.position = new Vector2(_hitScreenPos.x, _hitScreenPos.y);
        EventSystem.current.RaycastAll(_pointerEventData, _raycastResults);

        if (_raycastResults.Count > 0)
        {
            _pointerEventData.pointerCurrentRaycast = _raycastResults[0];
            return _raycastResults[0].gameObject;
        }
        return null;
    }

    private void InjectPointerDown()
    {
        _pointerWasDragged = false;
        if (!_hasHit) { _pointerPressTarget = null; return; }

        GameObject hitUI = RaycastUI();
        if (hitUI != null)
        {
            _pointerPressTarget = hitUI;
            _pointerEventData.pressPosition = new Vector2(_hitScreenPos.x, _hitScreenPos.y);
            _pointerEventData.pointerPressRaycast = _pointerEventData.pointerCurrentRaycast;
            ExecuteEvents.ExecuteHierarchy(hitUI, _pointerEventData, ExecuteEvents.pointerDownHandler);
        }
        else
        {
            _pointerPressTarget = null;
        }
    }

    private void InjectPointerUp()
    {
        if (_pointerPressTarget == null) return;

        ExecuteEvents.ExecuteHierarchy(_pointerPressTarget, _pointerEventData, ExecuteEvents.pointerUpHandler);

        // Fire click if no drag occurred, OR if the target is a dropdown (dropdowns
        // need clicks even when tiny controller jitter triggers a micro-drag)
        if (!_pointerWasDragged || IsDropdownTarget(_pointerPressTarget))
            ExecuteEvents.ExecuteHierarchy(_pointerPressTarget, _pointerEventData, ExecuteEvents.pointerClickHandler);

        _pointerPressTarget = null;
    }

    /// <summary>
    /// Returns true if the target is part of a TMP_Dropdown's interaction surface —
    /// either the main dropdown button itself, or an OPTION in an open dropdown list.
    /// This matters because when a dropdown is open and the user clicks an option,
    /// BE2's drag state machine may have started a phantom block drag at the same
    /// screen position (dropdown list overlaps a BE2 block), setting _pointerWasDragged
    /// to true. Without treating dropdown options as click-through, the option click
    /// gets suppressed and the dropdown becomes unusable.
    ///
    /// Dropdown options live under a runtime-created GameObject named "Dropdown List"
    /// which TMP_Dropdown adds as a sibling/child of the template, NOT as a child of
    /// the dropdown itself — so GetComponentInParent&lt;TMP_Dropdown&gt;() doesn't find it.
    /// We detect options by walking up the hierarchy looking for a "Dropdown List" ancestor.
    /// </summary>
    private bool IsDropdownTarget(GameObject target)
    {
        if (target == null) return false;

        // Case 1: the main TMP_Dropdown button (clicking to open/close the dropdown)
        if (target.GetComponentInParent<TMP_Dropdown>() != null) return true;

        // Case 2: an option inside an open "Dropdown List"
        Transform t = target.transform;
        while (t != null)
        {
            if (t.name == "Dropdown List") return true;
            t = t.parent;
        }
        return false;
    }

    private void UpdateUIHover()
    {
        if (!_hasHit)
        {
            SetHoverTarget(null);
            return;
        }

        // Only update hover when not pressing (matches Unity's native behavior)
        if (_primaryDown) return;

        GameObject hitUI = RaycastUI();
        SetHoverTarget(hitUI);
    }

    private void SetHoverTarget(GameObject target)
    {
        if (target == _pointerEnterTarget) return;

        if (_pointerEnterTarget != null && _pointerEventData != null)
            ExecuteEvents.ExecuteHierarchy(_pointerEnterTarget, _pointerEventData, ExecuteEvents.pointerExitHandler);

        _pointerEnterTarget = target;

        if (_pointerEnterTarget != null && _pointerEventData != null)
            ExecuteEvents.ExecuteHierarchy(_pointerEnterTarget, _pointerEventData, ExecuteEvents.pointerEnterHandler);
    }

    // ------------------------------------------------------------------ BE2 canvas pointer

    private Vector3 GetCanvasPointerPosition()
    {
        if (!_hasPanelHit) return Vector3.zero;

        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if (setup == null || setup.VRCamera == null) return Vector3.zero;

        Canvas canvas = BE2_DragDropManager.DragDropComponentsCanvas;
        if (canvas == null) return Vector3.zero;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return Vector3.zero;

        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            canvasRect,
            _hitScreenPos,
            setup.VRCamera,
            out Vector3 worldPoint
        );

        return worldPoint;
    }
}
