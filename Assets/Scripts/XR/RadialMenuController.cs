using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Thumbstick-driven radial context menu on the left controller.
/// Hold thumbstick in any direction to open, slide to highlight, release to center to select.
/// Attaches a world-space canvas to the left controller anchor.
///
/// Visual design: ring-style menu with arc wedges, smooth highlight animations,
/// icon support, glow center dot, and subtle divider lines.
/// </summary>
public class RadialMenuController : MonoBehaviour
{
    [Serializable]
    public class MenuOption
    {
        public string label;
        public Sprite icon;
    }

    [Header("Menu Options")]
    [SerializeField] private MenuOption[] options = new MenuOption[]
    {
        new MenuOption { label = "Block Editor" },
        new MenuOption { label = "Reset Vehicles" },
        new MenuOption { label = "Place Zone" }
    };

    [Header("Input")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.LTouch;
    [SerializeField] private float activateThreshold = 0.6f;
    [SerializeField] private float selectionDeadzone = 0.3f;

    [Header("Appearance")]
    [SerializeField] private float menuDistance = 0.12f;
    [SerializeField] private float wedgeWidth = 110f;
    [SerializeField] private float wedgeHeight = 38f;
    [SerializeField] private float menuRadius = 70f;
    [SerializeField] private float canvasScale = 0.0008f;

    [Header("Colors")]
    [SerializeField] private Color bgColor = new Color(0.06f, 0.06f, 0.1f, 0.92f);
    [SerializeField] private Color normalColor = new Color(0.12f, 0.14f, 0.22f, 0.88f);
    [SerializeField] private Color highlightColor = new Color(0.15f, 0.5f, 0.95f, 0.95f);
    [SerializeField] private Color selectedFlashColor = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private Color textColor = new Color(0.85f, 0.88f, 0.95f, 1f);
    [SerializeField] private Color highlightTextColor = Color.white;
    [SerializeField] private Color borderColor = new Color(0.2f, 0.4f, 0.8f, 0.3f);

    [Header("Animation")]
    [SerializeField] private float highlightLerpSpeed = 12f;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private GameObject[] _wedgeObjects;
    private Image[] _wedgeImages;
    private TMP_Text[] _wedgeLabels;
    private Image[] _wedgeIcons;
    private Image _centerDot;
    private Image _centerGlow;
    private int _highlightedIndex = -1;
    private bool _isVisible;
    private bool _wasActive;
    private float _selectedFlashTimer;

    // Target & current values for smooth lerp
    private Color[] _targetWedgeColors;
    private Color[] _currentWedgeColors;
    private Color[] _targetTextColors;
    private Color[] _currentTextColors;
    private float[] _targetScales;
    private float[] _currentScales;

    public event Action<int> OnOptionSelected;
    public event Action<string> OnOptionSelectedByName;

    private void Awake()
    {
        BuildMenuUI();
        SetVisible(false);
    }

    private void BuildMenuUI()
    {
        int count = options.Length;

        // --- World-space canvas ---
        GameObject canvasGO = new GameObject("RadialMenuCanvas");
        canvasGO.transform.SetParent(transform, false);

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        _canvasRect = _canvas.GetComponent<RectTransform>();
        _canvasRect.sizeDelta = new Vector2(300, 300);
        _canvasRect.localScale = Vector3.one * canvasScale;
        _canvasRect.localPosition = new Vector3(0f, 0.06f, menuDistance);
        _canvasRect.localRotation = Quaternion.Euler(45f, 0f, 0f);

        // --- Outer ring border ---
        CreateCircleImage("OuterBorder", _canvasRect, new Vector2(264, 264),
            borderColor);

        // --- Background ring ---
        CreateCircleImage("Background", _canvasRect, new Vector2(260, 260),
            bgColor);

        // --- Divider lines between wedges ---
        float angleStep = 360f / count;
        for (int i = 0; i < count; i++)
        {
            float boundaryAngle = 90f - angleStep * i + angleStep * 0.5f;
            float rad = boundaryAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            GameObject divGO = new GameObject($"Divider_{i}");
            divGO.transform.SetParent(_canvasRect, false);
            RectTransform divRect = divGO.AddComponent<RectTransform>();
            // Line from center toward edge
            divRect.sizeDelta = new Vector2(2, 90);
            divRect.anchoredPosition = dir * 45f;
            divRect.localRotation = Quaternion.Euler(0, 0, boundaryAngle - 90f);
            Image divImg = divGO.AddComponent<Image>();
            divImg.color = new Color(1f, 1f, 1f, 0.06f);
        }

        // --- Center glow (soft behind dot) ---
        _centerGlow = CreateCircleImage("CenterGlow", _canvasRect,
            new Vector2(24, 24), new Color(0.3f, 0.6f, 1f, 0.15f));

        // --- Center dot ---
        _centerDot = CreateCircleImage("CenterDot", _canvasRect,
            new Vector2(16, 16), new Color(1f, 1f, 1f, 0.6f));

        // --- Wedge buttons ---
        _wedgeObjects = new GameObject[count];
        _wedgeImages = new Image[count];
        _wedgeLabels = new TMP_Text[count];
        _wedgeIcons = new Image[count];

        _targetWedgeColors = new Color[count];
        _currentWedgeColors = new Color[count];
        _targetTextColors = new Color[count];
        _currentTextColors = new Color[count];
        _targetScales = new float[count];
        _currentScales = new float[count];

        for (int i = 0; i < count; i++)
        {
            float angleDeg = 90f - angleStep * i;
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * menuRadius;

            // Wedge container
            GameObject wedge = new GameObject($"Wedge_{options[i].label}");
            wedge.transform.SetParent(_canvasRect, false);
            RectTransform wedgeRect = wedge.AddComponent<RectTransform>();
            wedgeRect.anchoredPosition = pos;
            wedgeRect.sizeDelta = new Vector2(wedgeWidth, wedgeHeight);

            // Rounded background
            Image img = wedge.AddComponent<Image>();
            img.color = normalColor;
            img.pixelsPerUnitMultiplier = 3f; // Smoother rounded corners
            _wedgeImages[i] = img;

            // Icon (left side of wedge)
            bool hasIcon = options[i].icon != null;
            float labelOffsetX = 0;
            if (hasIcon || true) // reserve space for icon
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(wedge.transform, false);
                RectTransform iconRect = iconGO.AddComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(22, 22);
                iconRect.anchoredPosition = new Vector2(-wedgeWidth * 0.5f + 18f, 0);
                Image iconImg = iconGO.AddComponent<Image>();
                iconImg.color = hasIcon ? Color.white : new Color(1, 1, 1, 0.3f);
                if (hasIcon) iconImg.sprite = options[i].icon;
                _wedgeIcons[i] = iconImg;
                labelOffsetX = 8f;
            }

            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(wedge.transform, false);
            RectTransform labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchoredPosition = new Vector2(labelOffsetX, 0);
            labelRect.sizeDelta = new Vector2(wedgeWidth - 36f, wedgeHeight);

            TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = options[i].label;
            label.fontSize = 15;
            label.alignment = TextAlignmentOptions.Center;
            label.color = textColor;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.characterSpacing = 1.2f;
            _wedgeLabels[i] = label;

            _wedgeObjects[i] = wedge;

            // Initialize lerp state
            _targetWedgeColors[i] = normalColor;
            _currentWedgeColors[i] = normalColor;
            _targetTextColors[i] = textColor;
            _currentTextColors[i] = textColor;
            _targetScales[i] = 1f;
            _currentScales[i] = 1f;
        }
    }

