using System;
using System.Collections.Generic;
using System.Text.Json;
using Xamarin.Essentials;

namespace OneWear
{
    static class Prefs
    {
        public static bool UseMetric { get { return Preferences.Get("useMetric", true); } }
        public static int TyreCircumference { get { return int.Parse(Preferences.Get("tyreCircumference", "877")); } } //11" = 877mm
        public static int SpeedScale { get { return Preferences.Get("speedScale", 31); } }
        public static bool SpeedWarning { get { return Preferences.Get("speedWarning", true); } }
        public static int SpeedWarningScale { get { return Preferences.Get("speedWarningScale", 26); } }
        public static SortedDictionary<string, string> BoardMacDictionary
        {
            get { return JsonSerializer.Deserialize<SortedDictionary<string, string>>(Preferences.Get("boardMacDictionary", "{ }")); }
            set { Preferences.Set("boardMacDictionary", JsonSerializer.Serialize(value)); } 
        }
        public static string BoardMac { get { return Preferences.Get("boardMac", ""); } set { Preferences.Set("boardMac", value); } }

        public static int UiUpdateSpeed { get; set; } = 500;
    }
}