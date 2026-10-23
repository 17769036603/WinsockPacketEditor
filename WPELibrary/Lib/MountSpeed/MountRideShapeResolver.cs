using System;
using System.Collections.Generic;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑形态解析的纯逻辑 helper。
    /// 只从只读快照里按 rideShapeId 解析唯一的坐骑实例 ID；
    /// 不访问 ADB、探针、Socket 或数据库，也不执行任何发送。
    /// 坐骑实例 ID 由服务端临时下发并会变化，因此发送前必须按稳定形态重新解析。
    /// </summary>
    public static class MountRideShapeResolver
    {
        /// <summary>目标形态 ID 非法（&lt;= 0）。</summary>
        public const string ErrorShapeIdInvalid = "mount_ride_shape_id_invalid";

        /// <summary>只读快照为空。</summary>
        public const string ErrorSnapshotMissing = "mount_ride_shape_snapshot_missing";

        /// <summary>快照中没有该形态的坐骑实例。</summary>
        public const string ErrorNoMatch = "mount_ride_shape_no_match";

        /// <summary>快照中有多个该形态的坐骑实例，无法唯一绑定。</summary>
        public const string ErrorAmbiguous = "mount_ride_shape_ambiguous";

        /// <summary>匹配到的实例 ID 不是可写入 A050 帧尾的 19 位数字。</summary>
        public const string ErrorInstanceIdInvalid = "mount_ride_shape_instance_id_invalid";

        /// <summary>
        /// 是否需要先读取一次只读快照来解析当前实例 ID。
        /// 形态为空（旧预设）时必须返回 false，避免引入额外的设备读取。
        /// </summary>
        public static bool RequiresShapeResolution(int? targetRideShapeId)
        {
            return targetRideShapeId.HasValue;
        }

        /// <summary>
        /// 纯逻辑：按可空的目标形态决定本次 refine 预检要绑定的坐骑实例 ID。
        /// 形态为空时只沿用模板（包内）实例 ID：不读取快照、不调用形态匹配，
        /// 保证旧预设行为与既有实现完全一致。
        /// </summary>
        public static bool TryResolvePreferredRideInstanceId(
            MountStatusReadOnlySnapshot snapshot,
            int? targetRideShapeId,
            string templateRideInstanceId,
            out string preferredRideInstanceId,
            out bool usedShapeResolution,
            out string errorCode,
            out string errorMessage)
        {
            errorCode = string.Empty;
            errorMessage = string.Empty;
            usedShapeResolution = false;
            if (!targetRideShapeId.HasValue)
            {
                // 旧预设路径：沿用已经过 A050 结构校验的包内实例 ID。
                preferredRideInstanceId = templateRideInstanceId ?? string.Empty;
                return true;
            }

            usedShapeResolution = true;
            return TryResolveRideInstanceId(
                snapshot,
                targetRideShapeId.Value,
                out preferredRideInstanceId,
                out errorCode,
                out errorMessage);
        }

        /// <summary>
        /// 纯逻辑：按形态在只读快照中解析唯一的坐骑实例 ID。
        /// 快照为空、形态非法、无匹配或匹配到多个（即使实例 ID 相同）都返回失败。
        /// </summary>
        public static bool TryResolveRideInstanceId(
            MountStatusReadOnlySnapshot snapshot,
            int shapeId,
            out string rideInstanceId,
            out string errorCode,
            out string errorMessage)
        {
            rideInstanceId = string.Empty;
            errorCode = string.Empty;
            errorMessage = string.Empty;
            if (snapshot == null)
            {
                errorCode = ErrorSnapshotMissing;
                errorMessage = "坐骑只读快照为空，无法按形态解析坐骑实例。";
                return false;
            }

            if (shapeId <= 0)
            {
                errorCode = ErrorShapeIdInvalid;
                errorMessage = string.Format(
                    "目标坐骑形态 ID 无效：{0}；必须为正整数。",
                    shapeId);
                return false;
            }

            int matchCount = 0;
            string matchedRideInstanceId = string.Empty;
            foreach (MountRideInstanceSnapshot instance in
                snapshot.RideInstances ?? new List<MountRideInstanceSnapshot>())
            {
                if (instance == null ||
                    !instance.RideShapeId.HasValue ||
                    instance.RideShapeId.Value != shapeId)
                {
                    continue;
                }

                // 只统计匹配数量。形态重复必须拒绝，即使重复项的实例 ID 恰好相同，
                // 因此这里不能按实例 ID 去重。
                matchCount++;
                matchedRideInstanceId = instance.RideInstanceId;
            }

            if (matchCount == 0)
            {
                errorCode = ErrorNoMatch;
                errorMessage = string.Format(
                    "只读快照中没有形态 ID 为 {0} 的坐骑实例。",
                    shapeId);
                return false;
            }

            if (matchCount > 1)
            {
                errorCode = ErrorAmbiguous;
                errorMessage = string.Format(
                    "只读快照中有 {0} 个形态 ID 为 {1} 的坐骑实例，无法唯一绑定。",
                    matchCount,
                    shapeId);
                return false;
            }

            if (!IsValidRideInstanceId(matchedRideInstanceId))
            {
                errorCode = ErrorInstanceIdInvalid;
                errorMessage = string.Format(
                    "形态 ID 为 {0} 的坐骑实例 ID 不是 19 位数字，无法写入 A050 帧尾。",
                    shapeId);
                return false;
            }

            rideInstanceId = matchedRideInstanceId;
            return true;
        }

        private static bool IsValidRideInstanceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length != MountRefineA050PacketTemplate.RideInstanceIdLength)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
