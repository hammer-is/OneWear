using Android.Content;
using Android.Graphics.Drawables;
using Java.Lang;
using static AndroidX.Wear.Widget.Drawer.WearableNavigationDrawerView;

namespace OneWear
{
    public class MainNavigationAdapter : WearableNavigationDrawerAdapter
    {
        private Context _context;

        public MainNavigationAdapter(Context context)
        {
            _context = context;
        }

        public override int Count => 3;

        public override Drawable GetItemDrawable(int pos)
        {
            Drawable drawable = null;
            switch (pos)
            {
                case 0:
                    drawable = _context.GetDrawable(Resource.Drawable.ic_settings_black_18dp);
                    break;
                case 1:
                    drawable = _context.GetDrawable(Resource.Drawable.ic_speed_black_18dp);
                    break;
                case 2:
                    drawable = _context.GetDrawable(Resource.Drawable.ic_child_care_black_18dp);
                    break;
            }
            return drawable;
        }

        public override ICharSequence GetItemTextFormatted(int pos)
        {
            switch (pos)
            {
                case 0:
                    return new Java.Lang.String("Settings");
                case 1:
                    return new Java.Lang.String("Speed");
                case 2:
                    return new Java.Lang.String("Diag");
            }
            return new Java.Lang.String("");
        }
    }
}