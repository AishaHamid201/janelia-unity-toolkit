using System;
using UnityEngine;

public class AnimateCylinderTextureLoom : MonoBehaviour
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
    }

    [Serializable]
    public struct PositionConfig
    {
        [Tooltip("Azimuth position in degrees (0-360).")]
        [Range(0f, 360f)]
        public float azimuthDeg;

        [Tooltip("Loom rates at this position. Each rate runs in sequence.")]
        public LoomRateConfig[] loomRates;
    }

    [Header("Protocol")]
    [Tooltip("Each position has an azimuth and one or more loom rates with sweep counts. Runs in sequence.")]
    public PositionConfig[] positions;

    [Header("Loom Settings")]
    [Tooltip("Maximum expansion ratio. Spot appears this many times larger at peak (e.g., 10 = 10x bigger).")]
    public float maxScaleFactor = 10f;

    [Tooltip("If true, each loom includes both progressive (expand) and regressive (contract). If false, progressive only.")]
    public bool includeRegressive = true;

    [Tooltip("Pause between individual looms at base size (seconds).")]
    public float interLoomPauseSec = 1f;

    [Tooltip("Optional pause at peak expansion before regressive phase (seconds).")]
    public float peakPauseSec = 0f;

    [Header("Position")]
    [Tooltip("Fixed elevation (y position).")]
    public float elevation = 0f;

    [Header("Block Settings (LED ON period)")]
    [Tooltip("Duration to hold spot at block position before each new azimuth starts (seconds).")]
    public float blockDurationSeconds = 5f;

    [Tooltip("Azimuth to park the spot during blocks (e.g., 180 = behind the fly).")]
    [Range(0f, 360f)]
    public float blockAzimuthDeg = 180f;

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
    private Material cylinderMaterial;
    private int posIndex = 0;       // Current position in positions[]
    private int rateIndex = 0;      // Current rate within positions[posIndex].loomRates[]
    private int sweepCount = 0;     // Sweeps completed at current rate
    private float phaseStartTime;
    private bool finished = false;
    private int _debugFrameCount = 0;

    // Current rate derived timing
    private float _currentLOverV;
    private float _tc;
    private float _progressiveDuration;
    private float _regressiveDuration;

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
    private class textureLogEntry : Janelia.Logger.Entry
    {
        public float xpos;
        public float ypos;
        public float scaleFactor;
        public float azimuthDeg;
        public int isInBlock;
    };

    private textureLogEntry _currentLogEntry = new textureLogEntry();

    void Start()
    {
        cylinderMaterial = Resources.Load(Janelia.CylinderBackgroundResources.MaterialName, typeof(Material)) as Material;

        if (cylinderMaterial == null)
        {
            Debug.LogError("AnimateCylinderTextureLoom: Could not load material '" + Janelia.CylinderBackgroundResources.MaterialName + "'");
        }

        // Start in block phase
        currentPhase = LoomPhase.Block;
        phaseStartTime = Time.time;
        _azimuthDeg = blockAzimuthDeg;
        _currentScaleFactor = 1f;
        _isInBlock = true;
        _isLooming = false;

        ApplyTextureTransform(blockAzimuthDeg, 1f);

        if (showDebugLog)
        {
            Debug.Log($"[Loom] Start: {positions?.Length} positions, maxScale={maxScaleFactor}, regressive={includeRegressive}");
            if (positions != null)
            {
                for (int p = 0; p < positions.Length; p++)
                {
                    Debug.Log($"[Loom]   Position[{p}]: {positions[p].azimuthDeg}°, {positions[p].loomRates?.Length} rates");
                    if (positions[p].loomRates != null)
                    {
                        for (int r = 0; r < positions[p].loomRates.Length; r++)
                        {
                            var rate = positions[p].loomRates[r];
                            float lv = rate.l / Mathf.Max(rate.v, 0.0001f);
                            Debug.Log($"[Loom]     Rate[{r}]: l={rate.l}, v={rate.v}, l/v={lv:F4}s, sweeps={rate.numSweeps}");
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

        if (currentPhase == LoomPhase.Finished || cylinderMaterial == null)
        {
            if (!finished && showDebugLog)
            {
                Debug.Log("[Loom] FINISHED — all positions completed");
                finished = true;
            }
            return;
        }

        if (positions == null || positions.Length == 0 || posIndex >= positions.Length)
        {
            currentPhase = LoomPhase.Finished;
            _isInBlock = false;
            _isLooming = false;
            ResetTiling();
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
        ApplyTextureTransform(blockAzimuthDeg, 1f);

        if (showDebugLog && _debugFrameCount <= 10)
            Debug.Log($"[Loom] BLOCK (LED ON) — remaining={blockDurationSeconds - elapsed:F1}s, next pos[{posIndex}]={positions[posIndex].azimuthDeg}°");

        LogState();

        if (elapsed >= blockDurationSeconds)
        {
            // Start first rate at current position
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
                Debug.Log($"[Loom] Block done → Position[{posIndex}]={pos.azimuthDeg}°, " +
                          $"Rate[{rateIndex}]: l={rate.l}, v={rate.v}, l/v={_currentLOverV:F4}s, " +
                          $"{rate.numSweeps} sweeps, expand duration={_progressiveDuration:F3}s");
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
        scale = Mathf.Clamp(scale, 1f, maxScaleFactor);
        _currentScaleFactor = scale;

        ApplyTextureTransform(azimuth, scale);

        if (showDebugLog && _debugFrameCount <= 15)
        {
            var rate = positions[posIndex].loomRates[rateIndex];
            Debug.Log($"[Loom] PROGRESSIVE — pos[{posIndex}]={azimuth}°, rate[{rateIndex}] l/v={_currentLOverV:F4}s, " +
                      $"sweep {sweepCount + 1}/{rate.numSweeps}, scale={scale:F2}");
        }

        LogState();

        if (elapsed >= _progressiveDuration)
        {
            if (peakPauseSec > 0f)
            {
                currentPhase = LoomPhase.PeakPause;
                phaseStartTime = Time.time;
            }
            else if (includeRegressive)
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
        _currentScaleFactor = maxScaleFactor;
        _isLooming = true;
        ApplyTextureTransform(azimuth, maxScaleFactor);
        LogState();

        if (elapsed >= peakPauseSec)
        {
            if (includeRegressive)
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
        // scale(t) = tc / (tc/maxScaleFactor + t)
        // At t=0: scale = maxScaleFactor
        // At t=duration: scale = 1
        float scale = _tc / (_tc / maxScaleFactor + elapsed);
        scale = Mathf.Clamp(scale, 1f, maxScaleFactor);
        _currentScaleFactor = scale;

        ApplyTextureTransform(azimuth, scale);
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
        ApplyTextureTransform(azimuth, 1f);
        LogState();

        if (elapsed >= interLoomPauseSec)
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
            Debug.Log($"[Loom] Sweep {sweepCount}/{rate.numSweeps} done — pos[{posIndex}]={positions[posIndex].azimuthDeg}°, rate[{rateIndex}] l/v={_currentLOverV:F4}s");

        if (sweepCount >= rate.numSweeps)
        {
            // All sweeps at this rate done — advance to next rate at this position
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
                    ResetTiling();
                    if (showDebugLog) Debug.Log("[Loom] All positions completed");
                    return;
                }

                // Block (LED ON) before next position
                currentPhase = LoomPhase.Block;
                phaseStartTime = Time.time;
                _isLooming = false;
                ApplyTextureTransform(blockAzimuthDeg, 1f);
                return;
            }

            // Next rate at same position — recompute timing, no block between rates
            sweepCount = 0;
            ComputeCurrentRateTiming();

            if (showDebugLog)
            {
                var nextRate = positions[posIndex].loomRates[rateIndex];
                Debug.Log($"[Loom] Next rate[{rateIndex}]: l={nextRate.l}, v={nextRate.v}, l/v={_currentLOverV:F4}s, " +
                          $"{nextRate.numSweeps} sweeps, expand duration={_progressiveDuration:F3}s");
            }

            // Inter-loom pause before starting the new rate
            currentPhase = LoomPhase.InterLoomPause;
            phaseStartTime = Time.time;
            float azimuth = positions[posIndex].azimuthDeg;
            ApplyTextureTransform(azimuth, 1f);
            return;
        }

        // More sweeps at this rate — inter-loom pause
        currentPhase = LoomPhase.InterLoomPause;
        phaseStartTime = Time.time;
        float az = positions[posIndex].azimuthDeg;
        ApplyTextureTransform(az, 1f);
    }

    // ======================== Helpers ========================

    /// <summary>
    /// Compute timing values for the current rate (l/v, tc, durations).
    /// Call whenever rateIndex changes.
    /// </summary>
    private void ComputeCurrentRateTiming()
    {
        var rate = positions[posIndex].loomRates[rateIndex];
        _currentLOverV = rate.l / Mathf.Max(rate.v, 0.0001f);
        _tc = _currentLOverV * maxScaleFactor;
        _progressiveDuration = _currentLOverV * (maxScaleFactor - 1f);
        _regressiveDuration = _progressiveDuration;
    }

    /// <summary>
    /// Applies texture tiling and offset to keep the spot centered at the given azimuth.
    /// Assumes the spot is centered in the PNG at UV (0.5, 0.5).
    /// </summary>
    private void ApplyTextureTransform(float azimuthDeg, float scaleFactor)
    {
        if (cylinderMaterial == null) return;

        float tiling = 1f / scaleFactor;
        float offsetX = 0.5f * (1f - tiling) + (azimuthDeg / 360f) * tiling;
        float offsetY = 0.5f * (1f - tiling) + elevation * tiling;

        cylinderMaterial.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
        cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(offsetX, offsetY));
    }

    /// <summary>
    /// Resets tiling to (1,1) so other scripts are not affected after protocol ends.
    /// </summary>
    private void ResetTiling()
    {
        if (cylinderMaterial == null) return;
        cylinderMaterial.SetTextureScale("_MainTex", new Vector2(1f, 1f));
    }

    private void LogState()
    {
        float tiling = 1f / _currentScaleFactor;
        float offsetX = 0.5f * (1f - tiling) + (_azimuthDeg / 360f) * tiling;
        float offsetY = 0.5f * (1f - tiling) + elevation * tiling;

        _currentLogEntry.xpos = offsetX;
        _currentLogEntry.ypos = offsetY;
        _currentLogEntry.scaleFactor = _currentScaleFactor;
        _currentLogEntry.azimuthDeg = _azimuthDeg;
        _currentLogEntry.isInBlock = _isInBlock ? 1 : 0;
        Janelia.Logger.Log(_currentLogEntry);
    }

    private void OnDestroy()
    {
        ResetTiling();
    }
}
