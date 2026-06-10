using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the challenge sequence that begins after the tutorial ends
/// AND the game zone has been placed.
///
/// Flow:
///   1. Tutorial completes (OnTutorialComplete fires)
///   2. If zone already placed → start Challenge 1 immediately
///   3. If zone not placed → show "Place the game zone" prompt,
///      wait for OnZonePlaced, then start Challenge 1
///   4. Each goal reached → transition to next challenge
///   5. All challenges complete → show congratulations
/// </summary>
public class ChallengeManager : MonoBehaviour
{
    public enum ChallengeType
    {
        NavigateToGoal,     // One goal zone, any ship counts
        ShootTargets,       // N shoot targets, hit all to complete
        BothShipsToGoals    // N goal zones, each assigned to a specific ship
    }

    [System.Serializable]
    public class ChallengeData
    {
        public ChallengeType type = ChallengeType.NavigateToGoal;
        public string description = "Navigate the ship to the goal";
        public Vector3[] goalLocalPositions = new[] { new Vector3(0f, 0f, 1f) };
        public int[] assignedShipIndices;   // -1 = any ship; else index into targetObjects
        public float goalRadius = 0.3f;
        public int requiredHits = 2;        // ShootTargets only
    }

    [Header("Challenges")]
    [SerializeField] private ChallengeData[] challenges = new ChallengeData[]
    {
        new ChallengeData
        {
            type              = ChallengeType.NavigateToGoal,
            description       = "Navigate the ship to the goal!",
            goalLocalPositions = new[] { new Vector3(0f, 0.05f, 1.0f) },
            assignedShipIndices = new[] { -1 },
            goalRadius        = 0.3f,
        },
        new ChallengeData
        {
            type              = ChallengeType.ShootTargets,
            description       = "Shoot both green targets!",
            goalLocalPositions = new[]
            {
                new Vector3(-0.5f, 0.4f, 1.0f),
                new Vector3( 0.5f, 0.4f, 1.0f),
            },
            assignedShipIndices = new[] { -1, -1 },
            goalRadius        = 0.3f,
            requiredHits      = 2,
        },
        new ChallengeData
        {
            type              = ChallengeType.BothShipsToGoals,
            description       = "Pilot BOTH ships to their goals!",
            goalLocalPositions = new[]
            {
                new Vector3(-1.0f, 0.05f, 1.0f),
                new Vector3( 1.0f, 0.05f, 1.0f),
            },
            assignedShipIndices = new[] { 0, 1 },
            goalRadius        = 0.3f,
        }
    };

    [Header("Scene References")]
    [SerializeField] private Transform environmentRoot;
    [SerializeField] private Transform[] targetObjects;

    [Header("UI Settings")]
    [SerializeField] private float uiDistanceFromPlayer = 1.8f;
    [SerializeField] private float uiVerticalOffset     = 0.45f;

    public static ChallengeManager Instance { get; private set; }

    private int               _currentIndex = -1;
    private readonly List<GameObject> _activeObjectives = new List<GameObject>();
    private int               _goalsCompleted;
    private int               _hitsReceived;
    private int               _goalsRequired;
    private bool              _tutorialDone;
    private bool              _zonePlaced;

    // UI
    private GameObject       _uiRoot;
    private TextMeshProUGUI  _challengeText;
    private TextMeshProUGUI  _feedbackText;
    private Transform        _cameraTransform;

