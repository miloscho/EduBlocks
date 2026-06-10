using UnityEngine;

/// <summary>
/// A glowing goal zone that signals ChallengeManager when a ship enters it.
/// Created at runtime by ChallengeManager. Uses per-frame proximity detection
/// so it works whether or not the ship has a collider.
/// </summary>
public class ChallengeGoalZone : MonoBehaviour
{
    [Header("Sizes (world meters)")]
    public float visualRadius = 0.015f;     // disc + halo visible size
    public float detectionRadius = 0.05f;   // INVISIBLE hitbox — larger for forgiving proximity check

    // Set by ChallengeManager after creation
    [HideInInspector] public Transform[] trackedShips;
    [HideInInspector] public Transform assignedShip; // null = any tracked ship; else only this one

    /// <summary>Fired when a qualifying ship enters the zone. ChallengeManager subscribes.</summary>
    public event System.Action OnCompleted;

    // Visuals
    private Renderer _discRenderer;
    private Light    _glow;
    private bool     _triggered;

    // Pulse parameters
    private const float PulseSpeed = 1.2f;
    private const float PulseMin   = 0.85f;
    private const float PulseMax   = 1.15f;

    // �� Factory ��������������������������������������������������������

    /// <summary>
    /// Instantiate a goal zone at <paramref name="worldPosition"/> with the given
    /// world-space VISUAL radius. The invisible detection hitbox is automatically
    /// 3× larger for forgiving ship proximity detection.
    /// Radius values MUST be set BEFORE Build() runs.
    /// </summary>
    public static ChallengeGoalZone Create(Vector3 worldPosition, float radius, Transform parent = null)
    {
        var go  = new GameObject("ChallengeGoalZone");
        var zone = go.AddComponent<ChallengeGoalZone>();
        zone.visualRadius = radius;
        zone.detectionRadius = radius * 3f; // 3× larger invisible hitbox

        if (parent != null)
        {
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPosition;
        }
        else
        {
            go.transform.position = worldPosition;
        }

        zone.Build();
        return zone;
    }

    // �� Build visuals ��������������������������������������������������

    private void Build()
    {
        // Counter-scale against the parent (scaled environmentRoot ~0.0122) so that
        // visualRadius is interpreted as world meters for the disc visuals.
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        float invX = parentScale.x > 0.0001f ? 1f / parentScale.x : 1f;
        float invY = parentScale.y > 0.0001f ? 1f / parentScale.y : 1f;
        float invZ = parentScale.z > 0.0001f ? 1f / parentScale.z : 1f;
        transform.localScale = new Vector3(invX, invY, invZ);

        // Main glowing disc (Cylinder primitive, mesh radius 0.5)
        // World radius = visualRadius after counter-scale
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Disc";
        disc.transform.SetParent(transform, false);
        disc.transform.localPosition = Vector3.zero;
        disc.transform.localScale    = new Vector3(visualRadius * 2f, 0.01f, visualRadius * 2f);
        Destroy(disc.GetComponent<Collider>());

        _discRenderer = disc.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.1f, 1f, 0.2f);
        _discRenderer.material = mat;

        // Outer halo disc — slightly larger, semi-transparent, for "outline" look
        var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        halo.name = "Halo";
        halo.transform.SetParent(transform, false);
        halo.transform.localPosition = new Vector3(0, -0.001f, 0); // just below main disc
        halo.transform.localScale = new Vector3(visualRadius * 2.5f, 0.005f, visualRadius * 2.5f);
        Destroy(halo.GetComponent<Collider>());
        var haloMat = new Material(Shader.Find("Sprites/Default"));
        haloMat.color = new Color(0.1f, 1f, 0.2f, 0.35f);
        halo.GetComponent<Renderer>().material = haloMat;

        // Point light for glow effect
        var lightGO = new GameObject("GoalLight");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.localPosition = new Vector3(0, 0.05f, 0);
        _glow           = lightGO.AddComponent<Light>();
        _glow.type      = LightType.Point;
        _glow.color     = new Color(0.2f, 1f, 0.3f);
        _glow.intensity = 2f;
        _glow.range     = visualRadius * 5f;
    }

    // �� Runtime ��������������������������������������������������������

    private void Update()
    {
        AnimatePulse();
        if (!_triggered) CheckProximity();
    }

    private void AnimatePulse()
    {
        float t     = (Mathf.Sin(Time.time * PulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float scale = Mathf.Lerp(PulseMin, PulseMax, t);

        if (_discRenderer != null)
        {
            _discRenderer.transform.localScale =
                new Vector3(visualRadius * 2f * scale, 0.015f, visualRadius * 2f * scale);
            float b = Mathf.Lerp(0.6f, 1.0f, t);
            _discRenderer.material.color = new Color(0.1f * b, b, 0.2f * b);
        }

        if (_glow != null)
            _glow.intensity = Mathf.Lerp(1.5f, 3.5f, t);
    }

    private void CheckProximity()
    {
        if (trackedShips == null) return;

        foreach (var ship in trackedShips)
        {
            if (ship == null) continue;
            if (assignedShip != null && ship != assignedShip) continue;
            if (Vector3.Distance(ship.position, transform.position) <= detectionRadius)
            {
                _triggered = true;
                OnCompleted?.Invoke();
                return;
            }
        }
    }
}
