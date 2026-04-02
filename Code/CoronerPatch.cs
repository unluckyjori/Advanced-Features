using HarmonyLib;
using System.Reflection;
using System;
using GameNetcodeStuff;

namespace AdvancedFeatures
{
    public static class CoronerOverridePerformanceReportPrefixPatch
    {
        private static MethodInfo getCauseOfDeathMethod;
        private static MethodInfo stringifyCauseOfDeathMethod;

        public static void TryPatch(Harmony harmony)
        {
            try
            {
                Type coronerApiType = AccessTools.TypeByName("Coroner.API");
                if (coronerApiType == null)
                {
                    Plugin.Log.LogInfo("Coroner API type not found, skipping Coroner patch");
                    return;
                }

                getCauseOfDeathMethod = AccessTools.Method(coronerApiType, "GetCauseOfDeath", new[] { typeof(PlayerControllerB) });
                stringifyCauseOfDeathMethod = AccessTools.Method(coronerApiType, "StringifyCauseOfDeath");

                MethodInfo original = AccessTools.Method("Coroner.Patch.HUDManagerFillEndGameStatsPatch:OverridePerformanceReport");
                MethodInfo prefix = AccessTools.Method(typeof(CoronerOverridePerformanceReportPrefixPatch), nameof(Prefix));

                if (original == null)
                {
                    Plugin.Log.LogInfo("Could not find Coroner OverridePerformanceReport");
                    return;
                }

                if (prefix == null)
                {
                    Plugin.Log.LogError("Could not find Coroner prefix method");
                    return;
                }

                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                Plugin.Log.LogInfo("Applied Coroner compatibility patch");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to patch Coroner compatibility: " + e);
            }
        }

        public static void Prefix(HUDManager __instance)
        {
            Plugin.Log.LogInfo("Running before Coroner OverridePerformanceReport");

            if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null)
            {
                Plugin.Log.LogError("Coroner prefix: StartOfRound player list not ready");
                return;
            }

            if (getCauseOfDeathMethod == null || stringifyCauseOfDeathMethod == null)
            {
                Plugin.Log.LogError("Coroner methods not initialized");
                return;
            }

            int playerCount = StartOfRound.Instance.allPlayerScripts.Length;
            CoronerPatch.InitializeStorage(playerCount);

            Random syncedRandom = BuildSyncedRandom();

            for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
            {
                try
                {
                    PlayerControllerB playerController = StartOfRound.Instance.allPlayerScripts[playerIndex];
                    if (playerController == null)
                        continue;

                    object causeOfDeath = getCauseOfDeathMethod.Invoke(null, new object[] { playerController });

                    if (causeOfDeath == null)
                        continue;

                    string stringifiedDeath = (string)stringifyCauseOfDeathMethod.Invoke(
                        null,
                        new object[] { causeOfDeath, syncedRandom }
                    );

                    CoronerPatch.CoronerVariableInterceptor(
                        playerIndex,
                        causeOfDeath.ToString(),
                        stringifiedDeath
                    );
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Error collecting Coroner death data for player " + playerIndex + ": " + e);
                }
            }
        }

        static Random BuildSyncedRandom()
        {
            var seed = StartOfRound.Instance.randomMapSeed;
            Plugin.Log.LogDebug("Syncing randomization to map seed: '" + seed + "'");
            return new Random(seed);
        }
    }

    public static class CoronerPatch
    {
        public class StoredDeathData
        {
            public string CauseOfDeath;
            public string StringifiedDeath;
        }

        public static StoredDeathData[] DeathData;

        public static void InitializeStorage(int playerCount)
        {
            DeathData = new StoredDeathData[playerCount];
        }

        public static void CoronerVariableInterceptor(int playerIndex, string causeOfDeath, string stringifiedDeath)
        {
            if (DeathData == null || playerIndex < 0 || playerIndex >= DeathData.Length)
                return;

            DeathData[playerIndex] = new StoredDeathData
            {
                CauseOfDeath = causeOfDeath,
                StringifiedDeath = stringifiedDeath
            };
        }
    }
}