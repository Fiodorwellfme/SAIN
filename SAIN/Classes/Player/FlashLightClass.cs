using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using SAIN.Components.PlayerComponentSpace;
using SAIN.SAINComponent;
using UnityEngine;

namespace SAIN.Components;

public class FlashLightClass(PlayerComponent component) : PlayerComponentBase(component)
{
    public event Action<bool> OnLightToggle;

    public event Action<bool> OnLaserToggle;

    public List<TacticalComboVisualController> TacticalDevices { get; private set; }

    public bool UsingLight { get; private set; }
    public bool UsingLaser { get; private set; }

    public bool LaserOnly
    {
        get { return !WhiteLight && !IRLight && (Laser || IRLaser); }
    }

    public bool DeviceActive
    {
        get { return ActiveModes != 0; }
    }

    public bool IRLaser
    {
        get { return (ActiveModes & DeviceMode.IRLaser) != 0; }
    }

    public bool IRLight
    {
        get { return (ActiveModes & DeviceMode.IRLight) != 0; }
    }

    public bool Laser
    {
        get { return (ActiveModes & DeviceMode.VisibleLaser) != 0; }
    }

    public bool WhiteLight
    {
        get { return (ActiveModes & DeviceMode.WhiteLight) != 0; }
    }

    public LightDetectionClass LightDetection { get; } = new LightDetectionClass(component);

    public void Update() { }

    public void CheckDevice()
    {
        CheckUsingLightModes();

        bool wasUsingLight = UsingLight;
        UsingLight = (ActiveModes & (DeviceMode.WhiteLight | DeviceMode.IRLight)) != 0;
        if (wasUsingLight != UsingLight)
        {
            OnLightToggle?.Invoke(UsingLight);
        }

        bool wasUsingLaser = UsingLaser;
        UsingLaser = (ActiveModes & (DeviceMode.VisibleLaser | DeviceMode.IRLaser)) != 0;
        if (wasUsingLaser != UsingLaser)
        {
            OnLaserToggle?.Invoke(UsingLaser);
        }
    }

    private void CheckUsingLightModes()
    {
        ActiveModes = DeviceMode.None;
        Player player = Player;
        if (player == null)
        {
            return;
        }

        if (_tacticalModesField == null)
        {
#if DEBUG
            Logger.LogError("Could find not find _tacticalModesField");
#endif
            return;
        }

        // Get the firearmsController for the player, this will be their IsCurrentEnemy weapon
        Player.FirearmController firearmController = player.HandsController as Player.FirearmController;
        if (firearmController == null)
        {
#if DEBUG
            Logger.LogError("Could find not find firearmController");
#endif
            return;
        }

        // Get the list of tacticalComboVisualControllers for the current weapon (One should exist for every flashlight, laser, or combo device)
        Transform weaponRoot = firearmController.WeaponRoot;
        TacticalDevices = weaponRoot.GetComponentsInChildrenActiveIgnoreFirstLevel<TacticalComboVisualController>();
        if (TacticalDevices == null)
        {
#if DEBUG
            Logger.LogError("Could find not find tacticalComboVisualControllers");
#endif
            return;
        }

        // Loop through all active tactical modes and classify them from the runtime markers
        foreach (TacticalComboVisualController tacticalComboVisualController in TacticalDevices)
        {
            List<Transform> tacticalModes = _tacticalModesField(tacticalComboVisualController);
            if (tacticalModes == null)
            {
                continue;
            }

            foreach (var mode in tacticalModes)
            {
                if (mode == null || !mode.gameObject.activeInHierarchy)
                {
                    continue;
                }

                ActiveModes |= ClassifyActiveMode(mode, player);
            }
        }
    }

    private static DeviceMode ClassifyActiveMode(Transform mode, Player player)
    {
        DeviceMode result = DeviceMode.None;

        for (int i = 0; i < mode.childCount; i++)
        {
            Transform child = mode.GetChild(i);
            if (child == null)
            {
                continue;
            }

            string name = child.name;
            if (name.StartsWith("light_", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (_debugMode)
                {
                    Logger.LogDebug($"[{player.name}] Found WhiteLight : Mode:{mode.name} Name:{name}");
                }
#endif
                result |= DeviceMode.WhiteLight;
            }
            else if (name.StartsWith("vis_", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (_debugMode)
                {
                    Logger.LogDebug($"[{player.name}] Found VisibleLaser : Mode:{mode.name} Name:{name}");
                }
#endif
                result |= DeviceMode.VisibleLaser;
            }
            else if (name.StartsWith("il_", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (_debugMode)
                {
                    Logger.LogDebug($"[{player.name}] Found IRLight : Mode:{mode.name} Name:{name}");
                }
#endif
                result |= DeviceMode.IRLight;
            }
            else if (name.StartsWith("ir_", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (_debugMode)
                {
                    Logger.LogDebug($"[{player.name}] Found IRLaser : Mode:{mode.name} Name:{name}");
                }
#endif
                result |= DeviceMode.IRLaser;
            }
        }

        LaserBeam[] lasers = mode.GetComponentsInChildren<LaserBeam>(true);
        for (int i = 0; i < lasers.Length; i++)
        {
            LaserBeam laser = lasers[i];
            if (laser == null || !laser.isActiveAndEnabled)
            {
                continue;
            }

            string beamMaterial = laser.BeamMaterial != null ? laser.BeamMaterial.name : string.Empty;
            string pointMaterial = laser.PointMaterial != null ? laser.PointMaterial.name : string.Empty;
            bool irLaser = beamMaterial.IndexOf("ik", StringComparison.OrdinalIgnoreCase) >= 0
                || pointMaterial.IndexOf("ik", StringComparison.OrdinalIgnoreCase) >= 0;

#if DEBUG
            if (_debugMode)
            {
                Logger.LogDebug(
                    $"[{player.name}] Found {(irLaser ? "IRLaser" : "VisibleLaser")} : " +
                    $"Mode:{mode.name} BeamMat:{beamMaterial} PointMat:{pointMaterial}"
                );
            }
#endif

            result |= irLaser ? DeviceMode.IRLaser : DeviceMode.VisibleLaser;
        }

        return result;
    }

    private static bool _debugMode
    {
        get { return SAINPlugin.LoadedPreset.GlobalSettings.General.Flashlight.DebugFlash; }
    }

    public DeviceMode ActiveModes { get; set; }

    private static readonly AccessTools.FieldRef<TacticalComboVisualController, List<Transform>> _tacticalModesField =
        AccessTools.FieldRefAccess<TacticalComboVisualController, List<Transform>>("list_0");
}
