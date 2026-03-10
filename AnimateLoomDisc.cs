using System;
using UnityEngine;

public class AnimateLoomDisc : MonoBehaviour
{
    [Serializable]
    public struct LoomRateConfig
    {
        [Tooltip("Half-size of the virtual approaching object (arbitrary units).")]
        public float l;

        [Tooltip("Approach velocity (same units per second).")]
        public float v;

        [Tooltip("Number of complete loom cycles at this rate.")]
        public int numSweeps;

        [Tooltip("Maximum expansion ratio (e.g., 10 = spot appears 10x larger at peak).")]
        public float maxScaleFactor;

        [Tooltip("If true, loom includes both progressive (expand) and regressive (contract). If false, progressive only.")]
        public bool includeRegressive;

        [Tooltip("Pause between individual looms at base size (seconds).")]
        public float interLoomPauseSec;

        [Tooltip("Optional pause at peak expansion before regressive phase (seconds).")]
        public float peakPauseSec;
    }

    [Serializable]
    public struct PositionConfig
    {
        [Tooltip("Azimuth position in degrees (0-360).")]
        [Range(0f, 360f)]
        public float azimuthDeg;

        [Tooltip("Loom rates at this position. Each rate runs in sequence with its own settings.")]
        public LoomRateConfig[] loomRates;
    }

    [Header("Protocol")]
    [Tooltip("Each position has an azimuth and one or more loom rates. Runs in sequence.")]
    public PositionConfig[] positions;

    [Header("Disc Appearance")]
    [Tooltip("Base angular radius of the spot in degrees when scale factor = 1. The spot grows to baseRadius * maxScaleFactor at peak.")]
    public float baseSpotAngularRadiusDeg = 5f;

    [Tooltip("Color of the looming disc.")]
    public Color spotColor = Color.black;

    [Header("Position")]
    [Tooltip("Elevation angle in degrees (0 = horizontal, positive = above).")]
    public float elevationDeg = 0f;

    [Tooltip("Azimuth offset for calibrating the disc direction to match your rig (degrees). Adjust if the disc doesn't appear at the expected position.")]
    public float azimuthOffsetDeg = 0f;

    [Header("Block Settings (LED ON period)")]
    [Tooltip("Duration to hold disc at block position before each new azimuth starts (seconds).")]
    public float blockDurationSeconds = 5f;

    [Tooltip("Azimuth to park the disc during blocks (e.g., 180 = behind the fly).")]
    [Range(0f, 360f)]
    public float blockAzimuthDeg = 180f;

    [Header("Cylinder Reference")]
    [Tooltip("Radius of the background cylinder in Unity units (for distance clamping).")]
    public float cylinderRadius = 1f;

    [Header("Debug")]
    public bool showDebugLog = false;

    // ---- Public state (readable by LED trigger and other scripts) ----
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;

    private float _currentScaleFactor = 1f;
    public float CurrentScaleFactor => _currentScaleFactor;

    private bool _isInBlock = true;
    public bool IsInBlock => _isInBlock;

    private bool _isLooming = false;
    public bool IsLooming => _isLooming;

    // ---- Internal state ----
    private GameObject discObject;
    private Transform flyTransform;
    private int posIndex = 0;
    private int rateIndex = 0;
    private int sweepCount = 0;
    private float phaseStartTime;
    private bool finished = false;
    private int _debugFrameCount = 0;

    // Current rate derived timing
    private float _currentLOverV;
    private float _currentMaxScale;
    private float _tc;
    private float _progressiveDuration;
    private float _regressiveDuration;
    private bool _currentIncludeRegressive;
    private float _currentInterLoomPause;
    private float _currentPeakPause;

    private enum LoomPhase
    {
        Block,
        Progressive,
        PeakPause,
        Regressive,
        InterLoomPause,
        Finished
    }
    private LoomPhase currentPhase = LoomPhase.Block;

