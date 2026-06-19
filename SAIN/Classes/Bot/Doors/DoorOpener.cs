using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using SAIN.Components;
using SAIN.Helpers;
using UnityEngine;

namespace SAIN.SAINComponent.Classes.Mover;

public class DoorOpener : BotComponentClassBase
{
    public bool Interacting { get; private set; }
    public EInteractionType InteractionType { get; private set; }

    public DoorOpener(BotComponent sain) : base(sain)
    {
        TickRequirement = ESAINTickState.OnlyNoSleep;
    }

    private const float DOOR_UPDATE_INTERVAL = 0.5f;
    private const float MAX_WAIT_FOR_DOOR_OPEN = 3f; // 等门全开/全关的最大时间

    private List<DoorDataStruct> _interactionDoors { get; } = [];
    private List<DoorDataStruct> _allDoors { get; } = [];
    public NavGraphVoxelSimple CurrentVoxel { get; private set; }

    public override void ManualUpdate()
    {
        // 1. 每帧强制检测正前方是否有门需要处理（防穿门核心）
        ForceDoorCheck();

        if (!Interacting)
            return;

        // 2. 门是否已到达目标状态
        if (ActiveDoor.Door != null)
        {
            bool done = false;
            switch (InteractionType)
            {
                case EInteractionType.Open:
                    done = ActiveDoor.Door.DoorState == EDoorState.Open;
                    break;
                case EInteractionType.Close:
                    done = ActiveDoor.Door.DoorState == EDoorState.Shut;
                    break;
                default:
                    done = true;
                    break;
            }
            if (done)
            {
                Clear();
                return;
            }
        }

        // 3. 超时保护
        if (Time.time > _doorInteractionEndTime + MAX_WAIT_FOR_DOOR_OPEN)
        {
            Clear();
        }
    }

    /// <summary>
    /// 用 Bot 视线方向检测前方极近处是否有门（非 Open 状态），
    /// 如有则强制交互，等待门全开，杜绝穿门。
    /// </summary>
    private void ForceDoorCheck()
    {
        Vector3 origin = Bot.Position + Vector3.up * 0.5f;
        Vector3 forward = Bot.LookDirection.normalized;
        float checkDist = 1.2f;
        float sphereRadius = 0.4f;

        if (!Physics.SphereCast(origin, sphereRadius, forward, out RaycastHit hit, checkDist,
            LayerMaskClass.PlayerStaticDoorMask | LayerMaskClass.DoorLayer))
            return;

        // 从候选列表匹配或临时创建门数据
        DoorDataStruct data;
        if (!FindDoorFromCollider(hit.collider, out data, out int index, _interactionDoors))
        {
            NavMeshDoorLink link = hit.collider.GetComponent<NavMeshDoorLink>();
            if (link == null || link.Door == null || !link.Door.Operatable)
                return;
            data = new DoorDataStruct(link);
        }

        // 门已全开 → 不用管
        if (data.Door.DoorState == EDoorState.Open)
            return;

        // 如果已经在交互这扇门，保持等待
        if (Interacting && ActiveDoor.Door == data.Door)
            return;

        // 如果是另一扇门，先清掉旧交互，再对新门强制开门
        if (Interacting)
            Clear();

        TryInteractWithDoor(EInteractionType.Open, Time.time, data);
    }

    public bool TryInteractWithDoor(EInteractionType interactionType, float time, DoorDataStruct data)
    {
        if (!InteractWithDoor(ref data, interactionType))
        {
#if DEBUG
            Logger.LogDebug($"[{Bot.name}]:[{data.Door.Id}] failed to interact with door");
#endif
            Clear();
            return false;
        }
        _interactionDoors[_interactionDoorIndex] = data;
        Interacting = true;
        ActiveDoor = data;
        InteractionType = interactionType;
        _doorInteractionEndTime = time + (IsDoorPullOpen(data, Bot.NavMeshPosition) ? 1.25f : 1f);
        Bot.Player.MovementContext.IgnoreInteractionCollision(data.Door.Collider, true);
        return true;
    }

