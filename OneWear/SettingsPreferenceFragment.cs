using Android.OS;
using Android.Widget;
using AndroidX.Preference;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xamarin.Essentials;

namespace OneWear
{
    public class SettingsPreferenceFragment : PreferenceFragmentCompat /*AndroidX.Fragment.App.Fragment*/
    {
        private Preference _boardScan;
        private ListPreference _boardMacPreference;
        private SwitchPreference _speedWarningPreference, _useMetricPreference;
        private SeekBarPreference _speedScalePreference, _speedWarningScalePreference;
        private EditTextPreference _tyreCircumferencePreference;

        OWBLEscan _owbleScan;

        public override void OnCreatePreferences(Bundle savedInstanceState, string rootKey)
        {
            _owbleScan = new OWBLEscan();

            SetPreferencesFromResource(Resource.Layout.preferencefragment_settings, rootKey);

            _boardScan = (Preference)FindPreference("boardScan");
            _boardMacPreference = (ListPreference)FindPreference("boardMac");
            _speedScalePreference = (SeekBarPreference)FindPreference("speedScale");
            _speedWarningPreference = (SwitchPreference)FindPreference("speedWarning");
            _speedWarningScalePreference = (SeekBarPreference)FindPreference("speedWarningScale");
            _useMetricPreference = (SwitchPreference)FindPreference("useMetric");
            _tyreCircumferencePreference = (EditTextPreference)FindPreference("tyreCircumference");

            _boardScan.PreferenceClick += BoardScan_PreferenceClick;
            _boardMacPreference.PreferenceClick += BoardMacPreference_PreferenceClick;

            _useMetricPreference.PreferenceChange += UseMetricPreference_PreferenceChange;
            _boardMacPreference.PreferenceChange += BoardMacPreference_PreferenceChange;
            _speedWarningPreference.PreferenceChange += SpeedWarningPreference_PreferenceChange;
            _tyreCircumferencePreference.PreferenceChange += TyreCircumferencePreference_PreferenceChange;

            _speedWarningScalePreference.Visible = _speedWarningPreference.Checked;
            _owbleScan.boards = Prefs.BoardMacDictionary;

            _speedWarningScalePreference.Min = _speedScalePreference.Min = _useMetricPreference.Checked ? 10 : (int)Math.Round(10 * 0.62137f);
            _speedWarningScalePreference.Max = _speedScalePreference.Max = _useMetricPreference.Checked ? 50 : (int)Math.Round(50 * 0.62137f);

            _speedScalePreference.Title = Prefs.UseMetric ? "Speed scale max (km/h)" : "Speed scale max (mph)";
            _speedWarningScalePreference.Title = Prefs.UseMetric ? "Speed Warning (km/h)" : "Speed Warning (mph)";

            BoardMacPreference_Populate();
        }

        private void UseMetricPreference_PreferenceChange(object sender, Preference.PreferenceChangeEventArgs e)
        {
            _speedWarningScalePreference.Min = _speedScalePreference.Min = (bool)e.NewValue ? 10 : (int)Math.Round(10 * 0.62137f);
            _speedWarningScalePreference.Max = _speedScalePreference.Max = (bool)e.NewValue ? 50 : (int)Math.Round(50 * 0.62137f);

            _speedScalePreference.Title = (bool)e.NewValue ? "Speed scale max (km/h)" : "Speed scale max (mph)";
            _speedWarningScalePreference.Title = (bool)e.NewValue ? "Speed Warning (km/h)" : "Speed Warning (mph)";

            _speedScalePreference.Value = (int)Math.Round((bool)e.NewValue ? _speedScalePreference.Value / 0.62137f : _speedScalePreference.Value * 0.62137f);
            _speedWarningScalePreference.Value = (int)Math.Round((bool)e.NewValue ? _speedWarningScalePreference.Value / 0.62137f : _speedWarningScalePreference.Value * 0.62137f);

            ((MainActivity)Platform.CurrentActivity).board.ClearValues(); //to avoid having values shows that are not calculated according to UseMetric prefs
        }

        private void TyreCircumferencePreference_PreferenceChange(object sender, Preference.PreferenceChangeEventArgs e)
        {
            int newValue;

            if (int.TryParse(e.NewValue.ToString(), out newValue) && (newValue >= 500) && (newValue <= 1000))
                return;

            Toast.MakeText(Platform.CurrentActivity, "Bad value. Must be between 500 and 1000. Default is 877.", ToastLength.Long).Show();
            e.Handled = false;
        }

        private async void BoardScan_PreferenceClick(object sender, Preference.PreferenceClickEventArgs e)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            ((MainActivity)Platform.CurrentActivity).oWBLE.Disconnect();
            _owbleScan.boards.Clear();

            _boardMacPreference.Value = ""; //"Not set"

            await _owbleScan.StartScanning();
        }

        private void SpeedWarningPreference_PreferenceChange(object sender, Preference.PreferenceChangeEventArgs e)
        {
            _speedWarningScalePreference.Visible = (bool)e.NewValue;
        }

        private void BoardMacPreference_PreferenceChange(object sender, Preference.PreferenceChangeEventArgs e)
        {
            if (e.NewValue.ToString() == "0")
            {
                e.Handled = false;
                return;
            }

            Prefs.BoardMacDictionary = _owbleScan.boards;

            ((MainActivity)Platform.CurrentActivity).oWBLE.Disconnect();
            ((MainActivity)Platform.CurrentActivity).oWBLE.Connect(e.NewValue.ToString());
        }

        private void BoardMacPreference_Populate()
        {
            string[] name = new string[_owbleScan.boards.Count()];
            string[] address = new string[_owbleScan.boards.Count()];
            int i = 0;

            foreach (KeyValuePair<string, string> kvp in _owbleScan.boards)
            {
                name[i] = kvp.Key;
                address[i++] = kvp.Value;
            }

            _boardMacPreference.SetEntries(name);
            _boardMacPreference.SetEntryValues(address);

        }
        private void BoardMacPreference_PreferenceClick(object sender, Preference.PreferenceClickEventArgs e)
        {
            _owbleScan.StopScanning();

            BoardMacPreference_Populate();
        }
    }
}