using UnityEngine;

/// <summary>
/// Simplified menu controller that delegates visibility to BE2_VRRenderTextureSetup.
/// Retains menuRoot reference for potential future use.
/// </summary>
public class BE2ControllerMenuController : MonoBehaviour
{
    [Tooltip("Root transform containing all BE2 menu canvases (BE2 Canvas)")]
    [SerializeField] private Transform menuRoot;

    public Transform MenuRoot => menuRoot;

    public void SetMenuVisible(bool visible)
    {
        BE2_VRRenderTextureSetup setup = BE2_VRRenderTextureSetup.Instance;
        if (setup != null)
            setup.SetVisible(visible);
    }
}
