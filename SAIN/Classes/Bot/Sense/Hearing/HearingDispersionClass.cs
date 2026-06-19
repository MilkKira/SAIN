using System.Collections.Generic;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Helpers;
using SAIN.Models.PlayerData;
using SAIN.Preset.GlobalSettings;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using UnityEngine.AI;

namespace SAIN.SAINComponent.Classes;

public class HearingDispersionClass(SAINHearingSensorClass hearing) : BotSubClass<SAINHearingSensorClass>(hearing), IBotClass
{
    // 原有常量保留，新模型仍会用到距离阈值
    private const float MIN_DISTANCE_LAST_KNOWN_NO_RANDOMIZATION = 3f;
    private const float MAX_DISTANCE_LASTKNOWN_REDUCE_RANDOM = 50f;
    private const float MIN_COEF_LASTKNOWN_REDUCE_RANDOM = 0.05f;

    // 角度误差的最小/最大值（单位：度）
    private const float MIN_ANGLE_ERROR = 3f;
    private const float MAX_ANGLE_ERROR = 60f;

    // 距离噪声比例（标准差相对于真实距离的比例）
    private const float DISTANCE_NOISE_RATIO = 0.15f;

    // 在 estimatedPos 周围采样时的搜索半径
    private const float NAV_SAMPLE_RADIUS = 2f;

    // 听力削弱系数：1.0 = 原版，>1.0 表示误差放大（听力变差）
    private const float HEARING_NERF_FACTOR = 1.15f;

    public Vector3 CalcRandomizedPosition(AISoundData Sound, float addDispersion)
    {
        EnemyPlace enemyLastKnown = Sound.Enemy.KnownPlaces.LastKnownPlace;
        float distanceFromLastKnown = enemyLastKnown != null ? enemyLastKnown.DistanceToEnemyRealPosition : float.MaxValue;

        // 极近：完全相信位置，直接返回
        if (distanceFromLastKnown <= MIN_DISTANCE_LAST_KNOWN_NO_RANDOMIZATION)
        {
            return enemyLastKnown.Position;
        }

        // 1. 计算基础方向：从 Bot 指向最后已知位置
        Vector3 botPos = Bot.Position;
        Vector3 directionToLastKnown = (enemyLastKnown.Position - botPos).normalized;

        // 2. 根据距离映射角度误差标准差
        float distClamped = Mathf.Clamp(distanceFromLastKnown, MIN_DISTANCE_LAST_KNOWN_NO_RANDOMIZATION, MAX_DISTANCE_LASTKNOWN_REDUCE_RANDOM);
        float ratio = Mathf.InverseLerp(MIN_DISTANCE_LAST_KNOWN_NO_RANDOMIZATION, MAX_DISTANCE_LASTKNOWN_REDUCE_RANDOM, distClamped);
        float angleStdDev = Mathf.Lerp(MIN_ANGLE_ERROR, MAX_ANGLE_ERROR, ratio);

        // 3. 加入原有 dispersionMod 影响（敌人是否在视野中心）
        float dispersionMod = getDispersionModifier(Sound.Enemy);
        angleStdDev *= dispersionMod;

        // 【削弱】角度误差标准差放大 15%
        angleStdDev *= HEARING_NERF_FACTOR;

        // 4. 高斯角度偏移
        float angleOffset = GaussianRandom(0f, angleStdDev);
        // 【削弱】同时放宽截断上限，避免被强裁
        angleOffset = Mathf.Clamp(angleOffset, -MAX_ANGLE_ERROR * HEARING_NERF_FACTOR, MAX_ANGLE_ERROR * HEARING_NERF_FACTOR);

        // 5. 旋转方向
        Quaternion rotation = Quaternion.Euler(0f, angleOffset, 0f);
        Vector3 estimatedDirection = rotation * directionToLastKnown;

        // 6. 距离估计：真实距离 + 高斯噪声 + lastKnownDistCoef 的影响（保持原有系数语义）
        float actualDist = Vector3.Distance(botPos, enemyLastKnown.Position);
        float lastKnownDistCoef = 1f;
        if (distanceFromLastKnown < MAX_DISTANCE_LASTKNOWN_REDUCE_RANDOM)
        {
            lastKnownDistCoef = Mathf.Lerp(MIN_COEF_LASTKNOWN_REDUCE_RANDOM, 1f, ratio);
        }
        // 【削弱】距离噪声的标准差放大 15%
        float distNoise = GaussianRandom(0f, actualDist * DISTANCE_NOISE_RATIO * lastKnownDistCoef * HEARING_NERF_FACTOR);
        float estimatedDist = actualDist + distNoise;
        estimatedDist = Mathf.Max(estimatedDist, 1f);

        // 7. 生成预估位置
        Vector3 estimatedPos = botPos + estimatedDirection * estimatedDist;

        // 8. 在 estimatedPos 附近寻找 NavMesh 上可达的点（同时检查从 Bot 出发的路径）
        // 【削弱】搜索半径放大 15%，让最终落点更离散
        if (TryGetReachablePointNear(botPos, estimatedPos, NAV_SAMPLE_RADIUS * HEARING_NERF_FACTOR, 10, out Vector3 result))
        {
#if DEBUG
            if (Sound.HeardPlayer.IsYourPlayer)
            {
                var line = DebugGizmos.DrawLine(Vector3.zero, Vector3.forward, Color.magenta, 0.1f, 10f, false);
                if (_lastDebugPath?.corners.Length > 1)
                    DebugGizmos.SetLinePositions(line, _lastDebugPath.corners);
            }
#endif
            return result;
        }

        // 降级方案：找不到则退回最后已知位置
        if (enemyLastKnown != null)
        {
#if DEBUG
            if (SAINPlugin.DebugMode)
                Logger.LogWarning($"[{Bot.name}] Failed to find reachable point near estimated pos for Enemy [{Sound.Enemy.Player.name}], fallback to last known.");
#endif
            return enemyLastKnown.Position;
        }

#if DEBUG
        Logger.LogWarning($"[{Bot.name}] Completely failed to find point for [{Sound.Enemy.Player.name}], using real position.");
#endif
        return Sound.Enemy.EnemyPosition;
    }

