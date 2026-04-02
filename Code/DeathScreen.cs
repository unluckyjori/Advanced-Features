using System;
using System.Collections.Generic;
using Dissonance;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using SteamImage = Steamworks.Data.Image;
using UIImage = UnityEngine.UI.Image;
using System.Linq;
using AdvancedFeatures;
using BepInEx;

namespace AdvancedFeatures
{
    // Handles the custom spectate view that appears when players die
    [HarmonyPatch]
    public class DeathScreen
    {
        public class SpectatorBox
        {
            public PlayerControllerB Player;
            public GameObject Container;
            public RawImage Avatar;
            public Animator Animator;
            public Text NameText;
            public Texture2D AvatarTexture;
            public float SmoothedVolume;
        }

        private static GameObject PlayerBoxPrefab;
        private static GridLayoutGroup GridLayout;
        private static readonly Dictionary<ulong, SpectatorBox> Spectators = new();
        private static int _prevChildCount = -1;

        public static void LoadAssets(AssetBundle assets)
        {
            Plugin.Log.LogInfo("Loading DeathScreen assets");
            // load the prefab used for each spectator box
            PlayerBoxPrefab = assets.LoadAsset<GameObject>("Assets/Prefabs/UI/PlayerBox.prefab");
            if (PlayerBoxPrefab == null)
            {
                Plugin.Log.LogError("Failed to load PlayerBox prefab for DeathScreen");
            }
            else if (Plugin.EnableAdvancedLogging.Value)
            {
                Plugin.Log.LogInfo("DeathScreen assets loaded");
            }
        }

        [HarmonyPatch(typeof(HUDManager), "Start")]
        [HarmonyPrefix]
        public static void Init(HUDManager __instance)
        {
            if (!Plugin.EnableDeathUI.Value)
                return;
            Plugin.Log.LogInfo("Initializing DeathScreen UI");
            // set up the spectate UI layout when HUD starts
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Initializing DeathScreen");

            RectTransform boxesContainer = __instance.SpectateBoxesContainer.GetComponent<RectTransform>();
            RectTransform spectateUI = boxesContainer.transform.parent.GetComponent<RectTransform>();
            RectTransform deathScreen = spectateUI.transform.parent.GetComponent<RectTransform>();
            spectateUI.anchorMin = Vector2.zero;
            spectateUI.anchorMax = Vector2.one;
            spectateUI.offsetMin = Vector2.zero;
            spectateUI.offsetMax = Vector2.zero;
            deathScreen.anchorMin = Vector2.zero;
            deathScreen.anchorMax = Vector2.one;
            deathScreen.offsetMin = Vector2.zero;
            deathScreen.offsetMax = Vector2.zero;
            boxesContainer.anchorMin = Vector2.zero;
            boxesContainer.anchorMax = new Vector2(1f, 0f);
            boxesContainer.pivot = Vector2.zero;
            boxesContainer.offsetMin = new Vector2(15f, 15f);
            boxesContainer.offsetMax = new Vector2(-15f, 115f);
            GridLayout = boxesContainer.gameObject.AddComponent<GridLayoutGroup>();
            GridLayout.spacing = new Vector2(5f, 0f);
            GridLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            GridLayout.constraintCount = 1;
            GridLayout.startCorner = GridLayoutGroup.Corner.LowerLeft;
            GridLayout.childAlignment = TextAnchor.LowerLeft;
        }

        [HarmonyPatch(typeof(HUDManager), "RemoveSpectateUI")]
        [HarmonyPrefix]
        public static void RemoveSpectateUI()
        {
            if (!Plugin.EnableDeathUI.Value)
                return;

            // clean up existing spectator boxes when leaving
            Plugin.Log.LogInfo("Cleaning up DeathScreen UI");
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Removing DeathScreen spectate UI");

            foreach (var id in Spectators.Keys.ToArray())
                DestroySpectatorBox(id);

            _prevChildCount = -1;
        }

