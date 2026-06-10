using UnityEngine;
using MG_BlocksEngine2.Environment;

/// <summary>
/// Clamps ship positions to within the 3D Environment's ground boundaries.
/// Ships use direct Transform movement (no Rigidbody), so physics colliders
/// alone can't stop them. This script enforces bounds each LateUpdate.
/// Attach to the 3D Environment GameObject.
/// </summary>
public class EnvironmentBoundary : MonoBehaviour
{
    [SerializeField] private float boundsPadding = 1f;
    [SerializeField] private float wallHeight = 20f;

    private Bounds _localBounds;
    private I_BE2_TargetObject[] _ships;

    private void Start()
    {
        // Find the ground to determine bounds
        Transform ground = transform.Find("Ground");
        if (ground != null)
        {
            Vector3 s = ground.localScale;
            float halfX = s.x / 2f;
            float halfZ = s.z / 2f;
            _localBounds = new Bounds(
                ground.localPosition,
                new Vector3(s.x - boundsPadding * 2, wallHeight, s.z - boundsPadding * 2));
        }
        else
        {
            // Fallback: 74x74 centered at origin
            _localBounds = new Bounds(Vector3.zero, new Vector3(72, wallHeight, 72));
        }

        // Create invisible wall colliders (for future physics use / editor reference)
        CreateWalls();

        // Find ships — refresh periodically in case they're spawned later
        RefreshShips();
    }

    private void LateUpdate()
    {
        if (_ships == null || _ships.Length == 0)
            RefreshShips();

        foreach (var ship in _ships)
        {
            if (ship == null || ship.Transform == null) continue;

            // Clamp in local space of the environment
            Vector3 localPos = transform.InverseTransformPoint(ship.Transform.position);
            localPos.x = Mathf.Clamp(localPos.x, _localBounds.min.x, _localBounds.max.x);
            localPos.z = Mathf.Clamp(localPos.z, _localBounds.min.z, _localBounds.max.z);
            ship.Transform.position = transform.TransformPoint(localPos);
        }
    }

    private void RefreshShips()
    {
        _ships = FindObjectsOfType<BE2_TargetObject>() as I_BE2_TargetObject[];
        if (_ships == null)
            _ships = new I_BE2_TargetObject[0];
    }

    private void CreateWalls()
    {
        float hw = _localBounds.extents.x;
        float hd = _localBounds.extents.z;
        float h = wallHeight;
        float thick = 0.5f;
        Vector3 center = _localBounds.center;

        CreateWall("Wall_North", center + new Vector3(0, h / 2, hd), new Vector3(hw * 2, h, thick));
        CreateWall("Wall_South", center + new Vector3(0, h / 2, -hd), new Vector3(hw * 2, h, thick));
        CreateWall("Wall_East", center + new Vector3(hw, h / 2, 0), new Vector3(thick, h, hd * 2));
        CreateWall("Wall_West", center + new Vector3(-hw, h / 2, 0), new Vector3(thick, h, hd * 2));
    }

    private void CreateWall(string name, Vector3 localPos, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPos;

        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
        col.isTrigger = false;

        // Invisible — no renderer
        wall.layer = gameObject.layer;
    }
}