    // Logging
    [Serializable]
    private class discLogEntry : Janelia.Logger.Entry
    {
        public float azimuthDeg;
        public float angularRadiusDeg;
        public float scaleFactor;
        public int isInBlock;
    };

    private discLogEntry _currentLogEntry = new discLogEntry();

    void Start()
    {
        // Find the camera (fly position = center of cylinder)
        Camera cam = Camera.main;
        if (cam != null)
        {
            flyTransform = cam.transform;
        }
        else
        {
            Debug.LogError("AnimateLoomDisc: No main camera found. The camera represents the fly position.");
            return;
        }

        // Create the 3D disc
        discObject = CreateDiscObject();

        // Start in block phase
        currentPhase = LoomPhase.Block;
        phaseStartTime = Time.time;
        _azimuthDeg = blockAzimuthDeg;
        _currentScaleFactor = 1f;
        _isInBlock = true;
        _isLooming = false;

        UpdateDiscTransform(blockAzimuthDeg, baseSpotAngularRadiusDeg);

        if (showDebugLog)
        {
            Debug.Log($"[LoomDisc] Start: {positions?.Length} positions, baseRadius={baseSpotAngularRadiusDeg}°");
            if (positions != null)
            {
                for (int p = 0; p < positions.Length; p++)
                {
                    Debug.Log($"[LoomDisc]   Position[{p}]: {positions[p].azimuthDeg}°, {positions[p].loomRates?.Length} rates");
                    if (positions[p].loomRates != null)
                    {
                        for (int r = 0; r < positions[p].loomRates.Length; r++)
                        {
                            var rate = positions[p].loomRates[r];
                            float lv = rate.l / Mathf.Max(rate.v, 0.0001f);
                            float dur = lv * (rate.maxScaleFactor - 1f);
                            float peakRadiusDeg = baseSpotAngularRadiusDeg * rate.maxScaleFactor;
                            Debug.Log($"[LoomDisc]     Rate[{r}]: l={rate.l}, v={rate.v}, l/v={lv:F4}s, " +
                                      $"maxScale={rate.maxScaleFactor}, sweeps={rate.numSweeps}, " +
                                      $"regressive={rate.includeRegressive}, expandDur={dur:F3}s, peakRadius={peakRadiusDeg:F1}°");
                        }
                    }
                }
            }
        }

        LogState();
    }

    void Update()
    {
        _debugFrameCount++;

        if (currentPhase == LoomPhase.Finished || discObject == null)
        {
            if (!finished && showDebugLog)
            {
                Debug.Log("[LoomDisc] FINISHED — all positions completed");
                finished = true;
            }
            return;
        }

        if (positions == null || positions.Length == 0 || posIndex >= positions.Length)
        {
            currentPhase = LoomPhase.Finished;
            _isInBlock = false;
            _isLooming = false;
            discObject.SetActive(false);
            return;
        }

        float elapsed = Time.time - phaseStartTime;

        switch (currentPhase)
        {
            case LoomPhase.Block:
                HandleBlockPhase(elapsed);
                break;
            case LoomPhase.Progressive:
                HandleProgressivePhase(elapsed);
                break;
            case LoomPhase.PeakPause:
                HandlePeakPausePhase(elapsed);
                break;
            case LoomPhase.Regressive:
                HandleRegressivePhase(elapsed);
                break;
            case LoomPhase.InterLoomPause:
                HandleInterLoomPausePhase(elapsed);
                break;
        }
    }

    // ======================== Phase handlers ========================

