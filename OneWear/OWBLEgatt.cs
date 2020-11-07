using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using AndroidX.Wear.Activity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace OneWear
{
    using static Globals;

    public class OWBLEgatt : BluetoothGattCallback
    {
        private string _name, _address;
        private int _firmwareRevision;
        private int _hardwareRevision;
        private System.Timers.Timer _idleTimer = null, _watchdogTimer = null;

        private int _queueNumber = 0;

        private BluetoothManager _bluetoothManager = null;
        private BluetoothDevice _bluetoothDevice = null;
        private BluetoothGatt _bluetoothGatt = null;
        private ConcurrentQueue<OWBLE_QueueItem> _gattOperationQueue = new ConcurrentQueue<OWBLE_QueueItem>(); //thread-safe

        private List<byte> _handshakeBuffer = null;
        private bool _isHandshaking = false;
        private TaskCompletionSource<byte[]> _handshakeTaskCompletionSource = null;

        Dictionary<string, BluetoothGattCharacteristic> _characteristics = new Dictionary<string, BluetoothGattCharacteristic>();
        private bool _characteristicChanged;
        Dictionary<string, TaskCompletionSource<byte[]>> _readQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _writeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _subscribeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _unsubscribeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();

        private enum OWBLE_QueueItemOperationType
        {
            Read,
            Write,
            Subscribe,
            Unsubscribe,
        }

        private List<string> _characteristicsToReadNow = new List<string>()
        {
            RideModeUUID,

            BatteryPercentUUID,
            BatteryVoltageUUID,
            TripOdometerUUID,

            TemperatureUUID,
            BatteryTemperatureUUID,

            TripAmpHoursUUID,
            TripRegenAmpHoursUUID,

            /*LifetimeOdometerUUID,
            LifetimeAmpHoursUUID,*/
        };

        // Android can subscribe up to 15 things at once. Handshake() needs to happen before SubscribeValue() else this list can only consist of 14 items as Handshake() subscribe to SerialReadUUID.
        private List<string> _characteristicsToSubscribeTo = new List<string>()
        {
            RpmUUID,
            CurrentAmpsUUID,

            PitchUUID,
            RollUUID,
            YawUUID,

            BatteryCellsUUID,

            BatteryPercentUUID,
            BatteryVoltageUUID,
            TripOdometerUUID,

            TemperatureUUID,
            BatteryTemperatureUUID,

            TripAmpHoursUUID,
            TripRegenAmpHoursUUID,

            /*LifetimeOdometerUUID,
            LifetimeAmpHoursUUID,*/
        };

        private class OWBLE_QueueItem
        {
            public OWBLE_QueueItemOperationType OperationType { get; private set; }
            public BluetoothGattCharacteristic Characteristic { get; private set; }
            public byte[] Data { get; set; }

            public OWBLE_QueueItem(BluetoothGattCharacteristic characteristic, OWBLE_QueueItemOperationType operationType, byte[] data = null)
            {
                Characteristic = characteristic;
                OperationType = operationType;
                Data = data;
            }
        }

        public override async void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            BluetoothGattService service = gatt.GetService(ServiceUUID);

            if (service == null)
                return;

            lock(_characteristics)
            { 
                //_characteristics needs to be rebuild on reconnect (TryAdd() is not enough as something is changing on the BluetoothGattCharacteristic on a new connection attempt and subscribe will fail)
                foreach (BluetoothGattCharacteristic characteristic in service.Characteristics)
                    _characteristics.Add(characteristic.Uuid.ToString().ToLower(), characteristic);
            }

            //foreach (string characteristic in _characteristicsToSubscribeTo) //not needed unless subscribing to 15 values!
            //    await UnsubscribeValue(characteristic);

            try
            {
                byte[] hardwareRevision = await ReadValue(HardwareRevisionUUID);
                if (hardwareRevision.Length == 0)
                    return;
                _hardwareRevision = BitConverter.ToUInt16(hardwareRevision, 0);
            }
            catch (TaskCanceledException){ return; };

            try
            { 
                byte[] firmwareRevision = await ReadValue(FirmwareRevisionUUID);
                if (firmwareRevision.Length == 0)
                    return;
                _firmwareRevision = BitConverter.ToUInt16(firmwareRevision, 0);
            }
            catch (TaskCanceledException) { return; };

            if (_hardwareRevision >= 4210)
            {
                Intent intent = new Intent(Platform.CurrentActivity, typeof(ConfirmationActivity))
                    .PutExtra(ConfirmationActivity.ExtraAnimationType, ConfirmationActivity.FailureAnimation)
                    .PutExtra(ConfirmationActivity.ExtraMessage, "Unsupported wheel (" + _hardwareRevision.ToString() + ")");
                for (int i = 0; i < 10; i++)
                {
                    Platform.CurrentActivity.StartActivity(intent); //Seems like it's only shows 1 sec
                    await Task.Delay(1500);
                }
                Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
            }

            if (_hardwareRevision > 3000 && _firmwareRevision > 4000)
            {
                await Handshake(); //Subscribes 1 characteristic temporarily
                _idleTimer.Enabled = true;
            }

            foreach (string characteristic in _characteristicsToSubscribeTo)
            {
                try
                {
                    await SubscribeValue(characteristic);
                }
                catch (TaskCanceledException) { return; };
            }

            foreach (string characteristic in _characteristicsToReadNow)
            {
                try
                {
                    await ReadValue(characteristic);
                }
                catch (TaskCanceledException) { return; };
            }
        }

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (newState == ProfileState.Connected)
            {
                _name = gatt.Device.Name;

                System.Diagnostics.Debug.WriteLine("Connected " + _name);

                _characteristicChanged = false;
                _watchdogTimer.Enabled = true;

                gatt.DiscoverServices();
            }
            else if (newState == ProfileState.Disconnected) //Don't do anything. Let the watchdog handle disconnects.
                System.Diagnostics.Debug.WriteLine("Disconnected " + _name);
        }

        private void ProcessQueue()
        {
            int queueNumber = Interlocked.Increment(ref _queueNumber); //thread-safe

            System.Diagnostics.Debug.WriteLine($"ProcessQueue {queueNumber}: {_gattOperationQueue.Count}");

            if (!_gattOperationQueue.TryDequeue(out OWBLE_QueueItem item))
                return;

            switch (item.OperationType)
            {
                case OWBLE_QueueItemOperationType.Read:
                    bool didRead = _bluetoothGatt.ReadCharacteristic(item.Characteristic);
                    if (didRead == false)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR {queueNumber}: Unable to read {item.Characteristic.Uuid}");
                    }
                    break;

                case OWBLE_QueueItemOperationType.Write:
                    bool didSetValue = item.Characteristic.SetValue(item.Data);
                    bool didWrite = _bluetoothGatt.WriteCharacteristic(item.Characteristic);
                    if (didWrite == false)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR {queueNumber}: Unable to write {item.Characteristic.Uuid}");
                    }
                    break;

                case OWBLE_QueueItemOperationType.Subscribe:
                    bool didSubscribe = _bluetoothGatt.SetCharacteristicNotification(item.Characteristic, true);
                    if (didSubscribe == false)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR {queueNumber}: Unable to subscribe {item.Characteristic.Uuid}");
                    }

                    BluetoothGattDescriptor subscribeDescriptor = item.Characteristic.GetDescriptor(ConfigUUID);
                    bool didSetSubscribeDescriptor = subscribeDescriptor.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                    bool didWriteSubscribeDescriptor = _bluetoothGatt.WriteDescriptor(subscribeDescriptor);
                    break;

                case OWBLE_QueueItemOperationType.Unsubscribe:
                    bool didUnsubscribe = _bluetoothGatt.SetCharacteristicNotification(item.Characteristic, false);
                    if (didUnsubscribe == false)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR {queueNumber}: Unable to unsubscribe {item.Characteristic.Uuid}");
                    }

                    BluetoothGattDescriptor unsubscribeDescriptor = item.Characteristic.GetDescriptor(ConfigUUID);
                    bool didSetUnsubscribeDescriptor = unsubscribeDescriptor.SetValue(BluetoothGattDescriptor.DisableNotificationValue.ToArray());
                    bool didWriteUnsubscribeDescriptor = _bluetoothGatt.WriteDescriptor(unsubscribeDescriptor);
                    break;
            }
        }

        public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            string uuid = characteristic.Uuid.ToString().ToLower();

            lock (_readQueue)
            {
                if (_readQueue.ContainsKey(uuid))
                {
                    TaskCompletionSource<byte[]> readItem = _readQueue[uuid];
                    _readQueue.Remove(uuid);

                    byte[] dataBytes = characteristic.GetValue();

                    if (SerialWriteUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false &&
                        SerialReadUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false)
                    {
                        // If our system is little endian, reverse the array.
                        if (BitConverter.IsLittleEndian && dataBytes != null)
                        {
                            Array.Reverse(dataBytes);
                        }
                    }

                    readItem.TrySetResult(dataBytes);
                    ((MainActivity)Platform.CurrentActivity).board.ValueChanged(uuid, dataBytes);
                }
            }

            ProcessQueue();
        }

        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            string uuid = characteristic.Uuid.ToString().ToLower();

            lock (_writeQueue)
            {

                if (_writeQueue.ContainsKey(uuid))
                {
                    TaskCompletionSource<byte[]> writeItem = _writeQueue[uuid];
                    _writeQueue.Remove(uuid);

                    byte[] dataBytes = characteristic.GetValue();

                    if (SerialWriteUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false &&
                        SerialReadUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false)
                    {
                        // If our system is little endian, reverse the array.
                        if (BitConverter.IsLittleEndian && dataBytes != null)
                        {
                            Array.Reverse(dataBytes);
                        }
                    }

                    writeItem.TrySetResult(dataBytes);
                }
            }

            ProcessQueue();
        }


        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            _characteristicChanged = true;

            string uuid = characteristic.Uuid.ToString().ToLower();

            byte[] dataBytes = characteristic.GetValue();

            if (SerialWriteUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false &&
                SerialReadUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false)
            {
                // If our system is little endian, reverse the array.
                if (BitConverter.IsLittleEndian && dataBytes != null)
                {
                    Array.Reverse(dataBytes);
                }
            }
            else if (_isHandshaking && uuid.Equals(SerialReadUUID, StringComparison.CurrentCultureIgnoreCase))
            {
                _handshakeBuffer.AddRange(dataBytes);
                if (_handshakeBuffer.Count >= 20) // it seems to become >20 if handshake goes wrong (happening to soon?)
                {
                    _isHandshaking = false;
                    _handshakeTaskCompletionSource.TrySetResult(_handshakeBuffer.ToArray<byte>());
                    //System.Diagnostics.Debug.WriteLine("_handshakeBuffer.Count() " + _handshakeBuffer.Count().ToString());
                }

                return;
            }

            ((MainActivity)Platform.CurrentActivity).board.ValueChanged(uuid, dataBytes);
        }

        public override void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, [GeneratedEnum] GattStatus status)
        {
            // TODO: ?
        }

        public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, [GeneratedEnum] GattStatus status)
        {
            string uuid = descriptor.Characteristic.Uuid.ToString().ToLower();

            // Check if its a subscribe or unsubscribe descriptor
            if (descriptor.Uuid.ToString().ToLower() == "00002902-0000-1000-8000-00805f9b34fb")
            {
                byte[] descriptorValue = descriptor.GetValue();
                if (descriptorValue == null)
                    return; //TODO: This avoids an exception. But it does not Remove() from _subscribeQueue() / _unsubscribeQueue but likely only happens during Cleanup() so not a real issue

                if (descriptorValue.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue.ToArray()))
                {
                    lock (_subscribeQueue)
                    {

                        if (_subscribeQueue.ContainsKey(uuid))
                        {
                            TaskCompletionSource<byte[]> subscribeItem = _subscribeQueue[uuid];
                            _subscribeQueue.Remove(uuid);
                            subscribeItem.TrySetResult(descriptorValue);
                        }
                    }
                }
                else if (descriptorValue.SequenceEqual(BluetoothGattDescriptor.DisableNotificationValue.ToArray()))
                {
                    lock (_subscribeQueue)
                    {
                        if (_unsubscribeQueue.ContainsKey(uuid))
                        {
                            TaskCompletionSource<byte[]> unsubscribeItem = _unsubscribeQueue[uuid];
                            _unsubscribeQueue.Remove(uuid);
                            unsubscribeItem.TrySetResult(descriptorValue);
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"OnDescriptorWrite Error: Unhandled descriptor of {descriptor.Uuid} on {uuid}.");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"OnDescriptorWrite Error: Unhandled descriptor of {descriptor.Uuid} on {uuid}.");
            }

            ProcessQueue();
        }

        public void Connect(string address)
        {
            Disconnect();

            _address = address;

            _hardwareRevision = 0;
            _firmwareRevision = 0;

            _bluetoothDevice = _bluetoothManager.Adapter.GetRemoteDevice(address);
            _bluetoothGatt = _bluetoothDevice.ConnectGatt(Platform.CurrentActivity, true, this);
        }

        public void Disconnect()
        {
            if (_bluetoothGatt != null)
            {
                _idleTimer.Enabled = false;
                _watchdogTimer.Enabled = false;

                if (_handshakeTaskCompletionSource != null)
                    _handshakeTaskCompletionSource.TrySetCanceled();

                lock (_characteristics)
                {
                    _characteristics.Clear();
                }

                lock (_readQueue)
                {
                    foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> tcs in _readQueue)
                        tcs.Value.TrySetCanceled();
                    _readQueue.Clear();
                }
                lock (_writeQueue)
                {
                    foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> tcs in _writeQueue)
                        tcs.Value.TrySetCanceled();
                    _writeQueue.Clear();
                }
                lock (_subscribeQueue)
                {
                    foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> tcs in _subscribeQueue)
                        tcs.Value.TrySetCanceled();
                    _subscribeQueue.Clear();
                }
                lock (_unsubscribeQueue)
                {
                    foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> tcs in _unsubscribeQueue)
                        tcs.Value.TrySetCanceled();
                    _unsubscribeQueue.Clear();
                }

                _gattOperationQueue.Clear();

                ((MainActivity)Platform.CurrentActivity).board.ClearValues();

                //_bluetoothGatt.Disconnect(); //= cancelOpen() in log. Sometimes! triggers OnConnectionStateChange() ProfileState.Disconnected
                _bluetoothGatt.Close();
                // If connect is still in progress (code executed in OnServicesDiscovered() ) - exceptions occur in Java code. Probably can also be triggered if IdleTimer is writing (but more difficult to hit). They seem harmleess.
                // 11-06 12:49:24.993 W/BluetoothGatt( 3243): Unhandled exception in callback
                // 11-06 12:49:24.993 W/BluetoothGatt( 3243): java.lang.NullPointerException: Attempt to invoke virtual method 'void android.bluetooth.BluetoothGattCallback.onCharacteristicChanged(android.bluetooth.BluetoothGatt, android.bluetooth.BluetoothGattCharacteristic)' on a null object reference
            }
        }

        public Task<byte[]> ReadValue(string characteristicGuid)
        {
            System.Diagnostics.Debug.WriteLine($"ReadValue: {characteristicGuid}");

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            string uuid = characteristicGuid.ToLower();

            lock(_characteristics)
            { 
                // TODO: Check for connected devices?
                if (_characteristics.ContainsKey(uuid) == false)
                    throw new TaskCanceledException();

                // Already awaiting it.
                if (_readQueue.ContainsKey(uuid))
                {
                    return _readQueue[uuid].Task;
                }

                lock(_readQueue)
                { 
                    _readQueue.Add(uuid, taskCompletionSource);
                }

                _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Read));
            }
            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task<byte[]> WriteValue(string characteristicGuid, byte[] data)
        {
            System.Diagnostics.Debug.WriteLine($"WriteValue: {characteristicGuid}");

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();
            
            lock(_characteristics)
            { 
                if (data.Length > 20)
                {
                    // TODO: Error, some Android BLE devices do not handle > 20byte packets well.
                    return null;
                }

                string uuid = characteristicGuid.ToLower();

                // TODO: Check for connected devices?
                if (_characteristics.ContainsKey(uuid) == false)
                    throw new TaskCanceledException();

                // TODO: Handle this.
                /*
                if (_readQueue.ContainsKey(uuid))
                {
                    return _readQueue[uuid].Task;
                }
                */

                lock (_writeQueue)
                { 
                    _writeQueue.Add(uuid, taskCompletionSource);
                }

                byte[] dataBytes = null;
                if (data != null)
                {
                    dataBytes = new byte[data.Length];
                    Array.Copy(data, dataBytes, data.Length);

                    if (SerialWriteUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false &&
                           SerialReadUUID.Equals(uuid, StringComparison.InvariantCultureIgnoreCase) == false)
                    {
                        // If our system is little endian, reverse the array.
                        if (BitConverter.IsLittleEndian && dataBytes != null)
                        {
                            Array.Reverse(dataBytes);
                        }
                    }
                }

                _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Write, dataBytes));
            }
            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task SubscribeValue(string characteristicGuid)
        {
            System.Diagnostics.Debug.WriteLine($"SubscribeValue: {characteristicGuid}");

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();
            
            string uuid = characteristicGuid.ToLower();

            lock(_characteristics)
            {
                // TODO: Check for connected devices?
                if (_characteristics.ContainsKey(uuid) == false)
                    throw new TaskCanceledException();

                lock(_subscribeQueue)
                { 
                    _subscribeQueue.Add(uuid, taskCompletionSource);
                }

                _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Subscribe));
            }
            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task UnsubscribeValue(string characteristicGuid)
        {
            System.Diagnostics.Debug.WriteLine($"UnsubscribeValue: {characteristicGuid}");

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            string uuid = characteristicGuid.ToLower();

            lock(_characteristics)
            { 
                // TODO: Check for connected devices?
                if (_characteristics.ContainsKey(uuid) == false)
                    throw new TaskCanceledException();

                lock(_unsubscribeQueue)
                { 
                    _unsubscribeQueue.Add(uuid, taskCompletionSource);
                }

                _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Unsubscribe));
            }
            ProcessQueue();

            return taskCompletionSource.Task;
        }

        protected void WatchdogTimer(object sender, System.Timers.ElapsedEventArgs e)
        {             
            if (!_characteristicChanged) //broken communication due to disconnect or other issue. 
            {    //NOTE/TODO: This only detects if there are "blocked" SubscribeValue - not ReadValue+WriteValue

                /*  This continues to trigger untill Android senses the disconnect.
                 
                    [0:] WatchdogTimer !_characteristicChanged
                    [0:] Connected owXXXXXX
                    [0:] ReadValue: E659F318-EA98-11E3-AC10-0800200C9A66
                    [0:] ProcessQueue 23: 1
                    [0:] WatchdogTimer !_characteristicChanged
                    [0:] Connected owXXXXXX
                    [0:] ReadValue: E659F318-EA98-11E3-AC10-0800200C9A66
                    [0:] ProcessQueue 24: 1
                    [0:] ProcessQueue 25: 0
                    [0:] Disconnected owXXXXXX
                    [0:] WatchdogTimer !_characteristicChanged 
                */
                System.Diagnostics.Debug.WriteLine("WatchdogTimer !_characteristicChanged");
                Connect(_address);
            }
            _characteristicChanged = false;
        }

        protected void IdleTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            byte[] firmwareRevision = BitConverter.GetBytes((UInt16)_firmwareRevision);
            WriteValue(FirmwareRevisionUUID, firmwareRevision); //note that we do NOT await!
        }

        public OWBLEgatt()
        {
            _bluetoothManager = Platform.CurrentActivity.GetSystemService(Context.BluetoothService) as BluetoothManager;

            _idleTimer = new System.Timers.Timer();
            _idleTimer.Interval = 15000;
            _idleTimer.Elapsed += new System.Timers.ElapsedEventHandler(IdleTimer);

            _watchdogTimer = new System.Timers.Timer();
            _watchdogTimer.Interval = 2500; //Seems to work even as low as 1000
            _watchdogTimer.Elapsed += new System.Timers.ElapsedEventHandler(WatchdogTimer);
        }

        private async Task<bool> Handshake()
        {
            try
            {
                byte[] byteArray = new byte[1];
                do
                {
                    _isHandshaking = true;
                    _handshakeTaskCompletionSource = new TaskCompletionSource<byte[]>();
                    _handshakeBuffer = new List<byte>();

                    await SubscribeValue(SerialReadUUID);

                    // Data does not send until this is triggered. 
                    byte[] firmwareRevision = BitConverter.GetBytes((UInt16)_firmwareRevision);

                    byte[] didWrite = await WriteValue(FirmwareRevisionUUID, firmwareRevision);

                    byteArray = await _handshakeTaskCompletionSource.Task;

                    await UnsubscribeValue(SerialReadUUID);
                }
                while (byteArray.Length != 20);

                if (byteArray.Length == 20)
                {
                    byte[] outputArray = new byte[20];
                    Array.Copy(byteArray, 0, outputArray, 0, 3);

                    // Take almost all of the bytes from the input array. This is almost the same as the last part as
                    // we are ignoring the first 3 and the last bytes.
                    byte[] arrayToMD5_part1 = new byte[16];
                    Array.Copy(byteArray, 3, arrayToMD5_part1, 0, 16);

                    // This appears to be a static value from the board.
                    byte[] arrayToMD5_part2 = new byte[] {
                        217,    // D9
                        37,     // 25
                        95,     // 5F
                        15,     // 0F
                        35,     // 23
                        53,     // 35
                        78,     // 4E
                        25,     // 19
                        186,    // BA
                        115,    // 73
                        156,    // 9C
                        205,    // CD
                        196,    // C4
                        169,    // A9
                        23,     // 17
                        101,    // 65
                        };

                    // New byte array we are going to MD5 hash. Part of the input string, part of this static string.
                    byte[] arrayToMD5 = new byte[arrayToMD5_part1.Length + arrayToMD5_part2.Length];
                    arrayToMD5_part1.CopyTo(arrayToMD5, 0);
                    arrayToMD5_part2.CopyTo(arrayToMD5, arrayToMD5_part1.Length);

                    // Start prepping the MD5 hash
                    byte[] md5Hash = null;
                    using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    {
                        md5Hash = md5.ComputeHash(arrayToMD5);
                    }

                    // Add it to the 3 bytes we already have.
                    Array.Copy(md5Hash, 0, outputArray, 3, md5Hash.Length);

                    // Validate the check byte.
                    outputArray[19] = 0;
                    for (int i = 0; i < outputArray.Length - 1; ++i)
                    {
                        outputArray[19] = ((byte)(outputArray[i] ^ outputArray[19]));
                    }

                    string inputString = BitConverter.ToString(byteArray).Replace("-", ":").ToLower();
                    string outputString = BitConverter.ToString(outputArray).Replace("-", ":").ToLower();

                    System.Diagnostics.Debug.WriteLine($"Input: {inputString}");
                    System.Diagnostics.Debug.WriteLine($"Output: {outputString}");

                    await WriteValue(SerialWriteUUID, outputArray);
                }
                return false;
            }
            catch (TaskCanceledException)
            { return false; };
        }
    }
}