using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Canvas-based VR keyboard that lives inside the block editor UI.
/// Built as UI buttons on a panel — same system as the craft selector.
/// Shows when a TMP_InputField is clicked, hides on Done.
/// </summary>
public class VRKeyboardManager : MonoBehaviour
{
    private TMP_InputField _activeField;
    private GameObject _lastSelectedGO;
    private GameObject _keyboardPanel;
    private bool _shiftActive;

    private static readonly string[] ROWS = {
        "QWERTYUIOP",
        "ASDFGHJKL",
        "ZXCVBNM"
    };

    private void Start()
    {
        // Delay keyboard creation until the BE2 setup is done
        Invoke(nameof(CreateKeyboard), 0.5f);
    }

    private void CreateKeyboard()
    {
        var setup = BE2_VRRenderTextureSetup.Instance;
        if (setup == null || setup.PanelTransform == null) return;

        // Find the craft selector canvas to parent our keyboard into
        Canvas targetCanvas = null;
        foreach (Canvas c in setup.PanelTransform.GetComponentsInChildren<Canvas>(true))
        {
            if (c.name == "Canvas") // the craft selector canvas
            {
                targetCanvas = c;
                break;
            }
        }
        // Fallback: use any canvas
        if (targetCanvas == null)
        {
            targetCanvas = setup.PanelTransform.GetComponentInChildren<Canvas>(true);
        }
        if (targetCanvas == null) return;

        // Create keyboard panel
        _keyboardPanel = new GameObject("VR_Keyboard");
        _keyboardPanel.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRT = _keyboardPanel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0, 0);
        panelRT.anchorMax = new Vector2(0, 0);
        panelRT.pivot = new Vector2(0, 0);
        panelRT.anchoredPosition = new Vector2(1350, 250);
        panelRT.sizeDelta = new Vector2(1100, 450);

        Image panelBg = _keyboardPanel.AddComponent<Image>();
        panelBg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        // Build key rows
        float rowHeight = 80;
        float startY = 360;

        // Row 0: numbers
        CreateKeyRow("1234567890", startY, panelRT, 85);
        // Row 1: QWERTY
        CreateKeyRow(ROWS[0], startY - rowHeight, panelRT, 95);
        // Row 2: ASDFGH
        CreateKeyRow(ROWS[1], startY - rowHeight * 2, panelRT, 105);
        // Row 3: ZXCVB + backspace
        CreateKeyRowWithBackspace(ROWS[2], startY - rowHeight * 3, panelRT);
        // Row 4: space + done
        CreateBottomRow(startY - rowHeight * 4, panelRT);

        _keyboardPanel.SetActive(false);
    }

    private void CreateKeyRow(string keys, float y, RectTransform parent, float startX)
    {
        float keySize = 80;
        float spacing = 10;
        for (int i = 0; i < keys.Length; i++)
        {
            char c = keys[i];
            float x = startX + i * (keySize + spacing);
            CreateKeyButton(c.ToString(), x, y, keySize, keySize, parent, () => TypeChar(c));
        }
    }

    private void CreateKeyRowWithBackspace(string keys, float y, RectTransform parent)
    {
        float keySize = 80;
        float spacing = 10;
        float startX = 135;
        for (int i = 0; i < keys.Length; i++)
        {
            char c = keys[i];
            float x = startX + i * (keySize + spacing);
            CreateKeyButton(c.ToString(), x, y, keySize, keySize, parent, () => TypeChar(c));
        }
        // Backspace button
        float bkX = startX + keys.Length * (keySize + spacing);
        CreateKeyButton("<", bkX, y, 120, keySize, parent, Backspace);
    }

    private void CreateBottomRow(float y, RectTransform parent)
    {
        // Space bar
        CreateKeyButton("Space", 135, y, 500, 75, parent, () => TypeChar(' '));
        // Done button
        CreateKeyButton("Done", 655, y, 200, 75, parent, HideKeyboard,
            new Color(0.2f, 0.7f, 0.3f, 1f));
        // Shift button
        CreateKeyButton("Shift", 875, y, 150, 75, parent, ToggleShift,
            new Color(0.3f, 0.4f, 0.6f, 1f));
    }

    private void CreateKeyButton(string label, float x, float y, float w, float h,
        RectTransform parent, Action onClick, Color? color = null)
    {
        GameObject btnGO = new GameObject($"Key_{label}");
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        Image img = btnGO.AddComponent<Image>();
        img.color = color ?? new Color(0.25f, 0.25f, 0.32f, 1f);

        Button btn = btnGO.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.4f, 0.5f, 0.7f, 1f);
        colors.pressedColor = new Color(0.5f, 0.6f, 0.9f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        // Label
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);

        RectTransform textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 36;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    private void TypeChar(char c)
    {
        if (_activeField == null) return;
        string ch = _shiftActive ? c.ToString().ToUpper() : c.ToString().ToLower();
        // Numbers and space don't change with shift
        if (char.IsDigit(c) || c == ' ') ch = c.ToString();
        _activeField.text += ch;
        if (_shiftActive) _shiftActive = false;
    }

    private void Backspace()
    {
        if (_activeField == null || string.IsNullOrEmpty(_activeField.text)) return;
        _activeField.text = _activeField.text.Substring(0, _activeField.text.Length - 1);
    }

    private void ToggleShift()
    {
        _shiftActive = !_shiftActive;
    }

    private void Update()
    {
        if (_keyboardPanel == null) return;

        GameObject currentSelected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;

        if (currentSelected != _lastSelectedGO)
        {
            _lastSelectedGO = currentSelected;
            OnSelectionChanged(currentSelected);
        }
    }

    private void OnSelectionChanged(GameObject selected)
    {
        if (selected == null) return;

        // Don't hide keyboard if user clicked a keyboard button
        if (_keyboardPanel != null && selected.transform.IsChildOf(_keyboardPanel.transform))
            return;

        TMP_InputField tmpField = selected.GetComponent<TMP_InputField>();
        if (tmpField == null)
            tmpField = selected.GetComponentInParent<TMP_InputField>();

        if (tmpField != null)
            ShowKeyboardForField(tmpField);
    }

    private void ShowKeyboardForField(TMP_InputField field)
    {
        _activeField = field;
        _shiftActive = false;
        if (_keyboardPanel != null)
            _keyboardPanel.SetActive(true);
    }

    public void HideKeyboard()
    {
        if (_activeField != null)
            _activeField.onEndEdit.Invoke(_activeField.text);
        _activeField = null;
        if (_keyboardPanel != null)
            _keyboardPanel.SetActive(false);
    }
}
