using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A floating glowing green orb used in Challenge 2 (Shoot Targets).
/// Detects bullet collisions via OnTriggerEnter and fires OnHit when a bullet enters.
///
/// Bullet detection: bullets are instantiated from the ships' Bullet child prefabs
/// with names like "Bullet(Clone)". We match by name containing "Bullet" so we
/// don't need to tag anything or know layer numbers.
/// </summary>
public class ChallengeShootTarget : MonoBehaviour
{
    /// <summary>Fired once when a bullet hits this target. ChallengeManager subscribes.</summary>
    public event System.Action OnHit;

    private bool _triggered;
    private Renderer _orbRenderer;
    private Light _glow;

    // Previous-position tracking for sweep-test bullet detection.
    // Unity's CCD doesn't fire OnTriggerEnter for triggers with fast-moving
    // rigidbodies (Meta ships' bullets move ~20 m/s, way faster than the physics
    // step can catch in a small trigger collider), so we do our own per-FixedUpdate
    // line-segment-to-point sweep check against all active bullets.
    private readonly Dictionary<int, Vector3> _bulletPrevPos = new Dictionary<int, Vector3>();

    // Visuals
    private const float PulseSpeed = 1.5f;
    private const float OrbRadius = 0.02f;  // sphere visual radius in world units
    private const float HitRadius = 0.08f;  // sweep-test hit radius — 4× larger invisible hitbox

    // ─── Factory ─────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiate a shoot target at the given world position, optionally
    /// parented to the environment so it moves with the game board.
    /// </summary>
    public static ChallengeShootTarget Create(Vector3 worldPosition, Transform parent = null)
    {
        var go = new GameObject("ChallengeShootTarget");
        if (parent != null)
        {
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPosition;
        }
        else
        {
            go.transform.position = worldPosition;
        }

        var target = go.AddComponent<ChallengeShootTarget>();
        target.Build();
        return target;
    }

    // ─── Build visuals + collider ────────────────────────────────────────

    private void Build()
    {
        // Counter-scale against parent (scaled environmentRoot) so visuals + collider
        // appear at a consistent world-space size — see ChallengeGoalZone for details.
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        float invX = parentScale.x > 0.0001f ? 1f / parentScale.x : 1f;
        float invY = parentScale.y > 0.0001f ? 1f / parentScale.y : 1f;
        float invZ = parentScale.z > 0.0001f ? 1f / parentScale.z : 1f;
        transform.localScale = new Vector3(invX, invY, invZ);

        // Glowing sphere visual (Sphere primitive default radius is 0.5)
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "OrbVisual";
        sphere.transform.SetParent(transform, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * (OrbRadius * 2f);
        Destroy(sphere.GetComponent<Collider>()); // hit collider lives on the root

        _orbRenderer = sphere.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.1f, 1f, 0.2f);
        _orbRenderer.material = mat;

        // Trigger collider on the root for bullet hit detection.
        // Larger than the visual orb so fast bullets are more likely to be caught.
        var col = gameObject.AddComponent<SphereCollider>();
        col.radius = HitRadius;
        col.isTrigger = true;

        // Glow light
        var lightGO = new GameObject("OrbLight");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.localPosition = Vector3.zero;
        _glow = lightGO.AddComponent<Light>();
        _glow.type = LightType.Point;
        _glow.color = new Color(0.2f, 1f, 0.3f);
        _glow.intensity = 2f;
        _glow.range = OrbRadius * 8f;
    }

    // ─── Runtime ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (_triggered) return;

        // Pulse the orb color and glow intensity
        float t = (Mathf.Sin(Time.time * PulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        if (_orbRenderer != null)
        {
            float b = Mathf.Lerp(0.7f, 1.0f, t);
            _orbRenderer.material.color = new Color(0.1f * b, b, 0.2f * b);
        }
        if (_glow != null)
            _glow.intensity = Mathf.Lerp(1.5f, 3.5f, t);
    }

    /// <summary>
    /// Sweep-test bullets against this target every physics step. Fast bullets
    /// that tunnel through the trigger collider in a single step would otherwise
    /// go undetected — here we check the LINE SEGMENT from the bullet's previous
    /// position to its current position for proximity to the target center.
    /// </summary>
    private void FixedUpdate()
    {
        if (_triggered) return;

        // Find all active rigidbodies and filter to bullets by name.
        // For a demo with a handful of bullets this is trivial cost.
        Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        float hitSq = HitRadius * HitRadius;
        Vector3 targetPos = transform.position;

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody rb = bodies[i];
            if (rb == null) continue;
            if (!rb.gameObject.name.Contains("Bullet")) continue;

            int id = rb.GetInstanceID();
            Vector3 currPos = rb.position;
            Vector3 prevPos;
            if (!_bulletPrevPos.TryGetValue(id, out prevPos))
                prevPos = currPos; // first sighting — no segment yet
            _bulletPrevPos[id] = currPos;

            // Closest point on segment [prevPos, currPos] to targetPos
            Vector3 seg = currPos - prevPos;
            float segLenSq = seg.sqrMagnitude;
            Vector3 closest;
            if (segLenSq < 1e-10f)
            {
                closest = currPos;
            }
            else
            {
                float t = Mathf.Clamp01(Vector3.Dot(targetPos - prevPos, seg) / segLenSq);
                closest = prevPos + seg * t;
            }

            if ((targetPos - closest).sqrMagnitude <= hitSq)
            {
                _triggered = true;
                OnHit?.Invoke();
                Destroy(rb.gameObject);
                Destroy(gameObject);
                return;
            }
        }
    }

    // Fallback: trigger-based detection for slow bullets (close-range shots)
    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (!other.gameObject.name.Contains("Bullet")) return;

        _triggered = true;
        OnHit?.Invoke();
        Destroy(gameObject);
    }
}