    private void HandleBlockPhase(float elapsed)
    {
        _azimuthDeg = blockAzimuthDeg;
        _currentScaleFactor = 1f;
        _isInBlock = true;
        _isLooming = false;
        UpdateDiscTransform(blockAzimuthDeg, baseSpotAngularRadiusDeg);

        if (showDebugLog && _debugFrameCount <= 10)
            Debug.Log($"[LoomDisc] BLOCK (LED ON) — remaining={blockDurationSeconds - elapsed:F1}s, next pos[{posIndex}]={positions[posIndex].azimuthDeg}°");

        LogState();

        if (elapsed >= blockDurationSeconds)
        {
            rateIndex = 0;
            sweepCount = 0;
            ComputeCurrentRateTiming();

            currentPhase = LoomPhase.Progressive;
            phaseStartTime = Time.time;
            _isInBlock = false;

            if (showDebugLog)
            {
                var pos = positions[posIndex];
                var rate = pos.loomRates[rateIndex];
                Debug.Log($"[LoomDisc] Block done → Position[{posIndex}]={pos.azimuthDeg}°, " +
                          $"Rate[{rateIndex}]: l={rate.l}, v={rate.v}, l/v={_currentLOverV:F4}s, " +
                          $"maxScale={_currentMaxScale}, {rate.numSweeps} sweeps, expandDur={_progressiveDuration:F3}s");
            }
        }
    }

    private void HandleProgressivePhase(float elapsed)
    {
        float azimuth = positions[posIndex].azimuthDeg;
        _azimuthDeg = azimuth;
        _isInBlock = false;
        _isLooming = true;

        // Looming expansion: scale(t) = tc / (tc - t)
        float scale = _tc / (_tc - elapsed);
        scale = Mathf.Clamp(scale, 1f, _currentMaxScale);
        _currentScaleFactor = scale;

        float angularRadiusDeg = baseSpotAngularRadiusDeg * scale;
        UpdateDiscTransform(azimuth, angularRadiusDeg);

        if (showDebugLog && _debugFrameCount <= 15)
        {
            var rate = positions[posIndex].loomRates[rateIndex];
            Debug.Log($"[LoomDisc] PROGRESSIVE — pos[{posIndex}]={azimuth}°, rate[{rateIndex}] l/v={_currentLOverV:F4}s, " +
                      $"sweep {sweepCount + 1}/{rate.numSweeps}, scale={scale:F2}/{_currentMaxScale}, radius={angularRadiusDeg:F1}°");
        }

        LogState();

        if (elapsed >= _progressiveDuration)
        {
            if (_currentPeakPause > 0f)
            {
                currentPhase = LoomPhase.PeakPause;
                phaseStartTime = Time.time;
            }
            else if (_currentIncludeRegressive)
            {
                currentPhase = LoomPhase.Regressive;
                phaseStartTime = Time.time;
            }
            else
            {
                CompleteSingleLoom();
            }
        }
    }

    private void HandlePeakPausePhase(float elapsed)
    {
        float azimuth = positions[posIndex].azimuthDeg;
        _azimuthDeg = azimuth;
        _currentScaleFactor = _currentMaxScale;
        _isLooming = true;

        float angularRadiusDeg = baseSpotAngularRadiusDeg * _currentMaxScale;
        UpdateDiscTransform(azimuth, angularRadiusDeg);
        LogState();

        if (elapsed >= _currentPeakPause)
        {
            if (_currentIncludeRegressive)
            {
                currentPhase = LoomPhase.Regressive;
                phaseStartTime = Time.time;
            }
            else
            {
                CompleteSingleLoom();
            }
        }
    }

    private void HandleRegressivePhase(float elapsed)
    {
        float azimuth = positions[posIndex].azimuthDeg;
        _azimuthDeg = azimuth;
        _isLooming = true;

        // Regressive: time-reversed looming
        // scale(t) = tc / (tc/maxScale + t)
        float scale = _tc / (_tc / _currentMaxScale + elapsed);
        scale = Mathf.Clamp(scale, 1f, _currentMaxScale);
        _currentScaleFactor = scale;

        float angularRadiusDeg = baseSpotAngularRadiusDeg * scale;
        UpdateDiscTransform(azimuth, angularRadiusDeg);
        LogState();

        if (elapsed >= _regressiveDuration)
        {
            CompleteSingleLoom();
        }
    }

