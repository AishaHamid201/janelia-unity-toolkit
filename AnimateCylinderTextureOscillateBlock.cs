using System;
using UnityEngine;

public class AnimateCylinderTextureOscillateBlock : MonoBehaviour
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

    [Header("Inter-Block Interval")]
    [Tooltip("Duration in seconds to hold the spot at the block position before each speed starts.")]
    public float blockDurationSeconds = 5f;

    [Tooltip("Azimuth to park the spot during the block (e.g., 180 = behind the fly).")]
    [Range(0f, 360f)]
    public float blockAzimuthDeg = 180f;

    [Header("Position")]
    [Tooltip("Fixed elevation (y position). No elevation stepping.")]
    public float elevation = 0f;

    [Header("Debug")]
    public bool showDebugLog = false;

    // Current azimuth in degrees (0-360), readable by other scripts (e.g., LED trigger)
    private float _azimuthDeg;
    public float AzimuthDeg => _azimuthDeg;

    private Material cylinderMaterial;
    private int vel = 0;                // Current index into vRotDeg_per_sec / sweepRepeatVec
    private float phaseStartTime;       // Time when current phase started
    private bool inBlock = true;        // true = holding at block position, false = oscillating
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
            Debug.LogError("AnimateCylinderTextureOscillateBlock: Could not load material '" + Janelia.CylinderBackgroundResources.MaterialName + "'");
        }

        // Start in block phase — park spot at block position
        inBlock = true;
        phaseStartTime = Time.time;
        _azimuthDeg = blockAzimuthDeg;
        float x = _azimuthDeg / 360.0f;
        float y = elevation;

        cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(x, y));

        if (showDebugLog)
        {
            Debug.Log($"[OscillateBlock] Start: material={(cylinderMaterial != null ? "LOADED" : "NULL")}, " +
                      $"speeds={vRotDeg_per_sec?.Length}, sweeps={sweepRepeatVec?.Length}, " +
                      $"from={oscillateFromDeg}°, to={oscillateToDeg}°, " +
                      $"blockDuration={blockDurationSeconds}s, blockAzimuth={blockAzimuthDeg}°");
        }

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
                Debug.Log("[OscillateBlock] FINISHED — all speeds completed");
                finished = true;
            }
            return;
        }

        if (cylinderMaterial == null)
            return;

        float elapsed = Time.time - phaseStartTime;

        if (inBlock)
        {
            // ===== BLOCK PHASE: hold spot at block position =====
            _azimuthDeg = blockAzimuthDeg;
            float bx = _azimuthDeg / 360.0f;
            cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(bx, elevation));

            if (showDebugLog && _debugFrameCount <= 10)
                Debug.Log($"[OscillateBlock] Frame {_debugFrameCount}: BLOCK — azimuth={_azimuthDeg:F1}°, remaining={blockDurationSeconds - elapsed:F1}s, next speed[{vel}]={vRotDeg_per_sec[vel]}");

            _currentLogEntry.xpos = bx;
            _currentLogEntry.ypos = elevation;
            Janelia.Logger.Log(_currentLogEntry);

            if (elapsed >= blockDurationSeconds)
            {
                // Transition to oscillation phase
                inBlock = false;
                phaseStartTime = Time.time;

                // Snap to oscillation start position
                _azimuthDeg = oscillateFromDeg;
                float startX = _azimuthDeg / 360.0f;
                cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(startX, elevation));

                if (showDebugLog)
                    Debug.Log($"[OscillateBlock] Block done — starting speed[{vel}]={vRotDeg_per_sec[vel]} deg/s, {sweepRepeatVec[vel]} sweeps");
            }
        }
        else
        {
            // ===== OSCILLATION PHASE: back and forth =====
            float dTime = elapsed;
            float range = Mathf.Abs(oscillateToDeg - oscillateFromDeg);

            if (range < 0.001f)
                return;

            float speed = vRotDeg_per_sec[vel];
            int sweepsForThisSpeed = sweepRepeatVec[vel];

            // Count completed sweeps: one full cycle = forward + backward = 2 * range
            float totalDegreesTraveled = dTime * speed;
            int currentSweepCount = Mathf.FloorToInt(totalDegreesTraveled / (2f * range));

            // Check if this speed block is done
            if (currentSweepCount >= sweepsForThisSpeed)
            {
                if (showDebugLog)
                    Debug.Log($"[OscillateBlock] Speed[{vel}] done — {sweepsForThisSpeed} sweeps at {speed} deg/s");

                vel += 1;

                // Go back to block phase for next speed
                inBlock = true;
                phaseStartTime = Time.time;

                // Snap to block position
                _azimuthDeg = blockAzimuthDeg;
                float resetX = _azimuthDeg / 360.0f;
                cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(resetX, elevation));
                return;
            }

            // PingPong: 0 → range → 0 → range → ...
            float pingPong = Mathf.PingPong(totalDegreesTraveled, range);
            _azimuthDeg = oscillateFromDeg + pingPong;
            float x = _azimuthDeg / 360.0f;

            cylinderMaterial.SetTextureOffset("_MainTex", new Vector2(x, elevation));

            if (showDebugLog && _debugFrameCount <= 10)
                Debug.Log($"[OscillateBlock] Frame {_debugFrameCount}: azimuth={_azimuthDeg:F1}°, vel[{vel}]={speed}, sweep={currentSweepCount}/{sweepsForThisSpeed}, dTime={dTime:F3}");

            _currentLogEntry.xpos = x;
            _currentLogEntry.ypos = elevation;
            Janelia.Logger.Log(_currentLogEntry);
        }
    }
}