        [HarmonyPatch(typeof(HUDManager), "Update")]
        [HarmonyPrefix]
        private static void Update()
        {
            if (!Plugin.EnableDeathUI.Value)
                return;
            if (StartOfRound.Instance.voiceChatModule == null)
                return;
            // update method drives volume changes and cleanup

            bool refreshed = false;
            List<ulong> remove = new();
            foreach (KeyValuePair<ulong, SpectatorBox> kv in Spectators)
            {
                if (kv.Value.Container == null)
                {
                    remove.Add(kv.Key);
                    continue;
                }

                PlayerControllerB player = kv.Value.Player;
                if (!player.isPlayerControlled && !player.isPlayerDead)
                    continue;

                if (player == GameNetworkManager.Instance.localPlayerController)
                {
                    if (!string.IsNullOrEmpty(StartOfRound.Instance.voiceChatModule.LocalPlayerName))
                    {
                        VoicePlayerState state = StartOfRound.Instance.voiceChatModule.FindPlayer(StartOfRound.Instance.voiceChatModule.LocalPlayerName);
                        if (state != null)
                        {
                            float targetVolume = (StartOfRound.Instance.voiceChatModule.IsMuted || !state.IsSpeaking || state.Amplitude < 0.005f)
                                ? 0f
                                : state.Amplitude * Plugin.DeathVoiceSensitivity.Value;
                            kv.Value.SmoothedVolume = Mathf.Lerp(
                                kv.Value.SmoothedVolume,
                                targetVolume,
                                Time.unscaledDeltaTime * Plugin.BounceSmoothness.Value
                            );
                            kv.Value.Animator.SetFloat("Volume", kv.Value.SmoothedVolume);
                        }
                    }
                }
                else if (player.voicePlayerState == null)
                {
                    if (!refreshed)
                    {
                        refreshed = true;
                        StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                    }
                }
                else
                {
                    VoicePlayerState state2 = player.voicePlayerState;


                    float targetVolume = (!state2.IsSpeaking || state2.IsLocallyMuted || state2.Amplitude < 0.005f)
                        ? 0f
                        : state2.Amplitude / Mathf.Max(state2.Volume, 0.01f) * Plugin.DeathVoiceSensitivity.Value;


                    kv.Value.SmoothedVolume = Mathf.Lerp(
                        kv.Value.SmoothedVolume,
                        targetVolume,
                        Time.unscaledDeltaTime * Plugin.BounceSmoothness.Value
                    );


                    kv.Value.Animator.SetFloat("Volume", kv.Value.SmoothedVolume);
                }
            }

            if (remove.Count > 0)
            {
                foreach (ulong r in remove)
                    DestroySpectatorBox(r);
                UpdateLayoutSize();
            }
        }

        private static void UpdateLayoutSize()
        {
            int childs = GridLayout.transform.childCount - 4;
            if (childs == _prevChildCount || childs <= 0)
                return;
            _prevChildCount = childs;
            Rect rect = GridLayout.GetComponent<RectTransform>().rect;
            float widthPerBox = rect.width / childs;
            widthPerBox -= 5f;
            if (widthPerBox > 70f)
            {
                widthPerBox = 70f;
            }
            GridLayout.cellSize = new Vector2(widthPerBox, widthPerBox);
            // record how many boxes fit after resizing
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Updated layout size: {childs} boxes, width {widthPerBox}");
        }