    private void HandleInterLoomPausePhase(float elapsed)
    {
        float azimuth = positions[posIndex].azimuthDeg;
        _azimuthDeg = azimuth;
        _currentScaleFactor = 1f;
        _isLooming = false;

        UpdateDiscTransform(azimuth, baseSpotAngularRadiusDeg);
        LogState();

        if (elapsed >= _currentInterLoomPause)
        {
            currentPhase = LoomPhase.Progressive;
            phaseStartTime = Time.time;
        }
    }

    // ======================== State transitions ========================

    private void CompleteSingleLoom()
    {
        sweepCount++;
        var rate = positions[posIndex].loomRates[rateIndex];

        if (showDebugLog)
            Debug.Log($"[LoomDisc] Sweep {sweepCount}/{rate.numSweeps} done — pos[{posIndex}]={positions[posIndex].azimuthDeg}°, rate[{rateIndex}] l/v={_currentLOverV:F4}s");

        if (sweepCount >= rate.numSweeps)
        {
            // All sweeps at this rate done — advance to next rate
            rateIndex++;

            if (rateIndex >= positions[posIndex].loomRates.Length)
            {
                // All rates at this position done — advance to next position
                posIndex++;

                if (posIndex >= positions.Length)
                {
                    currentPhase = LoomPhase.Finished;
                    _isInBlock = false;
                    _isLooming = false;
                    discObject.SetActive(false);
                    if (showDebugLog) Debug.Log("[LoomDisc] All positions completed");
                    return;
                }

                // Block (LED ON) before next position
                currentPhase = LoomPhase.Block;
                phaseStartTime = Time.time;
                _isLooming = false;
                UpdateDiscTransform(blockAzimuthDeg, baseSpotAngularRadiusDeg);
                return;
            }

            // Next rate at same position — recompute timing from the new rate's settings
            sweepCount = 0;
            ComputeCurrentRateTiming();

            if (showDebugLog)
            {
                var nextRate = positions[posIndex].loomRates[rateIndex];
                Debug.Log($"[LoomDisc] Next rate[{rateIndex}]: l={nextRate.l}, v={nextRate.v}, l/v={_currentLOverV:F4}s, " +
                          $"maxScale={_currentMaxScale}, {nextRate.numSweeps} sweeps, expandDur={_progressiveDuration:F3}s");
            }

            // Use the NEW rate's inter-loom pause for the transition
            currentPhase = LoomPhase.InterLoomPause;
            phaseStartTime = Time.time;
            float azimuth = positions[posIndex].azimuthDeg;
            UpdateDiscTransform(azimuth, baseSpotAngularRadiusDeg);
            return;
        }

        // More sweeps at this rate — inter-loom pause
        currentPhase = LoomPhase.InterLoomPause;
        phaseStartTime = Time.time;
        float az = positions[posIndex].azimuthDeg;
        UpdateDiscTransform(az, baseSpotAngularRadiusDeg);
    }

    // ======================== Helpers ========================

    /// <summary>
    /// Reads all settings from the current LoomRateConfig and computes derived timing.
    /// Call whenever rateIndex changes.
    /// </summary>
    private void ComputeCurrentRateTiming()
    {
        var rate = positions[posIndex].loomRates[rateIndex];
        _currentLOverV = rate.l / Mathf.Max(rate.v, 0.0001f);
        _currentMaxScale = rate.maxScaleFactor;
        _currentIncludeRegressive = rate.includeRegressive;
        _currentInterLoomPause = rate.interLoomPauseSec;
        _currentPeakPause = rate.peakPauseSec;
        _tc = _currentLOverV * _currentMaxScale;
        _progressiveDuration = _currentLOverV * (_currentMaxScale - 1f);
        _regressiveDuration = _progressiveDuration;
    }

