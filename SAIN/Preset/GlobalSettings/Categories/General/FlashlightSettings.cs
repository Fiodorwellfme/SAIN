using SAIN.Attributes;

namespace SAIN.Preset.GlobalSettings;

public class FlashlightSettings : SAINSettingsBase<FlashlightSettings>, ISAINSettings
{
    [MinMax(0.25f, 10f, 100f)]
    public float DazzleEffectiveness = 3f;

    [MinMax(0f, 60f)]
    public float MaxDazzleRange = 40f;

    public bool AllowLightOnForDarkBuildings = true;

    public bool TurnLightOffNoEnemyPMC = true;

    public bool TurnLightOffNoEnemySCAV = false;

    public bool TurnLightOffNoEnemyGOONS = true;

    public bool TurnLightOffNoEnemyBOSS = false;

    public bool TurnLightOffNoEnemyFOLLOWER = false;

    public bool TurnLightOffNoEnemyRAIDERROGUE = false;

    [Description("Angle in degrees in which a bot can notice a flashlight.")]
    [MinMax(0f, 360f, 1f)]
    public float InvestigateFOVThreshold = 120f;

    [Description("Time it takes for a bot to notice a tactical device (laser/flashlight.")]
    [MinMax(0f, 10f, 100f)]
    public float InvestigateVisibleTimeRequired = 2f;

    [Description("Spacing of the hitpoints along the laser raycast.")]
    [MinMax(0.5f, 25f, 100f)]
    public float LaserPathPointSpacing = 5f;

    [Advanced]
    public bool DebugFlash = false;

    public bool SillyMode = false;
}
