using System;
using System.Collections;
using System.Collections.Generic;
using SAIN.Components.PlayerComponentSpace;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.Types.Jobs;
using Unity.Collections;
using UnityEngine;

namespace SAIN.Components;

public class FlashlightRaycastJob : SainJobTemplate, IDisposable
{
    private const float InvestigateVisibleTickTime = 0.1f;
    private const float LaserTraceDistance = 75;

    private const float Wide_FlashLightBeamAngle = 16f;
    private const int Wide_FlashlightBeamPointCount = 32;
    private const float Wide_FlashlightTraceDistance = 30;

    private const float Tight_FlashLightBeamAngle = 8f;
    private const int Tight_FlashlightBeamPointCount = 16;
    private const float Tight_FlashlightTraceDistance = 60;

    public FlashlightRaycastJob(MonoBehaviour gameWorld)
        : base("Flashlight Detection Job", gameWorld, true, 0.1f)
    {
        GenerateRandomYawPitchRotationsNonAlloc(_rotationsList_Wide, Wide_FlashlightBeamPointCount, Wide_FlashLightBeamAngle);
        GenerateRandomYawPitchRotationsNonAlloc(_rotationsList_Tight, Tight_FlashlightBeamPointCount, Tight_FlashLightBeamAngle);
    }

    protected readonly List<RaycastJob> RaycastJobs = [];
    protected readonly List<Quaternion> _rotationsList_Wide = [];
    protected readonly List<Quaternion> _rotationsList_Tight = [];
    private readonly Dictionary<LightExposureKey, float> _visibleExposureTimes = [];
    private readonly HashSet<LightExposureKey> _currentExposureKeys = [];

    protected override IEnumerator PrimaryFunction()
    {
        CreateFlashlightJobs();
        int Total = RaycastJobs.Count;
        if (Total > 0)
        {
            ScheduleJobs(Total);
            yield return null;
            ReadFlashlightJobData(Total);
            Dispose();

            CreateLightDetectionJobs();
            Total = RaycastJobs.Count;
            if (Total > 0)
            {
                ScheduleJobs(Total);
                yield return null;
                ReadLightDetectionJobData(Total);
                Dispose();
            }
            else
            {
                _visibleExposureTimes.Clear();
                _currentExposureKeys.Clear();
            }
        }
    }

    private void CreateFlashlightJobs()
    {
        List<RandomDir> Directions = _directionsList;
        HashSet<PlayerComponent> players = GameWorldComponent.Instance.PlayerTracker.AlivePlayerArray;
        foreach (var player in players)
        {
            if (player != null && player.IsActive && player.Flashlight.DeviceActive)
            {
                Vector3 WeaponPointDir = player.Transform.WeaponData.PointDirection;
                Vector3 WeaponFirePort = player.Transform.WeaponData.FirePort;
                if (player.Flashlight.Laser || player.Flashlight.IRLaser)
                {
                    Directions.Add(new(LaserTraceDistance, WeaponPointDir));
                }
                if (player.Flashlight.WhiteLight || player.Flashlight.IRLight)
                {
                    CreateFlashlightBeam(Directions, _rotationsList_Wide, WeaponPointDir, Wide_FlashlightTraceDistance);
                    CreateFlashlightBeam(Directions, _rotationsList_Tight, WeaponPointDir, Tight_FlashlightTraceDistance);
                }
                if (Directions.Count > 0)
                {
                    RaycastJobs.Add(
                        new RaycastJob(
                            Directions,
                            WeaponFirePort,
                            LayerMaskClass.HighPolyWithTerrainMaskAI,
                            player.Player,
                            null
                        )
                    );
                    Directions.Clear();
                }
            }
        }
    }

    private void ReadFlashlightJobData(int Total)
    {
        for (int i = 0; i < Total; i++)
        {
            RaycastJob Job = RaycastJobs[i];
            Job.Complete();
            NativeArray<RaycastHit> Hits = Job.Hits;
            if (GameWorldComponent.TryGetPlayerComponent(Job.Owner, out PlayerComponent Player))
            {
                List<Vector3> LightPoints = Player.Flashlight.LightDetection.LightPoints;
                LightPoints.Clear();
                AddLaserPathPoints(Player, Hits, LightPoints);
                for (int j = Hits.Length - 1; j >= 0; j--)
                {
                    RaycastHit Hit = Hits[j];
                    if (Hit.collider != null)
                    {
                        // Offset the hit point slightly away from the thing it hit to allow easy visibilty checking and simulate "glow"
                        LightPoints.Add(Hit.point + (Hit.normal * 0.05f));
                    }
                }

                //if (Player.Player.IsYourPlayer)
                //{
                //    Logger.LogDebug($"player has {LightPoints.Count} light points");
                //    foreach (var point in LightPoints)
                //    {
                //        DebugGizmos.Line(Player.Transform.WeaponFirePort, point, 0.025f, 0.02f, true);
                //    }
                //}
            }
        }
    }

