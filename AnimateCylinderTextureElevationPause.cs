using System;
using UnityEngine;

public class AnimateCylinderTextureElevationPause : MonoBehaviour
{
    public float[] vRotDeg_per_sec;

    public int[] sweepRepeatVec;

    public float numElevationSteps = 10.0f;

    public float delaySeconds = 10;
    public float offsetTex = -90.0f;

    public float offsetEl = 0.1f;
    public float maxEl = 1.0f;

    [Header("Pause Settings (first sweep per elevation)")]
    [Tooltip("Azimuth where the cylinder pauses on the first sweep (degrees, 0-360). Set this to the toDeg of your LED range.")]
    [Range(0f, 360f)]
    public float pauseAtAzimuthDeg = 30f;

    [Tooltip("How long the cylinder pauses at the pause azimuth (seconds).")]
    public float pauseDurationSeconds = 10f;

    [Header("Debug")]
    public bool showDebugLog = false;

    private int repeats = 1;
    private Material cylinderMaterial;
    private float elevation;
    private int currentStep = 1;
    private float rotDir = 1.0f;
    private int vel = 0;
    private float waitTime = 0;
    private int _debugFrameCount = 0;

    // Pause state
    private float _totalPauseTime = 0f;
    private bool _isPaused = false;
    private float _pauseStartTime = 0f;
    private bool _hasTriggeredPauseThisElevation = false;
    private bool _isFirstSweepAtElevation = true;
    private float _prevAzimuthDeg = 0f;

    // Public API
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;
    public bool IsPaused => _isPaused;
    public bool IsFirstSweepAtElevation => _isFirstSweepAtElevation;

    // Logging
    [Serializable]
    private class textureLogEntry : Janelia.Logger.Entry
    {
        public float xpos;
        public float ypos;
        public int isPaused;
        public int isFirstSweep;
    };

    private textureLogEntry _currentLogEntry = new textureLogEntry();

    void Start()
    {
        cylinderMaterial = Resources.Load(Janelia.CylinderBackgroundResources.MaterialName, typeof(Material)) as Material;

        if (cylinderMaterial == null)
        {
            Debug.LogError("Could not load material '" + Janelia.CylinderBackgroundResources.MaterialName + "'");
        }

        if (showDebugLog)
        {
            Debug.Log($"[ElevationPause] Start: material={(cylinderMaterial != null ? "LOADED" : "NULL")}, " +
                      $"vRotDeg_per_sec.Length={vRotDeg_per_sec?.Length}, sweepRepeatVec.Length={sweepRepeatVec?.Length}, " +
                      $"delaySeconds={delaySeconds}, offsetTex={offsetTex}, numElevationSteps={numElevationSteps}, " +
                      $"offsetEl={offsetEl}, maxEl={maxEl}, pauseAt={pauseAtAzimuthDeg}°, pauseDuration={pauseDurationSeconds}s");
        }

        // Reset texture based on offset
        elevation = offsetEl;
        float x = offsetTex / 360.0f;
        float y = elevation;

        Vector2 offset = new Vector2(x, y);
        cylinderMaterial.SetTextureOffset("_MainTex", offset);

        _azimuthDeg = ((x % 1f) + 1f) % 1f * 360f;
        _prevAzimuthDeg = _azimuthDeg;
        _isFirstSweepAtElevation = true;
        _hasTriggeredPauseThisElevation = false;
        _totalPauseTime = 0f;

        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        _currentLogEntry.isPaused = 0;
        _currentLogEntry.isFirstSweep = 1;
        Janelia.Logger.Log(_currentLogEntry);
    }

