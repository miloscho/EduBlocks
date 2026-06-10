using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// VR Tutorial Menu built from 3D GameObjects — NOT a Unity Canvas.
///
/// WHY 3D GameObjects instead of Canvas:
///   Canvas-based tutorials in this project had persistent issues with WorldSpace
///   Canvas + GraphicRaycaster + screen-space projection (pointer only hit top half
///   of panel, RawImage tint hid videos, CanvasScaler races, etc.). The 3D approach
///   eliminates every failure point:
///     - No Canvas, CanvasScaler, GraphicRaycaster — no layout/projection math
///     - No screen-space → world-space roundtrip — pointer is always accurate
///     - Each button is its own GameObject with its own BoxCollider — click detection
///       is a single Collider.Raycast per button (proven pattern from block editor)
///     - VideoPlayer writes directly to a material (MaterialOverride mode), no
///       intermediate RenderTexture or RawImage tint to get wrong
///     - TextMeshPro 3D (NOT TextMeshProUGUI) — standalone world-space text
///
/// Hierarchy built at runtime by BuildTutorial():
///   TutorialRoot (positioned by PositionTutorial)
///    ├── Background    — dark semi-transparent quad (backdrop)
///    ├── TitleText     — "Dragging Blocks" etc.
///    ├── VideoQuad     — VideoPlayer MaterialOverride target
///    ├── PageIndicator — "1 / 20"
///    ├── BackButton    — Quad + BoxCollider + Label (TextMeshPro 3D child)
///    ├── NextButton    — same structure
///    └── CloseButton   — same structure
///
/// Click detection (handled by BE2_VRInputManager):
///   1. BE2_VRInputManager.UpdateRaycast iterates TutorialMenuController.ActiveButtons
///   2. For each button, Collider.Raycast(ray) on its BoxCollider
///   3. If hit, shows cursor dot at hit point
///   4. On trigger down, invokes the button's onClick Action
///   No EventSystem, no GraphicRaycaster, no RectTransformUtility.
/// </summary>
public class TutorialMenuController : MonoBehaviour
{
    /// <summary>Fired when player clicks Finish or Close on the tutorial.</summary>
    public static event System.Action OnTutorialComplete;

    /// <summary>
    /// Registered tutorial buttons — BE2_VRInputManager reads this list and does
    /// direct Collider.Raycast against each button on every frame.
    /// </summary>
    public static List<TutorialButton> ActiveButtons { get; private set; }

    /// <summary>
    /// A clickable 3D button. Each tutorial button has its own GameObject + BoxCollider.
    /// BE2_VRInputManager hits the collider and invokes onClick directly.
    /// </summary>
    public class TutorialButton
    {
        public GameObject go;
        public BoxCollider collider;
        public MeshRenderer renderer;
        public System.Action onClick;
        public Color normalColor;
        public Color hoverColor;
    }

    [Header("References")]
    [SerializeField] private RadialMenuController radialMenu;

    // ─── Hardcoded screen definitions ────────────────────────────────────
    // Videos live in Assets/Videos/{filename}.mp4 (loaded at runtime via VideoPlayer.url)
    private static readonly string[][] ScreenData = new string[][]
    {
        new[] { "Dragging Blocks",           "DragBlocks" },
        new[] { "Block Types",               "BlockTypes" },
        new[] { "Scrolling Through Blocks",  "ScrollThroughBlocks" },
        new[] { "Rearranging Blocks",        "RearrangeBlocks" },
        new[] { "Deleting a Block",          "DeleteBlock" },
        new[] { "Deleting Multiple Blocks",  "DeleteBlocks" },
        new[] { "Duplicating a Block",       "DuplicateBlock" },
        new[] { "Executing Your Program",    "Execute" },
        new[] { "Stopping Execution",        "StopExecution" },
        new[] { "Sliding vs Moving",         "SlidingVsMoving" },
        new[] { "Changing Color",            "ChangeColor" },
        new[] { "Control: Loops",            "ControlLoop" },
        new[] { "Infinite Loops",            "InfiniteLoop" },
        new[] { "Using Wait Control",        "UsingWaitControl" },
        new[] { "Creating Variables",        "CreateVariable" },
        new[] { "Using Variables",           "UsingVariable" },
        new[] { "Playing a Sound",           "PlayASound" },
        new[] { "Resetting Ship Position",   "ResetShipPosition" },
        new[] { "Custom Blocks",             "CustomBlock" },
        new[] { "You're Ready!",             "" },
    };

