using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OneWear
{
    public class SettingsFragment : AndroidX.Fragment.App.Fragment
    {
        private View _prefsView;
        AppCompatSpinner _prefsWheels;

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Create your fragment here
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            // Use this to return your custom view for this Fragment
            _prefsView = inflater.Inflate(Resource.Layout.fragment_settings, container, false);

            _prefsWheels = _prefsView.FindViewById<AppCompatSpinner>(Resource.Id.prefsWheels);

            String[] items = new String[] { "ow12345", "ow01212", "three" };
            //create an adapter to describe how the items are displayed, adapters are used in several places in android.
            //There are multiple variations of this, but this is the basic variant.
            ArrayAdapter<string> adapter = new ArrayAdapter<string>(_prefsView.Context, Resource.Layout.support_simple_spinner_dropdown_item, items);
            //set the spinners adapter to the previously created one.
            _prefsWheels.Adapter = adapter;

            return _prefsView;
        }
    }
}