    /// <summary>
    /// 从起点出发，在目标点附近随机采样，寻找一个可达的 NavMesh 位置
    /// </summary>
    private bool TryGetReachablePointNear(Vector3 startPos, Vector3 targetPos, float sampleRadius, int maxTries, out Vector3 result)
    {
        for (int i = 0; i < maxTries; i++)
        {
            // 在目标点周围圆盘内随机采样
            Vector2 circle = Random.insideUnitCircle * sampleRadius;
            Vector3 samplePoint = targetPos + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(samplePoint, out NavMeshHit hit, sampleRadius + 1f, NavMesh.AllAreas))
                continue;

            // 检查是否可以从起点到达该点
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(startPos, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                result = hit.position;
                _lastDebugPath = path;
                return true;
            }
        }
        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 高斯（正态）分布随机数生成器（Box-Muller 变换）
    /// </summary>
    private static float GaussianRandom(float mean, float stdDev)
    {
        float u1 = 1.0f - Random.value; // 避免 log(0)
        float u2 = 1.0f - Random.value;
        float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    // ===================== 以下为原有方法，保持不变 =====================

    public static bool GetRandomReachablePointInBoxAroundPlayer(
        PlayerComponent playerComp,
        Vector3 size,
        out Vector3 result,
        out NavMeshPath path,
        float navSampleRange = 2f,
        int maxTries = 10
    )
    {
        PlayerNavData navData = playerComp.Transform.NavData;
        Vector3 minSize = size * 0.5f;
        Vector3 origin = navData.Status == EPlayerNavMeshDistance.OffNavMesh ? playerComp.Position : playerComp.Transform.NavData.Position;
        for (int i = 0; i < maxTries; i++)
        {
            Vector3 randomPoint = RandomPointInBox(origin, size);
            if (!NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, navSampleRange, -1))
            {
                continue;
            }
            path = new NavMeshPath();
            if (!NavMesh.CalculatePath(origin, hit.position, -1, path))
            {
                continue;
            }
            Vector3[] corners = path.corners;
            int length = corners.Length;
            if (length > 1)
            {
                result = corners[length - 1];
                return true;
            }
        }

        result = Vector3.zero;
        path = null;
        return false;
    }

    public static bool GetRandomReachablePointAroundPlayer(
        PlayerComponent playerComp,
        float radius,
        float height,
        out Vector3 result,
        float navSampleRange = 2f,
        int maxTries = 10
    )
    {
        PlayerNavData navData = playerComp.Transform.NavData;
        Vector3 origin = navData.Status == EPlayerNavMeshDistance.OffNavMesh ? playerComp.Position : playerComp.Transform.NavData.Position;
        for (int i = 0; i < maxTries; i++)
        {
            Vector3 randomDir = new(Random.Range(-radius, radius), Random.Range(-height, height), Random.Range(-radius, radius));
            Vector3 randomPoint = origin + randomDir;
            if (!NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, navSampleRange, -1))
            {
                continue;
            }
            _randomPath ??= new NavMeshPath();
            _randomPath.ClearCorners();
            if (!NavMesh.CalculatePath(origin, hit.position, -1, _randomPath))
            {
                continue;
            }
            Vector3[] corners = _randomPath.corners;
            int length = corners.Length;
            if (length > 1)
            {
                result = corners[length - 1];
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    private static NavMeshPath _randomPath;
    private NavMeshPath _lastDebugPath; // 用于调试显示

    public static Vector3 RandomPointInBox(Vector3 center, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;

        return new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                Random.Range(-halfSize.y, halfSize.y),
                Random.Range(-halfSize.z, halfSize.z)
            ) + center;
    }

    private static readonly List<NavMeshPath> _preAlocPathList = [];

    private float getBaseDispersion(float enemyDistance, SAINSoundType soundType)
    {
        HearingSettings hearingSettings = GlobalSettingsClass.Instance.Hearing;
        if (hearingSettings.HEAR_DISPERSION_VALUES.TryGetValue(soundType, out float dispersionValue) == false)
        {
            dispersionValue = 12.5f;
        }
        return enemyDistance / dispersionValue;
    }

    private float getDispersionModifier(Enemy Enemy)
    {
        float dotProduct = Vector3.Dot(Bot.LookDirection.normalized, Enemy.EnemyDirectionNormal);
        float scaled = (dotProduct + 1) / 2;

        HearingSettings hearingSettings = GlobalSettingsClass.Instance.Hearing;
        float dispersionModifier = Mathf.Lerp(
            hearingSettings.HEAR_DISPERSION_ANGLE_MULTI_MAX,
            hearingSettings.HEAR_DISPERSION_ANGLE_MULTI_MIN,
            scaled
        );

        return dispersionModifier;
    }

    private static Vector3 getRandomizedDirection(float dispersion, float xCoef = 1f, float yCoef = 0.25f, float zCoef = 1f)
    {
        float randomX = Random.Range(-dispersion * xCoef, dispersion * xCoef);
        float randomy = Random.Range(-dispersion * yCoef, dispersion * yCoef);
        float randomZ = Random.Range(-dispersion * zCoef, dispersion * zCoef);
        return new(randomX, randomy, randomZ);
    }
}