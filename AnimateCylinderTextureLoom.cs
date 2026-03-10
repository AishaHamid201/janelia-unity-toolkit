using System;
using UnityEngine;

public class AnimateCylinderTextureLoom : MonoBehaviour
{
    [Header("Loom Parameters")]
    [Tooltip("l/|v| ratio in seconds. Standard looming parameter. Smaller = faster loom. Typical fly values: 0.01–0.1s.")]
    public float l_over_v = 0.05f;

    [Tooltip("Maximum expansion ratio. Spot appears this many times larger at peak. E.g., 10 = 10x bigger.")]
    public float maxScaleFactor = 10f;

    [Tooltip("If true, each loom includes both progressive (expand) and regressive (contract). If false, progressive only — spot resets instantly after reaching max.")]
    public bool includeRegressive = true;

    [Header("Protocol — Position Sequence")]
    [Tooltip("Azimuth positions in degrees (0-360). Protocol runs each position in sequence.")]
    public float[] azimuthPositionsDeg;

    [Tooltip("Number of looms at each position. Must match azimuthPositionsDeg length.")]
    public int[] loomRepeatVec;

    [Header("Position")]
    [Tooltip("Fixed elevation (y position). No elevation stepping.")]
    public float elevation = 0f;

    [Header("Timing")]
    [Tooltip("Pause between individual looms at the same position (seconds). Spot is at base size.")]
    public float interLoomPauseSec = 1f;

    [Tooltip("Optional pause at peak expansion before regressive phase starts (seconds).")]
    public float peakPauseSec = 0f;

    [Tooltip("Duration to hold spot at block position before each new azimuth starts (seconds).")]
    public float blockDurationSeconds = 5f;

    [Tooltip("Azimuth to park the spot during inter-position blocks (e.g., 180 = behind the fly).")]
    [Range(0f, 360f)]
    public float blockAzimuthDeg = 180f;

    [Header("Debug")]
    public bool showDebugLog = false;

    // Public state — readable by LED trigger and other scripts
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;

    private float _currentScaleFactor = 1f;
    public float CurrentScaleFactor => _currentScaleFactor;

    private bool _isLooming = false;
    public bool IsLooming => _isLooming;

    // Internal state
    private Material cylinderMaterial;
    private int posIndex = 0;           // Current index into azimuthPositionsDeg
    private int loomCount = 0;          // Looms completed at current position
    private float phaseStartTime;
    private bool finished = false;
    private int _debugFrameCount = 0;

    // Derived timing
    private float _progressiveDuration;  // Duration of one expansion
    private float _regressiveDuration;   // Duration of one contraction (same as progressive)
    private float _tc;                   // Virtual collision time

    private enum LoomPhase
    {
        Block,              // Holding at block position between positions
        Progressive,        // Expanding (scale 1 → maxScaleFactor)
        PeakPause,          // Optional pause at max expansion
        Regressive,         // Contracting (scale maxScaleFactor → 1)
        InterLoomPause,     // Pause between looms at base size
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
    };

    private textureLogEntry _currentLogEntry = new textureLogEntry();

