using System;
using System.Collections.Generic;
using Xamarin.Essentials;

namespace OneWear
{
    using static Globals;
    public enum BoardType
    {
        Unknown,
        V1,
        Plus,
        XR,
        Pint,
    };
    public class Board
    {
        private BoardType _boardType = BoardType.Unknown;
        private float _yaw, _pitch, _roll, _speed, _batteryVoltage, _tripOdometer, _tripAmpHours, _tripRegenAmpHours, _currentAmps;
        private ushort _rpm, _rev, _hardwareRevision, _firmwareRevision, _rideMode;
        private float _motorTemperature, _controllerTemperature, _unknownTemperature, _batteryTemperature;
        private byte _batteryPercent;
        private Dictionary<byte, float> _batteryCells = new Dictionary<byte, float>();
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float Roll => _roll;
        public float BatteryVoltage => _batteryVoltage;
        public byte BatteryPercent => _batteryPercent;
        public float CurrentAmps => _currentAmps;
        public float TripOdometer => _tripOdometer;
        public float TripAmpHours => _tripAmpHours;
        public float TripRegenHours => _tripRegenAmpHours;
        public float Speed => _speed;
        public ushort HardwareRevision => _hardwareRevision;
        public ushort FirmwareRevision => _firmwareRevision;
        public float MotorTemperature => _motorTemperature;
        public float ControllerTemperature => _controllerTemperature;
        public float BatteryTemperature => _batteryTemperature;
        public string RideMode
        {
            get
            {
                if (_boardType == BoardType.V1)
                {
                    return _rideMode switch
                    {
                        1 => "Classic",
                        2 => "Extreme",
                        3 => "Elevated",
                        _ => "Unknown",
                    };
                }
                else if (_boardType == BoardType.Plus || _boardType == BoardType.XR)
                {
                    return _rideMode switch
                    {
                        4 => "Sequoia",
                        5 => "Cruz",
                        6 => "Mission",
                        7 => "Elevated",
                        8 => "Delirium",
                        9 => "Custom",
                        _ => "Unknown",
                    };
                }
                else if (_boardType == BoardType.Pint)
                {
                    return _rideMode switch
                    {
                        5 => "Redwood",
                        6 => "Pacific",
                        7 => "Elevated",
                        8 => "Skyline",
                        _ => "Unknown",
                    };
                }
                return "";
            }
        }

        public Dictionary<byte, float> BatteryCells => _batteryCells;
        public void ClearValues()
        {
            //_boardType = BoardType.Unknown; //do not clear as boardtype is not re-read on reconnect
            _yaw = _pitch = _roll = _speed = _batteryVoltage = _tripOdometer = _tripAmpHours = _tripRegenAmpHours = _currentAmps = 0;
            _rpm = _rev = /*_hardwareRevision = _firmwareRevision =*/ _rideMode = 0; //do not clear hw+fw as they are not re-read on reconnect
            _motorTemperature = _controllerTemperature = _unknownTemperature = _batteryTemperature = 0;
            _batteryPercent = 0;
            _batteryCells.Clear();
        }
        public void ValueChanged(string uuid, byte[] data)
        {
            if (data == null)
                return;

            uuid = uuid.ToUpper();

            if (data.Length != 2)
                return;

            if (uuid == TemperatureUUID)
            {
                _motorTemperature = Prefs.UseMetric ? data[0] : (data[0] * 9) / 5f + 32;
                _controllerTemperature = Prefs.UseMetric ? data[1] : (data[1] * 9) / 5f + 32;
                return;
            }
            else if (uuid == BatteryTemperatureUUID)
            {
                _unknownTemperature = Prefs.UseMetric ? data[0] : (data[0] * 9) / 5 + 32;
                _batteryTemperature = Prefs.UseMetric ? data[1] : (data[1] * 9) / 5 + 32;
                return;
            }
            else if (uuid == BatteryPercentUUID)
            {
                if (data[0] > 0)
                {
                    _batteryPercent = data[0];
                }
                else if (data[1] > 0)
                {
                    _batteryPercent = data[1];
                }
                return;
            }

            ushort value = BitConverter.ToUInt16(data, 0);

            switch (uuid)
            {
                case PitchUUID:
                    _pitch = 0.1f * (1800 - value);
                    break;
                case RollUUID:
                    _roll = 0.1f * (1800 - value);
                    break;
                case YawUUID:
                    _yaw = 0.1f * (1800 - value); //output does not seem to match offical app
                    break;
                case TripOdometerUUID:
                    if (_rev != value)
                    {
                        _rev = value;

                        float circumference = Prefs.TyreCircumference; //mm

                        if (Prefs.UseMetric)
                        {
                            _tripOdometer = _rev * circumference / 1000000; //km
                        }
                        else
                        {
                            _tripOdometer = _rev * circumference / 1000000 * 0.62137f; //miles
                        }
                    }
                    break;
                case RpmUUID:
                    if (_rpm != value)
                    {
                        _rpm = value;

                        float circumference = Prefs.TyreCircumference; //mm
                        float radius = circumference / (2f * (float)Math.PI) / 1000f; // In meters
                        float radPerSecond = (((float)Math.PI * 2f) / 60f) * _rpm;
                        float speedInMetersPerSecond = radius * radPerSecond;

                        if (Prefs.UseMetric)
                        {
                            float speedInKilometersPerHour = speedInMetersPerSecond * 3.6f;
                            _speed = speedInKilometersPerHour;
                        }
                        else
                        {
                            float speedInMilesPerHour = speedInMetersPerSecond * 2.23694f;
                            _speed = speedInMilesPerHour;
                        }
                    }
                    break;
                case RideModeUUID:
                    _rideMode = value;
                    break;
                case FirmwareRevisionUUID:
                    _firmwareRevision = value;
                    break;
                case CurrentAmpsUUID:
                    var scaleFactor = (_boardType == BoardType.V1) ? 0.9f : 1.8f;
                    _currentAmps = (float)value * 0.001f * scaleFactor; //needs sanity check before showing in app!
                    break;
                case TripAmpHoursUUID:
                    _tripAmpHours = (float)value * 0.0002f;
                    break;
                case TripRegenAmpHoursUUID:
                    _tripRegenAmpHours = (float)value * 0.0002f;
                    break;
                case BatteryVoltageUUID:
                    _batteryVoltage = 0.1f * value;
                    break;
                case HardwareRevisionUUID:
                    _hardwareRevision = value;
                    if (value >= 1 && value <= 2999)
                        _boardType = BoardType.V1;
                    else if (value >= 3000 && value <= 3999)
                        _boardType = BoardType.Plus;
                    else if (value >= 4000 && value <= 4999)
                        _boardType = BoardType.XR;
                    else if (value >= 5000 && value <= 5999)
                        _boardType = BoardType.Pint;
                    break;
                case BatteryCellsUUID:
                    byte batteryVoltage = data[0];
                    byte cellID = data[1];
                    _batteryCells[cellID] = batteryVoltage * 0.02f;
                    break;
            }
        }
    }
}