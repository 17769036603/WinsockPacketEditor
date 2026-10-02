using System;
using System.Collections.Generic;
using System.Globalization;

namespace WPELibrary.Lib
{
    /// <summary>
    /// 师门采购任务的只读目录条目。
    /// 该条目只保存静态任务映射，不代表已经接入实时读取或采购执行。
    /// </summary>
    public sealed class ShimenTaskPresetEntry
    {
        public ShimenTaskPresetEntry(
            int taskId,
            string taskType,
            int itemId,
            int quantity,
            int sellerId,
            string itemName)
        {
            this.TaskId = taskId;
            this.TaskType = taskType ?? string.Empty;
            this.ItemId = itemId;
            this.Quantity = quantity;
            this.SellerId = sellerId;
            this.ItemName = itemName ?? string.Empty;
        }

        public int TaskId { get; private set; }
        public string TaskType { get; private set; }
        public int ItemId { get; private set; }
        public int Quantity { get; private set; }
        public int SellerId { get; private set; }
        public string ItemName { get; private set; }

        public string DisplayText
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "任务 {0}：{1}，采购 {2} × {3}（物品ID {4}，商人ID {5}）",
                    this.TaskId,
                    this.TaskType,
                    this.ItemName,
                    this.Quantity,
                    this.ItemId,
                    this.SellerId);
            }
        }
    }

    /// <summary>
    /// 师门任务只读目录的持久化格式和默认六条采购映射。
    /// 格式：ShimenTask|Version|TaskId|TaskType|ItemId|Quantity|SellerId|ItemName
    /// </summary>
    public static class ShimenTaskPresetPlan
    {
        public const int Version = 1;
        public const string InstructionPrefix = "ShimenTask";
        public const string PurchaseTaskType = "采购物品";

        public static List<ShimenTaskPresetEntry> GetEntries()
        {
            return new List<ShimenTaskPresetEntry>
            {
                new ShimenTaskPresetEntry(400003, PurchaseTaskType, 10004, 3, 90222, "血烟石"),
                new ShimenTaskPresetEntry(400013, PurchaseTaskType, 2001110, 1, 90014, "铁骨扇"),
                new ShimenTaskPresetEntry(400023, PurchaseTaskType, 10001, 3, 90222, "风水混元丹"),
                new ShimenTaskPresetEntry(400033, PurchaseTaskType, 10002, 3, 90222, "灵翅天香"),
                new ShimenTaskPresetEntry(400043, PurchaseTaskType, 10003, 3, 90222, "千年灵花"),
                new ShimenTaskPresetEntry(400053, PurchaseTaskType, 90005, 5, 90026, "引妖香")
            };
        }

        public static string EncodeEntry(ShimenTaskPresetEntry entry)
        {
            if (entry == null ||
                entry.TaskId <= 0 ||
                entry.ItemId <= 0 ||
                entry.Quantity <= 0 ||
                entry.SellerId <= 0 ||
                string.IsNullOrWhiteSpace(entry.TaskType) ||
                string.IsNullOrWhiteSpace(entry.ItemName) ||
                entry.TaskType.IndexOf('|') >= 0 ||
                entry.ItemName.IndexOf('|') >= 0)
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                InstructionPrefix,
                Version.ToString(CultureInfo.InvariantCulture),
                entry.TaskId.ToString(CultureInfo.InvariantCulture),
                entry.TaskType,
                entry.ItemId.ToString(CultureInfo.InvariantCulture),
                entry.Quantity.ToString(CultureInfo.InvariantCulture),
                entry.SellerId.ToString(CultureInfo.InvariantCulture),
                entry.ItemName);
        }

        public static bool TryDecodeEntry(
            string content,
            out ShimenTaskPresetEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            string[] parts = content.Split(new[] { '|' }, StringSplitOptions.None);
            if (parts.Length != 8 ||
                !string.Equals(parts[0], InstructionPrefix, StringComparison.Ordinal) ||
                !string.Equals(parts[3], PurchaseTaskType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(parts[7]))
            {
                return false;
            }

            int version;
            int taskId;
            int itemId;
            int quantity;
            int sellerId;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) ||
                version != Version ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out taskId) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) ||
                !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out sellerId) ||
                taskId <= 0 ||
                itemId <= 0 ||
                quantity <= 0 ||
                sellerId <= 0)
            {
                return false;
            }

            entry = new ShimenTaskPresetEntry(
                taskId,
                parts[3],
                itemId,
                quantity,
                sellerId,
                parts[7]);
            return true;
        }
    }
}
