using Android.App;
using Android.OS;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Wear.Ambient;
using AndroidX.Wear.Widget.Drawer;
using Xamarin.Essentials;
using static AndroidX.Wear.Ambient.AmbientModeSupport;
using static AndroidX.Wear.Widget.Drawer.WearableNavigationDrawerView;

namespace OneWear
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : /*WearableActivity*/ AppCompatActivity, IOnItemSelectedListener, IAmbientCallbackProvider
    {
        public OWBLEgatt oWBLE;

        private WearableNavigationDrawerView _wearableNavigationDrawerView;
        private SettingsPreferenceFragment _settingsPreferenceFragment;
        private SettingsFragment _settingsFragment;
        private SpeedFragment _speedFragment;
        private DiagFragment _diagFragment;

        private AmbientController _ambientController;

        public Board board = new Board();

        public static int REQUEST_ENABLE_BT = 3;

        public AmbientCallback AmbientCallback => new MainAmbientCallback();

        //public static MainActivity instance { get; set; }

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            Xamarin.Essentials.Platform.Init(this, bundle);

            //Preferences.Clear();

            SetContentView(Resource.Layout.activity_main);

            _wearableNavigationDrawerView = FindViewById<WearableNavigationDrawerView>(Resource.Id.top_navigation_drawer);
            _wearableNavigationDrawerView.SetAdapter(new MainNavigationAdapter(this));
            _wearableNavigationDrawerView.AddOnItemSelectedListener(this);

            _ambientController = AndroidX.Wear.Ambient.AmbientModeSupport.Attach(this);

            oWBLE = new OWBLEgatt();

            _settingsPreferenceFragment = new SettingsPreferenceFragment();
            _speedFragment = new SpeedFragment();
            _diagFragment = new DiagFragment();
            _settingsFragment = new SettingsFragment();

            //Enable bluetooth...
            /*BluetoothManager manager = Xamarin.Essentials.Platform.CurrentActivity.GetSystemService(Context.BluetoothService) as BluetoothManager;
            if (!manager.Adapter.IsEnabled)
            { 
                Intent enableBtIntent = new Intent(BluetoothAdapter.ActionRequestEnable);
                Xamarin.Essentials.Platform.CurrentActivity.StartActivityForResult(enableBtIntent, MainActivity.REQUEST_ENABLE_BT);
            }*/

            string boardMac = Prefs.BoardMac;
            if (boardMac != "")
            {
                _wearableNavigationDrawerView.SetCurrentItem(1, false);
                oWBLE.Connect(boardMac);
            }
            else
                //SupportFragmentManager.BeginTransaction().Add(Resource.Id.linearLayout1, _settingsPreferenceFragment).Commit();
                SupportFragmentManager.BeginTransaction().Add(Resource.Id.linearLayout1, _settingsFragment).Commit();

            _wearableNavigationDrawerView.Controller.PeekDrawer();

            Toast.MakeText(this, AppInfo.Name + " v" + AppInfo.Version.ToString(), ToastLength.Long).Show();

            //instance = this;
        }

        public void OnItemSelected(int pos)
        {
            switch (pos)
            {
                case 0:
                    //SupportFragmentManager.BeginTransaction().Replace(Resource.Id.linearLayout1, _settingsPreferenceFragment).Commit();
                    SupportFragmentManager.BeginTransaction().Replace(Resource.Id.linearLayout1, _settingsFragment).Commit();
                    break;
                case 1:
                    SupportFragmentManager.BeginTransaction().Replace(Resource.Id.linearLayout1, _speedFragment).Commit();
                    break;
                case 2:
                    SupportFragmentManager.BeginTransaction().Replace(Resource.Id.linearLayout1, _diagFragment).Commit();
                    break;
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        protected override void OnDestroy()
        {
            oWBLE.Disconnect();
            base.OnDestroy();
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
        }
    }
}
