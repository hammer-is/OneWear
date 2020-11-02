using Android.OS;
using AndroidX.Wear.Ambient;

namespace OneWear
{
    public class MainAmbientCallback : AmbientModeSupport.AmbientCallback
    {
        public override void OnEnterAmbient(Bundle ambientDetails)
        {
            System.Diagnostics.Debug.WriteLine("OnEnterAmbient");
            Prefs.UiUpdateSpeed = 1000;
            base.OnEnterAmbient(ambientDetails);
        }

        public override void OnExitAmbient()
        {
            System.Diagnostics.Debug.WriteLine("OnExitAmbient");
            Prefs.UiUpdateSpeed = 500;
            base.OnExitAmbient();
        }

        public override void OnUpdateAmbient()
        {
            System.Diagnostics.Debug.WriteLine("OnUpdateAmbient");
            base.OnUpdateAmbient();
        }
    }
}


