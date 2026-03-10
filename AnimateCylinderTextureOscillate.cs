using System;
using UnityEngine;

public class AnimateCylinderTextureOscillate : MonoBehaviour
{
    [Header("Oscillation Settings")]
    [Tooltip("Oscillation speeds in degrees per second. Each entry runs in sequence.")]
    public float[] vRotDeg_per_sec;

    [Tooltip("Number of back-and-forth sweeps for each speed. Must match vRotDeg_per_sec length.")]
    public int[] sweepRepeatVec;

    [Tooltip("Start azimuth of oscillation range (degrees, 0-360).")]
    [Range(0f, 360f)]
    public float oscillateFromDeg = 120f;

    [Tooltip("End azimuth of oscillation range (degrees, 0-360).")]
    [Range(0f, 360f)]
    public float oscillateToDeg = 240f;

    [Header("Position")]
    [Tooltip("Fixed elevation (y position). No elevation stepping.")]
    public float elevation = 0f;

    [Tooltip("Delay in seconds before each speed block starts.")]
    public float delaySeconds = 0f;

    [Header("Debug")]
    public bool showDebugLog = false;

    // Current azimuth in degrees (0-360), readable by other scripts (e.g., LED trigger)
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;

    private Material cylinderMaterial;
    private int vel = 0;                // Current index into vRotDeg_per_sec / sweepRepeatVec
    private float velStartTime;         // Time when current speed block started
    private bool finished = false;
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

        velStartTime = Time.time;

        if (showDebugLog)
        {
            Debug.Log($"[AnimateCylinderTextureOscillate] Start: material={(cylinderMaterial != null ? "LOADED" : "NULL")}, " +
                      $"vRotDeg_per_sec.Length={vRotDeg_per_sec?.Length}, sweepRepeatVec.Length={sweepRepeatVec?.Length}, " +
                      $"from={oscillateFromDeg}°, to={oscillateToDeg}°, elevation={elevation}, delay={delaySeconds}");
        }

        // Log initial values
        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        Janelia.Logger.Log(_currentLogEntry);
    }

    void Update()
    {
        _debugFrameCount++;

        // Done with all speeds
        if (vel >= vRotDeg_per_sec.Length || vel >= sweepRepeatVec.Length)
        {
            if (!finished && showDebugLog)
            {
                Debug.Log("[AnimateCylinderTextureOscillate] FINISHED — all speeds completed");
                finished = true;
            }
            return;
        }

        if (cylinderMaterial == null)
            return;

        // Wait for delay before this speed block starts
        float elapsed = Time.time - velStartTime;
        if (elapsed < delaySeconds)
        {
            if (showDebugLog && _debugFrameCount <= 5)
                Debug.Log($"[AnimateCylinderTextureOscillate] Frame {_debugFrameCount}: WAITING — vel[{vel}]={vRotDeg_per_sec[vel]}, elapsed={elapsed:F3}, delay={delaySeconds}");
            return;
        }

        float dTime = elapsed - delaySeconds;
        float range = Mathf.Abs(oscillateToDeg - oscillateFromDeg);

        if (range < 0.001f)
            return; // No range to oscillate

        float speed = vRotDeg_per_sec[vel];
        int sweepsForThisSpeed = sweepRepeatVec[vel];

        // Count completed sweeps: one full cycle = forward + backward = 2 * range of travel
        float totalDegreesTraveled = dTime * speed;
        int currentSweepCount = Mathf.FloorToInt(totalDegreesTraveled / (2f * range));

        // Check if this speed block is done
        if (currentSweepCount >= sweepsForThisSpeed)
        {
            if (showDebugLog)
                Debug.Log($"[AnimateCylinderTextureOscillate] Speed {vel} done — {sweepsForThisSpeed} sweeps at {speed} deg/s");

            vel += 1;
            velStartTime = Time.time;

            // Snap back to start position between speed blocks
            _azimuthDeg = oscillateFromDeg;
            float resetX = _azimuthDeg / 360.0f;
            cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(resetX, elevation));
            return;
        }

        // PingPong gives a value that goes 0 → range → 0 → range → ...
        float pingPong = Mathf.PingPong(totalDegreesTraveled, range);
        _azimuthDeg = oscillateFromDeg + pingPong;
        float x = _azimuthDeg / 360.0f;
        float y = elevation;

        Vector2 offset = new Vector2(x, y);
        cylinderMaterial.SetTextureOffset("_MainTex", offset);

        if (showDebugLog && _debugFrameCount <= 10)
        {
            Debug.Log($"[AnimateCylinderTextureOscillate] Frame {_debugFrameCount}: azimuth={_azimuthDeg:F1}°, vel[{vel}]={speed}, sweep={currentSweepCount}/{sweepsForThisSpeed}, dTime={dTime:F3}");
        }

        // Log values
        _currentLogEntry.xpos = x;
        _currentLogEntry.ypos = y;
        Janelia.Logger.Log(_currentLogEntry);
    }
}