    public DoorDataStruct GetActiveDoor()
    {
        if (_interactionDoors.Count > 0 && _interactionDoorIndex < _interactionDoors.Count)
            return _interactionDoors[_interactionDoorIndex];
        return ActiveDoor;
    }

    public bool SelectDoor(out EInteractionType interactionType, out DoorDataStruct currentDoor, IBotPathData pathData)
    {
        const float RAY_LENGTH = 3f;
        Vector3 botPosition = Bot.Position;
        float time = Time.time;

        if (Interacting)
        {
            interactionType = InteractionType;
            currentDoor = _interactionDoors[_interactionDoorIndex];
            return true;
        }

        SearchForDoors(botPosition, time);

        if (_interactionDoors.Count == 0)
        {
            interactionType = EInteractionType.Open;
            currentDoor = ActiveDoor;
            return false;
        }

        CornerMoveData moveData = pathData.CurrentCornerMoveData;
        Ray ray = new()
        {
            origin = botPosition + Vector3.up,
            direction = moveData.CornerDirectionFromBotNormal * RAY_LENGTH
        };

        DoorDataStruct data = ActiveDoor;
        if (!RaycastToDoors(out interactionType, ref data, out int index, RAY_LENGTH, ray, _interactionDoors))
        {
            currentDoor = ActiveDoor;
            return false;
        }
        _interactionDoorIndex = index;
        _interactionDoors[index] = data;
        currentDoor = data;
        return true;
    }

    private void Clear()
    {
        Bot.Player.MovementContext.IgnoreInteractionCollision(ActiveDoor.Door?.Collider, false);
        Interacting = false;
        _interactionDoorIndex = 0;
        ActiveDoor = new();
        _doorInteractionEndTime = 0;
        InteractionType = EInteractionType.Open;
    }

    private int _interactionDoorIndex;

    private void SearchForDoors(Vector3 botPosition, float time)
    {
        if (_nextDoorUpdateTime < time)
        {
            _nextDoorUpdateTime = time + DOOR_UPDATE_INTERVAL;
            BotOwner.AIData.SetPosToVoxel(botPosition);

            var lastVoxel = CurrentVoxel;
            CurrentVoxel = BotOwner.VoxelesPersonalData.CurVoxel;

            if (lastVoxel != CurrentVoxel)
            {
                _allDoors.Clear();
                if (CurrentVoxel != null)
                {
                    foreach (var link in CurrentVoxel.DoorLinks)
                    {
                        if (IsDoorOpenable(link.Door))
                            _allDoors.Add(new DoorDataStruct(link));
                    }
                }
            }

            _interactionDoors.Clear();
            for (int i = 0; i < _allDoors.Count; i++)
            {
                DoorDataStruct door = _allDoors[i];
                door.ManualUpdate(botPosition);
                if (door.InRangeToInteract(door.Door) && door.CanInteractByTime(time))
                    _interactionDoors.Add(door);
                _allDoors[i] = door;
            }
        }
    }

    private static bool FindDoorFromCollider(Collider collider, out DoorDataStruct data, out int index, List<DoorDataStruct> doors)
    {
        if (collider == null)
        {
            data = default;
            index = -1;
            return false;
        }
        data = default;
        for (index = 0; index < doors.Count; index++)
        {
            data = doors[index];
            if (data.Door.Collider == collider)
                return true;
            if (collider.GetComponent<NavMeshDoorLink>() == data.Link)
            {
                Logger.LogDebug("Found NavMeshDoorLink from collider");
                return true;
            }
        }
        return false;
    }

