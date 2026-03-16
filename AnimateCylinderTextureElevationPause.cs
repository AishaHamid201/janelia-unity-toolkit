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

    [Header("Hold Settings (first sweep per elevation)")]
    [Tooltip("Azimuth where the cylinder holds on the first sweep (degrees, 0-360).")]
    [Range(0f, 360f)]
    public float holdAtAzimuthDeg = 20f;

    [Tooltip("LED ON hold duration — cylinder stops, LED is ON for this many seconds.")]
    public float ledOnHoldSeconds = 5f;

    [Tooltip("LED OFF hold duration — after LED turns OFF, cylinder stays stopped for this many more seconds before resuming.")]
    public float ledOffHoldSeconds = 10f;

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

    // Hold state
    private float _totalHoldTime = 0f;
    private bool _isHolding = false;
    private bool _isLedOn = false;
    private float _holdStartTime = 0f;
    private bool _hasTriggeredHoldThisElevation = false;
    private bool _isFirstSweepAtElevation = true;
    private float _sweepStartAzimuth = 0f;

    // Public API
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;
    public bool IsHolding => _isHolding;
    public bool IsLedOn => _isLedOn;
    public bool IsFirstSweepAtElevation => _isFirstSweepAtElevation;

    // Logging
    [Serializable]
    private class textureLogEntry : Janelia.Logger.Entry
    {
        public float xpos;
        public float ypos;
        public int isHolding;
        public int isLedOn;
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
                      $"offsetEl={offsetEl}, maxEl={maxEl}, holdAt={holdAtAzimuthDeg}°, ledOn={ledOnHoldSeconds}s, ledOff={ledOffHoldSeconds}s");
        }

        // Reset texture based on offset
        elevation = offsetEl;
        float x = offsetTex / 360.0f;
        float y = elevation;

        Vector2 offset = new Vector2(x, y);
        cylinderMaterial.SetTextureOffset("_MainTex", offset);

        _azimuthDeg = ((x % 1f) + 1f) % 1f * 360f;
        _sweepStartAzimuth = _azimuthDeg;
        _isFirstSweepAtElevation = true;
        _hasTriggeredHoldThisElevation = false;
        _totalHoldTime = 0f;

        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        _currentLogEntry.isHolding = 0;
        _currentLogEntry.isLedOn = 0;
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
            // Reset hold state for new velocity
            _totalHoldTime = 0f;
            _hasTriggeredHoldThisElevation = false;
            _isFirstSweepAtElevation = true;
            _isHolding = false;
            _isLedOn = false;
            _sweepStartAzimuth = _azimuthDeg;
            if (vel <= vRotDeg_per_sec.Length)
            {
                waitTime = Time.time;
            }
        }

        if (cylinderMaterial & Time.time >= (waitTime + delaySeconds))
        {
            // Handle active hold (two phases: LED ON then LED OFF)
            if (_isHolding)
            {
                float holdElapsed = Time.time - _holdStartTime;
                float totalHoldDuration = ledOnHoldSeconds + ledOffHoldSeconds;

                if (holdElapsed < ledOnHoldSeconds)
                {
                    // Phase 1: LED ON
                    _isLedOn = true;
                }
                else if (holdElapsed < totalHoldDuration)
                {
                    // Phase 2: LED OFF, still holding
                    _isLedOn = false;
                }
                else
                {
                    // Hold ended — resume rotation
                    _totalHoldTime += totalHoldDuration;
                    _isHolding = false;
                    _isLedOn = false;
                    if (showDebugLog)
                        Debug.Log($"[ElevationPause] Hold ended — totalHoldTime={_totalHoldTime:F2}s, resuming rotation");
                }

                if (_isHolding)
                {
                    // Still holding — keep texture frozen, log state
                    _currentLogEntry.xpos = holdAtAzimuthDeg / 360f;
                    _currentLogEntry.ypos = elevation;
                    _currentLogEntry.isHolding = 1;
                    _currentLogEntry.isLedOn = _isLedOn ? 1 : 0;
                    _currentLogEntry.isFirstSweep = _isFirstSweepAtElevation ? 1 : 0;
                    Janelia.Logger.Log(_currentLogEntry);
                    return;
                }
            }

            // Compute position with hold-adjusted time
            float dTime = Time.time - (waitTime + delaySeconds) - _totalHoldTime;

            float x = offsetTex / 360.0f + dTime * rotDir * (vRotDeg_per_sec[vel] / 360.0f) % 1;

            // Check if a step (full 360° rotation) has been completed
            if (dTime * (vRotDeg_per_sec[vel] / 360.0f) > currentStep)
            {
                if (currentStep % (2 * sweepRepeatVec[vel]) == 0)
                {
                    // All repeats at this elevation done — change elevation
                    elevation += (maxEl - offsetEl) / numElevationSteps;
                    rotDir = 1.0f;
                    // Reset hold state for new elevation
                    _hasTriggeredHoldThisElevation = false;
                    _isFirstSweepAtElevation = true;
                    _sweepStartAzimuth = _azimuthDeg;

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

            // Check for hold trigger on first forward sweep using distance-based check.
            // Measures how far forward the cylinder has rotated from the sweep start
            // and triggers when it reaches the hold azimuth.
            if (_isFirstSweepAtElevation && !_hasTriggeredHoldThisElevation && rotDir > 0f)
            {
                float holdDist = ((holdAtAzimuthDeg - _sweepStartAzimuth) % 360f + 360f) % 360f;
                float currentDist = ((_azimuthDeg - _sweepStartAzimuth) % 360f + 360f) % 360f;

                if (holdDist > 0f && currentDist >= holdDist)
                {
                    _isHolding = true;
                    _isLedOn = true;
                    _holdStartTime = Time.time;
                    _hasTriggeredHoldThisElevation = true;
                    // Snap to exact hold position
                    x = holdAtAzimuthDeg / 360f;
                    _azimuthDeg = holdAtAzimuthDeg;

                    if (showDebugLog)
                        Debug.Log($"[ElevationPause] HOLD triggered at {holdAtAzimuthDeg}°, elevation={elevation:F3}, vel[{vel}]={vRotDeg_per_sec[vel]}, LED ON for {ledOnHoldSeconds}s then OFF for {ledOffHoldSeconds}s");
                }
            }

            float y = elevation;
            Vector2 offset = new Vector2(x, y);

            // Stop if done
            if (currentStep * vel <= (repeats * 2 * numElevationSteps * sweepRepeatVec[sweepRepeatVec.Length - 1] * vRotDeg_per_sec.Length))
            {
                cylinderMaterial.SetTextureOffset("_MainTex", offset);

                if (showDebugLog && _debugFrameCount <= 10)
                    Debug.Log($"[ElevationPause] Frame {_debugFrameCount}: x={x:F4}, y={y:F4}, azimuth={_azimuthDeg:F1}°, " +
                              $"vel[{vel}]={vRotDeg_per_sec[vel]}, step={currentStep}, firstSweep={_isFirstSweepAtElevation}, holding={_isHolding}, ledOn={_isLedOn}");

                _currentLogEntry.xpos = x;
                _currentLogEntry.ypos = y;
                _currentLogEntry.isHolding = _isHolding ? 1 : 0;
                _currentLogEntry.isLedOn = _isLedOn ? 1 : 0;
                _currentLogEntry.isFirstSweep = _isFirstSweepAtElevation ? 1 : 0;
                Janelia.Logger.Log(_currentLogEntry);
            }
        }
        else if (showDebugLog && _debugFrameCount <= 5)
        {
            Debug.Log($"[ElevationPause] Frame {_debugFrameCount}: WAITING — Time.time={Time.time:F3}, waitTime+delay={waitTime + delaySeconds:F3}");
        }
    }

}
