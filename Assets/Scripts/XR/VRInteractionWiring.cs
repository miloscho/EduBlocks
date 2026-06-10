using UnityEngine;

/// <summary>
/// Routes radial menu selections to the appropriate subsystem.
/// BE2 canvas/camera/inspector configuration is handled by BE2_VRRenderTextureSetup.
/// </summary>
public class VRInteractionWiring : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private RadialMenuController radialMenu;
    [SerializeField] private XRGameZonePlacementManager placementManager;
    [SerializeField] private FloatingBE2Panel floatingPanel;
    [SerializeField] private GameZoneInteractable gameZoneInteractable;

    private void OnEnable()
    {
        if (radialMenu != null)
            radialMenu.OnOptionSelectedByName += HandleRadialSelection;

        if (gameZoneInteractable != null)
            gameZoneInteractable.OnReleased += HandleGameZoneReleased;
    }

    private void OnDisable()
    {
        if (radialMenu != null)
            radialMenu.OnOptionSelectedByName -= HandleRadialSelection;

        if (gameZoneInteractable != null)
            gameZoneInteractable.OnReleased -= HandleGameZoneReleased;
    }

    private void HandleRadialSelection(string optionName)
    {
        switch (optionName)
        {
            case "Reset Vehicles":
                if (placementManager != null)
                    placementManager.ResetVehicles();
                break;

            case "Place Zone":
                if (placementManager != null)
                    placementManager.ToggleFromMenu();
                break;

            // "Block Editor" is handled by FloatingBE2Panel directly
        }
    }

    private void HandleGameZoneReleased()
    {
        // Spatial anchor already created by GameZoneInteractable.
    }
}