    private static void AddLaserPathPoints(PlayerComponent player, NativeArray<RaycastHit> hits, List<Vector3> lightPoints)
    {
        if (player == null || (!player.Flashlight.Laser && !player.Flashlight.IRLaser))
        {
            return;
        }

        Vector3 origin = player.Transform.WeaponData.FirePort;
        Vector3 direction = player.Transform.WeaponData.PointDirection;
        if (direction == Vector3.zero)
        {
            return;
        }

        float maxDistance = LaserTraceDistance;
        if (hits.Length > 0 && hits[0].collider != null)
        {
            maxDistance = Mathf.Min(LaserTraceDistance, Vector3.Distance(origin, hits[0].point));
        }

        float spacing = GetLaserPathPointSpacing();
        for (float distance = spacing; distance < maxDistance; distance += spacing)
        {
            lightPoints.Add(origin + direction * distance);
        }
    }

    private void CreateLightDetectionJobs()
    {
        _currentExposureKeys.Clear();
        HashSet<PlayerComponent> players = GameWorldComponent.Instance.PlayerTracker.AlivePlayerArray;
        foreach (BotComponent Bot in AliveBots.Values)
        {
            if (Bot == null || !Bot.BotActive)
            {
                continue;
            }

            foreach (PlayerComponent player in players)
            {
                if (
                    player == null
                    || !player.IsActive
                    || !player.Flashlight.DeviceActive
                    || player.Player == null
                    || Bot.Player == null
                    || player.ProfileId == Bot.ProfileId
                    || Bot.EnemyController.IsPlayerFriendly(player.Player)
                )
                {
                    continue;
                }

                float sqrDistance = (player.Position - Bot.Position).sqrMagnitude;
                if (sqrDistance > 125f * 125f)
                {
                    continue;
                }

                FlashLightClass playerLight = player.Flashlight;
                if (!Bot.PlayerComponent.Flashlight.LightDetection.CheckIsBeamVisible(playerLight))
                {
                    continue;
                }

                _currentExposureKeys.Add(new LightExposureKey(Bot.ProfileId, player.ProfileId));
                RaycastJobs.Add(
                    new RaycastJob(
                        playerLight.LightDetection.LightPoints,
                        Bot.Transform.EyePosition,
                        LayerMaskClass.HighPolyWithTerrainMaskAI,
                        Bot.Player,
                        player.Player
                    )
                );
            }
        }
    }

    private void ReadLightDetectionJobData(int Total)
    {
        for (int i = 0; i < Total; i++)
        {
            RaycastJob Job = RaycastJobs[i];
            Job.Complete();
            NativeArray<RaycastHit> Hits = Job.Hits;
            if (GameWorldComponent.TryGetPlayerComponent(Job.Owner, out PlayerComponent Player))
            {
                if (Job.Target == null)
                {
                    continue;
                }

                bool VisiblePoint = false;
                List<Vector3> points = Job.Points;
                Vector3 eyePosition = Player.Transform.EyePosition;
                Vector3 lookDirection = Player.Transform.LookDirection;
                LightExposureKey key = new(Player.ProfileId, Job.Target.ProfileId);
                for (int j = 0; j < Hits.Length; j++)
                {
                    RaycastHit Hit = Hits[j];
                    if (Hit.collider == null)
                    {
                        if (points == null || j >= points.Count)
                        {
                            VisiblePoint = true;
                            break;
                        }

                        Vector3 dirToPoint = (points[j] - eyePosition).normalized;
                        if (Vector3.Dot(lookDirection, dirToPoint) >= GetInvestigateFOVDotThreshold())
                        {
                            VisiblePoint = true;
                            break;
                        }
                    }
                }

                if (!VisiblePoint)
                {
                    _visibleExposureTimes.Remove(key);
                    continue;
                }

                float visibleTime = 0f;
                _visibleExposureTimes.TryGetValue(key, out visibleTime);
                visibleTime += InvestigateVisibleTickTime;
                _visibleExposureTimes[key] = visibleTime;

                if (visibleTime >= GetInvestigateVisibleTimeRequired())
                {
                    Player.Flashlight.LightDetection.TryToInvestigate(Job.Target);
                }
            }
        }

        ResetExpiredExposureKeys();
    }

