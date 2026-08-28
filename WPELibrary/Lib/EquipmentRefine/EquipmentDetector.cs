using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备检测器：从游戏背包界面识别装备
    /// </summary>
    public class EquipmentDetector
    {
        /// <summary>
        /// 装备位置信息
        /// </summary>
        public class EquipmentCell
        {
            public int SlotIndex { get; set; } = -1;
            public Point CellCenter { get; set; } = Point.Empty;
            public Rectangle CellBounds { get; set; } = Rectangle.Empty;
            public string InstanceId { get; set; } = string.Empty;
            public string EquipmentName { get; set; } = string.Empty;
            public string Quality { get; set; } = string.Empty;
            public bool IsEquipped { get; set; } = false;
            public bool IsEmpty { get; set; } = false;
            public string Fingerprint { get; set; } = string.Empty; // 视觉指纹
        }

        /// <summary>
        /// 在截图中扫描背包装备格子
        /// </summary>
        /// <param name="screenshot">游戏窗口截图</param>
        /// <param name="slotTemplates">装备格子模板图片列表</param>
        /// <param name="inventoryRegion">背包区域边界</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>装备列表</returns>
        public static async Task<List<EquipmentCell>> ScanInventoryAsync(
            Bitmap screenshot,
            IEnumerable<Bitmap> slotTemplates,
            Rectangle inventoryRegion,
            CancellationToken cancellationToken = default)
        {
            var results = new List<EquipmentCell>();
            
            await Task.Run(() =>
            {
                // 截取背包区域
                using (var regionBitmap = screenshot.Clone(inventoryRegion, screenshot.PixelFormat))
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // 匹配装备格子模板
                    foreach (Bitmap template in slotTemplates)
                    {
                        if (template == null || cancellationToken.IsCancellationRequested)
                            continue;

                        var matchResult = VisionTemplateMatcher.FindBestMatch(
                            regionBitmap,
                            template,
                            0.7, // 最小匹配度
                            new VisionTemplateMatchOptions
                            {
                                NormalizeBrightness = true,
                                MinScale = 0.9,
                                MaxScale = 1.1,
                                ScaleStep = 0.1
                            },
                            cancellationToken);

                        if (matchResult.Cancelled) break;
                        if (!matchResult.Found) continue;

                        var cell = new EquipmentCell
                        {
                            SlotIndex = results.Count,
                            CellCenter = new Point(
                                inventoryRegion.X + matchResult.Location.X + matchResult.Size.Width / 2,
                                inventoryRegion.Y + matchResult.Location.Y + matchResult.Size.Height / 2),
                            CellBounds = new Rectangle(
                                inventoryRegion.X + matchResult.Location.X,
                                inventoryRegion.Y + matchResult.Location.Y,
                                matchResult.Size.Width,
                                matchResult.Size.Height),
                            Fingerprint = GenerateFingerprint(regionBitmap, matchResult.Location, matchResult.Size)
                        };

                        results.Add(cell);
                    }
                }
            }, cancellationToken);

            return results;
        }

        /// <summary>
        /// 定位指定槽位的装备
        /// </summary>
        public static async Task<EquipmentCell> FindSlotAsync(
            Bitmap screenshot,
            Rectangle inventoryRegion,
            int slotIndex,
            int slotsPerRow,
            int slotWidth,
            int slotHeight,
            int paddingX,
            int paddingY,
            CancellationToken cancellationToken = default)
        {
            if (slotIndex < 0) return null;

            int col = slotIndex % slotsPerRow;
            int row = slotIndex / slotsPerRow;

            int centerX = inventoryRegion.X + col * (slotWidth + paddingX) + slotWidth / 2;
            int centerY = inventoryRegion.Y + row * (slotHeight + paddingY) + slotHeight / 2;

            return new EquipmentCell
            {
                SlotIndex = slotIndex,
                CellCenter = new Point(centerX, centerY),
                CellBounds = new Rectangle(
                    centerX - slotWidth / 2,
                    centerY - slotHeight / 2,
                    slotWidth,
                    slotHeight)
            };
        }

        /// <summary>
        /// 在装备卡片上识别装备名称
        /// </summary>
        public static async Task<string> RecognizeEquipmentNameAsync(
            Bitmap screenshot,
            Rectangle nameRegion,
            CancellationToken cancellationToken = default)
        {
            // 此处接入OCR识别装备名称
            // 返回识别结果字符串
            return string.Empty;
        }

        /// <summary>
        /// 生成装备视觉指纹（用于身份校验）
        /// </summary>
        private static string GenerateFingerprint(Bitmap bitmap, Point location, Size size)
        {
            try
            {
                using (var cellBitmap = bitmap.Clone(
                    new Rectangle(location, size), bitmap.PixelFormat))
                {
                    // 转换为灰度并计算简单hash
                    var bytes = new List<byte>();
                    int sampleRate = Math.Max(1, size.Width / 32);

                    for (int y = 0; y < size.Height; y += sampleRate)
                    {
                        for (int x = 0; x < size.Width; x += sampleRate)
                        {
                            Color pixel = cellBitmap.GetPixel(x, y);
                            bytes.Add(pixel.R);
                            bytes.Add(pixel.G);
                            bytes.Add(pixel.B);
                        }
                    }

                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    {
                        var hashBytes = sha.ComputeHash(bytes.ToArray());
                        return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 验证装备身份是否发生变化
        /// </summary>
        public static bool VerifyIdentity(EquipmentCell current, EquipmentCell previous)
        {
            if (current == null || previous == null) return false;
            
            // 检查槽位是否变化
            if (current.SlotIndex != previous.SlotIndex)
                return false;

            // 检查视觉指纹
            if (!string.IsNullOrEmpty(current.Fingerprint) &&
                !string.IsNullOrEmpty(previous.Fingerprint) &&
                current.Fingerprint != previous.Fingerprint)
                return false;

            return true;
        }
    }
}