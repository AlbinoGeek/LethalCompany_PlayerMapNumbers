using BepInEx;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Rethunk.LC.RadarIdentQuickSwitch;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static BepInEx.Logging.ManualLogSource LogSource { get; private set; }

    #region Configuration
    public static BepInEx.Configuration.ConfigFile config;

    public enum IdentType
    {
        Numeric = 0,
        LatinAlphabet,
        GreekAlphabet,
        PhoneticNATO,
        PhoneticIPA,
    }

    public static BepInEx.Configuration.ConfigEntry<bool> configGeneralEnabled;
    public static BepInEx.Configuration.ConfigEntry<bool> configIdentBackfill;
    public static BepInEx.Configuration.ConfigEntry<int> configIdentLength;
    public static BepInEx.Configuration.ConfigEntry<bool> configIdentSequential;
    public static BepInEx.Configuration.ConfigEntry<string> configIdentStart;
    public static BepInEx.Configuration.ConfigEntry<string> configIdentType;

    private static void LoadConfig()
    {
        configGeneralEnabled = config.Bind<bool>(
            new BepInEx.Configuration.ConfigDefinition(
                "General",
                "Enabled"
            ),
            true,
            new BepInEx.Configuration.ConfigDescription(
                "Whether or not to enable the plugin. If disabled, <strong>None</strong> of the following options do anything.",
                new BepInEx.Configuration.AcceptableValueList<bool>(true, false)
            )
        );

        configIdentBackfill = config.Bind<bool>(
            new BepInEx.Configuration.ConfigDefinition(
                "Identifiers",
                "Backfill"
            ),
            true,
            new BepInEx.Configuration.ConfigDescription(
                "Whether or not to backfill the identifiers of players that join mid-round.\n\nIf false, idents are not re-used.",
                new BepInEx.Configuration.AcceptableValueList<bool>(true, false)
            )
        );

        configIdentLength = config.Bind<int>(
            new BepInEx.Configuration.ConfigDefinition(
                "Identifiers",
                "Length"
            ),
            3,
            new BepInEx.Configuration.ConfigDescription(
                "The length of the identifiers to use.\n\nIf sequential is enabled, this is the maximum length of the identifier.\n\nIf sequential is disabled, this is the exact length of the identifier.",
                new BepInEx.Configuration.AcceptableValueRange<int>(1, 10)
            )
        );

        configIdentSequential = config.Bind<bool>(
            new BepInEx.Configuration.ConfigDefinition(
                "Identifiers",
                "Sequential"
            ),
            true,
            new BepInEx.Configuration.ConfigDescription(
                "Whether or not to use sequential identifiers.\n\nIf true, identifiers will be assigned in order of joining.\n\nIf false, identifiers will be assigned randomly.",
                new BepInEx.Configuration.AcceptableValueList<bool>(true, false)
            )
        );

        configIdentStart = config.Bind<string>(
            new BepInEx.Configuration.ConfigDefinition(
                "Identifiers",
                "Start"
            ),
            "0",
            new BepInEx.Configuration.ConfigDescription(
                "The first identifier to use.\n\nIf sequential is enabled, this is the first identifier to use.\n\nIf sequential is disabled, this is the only identifier to use.",
                new BepInEx.Configuration.AcceptableValueList<string>(
                    "0 (Numeric)", "1 (Numeric)",
                    "A (Latin)",
                    "α (Greek)",
                    "Alpha (NATO)",
                    "Alfa (IPA)"
                )
            )
        );

        configIdentType = config.Bind<string>(
            new BepInEx.Configuration.ConfigDefinition(
                "Identifiers",
                "Type"
            ),
            "Numeric",
            new BepInEx.Configuration.ConfigDescription(
                "The type of identifier to use.\n\nIf sequential is enabled, this is the type of identifier to use.\n\nIf sequential is disabled, this is ignored.",
                new BepInEx.Configuration.AcceptableValueList<string>(
                    "Numeric",
                    "Latin Alphabet",
                    "Greek Alphabet",
                    "Phonetic (NATO)",
                    "Phonetic (IPA)"
                )
            )
        );
    }
    #endregion

    #region Unity Methods
    private void Awake()
    {
        config = Config;
        LogSource = Logger;
        LoadConfig();

        LogSource.LogMessage("Applying patches to ManualCameraRenderer...");
        On.ManualCameraRenderer.Awake += ManualCameraRenderer_Awake;
        On.ManualCameraRenderer.AddTransformAsTargetToRadar += ManualCameraRenderer_AddTransformAsTargetToRadar;
        On.ManualCameraRenderer.RemoveTargetFromRadar += ManualCameraRenderer_RemoveTargetFromRadar;

        LogSource.LogMessage("Applying patches to PlayerControllerB...");
        On.GameNetcodeStuff.PlayerControllerB.KillPlayerClientRpc += PlayerControllerB_KillPlayerClientRpc;
        On.GameNetcodeStuff.PlayerControllerB.KillPlayerServerRpc += PlayerControllerB_KillPlayerServerRpc;
        On.GameNetcodeStuff.PlayerControllerB.SendNewPlayerValuesClientRpc += PlayerControllerB_SendNewPlayerValuesClientRpc;
        On.GameNetcodeStuff.PlayerControllerB.SendNewPlayerValuesServerRpc += PlayerControllerB_SendNewPlayerValuesServerRpc;
        On.GameNetcodeStuff.PlayerControllerB.SpawnDeadBody += PlayerControllerB_SpawnDeadBody;

        LogSource.LogMessage("Applying patch to Terminal...");
        On.Terminal.ParsePlayerSentence += Terminal_ParsePlayerSentence;

        LogSource.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} was loaded!");
    }

    private void OnDestroy()
    {
        LogSource.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} was unloaded!");
    }
    #endregion

    #region Radar Identification Logic
    // tracks which GameObject belongs to which terminal quickswitch identifier
    private static readonly Dictionary<GameObject, string> playerAssignments = [];
    private static readonly Dictionary<string, GameObject> playerMapLabels = [];

    internal static void TrackAllRadarTargets()
    {
        // If we're disabled, don't do it.
        if (!configGeneralEnabled.Value) return;

        var mapScreen = StartOfRound.Instance?.mapScreen;
        if (mapScreen == null) return;

        for (int i = 0; i < mapScreen.radarTargets.Count; ++i)
        {
            if (mapScreen.radarTargets[i]?.transform != null)
            {
                StartTracking(mapScreen.radarTargets[i].transform.gameObject);
            }
        }
    }

    internal static void StartTracking(GameObject target)
    {
        // If we're disabled, don't do it.
        if (!configGeneralEnabled.Value) return;

        if (target == null) return;
        GameObject mapDot = FindMapDot(target);
        if (mapDot == null) return;

        string identifier;
        TextMeshPro tmpText;

        // If we're not yet tracking the player, give them the next available identifier
        if (!playerAssignments.ContainsKey(target))
        {
            int nextAvailable = 0;
            while (playerAssignments.ContainsValue(nextAvailable.ToString())) nextAvailable++;
            playerAssignments.Add(target, nextAvailable.ToString());
        }

        identifier = playerAssignments[target];
        if (playerMapLabels.ContainsKey(identifier))
        {
            tmpText = playerMapLabels[identifier].GetComponent<TextMeshPro>();
            tmpText.text = identifier;
            return;
        }

        GameObject mapLabel = new();
        mapLabel.transform.SetParent(mapDot.transform, false);
        mapLabel.transform.SetLocalPositionAndRotation(new Vector3(0, 0.45f, 0), Quaternion.Euler(new Vector3(90, 0, 0)));
        mapLabel.transform.localScale = Vector3.one * 0.45f;
        mapLabel.layer = mapDot.layer;
        mapLabel.name = "MapNumber";
        mapLabel.AddComponent<Billboard>();

        tmpText = mapLabel.AddComponent<TextMeshPro>();
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.autoSizeTextContainer = false;
        tmpText.color = Color.green;
        tmpText.maxVisibleLines = 1;
        tmpText.maxVisibleWords = 1;
        tmpText.text = identifier;

        playerMapLabels.Add(identifier, mapLabel);
    }

    internal static void StopTracking(GameObject target)
    {
        // If we're not tracking any player, we don't need to do anything.
        if (!playerAssignments.TryGetValue(target, out string ident)) return;

        // If the player has a map label, destroy it and forget about it.
        if (playerMapLabels.TryGetValue(ident, out GameObject label))
        {
            playerMapLabels.Remove(ident);
            if (label != null) Destroy(label);
        }

        // Finally, forget about the player assignment itself.
        playerAssignments.Remove(target);
    }

    private static GameObject FindMapDot(GameObject target)
    {
        GameObject dot = null;

        var player = target.GetComponent<PlayerControllerB>();
        if (player != null)
        {
            // Dead Players direct the screen to their killer OR their body
            if (player.isPlayerDead)
            {
                if (player.redirectToEnemy != null)
                {
                    dot = player.redirectToEnemy.transform.Find("Misc")?.Find("MapDot")?.gameObject;
                }
                else if (player.deadBody != null)
                {
                    dot = player.deadBody.transform.Find("MapDot")?.gameObject;
                }
            }
            else
            {
                dot = target.transform.Find("Misc")?.Find("MapDot")?.gameObject;
            }
        }

        if (dot == null)
        {
            // At this point it might be a Radar Booster
            dot = target.transform.Find("RadarBoosterDot")?.gameObject;
        }

        return dot;
    }
    #endregion

    #region Hooked (MonoMod) Methods
    private static void ManualCameraRenderer_Awake(
        On.ManualCameraRenderer.orig_Awake orig,
        ManualCameraRenderer self
    )
    {
        orig(self);

        if (self.NetworkManager == null || !self.NetworkManager.IsListening)
            return;

        TrackAllRadarTargets();
    }

    private static string ManualCameraRenderer_AddTransformAsTargetToRadar(
        On.ManualCameraRenderer.orig_AddTransformAsTargetToRadar orig,
        ManualCameraRenderer self,
        Transform newTargetTransform,
        string targetName,
        bool isNonPlayer
    )
    {
        string result = orig(self, newTargetTransform, targetName, isNonPlayer);

        StartTracking(newTargetTransform.gameObject);

        return result;
    }

    private static void ManualCameraRenderer_RemoveTargetFromRadar(
        On.ManualCameraRenderer.orig_RemoveTargetFromRadar orig,
        ManualCameraRenderer self,
        Transform removeTransform
    )
    {
        orig(self, removeTransform);

        StopTracking(removeTransform.gameObject);
    }

    private void PlayerControllerB_KillPlayerServerRpc(
        On.GameNetcodeStuff.PlayerControllerB.orig_KillPlayerServerRpc orig,
        PlayerControllerB self,
        int playerId,
        bool spawnBody,
        Vector3 bodyVelocity,
        int causeOfDeath,
        int deathAnimation
    )
    {
        orig(self, playerId, spawnBody, bodyVelocity, causeOfDeath, deathAnimation);

        // TODO: Downgrade to just modifying the label of the player that died
        TrackAllRadarTargets();
    }

    private void PlayerControllerB_KillPlayerClientRpc(
        On.GameNetcodeStuff.PlayerControllerB.orig_KillPlayerClientRpc orig,
        PlayerControllerB self,
        int playerId,
        bool spawnBody,
        Vector3 bodyVelocity,
        int causeOfDeath,
        int deathAnimation
    )
    {
        orig(self, playerId, spawnBody, bodyVelocity, causeOfDeath, deathAnimation);

        // TODO: Downgrade to just modifying the label of the player that died
        TrackAllRadarTargets();
    }

    private void PlayerControllerB_SendNewPlayerValuesServerRpc(
        On.GameNetcodeStuff.PlayerControllerB.orig_SendNewPlayerValuesServerRpc orig,
        PlayerControllerB self,
        ulong newPlayerSteamId
    )
    {
        orig(self, newPlayerSteamId);

        // TODO: Downgrade to just modifying the label of the player that joined
        TrackAllRadarTargets();
    }

    private void PlayerControllerB_SendNewPlayerValuesClientRpc(
        On.GameNetcodeStuff.PlayerControllerB.orig_SendNewPlayerValuesClientRpc orig,
        PlayerControllerB self,
        ulong[] playerSteamIds
    )
    {
        orig(self, playerSteamIds);

        // TODO: Downgrade to just modifying the label of the player that joined
        TrackAllRadarTargets();
    }

    private void PlayerControllerB_SpawnDeadBody(
        On.GameNetcodeStuff.PlayerControllerB.orig_SpawnDeadBody orig,
        PlayerControllerB self,
        int playerId,
        Vector3 bodyVelocity,
        int causeOfDeath,
        PlayerControllerB deadPlayerController,
        int deathAnimation,
        Transform overridePosition
    )
    {
        orig(self, playerId, bodyVelocity, causeOfDeath, deadPlayerController, deathAnimation, overridePosition);

        // TODO: Downgrade to just modifying the label of the player that died
        TrackAllRadarTargets();
    }

    private static string RemovePunctuation(string s)
    {
        System.Text.StringBuilder stringBuilder = new();
        foreach (char c in s)
        {
            if (!char.IsPunctuation(c))
                stringBuilder.Append(c);
        }
        return stringBuilder.ToString().ToLower();
    }

    private TerminalNode Terminal_ParsePlayerSentence(
        On.Terminal.orig_ParsePlayerSentence orig,
        Terminal self
    )
    {
        TerminalNode result = orig(self);

        // 10, 11, 12 parse errors
        if (result != self.terminalNodes.specialNodes[10]) return result;

        string str1 = RemovePunctuation(self.screenText.text.Substring(self.screenText.text.Length - self.textAdded));
        string[] strArray = str1.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        if (strArray.Length == 1 && int.TryParse(strArray[0], out int outputNum))
        {
            int playerIndex = outputNum;
            if (playerIndex < StartOfRound.Instance.mapScreen.radarTargets.Count)
            {
                var controller = StartOfRound.Instance.mapScreen.radarTargets[playerIndex].transform.gameObject.GetComponent<PlayerControllerB>();
                if (controller != null && !controller.isPlayerControlled && !controller.isPlayerDead && controller.redirectToEnemy == null)
                {
                    return null;
                }
                StartOfRound.Instance.mapScreen.SwitchRadarTargetAndSync(playerIndex);

                return self.terminalNodes.specialNodes[20];
            }
        }

        return result;
    }
    #endregion
}

public class Billboard : MonoBehaviour
{
    public void Update()
    {
        // Forces the object to rotate "North" respective to the Manual Camera Renderer
        gameObject.transform.rotation = Quaternion.Euler(90, -45, 0);
    }
}