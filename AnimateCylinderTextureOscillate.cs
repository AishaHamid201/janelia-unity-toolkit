using System;
using UnityEngine;

public class AnimateCylinderTextureOscillate : MonoBehaviour
{
    [Header("Oscillation Settings")]
    [Tooltip("Speed of oscillation in degrees per second.")]
    public float vRotDeg_per_sec = 120f;

    [Tooltip("Start azimuth of oscillation range (degrees, 0-360).")]
    [Range(0f, 360f)]
    public float oscillateFromDeg = 120f;

    [Tooltip("End azimuth of oscillation range (degrees, 0-360).")]
    [Range(0f, 360f)]
    public float oscillateToDeg = 240f;

    [Tooltip("Number of full back-and-forth cycles before stopping. 0 = oscillate forever.")]
    public int numSweeps = 0;

    [Header("Position")]
    [Tooltip("Fixed elevation (y position). No elevation stepping.")]
    public float elevation = 0f;

    [Tooltip("Delay in seconds before oscillation starts.")]
    public float delaySeconds = 0f;

    [Header("Debug")]
    public bool showDebugLog = false;

    // Current azimuth in degrees (0-360), readable by other scripts (e.g., LED trigger)
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;

    private Material cylinderMaterial;
    private float startTime;
    private bool finished = false;
    private int completedSweeps = 0;
    private bool movingForward = true;
    private bool wasMovingForward = true;
    private int _debugFrameCount = 0;

    // Logging
    [Serializable]
    private class textureLogEntry : Janelia.Logger.Entry
    {
        public float xpos;
        public float ypos;
    };

    private textureLogEntry _currentLogEntry = new textureLogEntry();

    void Start()
    {
        cylinderMaterial = Resources.Load(Janelia.CylinderBackgroundResources.MaterialName, typeof(Material)) as Material;

        if (cylinderMaterial == null)
        {
            Debug.LogError("AnimateCylinderTextureOscillate: Could not load material '" + Janelia.CylinderBackgroundResources.MaterialName + "'");
        }

        // Set initial position to start of oscillation range
        float x = oscillateFromDeg / 360.0f;
        float y = elevation;
        _azimuthDeg = oscillateFromDeg;

        Vector2 offset = new Vector2(x, y);
        cylinderMaterial.SetTextureOffset("_MainTex", offset);

        startTime = Time.time;

        if (showDebugLog)
        {
            Debug.Log($"[AnimateCylinderTextureOscillate] Start: material={(cylinderMaterial != null ? "LOADED" : "NULL")}, " +
                      $"speed={vRotDeg_per_sec}, from={oscillateFromDeg}°, to={oscillateToDeg}°, " +
                      $"numSweeps={numSweeps}, elevation={elevation}, delay={delaySeconds}");
        }

        // Log initial values
        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        Janelia.Logger.Log(_currentLogEntry);
    }

    void Update()
    {
        _debugFrameCount++;

        if (finished || cylinderMaterial == null)
            return;

        // Wait for delay
        float elapsed = Time.time - startTime;
        if (elapsed < delaySeconds)
        {
            if (showDebugLog && _debugFrameCount <= 5)
                Debug.Log($"[AnimateCylinderTextureOscillate] Frame {_debugFrameCount}: WAITING — elapsed={elapsed:F3}, delay={delaySeconds}");
            return;
        }

        float dTime = elapsed - delaySeconds;
        float range = Mathf.Abs(oscillateToDeg - oscillateFromDeg);

        if (range < 0.001f)
            return; // No range to oscillate

        // PingPong gives a value that goes 0 → range → 0 → range → ...
        float pingPong = Mathf.PingPong(dTime * vRotDeg_per_sec, range);
        _azimuthDeg = oscillateFromDeg + pingPong;
        float x = _azimuthDeg / 360.0f;
        float y = elevation;

        // Track sweep completion by detecting direction changes
        movingForward = (pingPong < range * 0.5f && dTime * vRotDeg_per_sec > range * 0.5f) ?
            // Use derivative to determine direction
            ((dTime * vRotDeg_per_sec) % (2f * range) < range) :
            ((dTime * vRotDeg_per_sec) % (2f * range) < range);

        // Count sweeps: one full cycle = forward + backward = 2 * range of travel
        float totalDegreesTraveled = dTime * vRotDeg_per_sec;
        int currentSweepCount = Mathf.FloorToInt(totalDegreesTraveled / (2f * range));

        if (numSweeps > 0 && currentSweepCount >= numSweeps)
        {
            // Snap to start position when done
            _azimuthDeg = oscillateFromDeg;
            x = _azimuthDeg / 360.0f;
            finished = true;

            if (showDebugLog)
                Debug.Log($"[AnimateCylinderTextureOscillate] FINISHED — completed {numSweeps} sweeps");
        }

        Vector2 offset = new Vector2(x, y);
        cylinderMaterial.SetTextureOffset("_MainTex", offset);

        if (showDebugLog && _debugFrameCount <= 10)
        {
            Debug.Log($"[AnimateCylinderTextureOscillate] Frame {_debugFrameCount}: azimuth={_azimuthDeg:F1}°, x={x:F4}, sweep={currentSweepCount}/{numSweeps}, dTime={dTime:F3}");
        }

        // Log values
        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        Janelia.Logger.Log(_currentLogEntry);
    }
}
