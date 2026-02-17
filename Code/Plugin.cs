using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AdvancedFeatures
{
    [BepInPlugin("com.example.Advancedfeatures", "Advanced Features", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {

        public static ConfigEntry<bool> EnablePerformanceUI;
        public static ConfigEntry<bool> EnableDeathUI;
        public static ConfigEntry<bool> ShowDeathUsername;
        public static ConfigEntry<float> DeathVoiceSensitivity;
        public static ConfigEntry<float> BounceSmoothness;
        public static ConfigEntry<bool> ShowAvatars;
        public static ConfigEntry<bool> EnableAdvancedLogging;
        public static ConfigEntry<bool> EnablePerformanceReportCameraScroll;
        public static ConfigEntry<float> ForceQuit;
        internal static ManualLogSource Log;
        private Harmony _harmony;
        private AssetBundle _assetBundle;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Initializing Advanced Features plugin");

            EnablePerformanceUI = Config.Bind(
                "General",
                "EnablePerformanceReportUI",
                true,
                "Toggle the custom performance-report UI"
            );

            EnableDeathUI = Config.Bind(
                "General",
                "EnableDeathSpectateUI",
                true,
                "Toggle the custom death-spectate UI"
            );
            ShowDeathUsername = Config.Bind(
                "DeathScreen",
                "ShowUsernameUnderAvatar",
                true,
                "Enable or disable the player?s name under their avatar on the death spectate screen"
            );
            DeathVoiceSensitivity = Config.Bind(
                "DeathScreen",
                "VoiceSensitivity",
                10.0f,
                "How strongly avatars bounce in response to voice"
            );
            BounceSmoothness = Config.Bind(
                "DeathScreen",
                "BounceSmoothness",
                12.0f,
                "How quickly the avatar bounce reacts to voice volume. Higher = snappier bounce."
            );
            ShowAvatars = Config.Bind(
                "Performance Report UI",
                "ShowAvatars",
                false,
                "If true, fetch and display each player's Steam avatar on the performance report."
             );
            EnableAdvancedLogging = Config.Bind(
                "Logging",
                "EnableAdvancedLogging",
                false,
                "If true, logs when the mod does anything"
            );
            EnablePerformanceReportCameraScroll = Config.Bind(
                 "Performance Report UI",
                 "EnableCameraScroll",
                 false,
                 "If true, hides cursor and enables scroll wheel for all lists during performance report"
             );
            ForceQuit = Config.Bind(
                 "Performance Report UI",
                 "ForceQuit",
                 -1f,
                 "Set to -1 to disable, any value above will be a timer for how long the game should wait before quitting. Useful if performance report gets stuck for you."
             );
            if (EnableAdvancedLogging.Value)
                Log.LogInfo("Advanced logging enabled");

            _harmony = new Harmony("com.example.Advancedfeatures");
            _harmony.PatchAll();
            Log.LogInfo("Harmony patches applied");

            string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location)!, "advancedfeaturesassets");
            try
            {
                if (File.Exists(bundlePath))
                {
                    Log.LogInfo("Loading asset bundle for Advanced Features");
                    _assetBundle = AssetBundle.LoadFromFile(bundlePath);
                    Endscreen.LoadAssets(_assetBundle);
                    DeathScreen.LoadAssets(_assetBundle);
                    Log.LogInfo($"Asset bundle has been found at {bundlePath}");
                }
                else
                {
                    Log.LogWarning($"Asset bundle not found at {bundlePath}");
                }
            }
            catch (Exception e)
            {
                Log.LogError("Failed to load asset bundle");
                Log.LogError(e);
            }
        }
    }
}