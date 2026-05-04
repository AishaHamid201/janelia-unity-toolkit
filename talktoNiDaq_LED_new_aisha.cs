using System;
using UnityEngine;
using static Janelia.NiDaqMx;

public class SimpleNiDaqTrigger : MonoBehaviour
{
    public bool showEachWrite = false;
    public bool showEachRead = false;

    [Min(0f)] public float primaryDurationSec = 10f;
    [Min(0f)] public float secondaryDurationSec = 20f;

    private NiDaqInputs _currentLogEntry = new NiDaqInputs();
    private Janelia.NiDaqMx.InputParams _inputParams;
    private Janelia.NiDaqMx.OutputParams _outputParams;
    private double[] _readData;
    private double[] _writeData;
    private bool odd = true;

    private float _primaryElapsed = 0f;
    private float _secondaryElapsed = 0f;

    private void Start()
    {
        _inputParams = new Janelia.NiDaqMx.InputParams
        {
            ChannelNames = new string[] { "ai0", "ai1", "ai2", "ai3", "ai4", "ai5" }
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
        if (_primaryElapsed < primaryDurationSec)
        {
            _primaryElapsed += Time.deltaTime;
            // Input read
            int numReadPerChannel = 0;
            if (Janelia.NiDaqMx.ReadFromInputs(_inputParams, ref _readData, ref numReadPerChannel))
            {
                if (numReadPerChannel > 0)
                {
                    for (int i = 0; i < numReadPerChannel; i++)
                    {
                        _currentLogEntry.tracePD = _readData[IndexInReadBuffer(0, numReadPerChannel, i)];
                        _currentLogEntry.imgFrameTrigger = _readData[IndexInReadBuffer(1, numReadPerChannel, i)];
                        _currentLogEntry.ledTrigger = _readData[IndexInReadBuffer(2, numReadPerChannel, i)];
                        _currentLogEntry.cameraTrigger = _readData[IndexInReadBuffer(3, numReadPerChannel, i)];
                        _currentLogEntry.fictracCamera = _readData[IndexInReadBuffer(4, numReadPerChannel, i)];
                        _currentLogEntry.sideCamera = _readData[IndexInReadBuffer(5, numReadPerChannel, i)];
                        Janelia.Logger.Log(_currentLogEntry);

                        if (showEachRead)
                        {
                            Debug.Log($"tracePD: {_currentLogEntry.tracePD}, imgFrameTrigger: {_currentLogEntry.imgFrameTrigger}, ledTrigger: {_currentLogEntry.ledTrigger}, cameraTrigger: {_currentLogEntry.cameraTrigger}, fictracCamera: {_currentLogEntry.fictracCamera}, sideCamera: {_currentLogEntry.sideCamera}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("Read from input failed");
                Debug.LogWarning(Janelia.NiDaqMx.GetLatestError());
            }

            // Output ao0: toggling photodiode signal
            _writeData[0] = odd ? _outputParams.VoltageMax : _outputParams.VoltageMin;
            odd = !odd;

            // Output ao1: rotation Y modulation
            double rotationY = transform.rotation.eulerAngles.y;
            double modulator = (_outputParams.VoltageMax - _outputParams.VoltageMin) / 360.0;
            _writeData[1] = rotationY * modulator + _outputParams.VoltageMin;

            _writeData[2] = _outputParams.VoltageMax;

            // Send output to DAQ
            int expectedNumWritten = _writeData.Length;
            if (!Janelia.NiDaqMx.WriteToOutputs(_outputParams, _writeData, ref expectedNumWritten))
            {
                Debug.LogError("Write to outputs failed");
                Debug.LogError(Janelia.NiDaqMx.GetLatestError());
            }
            else if (showEachWrite)
            {
                Debug.Log($"Write: [{_writeData[0]:F2}, {_writeData[1]:F2}, {_writeData[2]:F2}]");
            }
        }
        else
        {
            PerformSecondaryFunctions();
        }
    }

    public void PerformSecondaryFunctions()
    {
        if (_secondaryElapsed < secondaryDurationSec)
        {
            _secondaryElapsed += Time.deltaTime;
            // Input read
            int numReadPerChannel = 0;
            if (Janelia.NiDaqMx.ReadFromInputs(_inputParams, ref _readData, ref numReadPerChannel))
            {
                if (numReadPerChannel > 0)
                {
                    for (int i = 0; i < numReadPerChannel; i++)
                    {
                        _currentLogEntry.tracePD = _readData[IndexInReadBuffer(0, numReadPerChannel, i)];
                        _currentLogEntry.imgFrameTrigger = _readData[IndexInReadBuffer(1, numReadPerChannel, i)];
                        _currentLogEntry.ledTrigger = _readData[IndexInReadBuffer(2, numReadPerChannel, i)];
                        _currentLogEntry.cameraTrigger = _readData[IndexInReadBuffer(3, numReadPerChannel, i)];
                        _currentLogEntry.fictracCamera = _readData[IndexInReadBuffer(4, numReadPerChannel, i)];
                        _currentLogEntry.sideCamera = _readData[IndexInReadBuffer(5, numReadPerChannel, i)];
                        Janelia.Logger.Log(_currentLogEntry);

                        if (showEachRead)
                        {
                            Debug.Log($"tracePD: {_currentLogEntry.tracePD}, imgFrameTrigger: {_currentLogEntry.imgFrameTrigger}, ledTrigger: {_currentLogEntry.ledTrigger}, cameraTrigger: {_currentLogEntry.cameraTrigger}, fictracCamera: {_currentLogEntry.fictracCamera}, sideCamera: {_currentLogEntry.sideCamera}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("Read from input failed");
                Debug.LogWarning(Janelia.NiDaqMx.GetLatestError());
            }

            // Output ao0: toggling photodiode signal
            _writeData[0] = odd ? _outputParams.VoltageMax : _outputParams.VoltageMin;
            odd = !odd;

            // Output ao1: rotation Y modulation
            double rotationY = transform.rotation.eulerAngles.y;
            double modulator = (_outputParams.VoltageMax - _outputParams.VoltageMin) / 360.0;
            _writeData[1] = rotationY * modulator + _outputParams.VoltageMin;

            _writeData[2] = _outputParams.VoltageMin;

            // Send output to DAQ
            int expectedNumWritten = _writeData.Length;
            if (!Janelia.NiDaqMx.WriteToOutputs(_outputParams, _writeData, ref expectedNumWritten))
            {
                Debug.LogError("Write to outputs failed");
                Debug.LogError(Janelia.NiDaqMx.GetLatestError());
            }
            else if (showEachWrite)
            {
                Debug.Log($"Write: [{_writeData[0]:F2}, {_writeData[1]:F2}, {_writeData[2]:F2}]");
            }
        }
        else
        {
            _primaryElapsed = 0;
            _secondaryElapsed = 0;
        }
    }

    private void OnDestroy()
    {
        double resetValue = _outputParams.VoltageMin;
        int expectedNumWritten = _writeData.Length;

        if (!Janelia.NiDaqMx.WriteToOutputs(_outputParams,
                new double[] { resetValue, resetValue, resetValue },
                ref expectedNumWritten))
        {
            Debug.LogError("Reset write failed on destroy");
            Debug.LogError(Janelia.NiDaqMx.GetLatestError());
        }

        Janelia.NiDaqMx.OnDestroy();
    }

    [Serializable]
    private class NiDaqInputs : Janelia.Logger.Entry
    {
        public double tracePD;
        public double imgFrameTrigger;
        public double ledTrigger;
        public double cameraTrigger;
        public double fictracCamera;
        public double sideCamera;
    }
}
