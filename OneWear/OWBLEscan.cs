using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace OneWear
{
    using static Globals;
    class OWBLEscan
    {
        private OWBLE_ScanCallback _scanCallback;
        private bool _isScanning = false;
        private BluetoothAdapter _adapter;
        private BluetoothLeScanner _bleScanner;
        public SortedDictionary<string, string> boards;

        public OWBLEscan()
        {
            BluetoothManager manager = Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager;
            _adapter = manager.Adapter;
            boards = new SortedDictionary<string, string>();
        }

        public async Task StartScanning(int timeout = 15)
        {
            if (_isScanning)
                return;

            Platform.CurrentActivity.RunOnUiThread(() => Toast.MakeText(Platform.CurrentActivity, "Scanning", ToastLength.Long).Show());

            _isScanning = true;

            // TODO: Handle power on state.

            _bleScanner = _adapter.BluetoothLeScanner;
            _scanCallback = new OWBLE_ScanCallback(this);
            var scanFilters = new List<ScanFilter>();
            var scanSettingsBuilder = new ScanSettings.Builder();

            var scanFilterBuilder = new ScanFilter.Builder();
            scanFilterBuilder.SetServiceUuid(ParcelUuid.FromString(ServiceUUID.ToString()));
            scanFilters.Add(scanFilterBuilder.Build());
            _bleScanner.StartScan(scanFilters, scanSettingsBuilder.Build(), _scanCallback);

            await Task.Delay(timeout * 1000);

            StopScanning();
        }

        public void StopScanning()
        {
            if (_isScanning == false)
                return;

            _bleScanner.StopScan(_scanCallback);

            _isScanning = false;
        }
    }

    class OWBLE_ScanCallback : ScanCallback
    {
        private OWBLEscan _owble;

        public OWBLE_ScanCallback(OWBLEscan owble)
        {
            _owble = owble;
        }

        public override void OnBatchScanResults(IList<ScanResult> results)
        {
            System.Diagnostics.Debug.WriteLine("OnBatchScanResults");
            base.OnBatchScanResults(results);
        }

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult result)
        {
            System.Diagnostics.Debug.WriteLine("OnScanResult");
            _owble.boards.TryAdd(result.Device.Name, result.Device.Address);
        }

        public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
        {
            System.Diagnostics.Debug.WriteLine("OnScanFailed");
            base.OnScanFailed(errorCode);
        }
    }
}