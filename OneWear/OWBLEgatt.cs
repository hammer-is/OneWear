using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using AndroidX.Wear.Activity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace OneWear
{
    using static Globals;

    public class OWBLEgatt : BluetoothGattCallback
    {
        private string _name;
        private int _firmwareRevision;
        private int _hardwareRevision;

        private System.Timers.Timer _idleTimer = null;
        private BluetoothGatt _bluetoothGatt = null;
        private Queue<OWBLE_QueueItem> _gattOperationQueue = new Queue<OWBLE_QueueItem>();
        private bool _gattOperationQueueProcessing;

        Dictionary<string, BluetoothGattCharacteristic> _characteristics = new Dictionary<string, BluetoothGattCharacteristic>();
        Dictionary<string, TaskCompletionSource<byte[]>> _readQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _writeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _subscribeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        Dictionary<string, TaskCompletionSource<byte[]>> _unsubscribeQueue = new Dictionary<string, TaskCompletionSource<byte[]>>();
        List<string> _notifyList = new List<string>();

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

            //_characteristics needs to be rebuild on reconnect (TryAdd() is not enough as something is changing on the BluetoothGattCharacteristic on a new connection attempt and subscribe will fail)
            foreach (BluetoothGattCharacteristic characteristic in service.Characteristics)
                _characteristics.Add(characteristic.Uuid.ToString().ToLower(), characteristic);

            //foreach (string characteristic in _characteristicsToSubscribeTo) //not needed unless subscribing to 15 values!
            //    await UnsubscribeValue(characteristic);

            if (_hardwareRevision == 0)
            { 
                byte[] hardwareRevision = await ReadValue(HardwareRevisionUUID);
                if (hardwareRevision == null)
                    return;
                _hardwareRevision = BitConverter.ToUInt16(hardwareRevision, 0);
            }

            if (_firmwareRevision == 0)
            { 
                byte[] firmwareRevision = await ReadValue(FirmwareRevisionUUID);
                if (firmwareRevision == null)
                    return;
                _firmwareRevision = BitConverter.ToUInt16(firmwareRevision, 0);
            }

            if (_hardwareRevision >= 4210)
            {
                Disconnect();

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

                if (_idleTimer == null)
                {
                    _idleTimer = new System.Timers.Timer();
                    _idleTimer.Interval = 15000;
                    _idleTimer.Elapsed += new System.Timers.ElapsedEventHandler(PingTimer);
                    _idleTimer.Start();
                }
                else
                {
                    _idleTimer.Enabled = true;
                }
            }

            foreach (string characteristic in _characteristicsToSubscribeTo)
                await SubscribeValue(characteristic);

            foreach (string characteristic in _characteristicsToReadNow)
                await ReadValue(characteristic);
        }

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (newState == ProfileState.Connected)
            {
                _name = gatt.Device.Name;

                //Platform.CurrentActivity.RunOnUiThread(() => Toast.MakeText(Platform.CurrentActivity, "Connected " + _name, ToastLength.Long).Show());
                System.Diagnostics.Debug.WriteLine("Connected " + _name);

                gatt.DiscoverServices();
            }
            else if (newState == ProfileState.Disconnected)
            {
                //Platform.CurrentActivity.RunOnUiThread(() => Toast.MakeText(Platform.CurrentActivity, "Disconnected " + _name, ToastLength.Long).Show());
                System.Diagnostics.Debug.WriteLine("Disconnected " + _name);

                if (_idleTimer != null)
                    _idleTimer.Enabled = false;
                
                _gattOperationQueue.Clear();

                _characteristics.Clear();
                _readQueue.Clear();
                _writeQueue.Clear();
                _subscribeQueue.Clear();
                _unsubscribeQueue.Clear();
                _notifyList.Clear();
                _gattOperationQueueProcessing = false;

                ((MainActivity)Platform.CurrentActivity).board.ClearValues();
            }
        }

        private int _queueNumber = 0;

        private void ProcessQueue()
        {
            int queueNumber = _queueNumber;
            ++_queueNumber;

            System.Diagnostics.Debug.WriteLine($"ProcessQueue {queueNumber}: {_gattOperationQueue.Count}");
            if (_gattOperationQueue.Count == 0)
                return;

            if (_gattOperationQueueProcessing)
                return;

            _gattOperationQueueProcessing = true;

            OWBLE_QueueItem item = _gattOperationQueue.Dequeue();
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

                ((MainActivity)Platform.CurrentActivity).board.ValueChanged(uuid, dataBytes);
                readItem.SetResult(dataBytes);
            }

            _gattOperationQueueProcessing = false;
            ProcessQueue();
        }

        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            string uuid = characteristic.Uuid.ToString().ToLower();

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

                writeItem.SetResult(dataBytes);
            }

            _gattOperationQueueProcessing = false;
            ProcessQueue();
        }


        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            string uuid = characteristic.Uuid.ToString().ToLower();

            if (_notifyList.Contains(uuid))
            {
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
                        _handshakeTaskCompletionSource.SetResult(_handshakeBuffer.ToArray<byte>());
                        //System.Diagnostics.Debug.WriteLine("_handshakeBuffer.Count() " + _handshakeBuffer.Count().ToString());
                    }

                    return;
                }

                ((MainActivity)Platform.CurrentActivity).board.ValueChanged(uuid, dataBytes);
            }
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

                if (descriptorValue.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue.ToArray()))
                {
                    if (_subscribeQueue.ContainsKey(uuid))
                    {
                        TaskCompletionSource<byte[]> subscribeItem = _subscribeQueue[uuid];
                        _subscribeQueue.Remove(uuid);
                        subscribeItem.SetResult(descriptorValue);
                    }
                }
                else if (descriptorValue.SequenceEqual(BluetoothGattDescriptor.DisableNotificationValue.ToArray()))
                {
                    if (_unsubscribeQueue.ContainsKey(uuid))
                    {
                        TaskCompletionSource<byte[]> unsubscribeItem = _unsubscribeQueue[uuid];
                        _unsubscribeQueue.Remove(uuid);
                        unsubscribeItem.SetResult(descriptorValue);
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

            _gattOperationQueueProcessing = false;
            ProcessQueue();
        }

        public void Connect(string address)
        {            
            _gattOperationQueueProcessing = false;
            _hardwareRevision = 0;
            _firmwareRevision = 0;

            //Platform.CurrentActivity.RunOnUiThread(() => Toast.MakeText(Platform.CurrentActivity, "Connecting " + address, ToastLength.Long).Show());

            BluetoothDevice device = BluetoothAdapter.DefaultAdapter.GetRemoteDevice(address);
            _bluetoothGatt = device.ConnectGatt(Xamarin.Essentials.Platform.CurrentActivity, true, this);
        }

        public void Disconnect()
        {
            if (_idleTimer != null)
            { 
                _idleTimer.Close();
                _idleTimer = null;
            }

            if (_bluetoothGatt != null)
            {
                _bluetoothGatt.Close();
                _bluetoothGatt = null;
            }

            _gattOperationQueue.Clear();

            _characteristics.Clear();
            _readQueue.Clear();
            _writeQueue.Clear();
            _subscribeQueue.Clear();
            _unsubscribeQueue.Clear();
            _notifyList.Clear();
            _gattOperationQueueProcessing = false;

            ((MainActivity)Platform.CurrentActivity).board.ClearValues();
        }

        public Task<byte[]> ReadValue(string characteristicGuid, bool important = false)
        {
            System.Diagnostics.Debug.WriteLine($"ReadValue: {characteristicGuid}");

            if (_bluetoothGatt == null)
                return null;

            string uuid = characteristicGuid.ToLower();

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            // Already awaiting it.
            if (_readQueue.ContainsKey(uuid))
            {
                return _readQueue[uuid].Task;
            }

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _readQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _readQueue.Add(uuid, taskCompletionSource);
            }

            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Read));

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task<byte[]> WriteValue(string characteristicGuid, byte[] data, bool important = false)
        {
            System.Diagnostics.Debug.WriteLine($"WriteValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            if (data.Length > 20)
            {
                // TODO: Error, some Android BLE devices do not handle > 20byte packets well.
                return null;
            }

            string uuid = characteristicGuid.ToLower();

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            // TODO: Handle this.
            /*
            if (_readQueue.ContainsKey(uuid))
            {
                return _readQueue[uuid].Task;
            }
            */

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _writeQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _writeQueue.TryAdd(uuid, taskCompletionSource);
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

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task SubscribeValue(string characteristicGuid, bool important = false)
        {
            System.Diagnostics.Debug.WriteLine($"SubscribeValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            string uuid = characteristicGuid.ToLower();

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            _notifyList.Add(uuid);

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _subscribeQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _subscribeQueue.Add(uuid, taskCompletionSource);
            }

            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Subscribe));

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task UnsubscribeValue(string characteristicGuid, bool important = false)
        {
            System.Diagnostics.Debug.WriteLine($"UnsubscribeValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            string uuid = characteristicGuid.ToLower();

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            _notifyList.RemoveAll(x => x == uuid);

            TaskCompletionSource<byte[]> taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _unsubscribeQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _unsubscribeQueue.Add(uuid, taskCompletionSource);
            }

            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Unsubscribe));

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        protected void PingTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("PingTimer");
            try
            {
                byte[] firmwareRevision = BitConverter.GetBytes((UInt16)_firmwareRevision);
                WriteValue(FirmwareRevisionUUID, firmwareRevision);
            }
            catch (Exception err)
            {
                // TODO: Couldnt update firmware revision.
                System.Diagnostics.Debug.WriteLine("ERROR: " + err.Message);
            }
        }

        private List<byte> _handshakeBuffer = null;
        private bool _isHandshaking = false;
        private TaskCompletionSource<byte[]> _handshakeTaskCompletionSource = null;

        private async Task<bool> Handshake()
        {
            byte[] byteArray = new byte[1];
            do
            {
                _isHandshaking = true;
                _handshakeTaskCompletionSource = new TaskCompletionSource<byte[]>();
                _handshakeBuffer = new List<byte>();

                await SubscribeValue(SerialReadUUID, true);

                // Data does not send until this is triggered. 
                byte[] firmwareRevision = BitConverter.GetBytes((UInt16)_firmwareRevision);

                byte[] didWrite = await WriteValue(FirmwareRevisionUUID, firmwareRevision, true);

                byteArray = await _handshakeTaskCompletionSource.Task;

                await UnsubscribeValue(SerialReadUUID, true);
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
    }
}