    private static bool RaycastToDoors(
        out EInteractionType interactionType,
        ref DoorDataStruct data,
        out int index,
        float RAY_LENGTH,
        Ray ray,
        List<DoorDataStruct> doors)
    {
        const float SPHERECAST_RADIUS = 0.15f;
        const float SPHERECAST_DISTANCE = 1.5f;

        if (Physics.SphereCast(ray, SPHERECAST_RADIUS, out RaycastHit hit, SPHERECAST_DISTANCE, LayerMaskClass.PlayerStaticDoorMask))
        {
            if (FindDoorFromCollider(hit.collider, out data, out index, doors))
            {
                if (data.Door.DoorState == EDoorState.Open)
                { interactionType = EInteractionType.Close; return true; }
                if (data.Door.DoorState == EDoorState.Shut)
                { interactionType = EInteractionType.Open; return true; }
            }
        }
        if (Physics.SphereCast(ray, SPHERECAST_RADIUS, out hit, SPHERECAST_DISTANCE, LayerMaskClass.DoorLayer))
        {
            if (FindDoorFromCollider(hit.collider, out data, out index, doors))
            {
                if (data.Door.DoorState == EDoorState.Open)
                { interactionType = EInteractionType.Close; return true; }
                if (data.Door.DoorState == EDoorState.Shut)
                { interactionType = EInteractionType.Open; return true; }
            }
        }
        for (index = 0; index < doors.Count; index++)
        {
            data = doors[index];
            if (data.CurrentSqrMagnitude > RAY_LENGTH * RAY_LENGTH) continue;
            if (!CanInteract(data.Link)) continue;
            Collider c = data.Door.Collider;
            if (c == null) continue;
            if (c.Raycast(ray, out hit, SPHERECAST_DISTANCE))
            {
                if (data.Door.DoorState == EDoorState.Open)
                { interactionType = EInteractionType.Close; return true; }
                if (data.Door.DoorState == EDoorState.Shut)
                { interactionType = EInteractionType.Open; return true; }
            }
        }
        interactionType = EInteractionType.Open;
        return false;
    }

    private static bool IsDoorOpenable(Door door)
    {
        return door.enabled && door.gameObject.activeInHierarchy && door.Operatable;
    }

    private float _nextDoorUpdateTime;

    private static bool CanInteract(NavMeshDoorLink link)
    {
        return link.ShallInteract() && link.Door.enabled && link.Door.gameObject.activeInHierarchy && link.Door.Operatable;
    }

    private bool InteractWithDoor(ref DoorDataStruct data, EInteractionType type)
    {
        if (data.Door == null) return false;

        switch (data.Door.DoorState)
        {
            case EDoorState.Shut: data.LastCloseTime = Time.time; break;
            case EDoorState.Open: data.LastOpenTime = Time.time; break;
            default: return false;
        }
        data.LastInteractTime = Time.time;
        Player.MovementContext.ResetCanUsePropState();
        var gstruct = Door.Interact(Player, type);
        if (gstruct.Succeeded)
        {
            switch (type)
            {
                case EInteractionType.Breach:
                    Player.vmethod_0(data.Door, gstruct.Value, null);
                    break;
                default:
                    Player.vmethod_1(data.Door, gstruct.Value);
                    break;
            }
            return true;
        }
        return false;
    }

    public bool ShallKickOpen(Door door, EInteractionType Etype)
    {
        if (Etype != EInteractionType.Open) return false;
        if (!WantToKick()) return false;
        var p = door.GetBreakInParameters(Bot.Position);
        return door.BreachSuccessRoll(p.InteractionPosition);
    }

    private bool WantToKick()
    {
        var enemy = Bot.GoalEnemy;
        if (enemy == null) return false;
        if (Bot.Info.PersonalitySettings.General.KickOpenAllDoors) return true;
        if (BotOwner.Memory.IsUnderFire) return true;
        float? t = enemy.TimeSinceSeen;
        if (t.HasValue && t.Value < 3f) return true;
        if (t.HasValue && t.Value < 5f && enemy.InLineOfSight) return true;
        return false;
    }

    public static bool IsDoorPullOpen(DoorDataStruct doorData, Vector3 botPosition)
    {
        Vector3 openPos = doorData.Link.Open2;
        Vector3 doorPos = doorData.Link.transform.position;
        Vector3 openDir = (openPos - doorPos).normalized;
        Vector3 botDir = (botPosition - doorPos).normalized;
        return Vector3.Dot(openDir, botDir) > 0;
    }

    public DoorDataStruct ActiveDoor = new();
    private float _doorInteractionEndTime;
}