    void Update()
    {
        _debugFrameCount++;

        // Done with all velocities
        if (vel >= vRotDeg_per_sec.Length || vel >= sweepRepeatVec.Length)
        {
            if (showDebugLog && _debugFrameCount <= 5)
                Debug.Log($"[ElevationPause] Frame {_debugFrameCount}: DONE — vel={vel} >= array length");
            return;
        }

        // Check if a velocity has been completed
        if (currentStep > repeats * 2 * sweepRepeatVec[vel] * numElevationSteps)
        {
            vel += 1;
            currentStep = 1;
            elevation = offsetEl;
            // Reset pause state for new velocity
            _totalPauseTime = 0f;
            _hasTriggeredPauseThisElevation = false;
            _isFirstSweepAtElevation = true;
            _isPaused = false;
            if (vel <= vRotDeg_per_sec.Length)
            {
                waitTime = Time.time;
            }
        }

        if (cylinderMaterial & Time.time >= (waitTime + delaySeconds))
        {
            // Handle active pause
            if (_isPaused)
            {
                float pauseElapsed = Time.time - _pauseStartTime;
                if (pauseElapsed >= pauseDurationSeconds)
                {
                    // Pause ended
                    _totalPauseTime += pauseDurationSeconds;
                    _isPaused = false;
                    if (showDebugLog)
                        Debug.Log($"[ElevationPause] Pause ended — totalPauseTime={_totalPauseTime:F2}s, resuming rotation");
                }
                else
                {
                    // Still paused — keep texture frozen, log state
                    _currentLogEntry.xpos = pauseAtAzimuthDeg / 360f;
                    _currentLogEntry.ypos = elevation;
                    _currentLogEntry.isPaused = 1;
                    _currentLogEntry.isFirstSweep = _isFirstSweepAtElevation ? 1 : 0;
                    Janelia.Logger.Log(_currentLogEntry);
                    return;
                }
            }

            // Compute position with pause-adjusted time
            float dTime = Time.time - (waitTime + delaySeconds) - _totalPauseTime;

            float x = offsetTex / 360.0f + dTime * rotDir * (vRotDeg_per_sec[vel] / 360.0f) % 1;

            // Check if a step (full 360° rotation) has been completed
            if (dTime * (vRotDeg_per_sec[vel] / 360.0f) > currentStep)
            {
                if (currentStep % (2 * sweepRepeatVec[vel]) == 0)
                {
                    // All repeats at this elevation done — change elevation
                    elevation += (maxEl - offsetEl) / numElevationSteps;
                    rotDir = 1.0f;
                    // Reset pause state for new elevation
                    _hasTriggeredPauseThisElevation = false;
                    _isFirstSweepAtElevation = true;

                    if (showDebugLog)
                        Debug.Log($"[ElevationPause] Elevation change → {elevation:F3}, step={currentStep + 1}");
                }
                else if (currentStep % 2 == 0)
                {
                    rotDir = 1.0f;
                    _isFirstSweepAtElevation = false;
                }
                else
                {
                    // First step completed (odd step) — no longer first sweep
                    rotDir = -1.0f;
                    _isFirstSweepAtElevation = false;
                }
                currentStep += 1;
            }

            // Compute azimuth
            _azimuthDeg = ((x % 1f) + 1f) % 1f * 360f;

            // Check for pause trigger on first forward sweep
            if (_isFirstSweepAtElevation && !_hasTriggeredPauseThisElevation && rotDir > 0f)
            {
                if (CrossedAzimuth(_prevAzimuthDeg, _azimuthDeg, pauseAtAzimuthDeg))
                {
                    _isPaused = true;
                    _pauseStartTime = Time.time;
                    _hasTriggeredPauseThisElevation = true;
                    // Snap to exact pause position
                    x = pauseAtAzimuthDeg / 360f;
                    _azimuthDeg = pauseAtAzimuthDeg;

                    if (showDebugLog)
                        Debug.Log($"[ElevationPause] PAUSE triggered at {pauseAtAzimuthDeg}°, elevation={elevation:F3}, vel[{vel}]={vRotDeg_per_sec[vel]}");
                }
            }

            _prevAzimuthDeg = _azimuthDeg;

            float y = elevation;
            Vector2 offset = new Vector2(x, y);

            // Stop if done
            if (currentStep * vel <= (repeats * 2 * numElevationSteps * sweepRepeatVec[sweepRepeatVec.Length - 1] * vRotDeg_per_sec.Length))
            {
                cylinderMaterial.SetTextureOffset("_MainTex", offset);

                if (showDebugLog && _debugFrameCount <= 10)
                    Debug.Log($"[ElevationPause] Frame {_debugFrameCount}: x={x:F4}, y={y:F4}, azimuth={_azimuthDeg:F1}°, " +
                              $"vel[{vel}]={vRotDeg_per_sec[vel]}, step={currentStep}, firstSweep={_isFirstSweepAtElevation}, paused={_isPaused}");

                _currentLogEntry.xpos = x;
                _currentLogEntry.ypos = y;
                _currentLogEntry.isPaused = _isPaused ? 1 : 0;
                _currentLogEntry.isFirstSweep = _isFirstSweepAtElevation ? 1 : 0;
                Janelia.Logger.Log(_currentLogEntry);
            }
        }
        else if (showDebugLog && _debugFrameCount <= 5)
        {
            Debug.Log($"[ElevationPause] Frame {_debugFrameCount}: WAITING — Time.time={Time.time:F3}, waitTime+delay={waitTime + delaySeconds:F3}");
        }
    }

    /// <summary>
    /// Detects if azimuth crossed the target degree between two frames (forward rotation).
    /// Handles wrap-around at 360°/0°.
    /// </summary>
    private bool CrossedAzimuth(float prevDeg, float currDeg, float targetDeg)
    {
        if (prevDeg <= currDeg)
        {
            // No wrap: simple range check
            return prevDeg < targetDeg && currDeg >= targetDeg;
        }
        else
        {
            // Wrapped around 360→0: target crossed if in [prev,360) or [0,curr]
            return prevDeg < targetDeg || currDeg >= targetDeg;
        }
    }
}