    /// <summary>
    /// Positions, orients, and scales the disc to produce the desired angular radius
    /// at the given azimuth, as seen from the fly.
    /// </summary>
    private void UpdateDiscTransform(float azimuthDeg, float angularRadiusDeg)
    {
        if (discObject == null || flyTransform == null) return;

        // Clamp angular radius to prevent degenerate cases
        angularRadiusDeg = Mathf.Clamp(angularRadiusDeg, 0.01f, 89f);
        float angRad = angularRadiusDeg * Mathf.Deg2Rad;
        float tanAng = Mathf.Tan(angRad);

        // Compute direction from fly to disc position
        // Azimuth rotates around Y axis, elevation tilts up/down
        Vector3 localDir = Quaternion.Euler(-elevationDeg, azimuthDeg + azimuthOffsetDeg, 0f) * Vector3.forward;
        Vector3 worldDir = flyTransform.TransformDirection(localDir);

        // Choose distance and disc radius to produce the desired angular size
        // For a flat disc: angularRadius = arctan(discRadius / distance)
        // So: discRadius = distance * tan(angularRadius)

        float minDistance = 0.02f;
        float maxDiscRadius = cylinderRadius * 0.95f;

        // Start at half the cylinder radius
        float distance = cylinderRadius * 0.5f;
        float discRadius = distance * tanAng;

        // If disc would extend beyond the cylinder, cap the radius and adjust distance
        if (discRadius > maxDiscRadius)
        {
            discRadius = maxDiscRadius;
            distance = discRadius / tanAng;
        }

        // Ensure minimum distance (above camera near plane)
        if (distance < minDistance)
        {
            distance = minDistance;
            discRadius = distance * tanAng;
        }

        // Position the disc
        discObject.transform.position = flyTransform.position + worldDir * distance;

        // Billboard: face the fly (disc's +Z faces toward the camera)
        discObject.transform.rotation = Quaternion.LookRotation(flyTransform.position - discObject.transform.position);

        // Scale the disc (mesh has unit radius, so localScale = discRadius)
        discObject.transform.localScale = new Vector3(discRadius, discRadius, 1f);
    }

    /// <summary>
    /// Creates the looming disc GameObject with a flat circular mesh and unlit material.
    /// </summary>
    private GameObject CreateDiscObject()
    {
        GameObject disc = new GameObject("LoomDisc");
        MeshFilter mf = disc.AddComponent<MeshFilter>();
        MeshRenderer mr = disc.AddComponent<MeshRenderer>();

        mf.mesh = CreateDiscMesh(64);

        // Use Unlit/Color shader for a solid color disc
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = spotColor;
        mr.material = mat;

        // No shadows
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return disc;
    }

    /// <summary>
    /// Generates a flat disc mesh (unit radius circle in the XY plane) with double-sided triangles.
    /// </summary>
    private Mesh CreateDiscMesh(int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "LoomDisc";

        int vertCount = segments + 1;
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];

        // Center vertex
        vertices[0] = Vector3.zero;
        normals[0] = Vector3.forward;

        // Edge vertices (unit circle in XY plane)
        for (int i = 0; i < segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            normals[i + 1] = Vector3.forward;
        }

        // Double-sided triangle fan (visible from both sides)
        int[] triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments + 1;

            // Front face (+Z side)
            triangles[i * 6 + 0] = 0;
            triangles[i * 6 + 1] = next;
            triangles[i * 6 + 2] = i + 1;

            // Back face (-Z side)
            triangles[i * 6 + 3] = 0;
            triangles[i * 6 + 4] = i + 1;
            triangles[i * 6 + 5] = next;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;

        return mesh;
    }

    private void LogState()
    {
        float angularRadiusDeg = baseSpotAngularRadiusDeg * _currentScaleFactor;
        _currentLogEntry.azimuthDeg = _azimuthDeg;
        _currentLogEntry.angularRadiusDeg = angularRadiusDeg;
        _currentLogEntry.scaleFactor = _currentScaleFactor;
        _currentLogEntry.isInBlock = _isInBlock ? 1 : 0;
        Janelia.Logger.Log(_currentLogEntry);
    }

    private void OnDestroy()
    {
        if (discObject != null)
        {
            Destroy(discObject);
        }
    }
}