    private readonly List<RandomDir> _directionsList = [];

    private static SAIN.Preset.GlobalSettings.FlashlightSettings FlashlightSettings
    {
        get { return SAINPlugin.LoadedPreset.GlobalSettings.General.Flashlight; }
    }

    private static float GetInvestigateFOVDotThreshold()
    {
        float totalFovDegrees = Mathf.Clamp(FlashlightSettings.InvestigateFOVThreshold, 0f, 360f);
        float halfFovRadians = (totalFovDegrees * 0.5f) * Mathf.Deg2Rad;
        return Mathf.Cos(halfFovRadians);
    }

    private static float GetInvestigateVisibleTimeRequired()
    {
        return FlashlightSettings.InvestigateVisibleTimeRequired;
    }

    private static float GetLaserPathPointSpacing()
    {
        return FlashlightSettings.LaserPathPointSpacing;
    }

    private void ResetExpiredExposureKeys()
    {
        if (_visibleExposureTimes.Count == 0)
        {
            return;
        }

        _keysToReset.Clear();
        foreach (LightExposureKey key in _visibleExposureTimes.Keys)
        {
            if (!_currentExposureKeys.Contains(key))
            {
                _keysToReset.Add(key);
            }
        }

        for (int i = 0; i < _keysToReset.Count; i++)
        {
            _visibleExposureTimes.Remove(_keysToReset[i]);
        }
    }

    private readonly List<LightExposureKey> _keysToReset = [];

    private void ScheduleJobs(int Total)
    {
        for (int i = 0; i < Total; i++)
        {
            RaycastJobs[i].Schedule();
        }
    }

    /// <summary>
    /// Generates a list of random rotations with given angle.
    /// </summary>
    /// <param name="count">Number of quaternions to generate.</param>
    /// <param name="maxYaw">Max yaw in degrees (horizontal rotation around Y).</param>
    /// <param name="maxPitch">Max pitch in degrees (vertical rotation around right axis).</param>
    /// <returns>List of Quaternion rotations.</returns>
    public static void GenerateRandomYawPitchRotationsNonAlloc(List<Quaternion> nonAllocList, int count, float coneAngle)
    {
        for (int i = 0; i < count; i++)
        {
            float yaw = UnityEngine.Random.Range(-coneAngle, coneAngle); // Y axis
            float pitch = UnityEngine.Random.Range(-coneAngle, coneAngle); // X axis
            float roll = UnityEngine.Random.Range(-coneAngle, coneAngle); // Z axis

            nonAllocList.Add(Quaternion.Euler(pitch, yaw, roll)); // (X, Y, Z) = (Pitch, Yaw, Roll)
        }
    }

    private static void CreateFlashlightBeam(
        List<RandomDir> beamDirections,
        List<Quaternion> rotationsList,
        Vector3 weaponPointDir,
        float distance
    )
    {
        for (int i = 0; i < rotationsList.Count; i++)
        {
            beamDirections.Add(new(distance, (rotationsList[i] * weaponPointDir).normalized));
        }
    }

    private readonly struct LightExposureKey : IEquatable<LightExposureKey>
    {
        public LightExposureKey(string botProfileId, string sourceProfileId)
        {
            BotProfileId = botProfileId;
            SourceProfileId = sourceProfileId;
        }

        public readonly string BotProfileId;
        public readonly string SourceProfileId;

        public bool Equals(LightExposureKey other)
        {
            return BotProfileId == other.BotProfileId && SourceProfileId == other.SourceProfileId;
        }

        public override bool Equals(object obj)
        {
            return obj is LightExposureKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((BotProfileId != null ? BotProfileId.GetHashCode() : 0) * 397)
                    ^ (SourceProfileId != null ? SourceProfileId.GetHashCode() : 0);
            }
        }
    }

    protected static RandomDir[] GenerateRandomDirections(int Count, float LengthMin, float LengthMax)
    {
        RandomDir[] Result = new RandomDir[Count];
        for (int i = 0; i < Count; i++)
        {
            Result[i] = new RandomDir(LengthMin, LengthMax);
        }
        return Result;
    }

    protected override bool CanProceed()
    {
        var bots = SAINBotController?.BotSpawnController?.BotDictionary;
        return bots != null && bots.Count > 0;
    }

    protected override bool LoopCondition()
    {
        return SAINGameWorld != null;
    }

    public override void Stop()
    {
        Dispose();
        base.Stop();
    }

    public void Dispose()
    {
        foreach (RaycastJob Job in RaycastJobs)
        {
            Job.Dispose();
        }

        RaycastJobs.Clear();
    }
}