    private void Update()
    {
        // Animate smooth transitions
        AnimateWedges();

        // Flash timer
        if (_selectedFlashTimer > 0)
        {
            _selectedFlashTimer -= Time.deltaTime;
            return; // Brief pause during selection flash
        }

        Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
        float magnitude = thumbstick.magnitude;
        bool isActive = magnitude > activateThreshold;

        if (!_isVisible)
        {
            if (isActive)
            {
                SetVisible(true);
                _wasActive = true;
            }
            return;
        }

        // Menu is visible
        if (magnitude > selectionDeadzone)
        {
            _wasActive = true;

            // Map thumbstick angle to wedge index
            float angle = Mathf.Atan2(thumbstick.y, thumbstick.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            float angleStep = 360f / options.Length;
            float adjusted = (90f - angle + 360f) % 360f;
            int index = Mathf.FloorToInt(adjusted / angleStep);
            index = Mathf.Clamp(index, 0, options.Length - 1);

            SetHighlight(index);

            // Move center dot toward thumbstick direction
            if (_centerDot != null)
            {
                Vector2 dotTarget = thumbstick.normalized * 10f;
                ((RectTransform)_centerDot.transform).anchoredPosition =
                    Vector2.Lerp(((RectTransform)_centerDot.transform).anchoredPosition,
                    dotTarget, Time.deltaTime * 15f);
            }
            if (_centerGlow != null)
            {
                ((RectTransform)_centerGlow.transform).anchoredPosition =
                    ((RectTransform)_centerDot.transform).anchoredPosition;
            }
        }
        else if (_wasActive)
        {
            // Thumbstick returned to center — select and close
            if (_highlightedIndex >= 0)
            {
                SelectOption(_highlightedIndex);
            }
            SetVisible(false);
            _wasActive = false;
        }
    }

    private void AnimateWedges()
    {
        if (_wedgeImages == null) return;
        float t = Time.deltaTime * highlightLerpSpeed;

        for (int i = 0; i < _wedgeImages.Length; i++)
        {
            _currentWedgeColors[i] = Color.Lerp(_currentWedgeColors[i], _targetWedgeColors[i], t);
            _currentTextColors[i] = Color.Lerp(_currentTextColors[i], _targetTextColors[i], t);
            _currentScales[i] = Mathf.Lerp(_currentScales[i], _targetScales[i], t);

            _wedgeImages[i].color = _currentWedgeColors[i];
            _wedgeLabels[i].color = _currentTextColors[i];
            _wedgeObjects[i].transform.localScale = Vector3.one * _currentScales[i];
        }
    }

    private void SetHighlight(int index)
    {
        if (_highlightedIndex == index) return;

        // Unhighlight previous
        if (_highlightedIndex >= 0 && _highlightedIndex < _wedgeImages.Length)
        {
            _targetWedgeColors[_highlightedIndex] = normalColor;
            _targetTextColors[_highlightedIndex] = textColor;
            _targetScales[_highlightedIndex] = 1f;

            if (_wedgeLabels[_highlightedIndex] != null)
                _wedgeLabels[_highlightedIndex].fontStyle = FontStyles.Normal;
        }

        _highlightedIndex = index;

        // Highlight new
        if (index >= 0 && index < _wedgeImages.Length)
        {
            _targetWedgeColors[index] = highlightColor;
            _targetTextColors[index] = highlightTextColor;
            _targetScales[index] = 1.12f;

            if (_wedgeLabels[index] != null)
                _wedgeLabels[index].fontStyle = FontStyles.Bold;
        }
    }

    private void SelectOption(int index)
    {
        if (index < 0 || index >= options.Length) return;

        // Brief selection flash
        _currentWedgeColors[index] = selectedFlashColor;
        _targetWedgeColors[index] = normalColor;
        _selectedFlashTimer = 0.08f;

        OnOptionSelected?.Invoke(index);
        OnOptionSelectedByName?.Invoke(options[index].label);
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_canvas != null)
            _canvas.gameObject.SetActive(visible);

        if (!visible)
        {
            // Reset highlight
            if (_highlightedIndex >= 0 && _highlightedIndex < _wedgeImages.Length)
            {
                _targetWedgeColors[_highlightedIndex] = normalColor;
                _targetTextColors[_highlightedIndex] = textColor;
                _targetScales[_highlightedIndex] = 1f;
                // Snap immediately on close
                _currentWedgeColors[_highlightedIndex] = normalColor;
                _currentTextColors[_highlightedIndex] = textColor;
                _currentScales[_highlightedIndex] = 1f;
                _wedgeImages[_highlightedIndex].color = normalColor;
                _wedgeLabels[_highlightedIndex].color = textColor;
                _wedgeObjects[_highlightedIndex].transform.localScale = Vector3.one;

                if (_wedgeLabels[_highlightedIndex] != null)
                    _wedgeLabels[_highlightedIndex].fontStyle = FontStyles.Normal;
            }
            _highlightedIndex = -1;

            if (_centerDot != null)
                ((RectTransform)_centerDot.transform).anchoredPosition = Vector2.zero;
            if (_centerGlow != null)
                ((RectTransform)_centerGlow.transform).anchoredPosition = Vector2.zero;
        }
    }

    private Image CreateCircleImage(string name, RectTransform parent,
        Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }
}
