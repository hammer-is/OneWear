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
            switch (pos)
            {
                case 0:
                    break;
                case 1:
                    break;
                case 2:
                    break;
            }
            Drawable drawable = _context.GetDrawable(Resource.Drawable.ic_cc_checkmark);
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