    void Start()
    {
        cylinderMaterial = Resources.Load(Janelia.CylinderBackgroundResources.MaterialName, typeof(Material)) as Material;

        if (cylinderMaterial == null)
        {
            Debug.LogError("AnimateCylinderTextureLoom: Could not load material '" + Janelia.CylinderBackgroundResources.MaterialName + "'");
        }

        // Compute loom timing from l/v and maxScaleFactor
        // Using small-angle approximation: scale(t) = tc / (tc - t)
        // tc = l_over_v * maxScaleFactor
        // Duration to reach maxScaleFactor = l_over_v * (maxScaleFactor - 1)
        _tc = l_over_v * maxScaleFactor;
        _progressiveDuration = l_over_v * (maxScaleFactor - 1f);
        _regressiveDuration = _progressiveDuration;

        // Start in block phase
        currentPhase = LoomPhase.Block;
        phaseStartTime = Time.time;
        _azimuthDeg = blockAzimuthDeg;
        _currentScaleFactor = 1f;

        ApplyTextureTransform(blockAzimuthDeg, 1f);

        if (showDebugLog)
        {
            Debug.Log($"[Loom] Start: l/v={l_over_v}s, maxScale={maxScaleFactor}, " +
                      $"progressive duration={_progressiveDuration:F3}s, " +
                      $"positions={azimuthPositionsDeg?.Length}, " +
                      $"regressive={includeRegressive}");
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

        // Validate arrays
        if (posIndex >= azimuthPositionsDeg.Length || posIndex >= loomRepeatVec.Length)
        {
            currentPhase = LoomPhase.Finished;
            _isLooming = false;
            ResetTiling();
            return;
        }

        float elapsed = Time.time - phaseStartTime;
        float currentAzimuth = azimuthPositionsDeg[posIndex];

        switch (currentPhase)
        {
            case LoomPhase.Block:
                HandleBlockPhase(elapsed, currentAzimuth);
                break;
            case LoomPhase.Progressive:
                HandleProgressivePhase(elapsed, currentAzimuth);
                break;
            case LoomPhase.PeakPause:
                HandlePeakPausePhase(elapsed, currentAzimuth);
                break;
            case LoomPhase.Regressive:
                HandleRegressivePhase(elapsed, currentAzimuth);
                break;
            case LoomPhase.InterLoomPause:
                HandleInterLoomPausePhase(elapsed, currentAzimuth);
                break;
        }
    }

    private void HandleBlockPhase(float elapsed, float nextAzimuth)
    {
        _azimuthDeg = blockAzimuthDeg;
        _currentScaleFactor = 1f;
        _isLooming = false;
        ApplyTextureTransform(blockAzimuthDeg, 1f);

        if (showDebugLog && _debugFrameCount <= 10)
            Debug.Log($"[Loom] BLOCK — remaining={blockDurationSeconds - elapsed:F1}s, next pos[{posIndex}]={nextAzimuth}°");

        LogState();

        if (elapsed >= blockDurationSeconds)
        {
            // Transition to first loom at this position
            loomCount = 0;
            currentPhase = LoomPhase.Progressive;
            phaseStartTime = Time.time;

            if (showDebugLog)
                Debug.Log($"[Loom] Block done — starting position[{posIndex}]={nextAzimuth}°, {loomRepeatVec[posIndex]} looms");
        }
    }

    private void HandleProgressivePhase(float elapsed, float azimuth)
    {
        _azimuthDeg = azimuth;
        _isLooming = true;

        // Looming expansion: scale(t) = tc / (tc - t)
        // Clamped to maxScaleFactor
        float scale = _tc / (_tc - elapsed);
        scale = Mathf.Clamp(scale, 1f, maxScaleFactor);
        _currentScaleFactor = scale;

        ApplyTextureTransform(azimuth, scale);

        if (showDebugLog && _debugFrameCount <= 15)
            Debug.Log($"[Loom] PROGRESSIVE — pos[{posIndex}]={azimuth}°, loom {loomCount + 1}/{loomRepeatVec[posIndex]}, scale={scale:F2}, t={elapsed:F3}s");

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
                // No regressive — count this as one complete loom
                CompleteSingleLoom(azimuth);
            }
        }
    }

    private void HandlePeakPausePhase(float elapsed, float azimuth)
    {
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
                CompleteSingleLoom(azimuth);
            }
        }
    }

    private void HandleRegressivePhase(float elapsed, float azimuth)
    {
        _azimuthDeg = azimuth;
        _isLooming = true;

        // Regressive: time-reversed looming
        // scale(t) = tc / (tc/maxScaleFactor + t)
        // At t=0: scale = maxScaleFactor
        // At t=_regressiveDuration: scale = 1
        float scale = _tc / (_tc / maxScaleFactor + elapsed);
        scale = Mathf.Clamp(scale, 1f, maxScaleFactor);
        _currentScaleFactor = scale;

        ApplyTextureTransform(azimuth, scale);

        if (showDebugLog && _debugFrameCount <= 20)
            Debug.Log($"[Loom] REGRESSIVE — pos[{posIndex}]={azimuth}°, loom {loomCount + 1}/{loomRepeatVec[posIndex]}, scale={scale:F2}, t={elapsed:F3}s");

        LogState();

        if (elapsed >= _regressiveDuration)
        {
            CompleteSingleLoom(azimuth);
        }
    }

    private void HandleInterLoomPausePhase(float elapsed, float azimuth)
    {
        _azimuthDeg = azimuth;
        _currentScaleFactor = 1f;
        _isLooming = false;
        ApplyTextureTransform(azimuth, 1f);
        LogState();

        if (elapsed >= interLoomPauseSec)
        {
            // Start next loom
            currentPhase = LoomPhase.Progressive;
            phaseStartTime = Time.time;
        }
    }

    private void CompleteSingleLoom(float azimuth)
    {
        loomCount++;

        if (showDebugLog)
            Debug.Log($"[Loom] Loom {loomCount}/{loomRepeatVec[posIndex]} done at {azimuth}°");

        if (loomCount >= loomRepeatVec[posIndex])
        {
            // All looms at this position done — move to next position
            posIndex++;

            if (posIndex >= azimuthPositionsDeg.Length || posIndex >= loomRepeatVec.Length)
            {
                currentPhase = LoomPhase.Finished;
                _isLooming = false;
                ResetTiling();

                if (showDebugLog)
                    Debug.Log("[Loom] All positions completed");
                return;
            }

            // Go to block phase for next position
            currentPhase = LoomPhase.Block;
            phaseStartTime = Time.time;
            ApplyTextureTransform(blockAzimuthDeg, 1f);
        }
        else
        {
            // More looms at this position — inter-loom pause
            currentPhase = LoomPhase.InterLoomPause;
            phaseStartTime = Time.time;
            ApplyTextureTransform(azimuth, 1f);
        }
    }

    /// <summary>
    /// Applies texture tiling (scale) and offset to keep the spot centered at the given azimuth.
    /// Assumes the spot is centered in the PNG at UV (0.5, 0.5).
    /// </summary>
    private void ApplyTextureTransform(float azimuthDeg, float scaleFactor)
    {
        if (cylinderMaterial == null) return;

        // Tiling = 1/scaleFactor (zoom in to make spot appear larger)
        float tiling = 1f / scaleFactor;

        // Offset to keep the spot centered at the desired azimuth while zooming.
        // Derivation: at the cylinder position where the spot should appear,
        // the sampled UV must still equal (0.5, 0.5) regardless of tiling.
        //   uv_cyl * tiling + offset = spotUV (0.5, 0.5)
        //   offset = 0.5 - uv_cyl * tiling
        //   At tiling=1: offset = azimuth/360, so uv_cyl = 0.5 - azimuth/360
        //   At tiling=T: offset = 0.5 - (0.5 - azimuth/360) * T
        //                       = 0.5*(1-T) + (azimuth/360)*T
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
        Janelia.Logger.Log(_currentLogEntry);
    }

    private void OnDestroy()
    {
        ResetTiling();
    }
}
