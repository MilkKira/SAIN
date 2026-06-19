using EFT;
using SAIN.Components;
using UnityEngine;

namespace SAIN.SAINComponent.Classes.EnemyClasses;

public class EnemyVisionDistanceClass : EnemyBase
{
    public EnemyVisionDistanceClass(EnemyData enemyData)
        : base(enemyData, enemyData.Enemy.Bot) { }

    /// <summary>
    /// 动态计算的对该敌人的“额外可见距离”加成。
    /// 实际总可见距离 = 基础可见距离 + 此值。
    /// </summary>
    public float Value
    {
        get
        {
            if (_nextCalcTime < Time.time)
            {
                _nextCalcTime = Time.time + _calcFreq;
                _visionDist = CalcVisionDistance();
            }
            return _visionDist;
        }
    }

    /// <summary>
    /// 当敌人几乎就在脸上且条件良好时，直接返回极高值保证可见。
    /// </summary>
    private bool IsEnemyAlwaysInVisibleDistance()
    {
        if (
            Enemy.Vision.Angles.AngleToEnemy < 30f
            && Enemy.KnownPlaces.EnemyDistanceFromLastKnown < 3
            && BotManagerComponent.Instance.TimeVision.VisibilityRatio > 0.5f
        )
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 主计算：根据角度、移动、装备、开火闪光、状态等因子，得出可见距离的增减量。
    /// </summary>
    private float CalcVisionDistance()
    {
        if (IsEnemyAlwaysInVisibleDistance())
        {
            return 1000f;
        }

        float angleMod = CalcAngleMod();
        float moveMod = CalcMovementMod();
        float gearMod = CalcGearStealthMod();   // 注意：gearMod 应表示“隐蔽能力”，越大越隐蔽
        float flareMod = GetFlare();

        SAINEnemyStatus status = Enemy.Status;
        bool posFlare = status.PositionalFlareEnabled;
        bool shotAtMe = status.ShotAtMeRecently;

        float positionalFlareMod = posFlare ? 1.5f : 1f;
        float underFire = shotAtMe ? 1.5f : 1f;

        // 分子：各种增加发现概率的因子；分母：降低发现概率的隐蔽因子
        float finalModifier = (moveMod * angleMod * flareMod * positionalFlareMod * underFire) / gearMod;

        float defaultVisDist = BotOwner.LookSensor.VisibleDist;
        float result = (defaultVisDist * finalModifier) - defaultVisDist;

        return result;
    }

    /// <summary>
    /// 移动速度修正：静止时略难发现（0.9），冲刺时更容易发现（_sprintMod，通常>1）。
    /// </summary>
    private float CalcMovementMod()
    {
        float velocity = Enemy.Vision.EnemyVelocity;
        float result = Mathf.Lerp(0.9f, _sprintMod, velocity);
        return result;
    }

    private static float _sprintMod
    {
        get { return SAINPlugin.LoadedPreset.GlobalSettings.Look.VisionDistance.MovementDistanceModifier; }
    }

    /// <summary>
    /// 视线角度修正：
    /// - 若敌人在正前方窄角内（≤15°），根据是否有瞄准镜返回 3 或 1.5。
    /// - 若角度超过最大视野角，返回 0（基本不可见）。
    /// - 若距离极近（<10m），无论角度均返回 1。
    /// - 其他情况：角度在 15° 到 MaxVisionAngle 之间平滑过渡从 1.5 到 0.25。
    /// </summary>
    private float CalcAngleMod()
    {
        float angleToEnemy = Enemy.Vision.Angles.AngleToEnemy;
        float maxAngle = Enemy.Vision.Angles.MaxVisionAngle;

        if (angleToEnemy > maxAngle)
        {
            return 0f;
        }

        float minAngle = 15f;
        if (angleToEnemy <= minAngle)
        {
            if (Bot.PlayerComponent.Equipment.CurrentWeaponInfo?.HasOptic == true)
            {
                return 3f;
            }
            return 1.5f;
        }

        // 近距离不再因角度削减可见性
        if (Enemy.RealDistance < 10f)
        {
            return 1f;
        }

        // 角度在 minAngle 到 maxAngle 之间，ratio 从 1 (靠近正前方) 线性递减到 0 (靠近视野边缘)
        float num = maxAngle - minAngle;
        float num2 = angleToEnemy - minAngle;
        float ratio = 1f - num2 / num;   // [0, 1]

        // 【修复】原代码使用 InverseLerp 导致值域严重错误，现改为 Lerp 平滑过渡
        float result = Mathf.Lerp(0.25f, 1.5f, ratio);
        return result;
    }

    /// <summary>
    /// 装备潜行修正：从外部 AI 装备模块获取隐蔽系数，数值越大 = 越难被发现。
    /// 因为它在分母，所以最终可见距离会相应降低。
    /// </summary>
    private float CalcGearStealthMod()
    {
        return Enemy.EnemyPlayerComponent.AIData.AIGearModifier.StealthModifier(Enemy.RealDistance);
    }

    /// <summary>
    /// 开火闪光修正：敌人近期开火会略微增加被发现几率，有消音器时加成减小。
    /// </summary>
    private float GetFlare()
    {
        bool flareEnabled = EnemyPlayer.AIData.GetFlare;
        bool usingSuppressor = Enemy.EnemyPlayerComponent?.Equipment.CurrentWeaponInfo?.HasSuppressor == true;

        float flareMod;
        if (flareEnabled && !usingSuppressor)
        {
            flareMod = 1.25f;
        }
        else if (flareEnabled && usingSuppressor)
        {
            flareMod = 1.1f;
        }
        else
        {
            flareMod = 1f;
        }
        return flareMod;
    }

    private float _nextCalcTime;
    private float _calcFreq = 0.05f;
    private float _visionDist;
}