    // ─── Runtime state ───────────────────────────────────────────────────
    private GameObject _tutorialRoot;
    private GameObject _videoQuad;
    private VideoPlayer _videoPlayer;
    private Material _videoMaterial;
    private TextMeshPro _titleText;
    private TextMeshPro _pageIndicator;
    private TutorialButton _backButton;
    private TutorialButton _nextButton;
    private TutorialButton _closeButton;
    private TextMeshPro _nextLabel;

    private int _currentScreen;
    private bool _completed;
    private string _videoFolder;
    private Transform _cameraTransform;

    // ═══════════════════════════════════════════════════════════════════
    // Unity Lifecycle
    // ═══════════════════════════════════════════════════════════════════

    void Start()
    {
        // try/catch so any init exception is visible in logcat instead of
        // silently killing the scene on device.
        try
        {
            Debug.Log("[Tutorial] Start begin");
            ActiveButtons = new List<TutorialButton>();
            // StreamingAssets is the only asset folder whose raw files ship with the APK
            // in a VideoPlayer-compatible way. Application.dataPath points *inside* the APK
            // on Android so File.Exists() always fails there.
            _videoFolder = Path.Combine(Application.streamingAssetsPath, "Videos");

            FindCamera();
            Debug.Log($"[Tutorial] Camera = {(_cameraTransform != null ? _cameraTransform.name : "NULL")}");

            BuildTutorial();
            Debug.Log("[Tutorial] BuildTutorial done");

            // Start hidden — show after a short delay so OVR tracking is ready
            _tutorialRoot.SetActive(false);
            StartCoroutine(ShowFirstScreenDeferred());

            if (radialMenu != null)
                radialMenu.OnOptionSelectedByName += HandleRadialSelection;
            Debug.Log("[Tutorial] Start complete");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Tutorial] Start failed with exception: {e}");
        }
    }

    private IEnumerator ShowFirstScreenDeferred()
    {
        // Wait for OVR camera tracking to settle (otherwise position is at origin)
        yield return new WaitForSeconds(0.5f);
        FindCamera();
        ShowTutorial();
    }

    /// <summary>
    /// Stop the VideoPlayer before Unity tears down native plugins on Play exit.
    /// Prevents OVR/VideoPlayer native crashes during domain reload.
    /// </summary>
    private void OnDisable()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _videoPlayer.url = "";
        }
    }

    private void OnDestroy()
    {
        if (radialMenu != null)
            radialMenu.OnOptionSelectedByName -= HandleRadialSelection;
        if (_videoPlayer != null)
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
        if (_tutorialRoot != null)
            Destroy(_tutorialRoot);
        ActiveButtons = null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Camera / Positioning
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find the VR camera dynamically — OVRCameraRig destroys/recreates
    /// centerEyeAnchor at runtime, so we must never cache a serialized ref.
    /// </summary>
    private void FindCamera()
    {
        var ovr = FindObjectOfType<OVRCameraRig>();
        if (ovr != null && ovr.centerEyeAnchor != null)
        {
            _cameraTransform = ovr.centerEyeAnchor;
            return;
        }
        _cameraTransform = Camera.main != null ? Camera.main.transform : transform;
    }

    /// <summary>
    /// Positions the tutorial root 0.9m in front of the user at eye level.
    /// Uses Mathf.Max(eye.y, 1.2f) so Editor testing (where eye is often at y=0)
    /// still places the panel at a comfortable seated VR height.
    /// Pivot is at the center of the tutorial root, so no corner offset needed.
    /// </summary>
    private void PositionTutorial()
    {
        if (_cameraTransform == null || _tutorialRoot == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

        float eyeY = Mathf.Max(_cameraTransform.position.y, 1.2f);
        _tutorialRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        _tutorialRoot.transform.position =
            new Vector3(_cameraTransform.position.x, eyeY - 0.1f, _cameraTransform.position.z)
            + forward * 0.9f;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tutorial Building (runs once on Start)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the full 3D tutorial hierarchy. All sizes are in world units (meters).
    /// Layout is centered on the tutorial root origin.
    /// </summary>
    private void BuildTutorial()
    {
        _tutorialRoot = new GameObject("TutorialRoot");

        // Background panel (dark backdrop behind everything)
        // Size: 1.8m wide × 1.1m tall (slightly bigger than video + title + buttons)
        CreateQuad(_tutorialRoot.transform, "Background",
            new Vector3(0, 0, 0), new Vector2(1.8f, 1.1f),
            new Color(0.05f, 0.05f, 0.1f, 0.92f));

        // Title text (above video)
        _titleText = CreateText(_tutorialRoot.transform, "TitleText",
            new Vector3(0, 0.45f, -0.01f), 0.08f, FontStyles.Bold);
        _titleText.text = "Tutorial";

        // Video quad — VideoPlayer MaterialOverride target
        // Size: 1.6m × 0.9m (16:9 aspect)
        _videoQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _videoQuad.name = "VideoQuad";
        Destroy(_videoQuad.GetComponent<Collider>()); // no collision on video
        _videoQuad.transform.SetParent(_tutorialRoot.transform, false);
        _videoQuad.transform.localPosition = new Vector3(0, 0.05f, -0.005f);
        _videoQuad.transform.localScale = new Vector3(1.6f, 0.9f, 1f);

        // Video material — Sprites/Default is the only Quest-safe shader
        var videoRenderer = _videoQuad.GetComponent<MeshRenderer>();
        _videoMaterial = new Material(Shader.Find("Sprites/Default"));
        _videoMaterial.color = Color.white;
        videoRenderer.material = _videoMaterial;

        // VideoPlayer setup — MaterialOverride writes directly to the material's texture
        _videoPlayer = _videoQuad.AddComponent<VideoPlayer>();
        _videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        _videoPlayer.targetMaterialRenderer = videoRenderer;
        _videoPlayer.targetMaterialProperty = "_MainTex";
        _videoPlayer.playOnAwake = false;
        _videoPlayer.isLooping = false;
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.prepareCompleted += OnVideoPrepared;

        // Page indicator (below video)
        _pageIndicator = CreateText(_tutorialRoot.transform, "PageIndicator",
            new Vector3(0, -0.35f, -0.01f), 0.04f, FontStyles.Normal);
        _pageIndicator.color = new Color(1, 1, 1, 0.7f);
        _pageIndicator.text = "1 / 20";

        // Buttons — each is its own GameObject with its own BoxCollider
        _backButton = CreateButton("BackButton",
            new Vector3(-0.55f, -0.45f, -0.01f), new Vector2(0.3f, 0.1f),
            "< Back", new Color(0.15f, 0.4f, 0.8f), PrevScreen);

        _nextButton = CreateButton("NextButton",
            new Vector3(0.55f, -0.45f, -0.01f), new Vector2(0.3f, 0.1f),
            "Next >", new Color(0.15f, 0.4f, 0.8f), NextScreen);
        _nextLabel = _nextButton.go.GetComponentInChildren<TextMeshPro>();

        _closeButton = CreateButton("CloseButton",
            new Vector3(0.83f, 0.48f, -0.01f), new Vector2(0.1f, 0.1f),
            "X", new Color(0.7f, 0.1f, 0.1f), CloseTutorial);
    }

    /// <summary>
    /// Creates a flat colored Quad with an unlit Sprites/Default material.
    /// Used for backdrop and button faces.
    /// </summary>
    private GameObject CreateQuad(Transform parent, string name, Vector3 localPos, Vector2 size, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPos;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        quad.GetComponent<MeshRenderer>().material = mat;
        return quad;
    }

    /// <summary>
    /// Creates a world-space TextMeshPro 3D (NOT TextMeshProUGUI — that requires a Canvas).
    /// Size is specified in world units so text scales naturally in VR.
    /// </summary>
    private TextMeshPro CreateText(Transform parent, string name, Vector3 localPos, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.enableAutoSizing = false;
        tmp.rectTransform.sizeDelta = new Vector2(2f, 0.2f); // wide enough for any label

        return tmp;
    }

    /// <summary>
    /// Creates a clickable button: Quad + BoxCollider + child TextMeshPro label.
    /// Registers the button in ActiveButtons so BE2_VRInputManager can find it.
    /// </summary>
    private TutorialButton CreateButton(string name, Vector3 localPos, Vector2 size,
        string label, Color color, System.Action onClick)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(_tutorialRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        // Replace the default MeshCollider with a BoxCollider (thin in Z for raycast precision)
        Destroy(go.GetComponent<Collider>());
        var collider = go.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 1f, 0.05f); // local space; object scale applies

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.material = mat;

        // Label — child TextMeshPro 3D
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        labelGO.transform.localPosition = new Vector3(0, 0, -0.01f);
        // Undo parent's non-uniform scale so text doesn't stretch
        labelGO.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontSize = 0.08f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableAutoSizing = false;
        tmp.rectTransform.sizeDelta = new Vector2(size.x, size.y);

        var btn = new TutorialButton
        {
            go = go,
            collider = collider,
            renderer = renderer,
            onClick = onClick,
            normalColor = color,
            hoverColor = Color.Lerp(color, Color.white, 0.3f),
        };
        ActiveButtons.Add(btn);
        return btn;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Screen Navigation
    // ═══════════════════════════════════════════════════════════════════

    private void ShowScreen(int index)
    {
        if (ScreenData.Length == 0) return;
        _currentScreen = Mathf.Clamp(index, 0, ScreenData.Length - 1);

        string title = ScreenData[_currentScreen][0];
        string videoFile = ScreenData[_currentScreen][1];

        _titleText.text = title;
        _pageIndicator.text = $"{_currentScreen + 1} / {ScreenData.Length}";

        // Disable back button on first screen
        SetButtonInteractable(_backButton, _currentScreen > 0);

        // Last screen: Next button becomes "Finish"
        bool isLast = _currentScreen == ScreenData.Length - 1;
        if (_nextLabel != null)
            _nextLabel.text = isLast ? "Finish" : "Next >";

        // Video
        _videoPlayer.Stop();
        if (!string.IsNullOrEmpty(videoFile))
        {
            // On Android, streamingAssetsPath is a jar:file:// URL pointing inside the APK —
            // VideoPlayer accepts it directly. Do NOT use File.Exists, it returns false for
            // jar:// paths even though VideoPlayer can still read them.
            // On Editor/Windows it's a plain path; prepend file:/// so it parses as a URL.
            string url = Path.Combine(_videoFolder, videoFile + ".mp4").Replace('\\', '/');
#if !UNITY_ANDROID || UNITY_EDITOR
            url = "file:///" + url;
#endif
            _videoPlayer.url = url;
            _videoPlayer.Prepare();
            Debug.Log($"[Tutorial] Preparing video: {videoFile} ({url})");
        }
        else
        {
            _videoPlayer.url = "";
            if (_videoMaterial != null)
                _videoMaterial.mainTexture = null;
        }

        // NOTE: do NOT call PositionTutorial() here — that would re-center the panel
        // on the player's current head pose on every Next/Back click, making it look
        // like the panel is jumping around. Positioning happens only in ShowTutorial().
    }

    /// <summary>Called when VideoPlayer finishes preparing a clip — now safe to Play.</summary>
    private void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log("[Tutorial] Video prepared, starting playback");
        vp.Play();
    }

    private void NextScreen()
    {
        if (_currentScreen < ScreenData.Length - 1)
            ShowScreen(_currentScreen + 1);
        else
            CloseTutorial();
    }

    private void PrevScreen()
    {
        if (_currentScreen > 0)
            ShowScreen(_currentScreen - 1);
    }

    private void CloseTutorial()
    {
        if (_completed) return;
        _completed = true;

        if (_videoPlayer != null)
            _videoPlayer.Stop();
        if (_tutorialRoot != null)
            _tutorialRoot.SetActive(false);
        OnTutorialComplete?.Invoke();
    }

    private void ShowTutorial()
    {
        PositionTutorial();
        _tutorialRoot.SetActive(true);
        ShowScreen(_completed ? 0 : _currentScreen);
        _completed = false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Radial Menu Integration
    // ═══════════════════════════════════════════════════════════════════

    private void HandleRadialSelection(string optionName)
    {
        if (optionName != "Tutorial" || _tutorialRoot == null) return;

        if (_tutorialRoot.activeSelf)
        {
            _videoPlayer.Stop();
            _tutorialRoot.SetActive(false);
        }
        else
        {
            FindCamera();
            ShowTutorial();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Button helpers
    // ═══════════════════════════════════════════════════════════════════

    private void SetButtonInteractable(TutorialButton btn, bool interactable)
    {
        if (btn == null) return;
        btn.collider.enabled = interactable;
        btn.renderer.material.color = interactable
            ? btn.normalColor
            : new Color(btn.normalColor.r * 0.4f, btn.normalColor.g * 0.4f, btn.normalColor.b * 0.4f, btn.normalColor.a);
    }
}
