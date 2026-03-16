using System;
using UnityEngine;
using static Janelia.NiDaqMx;

public class TalkToNiDaq_LED_ElevationPause : MonoBehaviour
{
    [Header("Cylinder Reference (auto-found if empty)")]
    public AnimateCylinderTextureElevationPause cylinderTexture;

    [Header("LED Angle Ranges (0 to 360 degrees)")]
    [Tooltip("LED is ON when the cylinder azimuth falls within any of these ranges, but ONLY during the first sweep at each elevation and NOT during the pause.")]
    public AngleRange[] ledOnAngleRanges = new AngleRange[]
    {
        new AngleRange { fromDeg = 330f, toDeg = 30f }
    };

    [Header("Debug")]
    public bool showEachWrite = false;
    public bool showEachRead = false;

    private NiDaqLedLogEntry _currentLogEntry = new NiDaqLedLogEntry();
    private Janelia.NiDaqMx.InputParams _inputParams;
    private Janelia.NiDaqMx.OutputParams _outputParams;
    private double[] _readData;
    private double[] _writeData;
    private bool odd = true;

    [Serializable]
    public struct AngleRange
    {
        [Range(0f, 360f)] public float fromDeg;
        [Range(0f, 360f)] public float toDeg;
    }

    private void Start()
    {
        if (cylinderTexture == null)
        {
            cylinderTexture = FindObjectOfType<AnimateCylinderTextureElevationPause>();
            if (cylinderTexture == null)
            {
                Debug.LogError("TalkToNiDaq_LED_ElevationPause: No AnimateCylinderTextureElevationPause found in scene.");
                return;
            }
        }

        _inputParams = new Janelia.NiDaqMx.InputParams
        {
            ChannelNames = new string[] { "ai0", "ai1", "ai2" }
        };
        _readData = new double[_inputParams.SampleBufferSize];

        if (!Janelia.NiDaqMx.CreateInputs(_inputParams))
        {
            Debug.LogError("Creating input failed");
            Debug.LogError(Janelia.NiDaqMx.GetLatestError());
            return;
        }

        _outputParams = new Janelia.NiDaqMx.OutputParams
        {
            ChannelNames = new string[] { "ao0", "ao1", "ao2" },
            VoltageMin = -5,
            VoltageMax = 5
        };

        if (!Janelia.NiDaqMx.CreateOutputs(_outputParams))
        {
            Debug.LogError("Creating output failed");
            Debug.LogError(Janelia.NiDaqMx.GetLatestError());
            return;
        }

        _writeData = new double[3] {
            _outputParams.VoltageMin,
            _outputParams.VoltageMin,
            _outputParams.VoltageMin
        };
    }

    private void Update()
    {
        if (cylinderTexture == null)
            return;

        // Read NiDaq inputs
        int numReadPerChannel = 0;
        if (Janelia.NiDaqMx.ReadFromInputs(_inputParams, ref _readData, ref numReadPerChannel))
        {
            if (numReadPerChannel > 0)
            {
                for (int i = 0; i < numReadPerChannel; i++)
                {
                    int j = IndexInReadBuffer(0, numReadPerChannel, i);
                    int k = IndexInReadBuffer(1, numReadPerChannel, i);
                    int l = IndexInReadBuffer(2, numReadPerChannel, i);
                    _currentLogEntry.tracePD = _readData[j];
                    _currentLogEntry.imgFrameTrigger = _readData[k];
                    _currentLogEntry.ledTrigger = _readData[l];
                }
            }
        }
        else
        {
            Debug.LogWarning("Read from input failed");
            Debug.LogWarning(Janelia.NiDaqMx.GetLatestError());
        }

        if (showEachRead)
        {
            Debug.Log($"tracePD: {_currentLogEntry.tracePD}, imgFrameTrigger: {_currentLogEntry.imgFrameTrigger}, ledTrigger: {_currentLogEntry.ledTrigger}");
        }

        // Get state from the animation script
        float azimuth = cylinderTexture.AzimuthDeg;
        bool isFirstSweep = cylinderTexture.IsFirstSweepAtElevation;
        bool isPaused = cylinderTexture.IsPaused;

        // LED is ON only during the first sweep, NOT during the pause, AND azimuth in range
        bool ledOn = false;
        if (isFirstSweep && !isPaused)
        {
            for (int i = 0; i < ledOnAngleRanges.Length; i++)
            {
                float from = ledOnAngleRanges[i].fromDeg;
                float to = ledOnAngleRanges[i].toDeg;
                if (from <= to)
                {
                    if (azimuth >= from && azimuth <= to)
                    {
                        ledOn = true;
                        break;
                    }
                }
                else
                {
                    // Wrap-around range: e.g., from=330 to=30 means 330->360 and 0->30
                    if (azimuth >= from || azimuth <= to)
                    {
                        ledOn = true;
                        break;
                    }
                }
            }
        }

        // ao0: toggling photodiode signal
        _writeData[0] = odd ? _outputParams.VoltageMax : _outputParams.VoltageMin;
        odd = !odd;

        // ao1: rotation Y modulation
        double rotationY = transform.rotation.eulerAngles.y;
        double modulator = (_outputParams.VoltageMax - _outputParams.VoltageMin) / 360.0;
        _writeData[1] = rotationY * modulator + _outputParams.VoltageMin;

        // ao2: LED — ON only during first sweep in LED range, OFF otherwise
        _writeData[2] = ledOn ? _outputParams.VoltageMax : _outputParams.VoltageMin;

        // Log
        _currentLogEntry.cylinderAzimuth = azimuth;
        _currentLogEntry.ledState = ledOn ? 1.0 : 0.0;
        _currentLogEntry.isFirstSweep = isFirstSweep ? 1.0 : 0.0;
        _currentLogEntry.isPaused = isPaused ? 1.0 : 0.0;
        Janelia.Logger.Log(_currentLogEntry);

        // Write outputs to DAQ
        int expectedNumWritten = _writeData.Length;
        if (!Janelia.NiDaqMx.WriteToOutputs(_outputParams, _writeData, ref expectedNumWritten))
        {
            Debug.LogError("Write to outputs failed");
            Debug.LogError(Janelia.NiDaqMx.GetLatestError());
        }
        else if (showEachWrite)
        {
            Debug.Log($"Azimuth: {azimuth:F1}° | FirstSweep: {isFirstSweep} | Paused: {isPaused} | LED: {(ledOn ? "ON" : "OFF")}");
        }
    }

    private void OnDestroy()
    {
        if (_writeData == null || _outputParams == null)
            return;

        try
        {
            double resetValue = _outputParams.VoltageMin;
            int expectedNumWritten = _writeData.Length;

            Janelia.NiDaqMx.WriteToOutputs(_outputParams,
                    new double[] { resetValue, resetValue, resetValue },
                    ref expectedNumWritten);
        }
        catch (Exception) { }

        try
        {
            Janelia.NiDaqMx.OnDestroy();
        }
        catch (Exception) { }
    }

    [Serializable]
    private class NiDaqLedLogEntry : Janelia.Logger.Entry
    {
        public double tracePD;
        public double imgFrameTrigger;
        public double ledTrigger;
        public float cylinderAzimuth;
        public double ledState;
        public double isFirstSweep;
        public double isPaused;
    }
}
