##How to use the package

1. New a Unity project - Select 3D.

2. Download [GoogleVRForUnity](https://github.com/googlevr/gvr-unity-sdk/raw/master/GoogleVRForUnity.unitypackage) package and import

3. Import AirSig package from Asset Store

4. Open the Demo scene from AirSig/Demo/Scene/

5. Move the Plugins folder from AirSig/Assets/AirSig/Plugins to the project's Assets root

6. In Unity Project window, traverse into /Plugins/Android/lib/ARMv7/ then select libAirSig. In the Inspector panel, make sure the CPU is set to ARMv7 in Platform settings.

7. Repeat step 7 for /Plugins/Android/lib/x86/ but make sure the CPU is set to x86 this time.

8. Switch platform to Android in Build Settings

9. Set Bundle Identifier to the package name you desire.

10. Set Minimum API Level to 23 (Daydream VR requires 23)

11. Tick "Virtual Reality SDKs" and select Daydream

12. Build and Run