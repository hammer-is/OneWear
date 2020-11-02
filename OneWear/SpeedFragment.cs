using Android.Animation;
using Android.OS;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using System;
using System.Timers;
using Xamarin.Essentials;

namespace OneWear
{
    using static Globals;
    public class SpeedFragment : AndroidX.Fragment.App.Fragment
    {
        private System.Timers.Timer _uiUpdate;
        private TextView _batteryPercentTextView, _speedTextView, _tripOdometerTextView, _voltageTextView, _ampereTextView;
        private ProgressBar _speedProgressBar;
        private ValueAnimator _speedWarningAnimator, _speedTestAnimator;
        private View _speedView;

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            _speedView = inflater.Inflate(Resource.Layout.fragment_speed, container, false);

            _batteryPercentTextView = _speedView.FindViewById<TextView>(Resource.Id.batteryPercentTextView);
            _speedTextView = _speedView.FindViewById<TextView>(Resource.Id.speedTextView);
            _tripOdometerTextView = _speedView.FindViewById<TextView>(Resource.Id.tripOdometerTextView);
            _speedProgressBar = _speedView.FindViewById<ProgressBar>(Resource.Id.speedProgressBar);
            _voltageTextView = _speedView.FindViewById<TextView>(Resource.Id.voltageTextView);
            _ampereTextView = _speedView.FindViewById<TextView>(Resource.Id.ampereTextView);
#if DEBUG
            _voltageTextView.Click += SpeedProgressBar_Click;
#endif
            _speedWarningAnimator = ValueAnimator.OfArgb(ContextCompat.GetColor(Platform.CurrentActivity, Resource.Color.red), ContextCompat.GetColor(Platform.CurrentActivity, Resource.Color.black));
            _speedWarningAnimator.Update += SpeedWarningAnimator_Update;
            _speedWarningAnimator.SetDuration(Prefs.UiUpdateSpeed);
            _speedWarningAnimator.RepeatCount = ValueAnimator.Infinite;
            _speedWarningAnimator.RepeatMode = ValueAnimatorRepeatMode.Restart;

            _uiUpdate = new System.Timers.Timer();
            _uiUpdate.Interval = Prefs.UiUpdateSpeed;
            _uiUpdate.Elapsed += new System.Timers.ElapsedEventHandler(UiUpdateTimer);
            _uiUpdate.Start();

            return _speedView;
        }

        private void SpeedProgressBar_Click(object sender, EventArgs e)
        {
            _speedTestAnimator = ValueAnimator.OfInt(0, 800);
            _speedTestAnimator.Update += SpeedTestAnimator_Update;
            _speedTestAnimator.RepeatCount = 1;
            _speedTestAnimator.RepeatMode = ValueAnimatorRepeatMode.Reverse;
            _speedTestAnimator.SetDuration(9999);
            _speedTestAnimator.Start();
        }

        private void SpeedTestAnimator_Update(object sender, ValueAnimator.AnimatorUpdateEventArgs e)
        {
            int newSpeed = (int)e.Animation.AnimatedValue;
            ((MainActivity)Platform.CurrentActivity).board.ValueChanged(RpmUUID, BitConverter.GetBytes((ushort)newSpeed));
        }

        private void SpeedWarningAnimator_Update(object sender, ValueAnimator.AnimatorUpdateEventArgs e)
        {
            int newColor = (int)e.Animation.AnimatedValue;
            ((MainActivity)Platform.CurrentActivity).RunOnUiThread(() => _speedView.SetBackgroundColor(new Android.Graphics.Color(newColor)));

            if (newColor == ContextCompat.GetColor(Platform.CurrentActivity, Resource.Color.red))
                Vibration.Vibrate(250);
        }

        private void UiUpdateTimer(object sender, ElapsedEventArgs e)
        {
            MainActivity mainActivity = (MainActivity)Platform.CurrentActivity;

            SpannableString text = new SpannableString(string.Format("{0:0.0}{1}", mainActivity.board.Speed, Prefs.UseMetric ? "km/h" : "mph "));
            text.SetSpan(new RelativeSizeSpan((float)0.3), text.Length() - 4, text.Length(), 0);

            mainActivity.RunOnUiThread(() =>
            {
                _batteryPercentTextView.Text = string.Format("{0}%", mainActivity.board.BatteryPercent);
                _speedTextView.TextFormatted = text;
                _speedProgressBar.Progress = (int)(mainActivity.board.Speed * 10 < _speedProgressBar.Max / 2 ? mainActivity.board.Speed * 10 : _speedProgressBar.Max / 2);
                _tripOdometerTextView.Text = string.Format("{0:0.0}{1}", mainActivity.board.TripOdometer, Prefs.UseMetric ? "km" : "mi");
                _voltageTextView.Text = string.Format("{0:0.0}V", mainActivity.board.BatteryVoltage);
                _ampereTextView.Text = string.Format("{0:0.0000}Ah", mainActivity.board.TripAmpHours - mainActivity.board.TripRegenHours);
            });

            if (Prefs.SpeedWarning && mainActivity.board.Speed > Prefs.SpeedWarningScale)
            {
                if (!_speedWarningAnimator.IsStarted)
                    mainActivity.RunOnUiThread(() => _speedWarningAnimator.Start());
            }
            else
            {
                if (_speedWarningAnimator.IsStarted)
                {
                    mainActivity.RunOnUiThread(() =>
                    {
                        _speedWarningAnimator.Cancel();
                        _speedView.SetBackgroundColor(new Android.Graphics.Color(Resource.Color.black));
                    });
                    Vibration.Cancel();
                }
            }

            if (_uiUpdate.Interval != Prefs.UiUpdateSpeed)
                _uiUpdate.Interval = Prefs.UiUpdateSpeed;
        }

        public override void OnPause()
        {
            if (_uiUpdate != null)
            {
                _uiUpdate.Enabled = false;

                if (_speedWarningAnimator.IsStarted)
                {
                    Platform.CurrentActivity.RunOnUiThread(() =>
                    {
                        _speedWarningAnimator.Cancel();
                        _speedView.SetBackgroundColor(new Android.Graphics.Color(Resource.Color.black));
                    });
                    Vibration.Cancel();
                }
            }

            base.OnPause();
        }

        public override void OnResume()
        {
            _speedProgressBar.Max = Prefs.SpeedScale * 10 * 2;

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