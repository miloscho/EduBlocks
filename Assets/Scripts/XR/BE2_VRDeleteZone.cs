using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.DragDrop;

/// <summary>
/// Visual "trash" indicator that appears when dragging a block over the palette area.
/// Tints the palette region red and shows a "Release to delete" label.
/// Purely visual — the actual deletion is handled by BE2_DragBlock.OnPointerUp()
/// which destroys blocks dropped outside valid spots.
/// </summary>
public class BE2_VRDeleteZone : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Color trashTintColor = new Color(0.8f, 0.15f, 0.15f, 0.3f);
    [SerializeField] private float paletteWidthRatio = 0.35f;

    private GameObject _overlayGO;
    private Image _overlayImage;
    private TMP_Text _deleteLabel;
    private BE2_DragDropManager _dragDropManager;
    private bool _isShowingTrash;

    private void Start()
    {
        _dragDropManager = BE2_DragDropManager.Instance;
        BuildOverlay();
    }

    private void BuildOverlay()
    {
        BE2_VRPanel panel = BE2_VRPanel.Instance;
        if (panel == null || panel.CanvasRectTransform == null) return;

        // Create a red tint overlay on the left (palette) region
        _overlayGO = new GameObject("DeleteZoneOverlay");
        _overlayGO.transform.SetParent(panel.CanvasRectTransform, false);

        RectTransform rt = _overlayGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(paletteWidthRatio, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _overlayImage = _overlayGO.AddComponent<Image>();
        _overlayImage.color = trashTintColor;
        _overlayImage.raycastTarget = false;

        // "Release to delete" label
        GameObject labelGO = new GameObject("DeleteLabel");
        labelGO.transform.SetParent(rt, false);
        RectTransform labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.1f, 0.4f);
        labelRect.anchorMax = new Vector2(0.9f, 0.6f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _deleteLabel = labelGO.AddComponent<TextMeshProUGUI>();
        _deleteLabel.text = "Release to delete";
        _deleteLabel.fontSize = 28;
        _deleteLabel.alignment = TextAlignmentOptions.Center;
        _deleteLabel.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        _deleteLabel.fontStyle = FontStyles.Bold;

        _overlayGO.SetActive(false);
    }

    private void Update()
    {
        if (_dragDropManager == null || _overlayGO == null) return;

        bool shouldShow = false;

        // Show trash zone when dragging a block and the ray points at the palette area
        if (_dragDropManager.isDragging && _dragDropManager.CurrentDrag != null)
        {
            shouldShow = IsPointerOverPalette();
        }

        if (shouldShow != _isShowingTrash)
        {
            _isShowingTrash = shouldShow;
            _overlayGO.SetActive(shouldShow);
        }
    }

    private bool IsPointerOverPalette()
    {
        BE2_VRPanel panel = BE2_VRPanel.Instance;
        if (panel == null || panel.RootCanvas == null) return false;

        // Use the input manager's screen position to check palette bounds
        I_BE2_InputManager input = BE2_InputManager.Instance;
        if (input == null) return false;

        Vector3 screenPos = input.ScreenPointerPosition;
        if (screenPos.x < -9000) return false; // No hit

        Camera cam = panel.RootCanvas.worldCamera;
        if (cam == null) return false;

        RectTransform canvasRect = panel.CanvasRectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPos, cam, out Vector2 localPoint);

        // Check if the local point is in the left portion (palette)
        Rect canvasSize = canvasRect.rect;
        float normalizedX = (localPoint.x - canvasSize.xMin) / canvasSize.width;
        return normalizedX < paletteWidthRatio;
    }
}
