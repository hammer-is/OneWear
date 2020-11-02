using Android.OS;
using Android.Views;
using Android.Widget;
using System;
using System.Timers;
using Xamarin.Essentials;

namespace OneWear
{
    public class DiagFragment : AndroidX.Fragment.App.Fragment
    {
        private System.Timers.Timer _uiUpdate;
        private TextView _rideModeTextView, _pryTextView, _tempTextView, _cell1TextView, _cell2TextView, _cell3TextView, _cell4TextView;
        private View _diagView;

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            _diagView = inflater.Inflate(Resource.Layout.fragment_diag, container, false);

            _rideModeTextView = _diagView.FindViewById<TextView>(Resource.Id.rideModeTextView);
            _pryTextView = _diagView.FindViewById<TextView>(Resource.Id.pryTextView);
            _tempTextView = _diagView.FindViewById<TextView>(Resource.Id.tempTextView);

            _cell1TextView = _diagView.FindViewById<TextView>(Resource.Id.cell1TextView);
            _cell2TextView = _diagView.FindViewById<TextView>(Resource.Id.cell2TextView);
            _cell3TextView = _diagView.FindViewById<TextView>(Resource.Id.cell3TextView);
            _cell4TextView = _diagView.FindViewById<TextView>(Resource.Id.cell4TextView);

            _uiUpdate = new System.Timers.Timer();
            _uiUpdate.Interval = Prefs.UiUpdateSpeed;
            _uiUpdate.Elapsed += new System.Timers.ElapsedEventHandler(UiUpdateTimer);
            _uiUpdate.Start();

            return _diagView;
        }

        private void UiUpdateTimer(object sender, ElapsedEventArgs e)
        {
            MainActivity mainActivity = (MainActivity)Platform.CurrentActivity;

            string text = string.Empty;
            string text1, text2, text3, text4;
            float voltage;

            mainActivity.RunOnUiThread(() => _rideModeTextView.Text = String.Format("Mode: {0}", mainActivity.board.RideMode));
            mainActivity.RunOnUiThread(() => _pryTextView.Text = String.Format("P/R/Y: {0:0.0}/{1:0.0}/{2:0.0}", mainActivity.board.Pitch, mainActivity.board.Roll, mainActivity.board.Yaw));
            mainActivity.RunOnUiThread(() => _tempTextView.Text = String.Format("B/C/M (°{0}): {1:0.0}/{2:0.0}/{3:0.0}", Prefs.UseMetric ? "C" : "F", mainActivity.board.BatteryTemperature, mainActivity.board.ControllerTemperature, mainActivity.board.MotorTemperature));

            //Hide last cell for XR and Pint
            for (byte cell = 0; cell < (mainActivity.board.HardwareRevision >= 4000 ? 15 : 16); cell++)
            {
                if (mainActivity.board.BatteryCells.TryGetValue(cell, out voltage))
                    text += string.Format("{0:0.00}V ", voltage);
                else
                    text += "- ";

                switch (cell)
                {
                    case 3:
                        text1 = text;
                        mainActivity.RunOnUiThread(() => _cell1TextView.Text = text1);
                        text = string.Empty;
                        break;
                    case 7:
                        text2 = text;
                        mainActivity.RunOnUiThread(() => _cell2TextView.Text = text2);
                        text = string.Empty;
                        break;
                    case 11:
                        text3 = text;
                        mainActivity.RunOnUiThread(() => _cell3TextView.Text = text3);
                        text = string.Empty;
                        break;
                }
            }
            text4 = text;
            mainActivity.RunOnUiThread(() => _cell4TextView.Text = text4);

            if (_uiUpdate.Interval != Prefs.UiUpdateSpeed)
                _uiUpdate.Interval = Prefs.UiUpdateSpeed;
        }
        public override void OnPause()
        {
            if (_uiUpdate != null)
                _uiUpdate.Enabled = false;

            base.OnPause();
        }

        public override void OnResume()
        {
            if (_uiUpdate != null)
                _uiUpdate.Enabled = true;

            base.OnResume();
        }

        public override void OnDestroyView()
        {
            _uiUpdate.Close();

            base.OnDestroyView();
        }
    }
}