        [HarmonyPatch(typeof(HUDManager), "UpdateBoxesSpectateUI")]
        [HarmonyPrefix]
        public static bool UpdateBoxes(HUDManager __instance)
        {
            if (!Plugin.EnableDeathUI.Value)
                return true;
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Updating death spectate boxes");
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo("Updating death spectate boxes - Advanced");
            if (PlayerBoxPrefab == null)
            {
                Plugin.Log.LogError("PlayerBox prefab missing, cannot update spectators");
                return true;
            }
            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB playerScript = StartOfRound.Instance.allPlayerScripts[i];
                if (!playerScript.isPlayerDead)
                {
                    if (!playerScript.isPlayerControlled && Spectators.ContainsKey(playerScript.playerClientId))
                    {
                        DestroySpectatorBox(playerScript.playerClientId);
                    }
                    continue;
                }


                if (Spectators.ContainsKey(playerScript.playerClientId))
                {
                    if (Spectators[playerScript.playerClientId].Container == null)
                    {
                        DestroySpectatorBox(playerScript.playerClientId);
                    }
                    else if (!Spectators[playerScript.playerClientId].Container.activeSelf)
                    {
                        Spectators[playerScript.playerClientId].Container.SetActive(true);
                    }
                    continue;
                }

                GameObject obj = UnityEngine.Object.Instantiate(PlayerBoxPrefab, __instance.SpectateBoxesContainer, false);
                // a new box is spawned for a dead player
                if (Plugin.EnableAdvancedLogging.Value)
                    Plugin.Log.LogInfo($"Created spectator box for {playerScript.playerUsername}");
                obj.transform.localScale = Vector3.one;
                obj.SetActive(true);
                SpectatorBox spectatorBox = new()
                {
                    Container = obj,
                    Animator = obj.GetComponent<Animator>(),
                    Player = playerScript,
                    Avatar = obj.transform.GetChild(0).GetChild(2).GetComponent<RawImage>()
                };
                Spectators[playerScript.playerClientId] = spectatorBox;

                if (Plugin.ShowDeathUsername.Value)
                {
                    GameObject nameObject = new GameObject("NameText", typeof(RectTransform));
                    nameObject.transform.SetParent(obj.transform, false);
                    Text txt = nameObject.AddComponent<Text>();

                    txt.text = playerScript.playerUsername;

                    txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    txt.fontSize = 14;
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = new Color32(0xFF, 0x4B, 0x36, 0xFF);
                    
                    txt.resizeTextForBestFit = true;
                    txt.resizeTextMinSize = 8;
                    txt.resizeTextMaxSize = 14;

                    var outline = nameObject.AddComponent<Outline>();
                    outline.effectColor = new Color32(0, 0, 0, 255);
                    outline.effectDistance = new Vector2(0.7f, -0.7f);

                    RectTransform rt = nameObject.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.sizeDelta = new Vector2(0f, 20f);

                    float avatarHeight = spectatorBox.Avatar.rectTransform.rect.height;
                    rt.anchoredPosition = new Vector2(0f, -(avatarHeight + 78f));
                    spectatorBox.NameText = txt;
                }
                if (!GameNetworkManager.Instance.disableSteam)
                    _ = FillImageWithSteamProfile(
                        spectatorBox,
                        (Steamworks.SteamId)playerScript.playerSteamId
                    );
            }
            UpdateLayoutSize();
            return false;
        }

        private static void DestroySpectatorBox(ulong id)
        {
            if (!Spectators.TryGetValue(id, out var box))
            {
                Plugin.Log.LogError($"Tried to destroy missing spectator box {id}");
                return;
            }

            // box is removed when player no longer needs it
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Destroying spectator box for {id}");

            if (box.AvatarTexture != null)
            {
                UnityEngine.Object.Destroy(box.AvatarTexture);
                box.AvatarTexture = null;
            }

            if (box.Container != null)
                UnityEngine.Object.Destroy(box.Container);

            Spectators.Remove(id);
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Spectator box removed. Remaining: {Spectators.Count}");
        }

        private static async Task FillImageWithSteamProfile(SpectatorBox box, Steamworks.SteamId steamId)
        {
            if (!SteamClient.IsValid) return;
            // fetch avatar texture from Steam if possible
            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Loading avatar for {steamId}");
            var steamImg = await SteamFriends.GetLargeAvatarAsync(steamId);
            if (!steamImg.HasValue)
            {
                Plugin.Log.LogError($"Steam avatar not found for {steamId}");
                return;
            }

            int w = (int)steamImg.Value.Width;
            int h = (int)steamImg.Value.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            bool loaded = false;
            var data = steamImg.Value.Data;
            if (data != null && data.Length == w * h * 4)
            {
                try
                {
                    tex.LoadRawTextureData(data);
                    loaded = true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Failed to load avatar texture data: " + e);
                }
            }

            if (!loaded)
            {
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var p = steamImg.Value.GetPixel(x, y);
                        pixels[(h - y - 1) * w + x] = new Color32(p.r, p.g, p.b, p.a);
                    }
                }
                tex.SetPixels32(pixels);
            }

            tex.Apply();

            if (box.AvatarTexture != null)
                UnityEngine.Object.Destroy(box.AvatarTexture);

            box.AvatarTexture = tex;
            box.Avatar.texture = tex;
            box.Avatar.uvRect = new Rect(0f, 1f, 1f, -1f);

            if (Plugin.EnableAdvancedLogging.Value)
                Plugin.Log.LogInfo($"Avatar loaded for {steamId}");

        }


    }

}