    private XRGameZonePlacementManager _placementMgr;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // try/catch so any init exception is visible in logcat on device.
        try
        {
            Debug.Log("[ChallengeManager] Start begin");
            FindCamera();
            BuildUI();
            _uiRoot.SetActive(false);

            TutorialMenuController.OnTutorialComplete += OnTutorialComplete;

            _placementMgr = FindObjectOfType<XRGameZonePlacementManager>();
            if (_placementMgr != null)
                _placementMgr.OnZonePlaced += OnZonePlaced;
            Debug.Log("[ChallengeManager] Start complete");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ChallengeManager] Start failed with exception: {e}");
        }
    }

    private void OnDestroy()
    {
        TutorialMenuController.OnTutorialComplete -= OnTutorialComplete;
        if (_placementMgr != null)
            _placementMgr.OnZonePlaced -= OnZonePlaced;
        ClearActiveObjectives();
        if (_uiRoot != null)
            Destroy(_uiRoot);
        if (Instance == this) Instance = null;
    }

    private void ClearActiveObjectives()
    {
        for (int i = 0; i < _activeObjectives.Count; i++)
        {
            if (_activeObjectives[i] != null)
                Destroy(_activeObjectives[i]);
        }
        _activeObjectives.Clear();
    }

    private void Update()
    {
        // Keep banner in front of player
        if (_uiRoot != null && _uiRoot.activeInHierarchy)
            PositionUI();
    }

    // ------------------------------------------------------------------ Flow

    private void OnTutorialComplete()
    {
        _tutorialDone = true;
        TryStartChallenges();
    }

    private void OnZonePlaced()
    {
        _zonePlaced = true;
        TryStartChallenges();
    }

    private void TryStartChallenges()
    {
        if (!_tutorialDone) return;

        if (!_zonePlaced)
        {
            // Prompt user to place the zone
            ShowBanner(0, "Place the game zone to begin challenges!");
            return;
        }

        // Both conditions met — start first challenge
        if (_currentIndex < 0)
            StartChallenge(0);
    }

    private void StartChallenge(int index)
    {
        if (index < 0 || index >= challenges.Length) return;

        _currentIndex = index;
        var data = challenges[index];

        // Fresh state for this challenge
        ClearActiveObjectives();
        _goalsCompleted = 0;
        _hitsReceived = 0;

        switch (data.type)
        {
            case ChallengeType.NavigateToGoal:
                SpawnNavigateGoal(data);
                break;
            case ChallengeType.ShootTargets:
                SpawnShootTargets(data);
                break;
            case ChallengeType.BothShipsToGoals:
                SpawnBothShipsGoals(data);
                break;
        }

        ShowBanner(index + 1, data.description);
    }

    /// <summary>Challenge 1: single goal zone, any ship reaches it → done.</summary>
    private void SpawnNavigateGoal(ChallengeData data)
    {
        _goalsRequired = 1;
        Vector3 worldPos = LocalToWorld(data.goalLocalPositions[0]);
        var zone = ChallengeGoalZone.Create(worldPos, data.goalRadius, environmentRoot);
        zone.trackedShips = targetObjects;
        zone.assignedShip = null; // any ship
        zone.OnCompleted += HandleSingleGoalCompleted;
        _activeObjectives.Add(zone.gameObject);
    }

    /// <summary>Challenge 2: N shoot targets, hit all to complete.</summary>
    private void SpawnShootTargets(ChallengeData data)
    {
        _goalsRequired = data.requiredHits;
        for (int i = 0; i < data.goalLocalPositions.Length; i++)
        {
            Vector3 worldPos = LocalToWorld(data.goalLocalPositions[i]);
            var target = ChallengeShootTarget.Create(worldPos, environmentRoot);
            target.OnHit += HandleShootTargetHit;
            _activeObjectives.Add(target.gameObject);
        }
    }

    /// <summary>Challenge 3: N goal zones, each assigned to a specific ship.
    /// Completion requires ALL goals reached.</summary>
    private void SpawnBothShipsGoals(ChallengeData data)
    {
        _goalsRequired = data.goalLocalPositions.Length;
        for (int i = 0; i < data.goalLocalPositions.Length; i++)
        {
            Vector3 worldPos = LocalToWorld(data.goalLocalPositions[i]);
            var zone = ChallengeGoalZone.Create(worldPos, data.goalRadius, environmentRoot);
            zone.trackedShips = targetObjects;

            // Assign a specific ship by index (falls back to null = any)
            int shipIdx = (data.assignedShipIndices != null && i < data.assignedShipIndices.Length)
                ? data.assignedShipIndices[i] : -1;
            zone.assignedShip = (shipIdx >= 0 && shipIdx < targetObjects.Length)
                ? targetObjects[shipIdx] : null;

            zone.OnCompleted += HandleMultiGoalCompleted;
            _activeObjectives.Add(zone.gameObject);
        }
    }

    // ─── Completion handlers ─────────────────────────────────────────────

    private void HandleSingleGoalCompleted()
    {
        AdvanceChallenge();
    }

    private void HandleShootTargetHit()
    {
        _hitsReceived++;
        if (_hitsReceived >= _goalsRequired)
            AdvanceChallenge();
    }

    private void HandleMultiGoalCompleted()
    {
        _goalsCompleted++;
        if (_goalsCompleted >= _goalsRequired)
            AdvanceChallenge();
    }

    private void AdvanceChallenge()
    {
        int next = _currentIndex + 1;
        if (next < challenges.Length)
            StartCoroutine(TransitionToChallenge(next));
        else
            StartCoroutine(ShowAllComplete());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private Vector3 LocalToWorld(Vector3 localPos)
    {
        return environmentRoot != null
            ? environmentRoot.TransformPoint(localPos)
            : localPos;
    }

    private IEnumerator TransitionToChallenge(int nextIndex)
    {
        ShowFeedback("Challenge Complete!", new Color(0.2f, 1f, 0.2f));
        yield return new WaitForSeconds(2.0f);

        // Reset ships to spawn before starting the next challenge
        if (_placementMgr != null)
            _placementMgr.ResetVehicles();

        yield return new WaitForSeconds(0.5f);
        ClearFeedback();
        StartChallenge(nextIndex);
    }

    private IEnumerator ShowAllComplete()
    {
        if (_challengeText != null)
            _challengeText.text = "All Challenges Complete!";
        ShowFeedback("Congratulations!", new Color(1f, 0.9f, 0.1f));
        yield break;
    }

    // ------------------------------------------------------------------ UI

    private void ShowBanner(int number, string description)
    {
        if (_challengeText == null || _feedbackText == null) return;

        _challengeText.text = number > 0
            ? $"Challenge {number}: {description}"
            : description;
        _feedbackText.gameObject.SetActive(false);
        _uiRoot.SetActive(true);
        PositionUI();
    }

    private void ShowFeedback(string message, Color color)
    {
        if (_feedbackText == null) return;
        _feedbackText.text  = message;
        _feedbackText.color = color;
        _feedbackText.gameObject.SetActive(true);
    }

    private void ClearFeedback()
    {
        if (_feedbackText != null)
            _feedbackText.gameObject.SetActive(false);
    }

    private void PositionUI()
    {
        if (_cameraTransform == null || _uiRoot == null) return;
        Vector3 fwd = _cameraTransform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        _uiRoot.transform.position = _cameraTransform.position
            + fwd * uiDistanceFromPlayer
            + Vector3.up * uiVerticalOffset;
        _uiRoot.transform.rotation = Quaternion.LookRotation(fwd);
    }

    private void FindCamera()
    {
        var ovr = FindObjectOfType<OVRCameraRig>();
        if (ovr != null) { _cameraTransform = ovr.centerEyeAnchor; return; }
        _cameraTransform = Camera.main != null ? Camera.main.transform : transform;
    }

    private void BuildUI()
    {
        _uiRoot = new GameObject("ChallengeBanner");

        var canvas = _uiRoot.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = _cameraTransform.GetComponent<Camera>() ?? Camera.main;
        _uiRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 1f;
        _uiRoot.AddComponent<GraphicRaycaster>();

        var panelSize = new Vector2(800, 100);
        var rt = _uiRoot.GetComponent<RectTransform>();
        rt.sizeDelta = panelSize;

        float scale = 0.9f / panelSize.x;
        _uiRoot.transform.localScale = Vector3.one * scale;

        // Background
        var bgGO  = MakeRect("BG", _uiRoot.transform, Vector2.zero, Vector2.one);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.04f, 0.12f, 0.93f);
        var outline = bgGO.AddComponent<Outline>();
        outline.effectColor    = new Color(1f, 0.85f, 0.1f, 0.85f);
        outline.effectDistance = new Vector2(3, 3);

        // Challenge text
        var txtGO = MakeRect("ChallengeText", _uiRoot.transform,
            new Vector2(0.02f, 0.1f), new Vector2(0.98f, 0.92f));
        _challengeText           = txtGO.AddComponent<TextMeshProUGUI>();
        _challengeText.text      = "";
        _challengeText.fontSize  = 30;
        _challengeText.fontStyle = FontStyles.Bold;
        _challengeText.alignment = TextAlignmentOptions.Center;
        _challengeText.color     = Color.white;

        // Feedback text (below banner)
        var fbGO = MakeRect("FeedbackText", _uiRoot.transform,
            new Vector2(0.05f, -0.8f), new Vector2(0.95f, -0.05f));
        _feedbackText           = fbGO.AddComponent<TextMeshProUGUI>();
        _feedbackText.text      = "";
        _feedbackText.fontSize  = 26;
        _feedbackText.alignment = TextAlignmentOptions.Center;
        _feedbackText.color     = Color.green;
        fbGO.SetActive(false);
    }

    private static GameObject MakeRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r    = go.AddComponent<RectTransform>();
        r.anchorMin  = anchorMin;
        r.anchorMax  = anchorMax;
        r.offsetMin  = Vector2.zero;
        r.offsetMax  = Vector2.zero;
        return go;
    }
}
