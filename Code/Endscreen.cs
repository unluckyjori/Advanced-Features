using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedFeatures
{
    // Provides a replacement end of round screen with more information
    [HarmonyPatch]
    public class Endscreen
    {
        static readonly MethodInfo _setTrigger = AccessTools.Method(
    typeof(UnityEngine.Animator),
    nameof(UnityEngine.Animator.SetTrigger),
    new[] { typeof(string) }
);

        private static GameObject Container;
        private static GameObject PerformanceReportPrefab;
        private static GameObject DeadContainerPrefab;
        private static GameObject MissingContainerPrefab;
        private static GameObject NoteContainerPrefab;

        private static Transform AllDead;
        private static Transform PlayerNoteContainer;
        private static Transform DeadNoteContainer;
        private static Transform MissingTitle;
        private static Transform MissingScrollBox;
        private static Transform MissingNoteContainer;
        private static Transform CollectedLabel;
        private static Transform CollectedLine;
        private static TextMeshProUGUI CollectedText;
        private static TextMeshProUGUI TotalText;
        private static Transform ScrapLost;
        private static TextMeshProUGUI ScrapLostText;
        private static TextMeshProUGUI GradeText;
        private static int CollectedScrap;
        private static int TotalScrap;
        private static string Grade;
        private static bool AreAllDead;
        private static int _playerNoteIndex;
        private static int _deadNoteIndex;
        private static int _missingNoteIndex;
        private static readonly WaitForEndOfFrame WaitFrame = new WaitForEndOfFrame();

        private static GameObject GetOrCreate(Transform container, GameObject prefab, ref int index)
        {
            GameObject obj;
            if (index < container.childCount)
            {
                obj = container.GetChild(index).gameObject;
            }
            else
            {
                obj = UnityEngine.Object.Instantiate(prefab, container);
            }
            obj.SetActive(true);
            index++;
            return obj;
        }

        public static void LoadAssets(AssetBundle assets)
        {
            Plugin.Log.LogInfo("Loading Endscreen assets");
            PerformanceReportPrefab = assets.LoadAsset<GameObject>("Assets/Prefabs/UI/PerformanceReport.prefab");
            DeadContainerPrefab = assets.LoadAsset<GameObject>("Assets/Prefabs/UI/DeadContainer.prefab");
            MissingContainerPrefab = assets.LoadAsset<GameObject>("Assets/Prefabs/UI/MissingContainer.prefab");
            NoteContainerPrefab = assets.LoadAsset<GameObject>("Assets/Prefabs/UI/NoteContainer.prefab");
            // load prefabs used by the performance report
            if (PerformanceReportPrefab == null || DeadContainerPrefab == null ||
                MissingContainerPrefab == null || NoteContainerPrefab == null)
            {
                Plugin.Log.LogError("Failed to load one or more Endscreen prefabs");
            }
            else if (Plugin.EnableAdvancedLogging.Value)
            {
                Plugin.Log.LogInfo("Endscreen assets loaded");
                Plugin.Log.LogInfo($"Prefabs: report={PerformanceReportPrefab != null}, dead={DeadContainerPrefab != null}, missing={MissingContainerPrefab != null}, note={NoteContainerPrefab != null}");
            }
        }

        public static void Open()
        {
            if (!Plugin.EnablePerformanceUI.Value)
                return;
            // begin constructing the custom end screen
            Plugin.Log.LogInfo("Opening custom performance report screen");
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Open() was called.");
            try
            {
                Transform t = HUDManager.Instance.endgameStatsAnimator.gameObject.transform;
                for (int m = 0; m < t.childCount; m++)
                {
                    Transform ch = t.GetChild(m);
                    if (ch.name == "Text")
                        ch.gameObject.SetActive(false);
                    if (ch.name == "BGBoxes" || ch.name == "Lines")
                        UnityEngine.Object.Destroy(ch.gameObject);
                }

                bool somebodyMissing = false;
                _playerNoteIndex = 0;
                _deadNoteIndex = 0;
                _missingNoteIndex = 0;

                int c = PlayerNoteContainer.childCount;
                for (int l = 0; l < c; l++)
                    PlayerNoteContainer.GetChild(l).gameObject.SetActive(false);

                c = DeadNoteContainer.childCount;
                for (int k = 0; k < c; k++)
                    DeadNoteContainer.GetChild(k).gameObject.SetActive(false);

                c = MissingNoteContainer.childCount;
                for (int j = 0; j < c; j++)
                    MissingNoteContainer.GetChild(j).gameObject.SetActive(false);

                for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
                {
                    PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
                    if (player.disconnectedMidGame)
                        continue;

                    string username = HUDManager.Instance.statsUIElements.playerNamesText[i].text;
                    Texture2D avatar = null;
                    string notes = HUDManager.Instance.statsUIElements.playerNotesText[i].text;
                    if (notes.StartsWith("Notes:"))
                        notes = notes.Substring(6);
                    int ind = notes.IndexOf("Cause of Death:", StringComparison.OrdinalIgnoreCase);
                    if (ind > -1)
                        notes = notes.Substring(0, ind);
                    notes = notes.Trim();
                    string deathReason;
                    try
                    {
                        if (Chainloader.PluginInfos.TryGetValue("com.elitemastereric.coroner", out var pluginInfo))
                        {
                            try
                            {
                                var apiType = pluginInfo.Instance.GetType().Assembly.GetType("Coroner.AdvancedDeathTracker");
                                if (apiType == null) throw new Exception("Coroner.AdvancedDeathTracker not found");

                                var getMethod = AccessTools.Method(
                                    apiType,
                                    "GetCauseOfDeath",
                                    new[] { typeof(PlayerControllerB), typeof(bool) }
                                );
                                if (getMethod == null)
                                    throw new Exception("GetCauseOfDeath(PlayerControllerB, bool) not found");

                                var nullableCause = getMethod.Invoke(null, new object[] { player, true });

                                var stringifyMethod = AccessTools.Method(
                                    apiType,
                                    "StringifyCauseOfDeath",
                                    new[] { getMethod.ReturnType }
                                );
                                if (stringifyMethod == null)
                                    throw new Exception($"StringifyCauseOfDeath({getMethod.ReturnType.Name}) not found");

                                deathReason = (string)stringifyMethod.Invoke(null, new object[] { nullableCause });

                                // report detailed cause of death when available
                                if (Plugin.EnableAdvancedLogging.Value)
                                    Plugin.Log.LogInfo($"[Coroner] {username} died of: {deathReason}");
                            }
                            catch (Exception e)
                            {
                                // reflection failures are logged so we know what went wrong
                                Plugin.Log.LogError("Coroner reflection failed: " + e);
                                deathReason = player.causeOfDeath.ToString();
                            }
                        }
                        else
                        {
                            deathReason = player.causeOfDeath.ToString();
                            if (Plugin.EnableAdvancedLogging.Value)
                                Plugin.Log.LogInfo($"[Vanilla] {username} died of: {deathReason}");
                        }
                    }
                    catch (Exception e)
                    {
                        // if the coroner API throws, log the issue
                        Plugin.Log.LogError("Coroner test failed: " + e);
                        deathReason = player.causeOfDeath.ToString();
                    }

                    bool isDead = HUDManager.Instance.statsUIElements.playerStates[i].sprite
                                         == HUDManager.Instance.statsUIElements.deceasedIcon;
                    bool isMissing = HUDManager.Instance.statsUIElements.playerStates[i].sprite
                                         == HUDManager.Instance.statsUIElements.missingIcon;

                    if (!string.IsNullOrEmpty(notes) && !isDead && !isMissing)
                        AddPlayerNote(player.playerSteamId, username, notes);
                    if (HUDManager.Instance.statsUIElements.playerStates[i].sprite == HUDManager.Instance.statsUIElements.deceasedIcon)
                        AddDeceasedNote(player.playerSteamId, username, deathReason);
                    if (HUDManager.Instance.statsUIElements.playerStates[i].sprite == HUDManager.Instance.statsUIElements.missingIcon)
                    {
                        somebodyMissing = true;
                        AddMissingNote(player.playerSteamId, username);
                    }
                }

                MissingTitle.gameObject.SetActive(somebodyMissing);
                MissingScrollBox.gameObject.SetActive(somebodyMissing);

                CollectedScrap = RoundManager.Instance.scrapCollectedInLevel;
                TotalScrap = (int)RoundManager.Instance.totalScrapValueInLevel;
                AreAllDead = HUDManager.Instance.statsUIElements.allPlayersDeadOverlay.enabled;
                AllDead.gameObject.SetActive(AreAllDead);

                CollectedText.text = string.Empty;
                TotalText.text = string.Empty;
                CollectedText.gameObject.SetActive(true);
                TotalText.gameObject.SetActive(true);
                CollectedLine.gameObject.SetActive(true);
                CollectedLabel.gameObject.SetActive(true);
                ScrapLost.gameObject.SetActive(false);

                Grade = HUDManager.Instance.statsUIElements.gradeLetter.text;
                GradeText.text = string.Empty;
                Plugin.Log.LogInfo($"Scrap collected: {CollectedScrap}/{TotalScrap} - Grade {Grade}");

                Container.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(Container.GetComponent<RectTransform>());
                HUDManager.Instance.StartCoroutine(AnimateMenu());
                HUDManager.Instance.StartCoroutine(ForceCloseAfterTime(10f));
                if (Plugin.EnableAdvancedLogging.Value)
                    Plugin.Log.LogInfo("End screen animation coroutine started");
                Plugin.Log.LogInfo("Performance report screen displayed");
            }
            catch (Exception e)
            {
                // report any unexpected issue while showing the end screen
                Plugin.Log.LogError("Error occurred while opening end screen!");
                Plugin.Log.LogError(e);
            }
        }

        private static IEnumerator ForceCloseAfterTime(float seconds)
        {
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Force close timer started ({seconds} seconds)");

            yield return new WaitForSeconds(seconds);

            if (Container != null && Container.activeSelf)
            {
                Plugin.Log.LogInfo("Force closing performance report after timeout");

                Container.SetActive(false);

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static IEnumerator AnimateMenu()
        {
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Animating performance report UI");
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = !Plugin.EnablePerformanceReportCameraScroll.Value;

            yield return new WaitForSeconds(1f);

            float p = 0f;
            TotalText.text = TotalScrap.ToString();
            while (p < 1f)
            {
                CollectedText.text = CollectedScrap.ToString();
                p += 0.05f;
                yield return WaitFrame;
            }

            if (AreAllDead)
            {
                yield return new WaitForSeconds(1f);
                CollectedText.gameObject.SetActive(false);
                TotalText.gameObject.SetActive(false);
                CollectedLine.gameObject.SetActive(false);
                CollectedLabel.gameObject.SetActive(false);
                ScrapLostText.text = "Lost 0% scrap";
                ScrapLost.gameObject.SetActive(true);
            }

            yield return new WaitForSeconds(1f);

            GradeText.text = Grade;

            yield return new WaitForSeconds(5.5f - (AreAllDead ? 1f : 0f));

            Container.SetActive(false);

            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Performance report UI closed");

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }


        private static void AddPlayerNote(ulong steamId, string username, string notes)
        {
            GameObject note = GetOrCreate(PlayerNoteContainer, NoteContainerPrefab, ref _playerNoteIndex);
            // record each player note that is created
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Adding player note for {username}");
            Transform playerNameContainer = note.transform.GetChild(0);
            var raw = playerNameContainer.GetChild(0).GetComponent<RawImage>();
            if (Plugin.ShowAvatars.Value)
            {
                HUDManager.FillImageWithSteamProfile(raw, (Steamworks.SteamId)steamId, true);
                raw.gameObject.SetActive(true);
            }
            else
            {
                raw.texture = null;
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
                raw.gameObject.SetActive(false);
            }
            var nameLabel = playerNameContainer.GetChild(1).GetComponent<TextMeshProUGUI>();
            nameLabel.text = username;
            nameLabel.fontSize = 36f;
            note.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = notes;
            note.transform.localScale = Vector3.one;
        }

        private static void AddDeceasedNote(ulong steamId, string username, string deathReason)
        {
            GameObject note = GetOrCreate(DeadNoteContainer, DeadContainerPrefab, ref _deadNoteIndex);
            // add a row for a player that died
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Adding deceased note for {username}");
            Transform playerNameContainer = note.transform.GetChild(0);
            var raw = playerNameContainer.GetChild(0).GetComponent<RawImage>();
            if (Plugin.ShowAvatars.Value)
            {
                HUDManager.FillImageWithSteamProfile(raw, (Steamworks.SteamId)steamId, true);
                raw.gameObject.SetActive(true);
            }
            else
            {
                raw.texture = null;
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
                raw.gameObject.SetActive(false);
            }
            playerNameContainer.GetChild(1).GetComponent<TextMeshProUGUI>().text = username;

            var deathLabel = note.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            deathLabel.text = "* " + deathReason;
            deathLabel.color = new Color32(255, 51, 1, 255);
            deathLabel.fontSize = 21.31f;

            note.transform.localScale = Vector3.one;
        }

        private static void AddMissingNote(ulong steamId, string username)
        {
            GameObject note = GetOrCreate(MissingNoteContainer, MissingContainerPrefab, ref _missingNoteIndex);
            // create a note for a player that did not return
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Adding missing note for {username}");
            Transform playerNameContainer = note.transform.GetChild(0);
            var raw = playerNameContainer.GetChild(0).GetComponent<RawImage>();
            if (Plugin.ShowAvatars.Value)
            {
                HUDManager.FillImageWithSteamProfile(raw, (Steamworks.SteamId)steamId, true);
                raw.gameObject.SetActive(true);
            }
            else
            {
                raw.texture = null;
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
                raw.gameObject.SetActive(false);
            }
            playerNameContainer.GetChild(1).GetComponent<TextMeshProUGUI>().text = username;
            note.transform.localScale = Vector3.one;
        }

        [HarmonyPatch(typeof(HUDManager), "Start")]
        [HarmonyPostfix]
        public static void Attach(HUDManager __instance)
        {
            if (!Plugin.EnablePerformanceUI.Value)
                return;
            // attach the performance report UI to the HUD
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Endscreen.Attach postfix invoked");
            Container = UnityEngine.Object.Instantiate(PerformanceReportPrefab, __instance.endgameStatsAnimator.transform.parent);
            Container.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Container.GetComponent<RectTransform>().sizeDelta = new Vector2(823f, 717f);

            Transform boxes = Container.transform.GetChild(1);
            Transform playerNotes = boxes.GetChild(0);
            Transform deadPlayers = boxes.GetChild(1);
            Transform bottom = Container.transform.GetChild(2);

            AllDead = playerNotes.GetChild(1);
            PlayerNoteContainer = playerNotes.GetChild(2).GetChild(0).GetChild(0);
            DeadNoteContainer = deadPlayers.GetChild(1).GetChild(0).GetChild(0);
            MissingTitle = deadPlayers.GetChild(2);
            MissingScrollBox = deadPlayers.GetChild(3);
            MissingNoteContainer = MissingScrollBox.GetChild(0).GetChild(0);

            var sr = MissingScrollBox.GetComponentInChildren<ScrollRect>();

            Transform collectedBox = bottom.GetChild(0);
            CollectedLabel = collectedBox.GetChild(0);
            CollectedText = collectedBox.GetChild(1).GetComponent<TextMeshProUGUI>();
            CollectedLine = collectedBox.GetChild(2);
            TotalText = collectedBox.GetChild(3).GetComponent<TextMeshProUGUI>();
            ScrapLost = collectedBox.GetChild(4);
            ScrapLostText = ScrapLost.GetChild(0).GetComponent<TextMeshProUGUI>();
            GradeText = bottom.GetChild(1).GetChild(1).GetComponent<TextMeshProUGUI>();

            Container.SetActive(false);
        }


        [HarmonyPatch(typeof(Animator), "SetTrigger", new Type[] { typeof(string) })]
        static class Animator_SetTrigger_Patch
        {
            [HarmonyPostfix]
            static void Postfix(Animator __instance, string name)
            {
                if (!Plugin.EnablePerformanceUI.Value)
                    return;
                if (name == "displayStats"
                 && __instance == HUDManager.Instance.endgameStatsAnimator)
                {
                    // animator signal to show the performance report
                    if (Plugin.EnableAdvancedLogging.Value)
                        Plugin.Log.LogInfo("Animator trigger received; opening end screen");
                    Endscreen.Open();
                }
            }
        }


    }


}
