using System;
using UnityEngine;
using static Janelia.NiDaqMx;

public class TalkToNiDaq_LED_LoomDisc : MonoBehaviour
{
    [Header("Loom Disc Reference (auto-found if empty)")]
    public AnimateLoomDisc loomDisc;

    [Header("Debug")]
    public bool showEachWrite = false;
    public bool showEachRead = false;

    private NiDaqLedLogEntry _currentLogEntry = new NiDaqLedLogEntry();
    private Janelia.NiDaqMx.InputParams _inputParams;
    private Janelia.NiDaqMx.OutputParams _outputParams;
    private double[] _readData;
    private double[] _writeData;
    private bool odd = true;

    private void Start()
    {
        if (loomDisc == null)
        {
            loomDisc = FindObjectOfType<AnimateLoomDisc>();
            if (loomDisc == null)
            {
                Debug.LogError("TalkToNiDaq_LED_LoomDisc: No AnimateLoomDisc found in scene.");
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
        if (loomDisc == null)
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

        // LED is ON during block phases, OFF during looming
        bool ledOn = loomDisc.IsInBlock;

        // ao0: toggling photodiode signal
        _writeData[0] = odd ? _outputParams.VoltageMax : _outputParams.VoltageMin;
        odd = !odd;

        // ao1: rotation Y modulation
        double rotationY = transform.rotation.eulerAngles.y;
        double modulator = (_outputParams.VoltageMax - _outputParams.VoltageMin) / 360.0;
        _writeData[1] = rotationY * modulator + _outputParams.VoltageMin;

        // ao2: LED — ON during block, OFF during looming
        _writeData[2] = ledOn ? _outputParams.VoltageMax : _outputParams.VoltageMin;

        // Log
        _currentLogEntry.cylinderAzimuth = loomDisc.AzimuthDeg;
        _currentLogEntry.scaleFactor = loomDisc.CurrentScaleFactor;
        _currentLogEntry.ledState = ledOn ? 1.0 : 0.0;
        _currentLogEntry.isInBlock = loomDisc.IsInBlock ? 1.0 : 0.0;
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
            Debug.Log($"Azimuth: {loomDisc.AzimuthDeg:F1}° | Scale: {loomDisc.CurrentScaleFactor:F2}x | Block: {loomDisc.IsInBlock} | LED: {(ledOn ? "ON" : "OFF")}");
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
        public float scaleFactor;
        public double ledState;
        public double isInBlock;
    }
}
