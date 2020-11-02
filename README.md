OneWear - Standalone Wear OS App
===========

For use with the [Onewheel](https://onewheel.com/) V1, Plus and XR boards from Future Motion. It supports XR with hardware including 4209. Never XR hardware and Pint require unlock using a private REST API and is not supported.

NOTE: OneWear is not endorsed by or affiliated with Future Motion in any way.

Written in C# with [Xamarin](http://www.xamarin.com) using Visual Studio 2019.

BLE communication is based on work by @beeradmoore in [OWCE](https://github.com/OnewheelCommunityEdition/OWCE_App).

## Features

- No phone needed for the ride :smile:
- Metric/Imperial
- Customizable tyre circumference - used for calculating speed and trip distance
- Customizable Speed Warning - with visual and vibration when exeeding the set limit
- Speed (text+gauge)
- Trip distance
- Amp hours used (shows "Usage"-"Regen" in one field to save screen estate) 
- Battery
- Voltage
- Ride mode
- Pitch/Roll/Yaw
- Cell voltage

![Screenshot1](https://github.com/hammer-is/OneWear/blob/main/OWscreen1.png) ![Screenshot2](https://github.com/hammer-is/OneWear/blob/main/OWscreen2.png)

## How to install on watch
Enable "Developer Mode" on watch

Install using "adb install C:\PathToFile\is.hammer.onewear-Signed.apk"

(if you don't have ADB I recommend https://forum.xda-developers.com/ for install help)
