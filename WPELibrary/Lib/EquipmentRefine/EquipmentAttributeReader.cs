using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using WPELibrary.Lib.Vision;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备属性读取器：使用 OCR 识别炼化属性
    /// </summary>
    public class EquipmentAttributeReader
    {
        // 属性文本模式匹配（正则表达式）
        private static readonly Dictionary<string, Regex> AttributePatterns = new Dictionary<string, Regex>
        {
            { "Attack", new Regex(@"(攻击力|攻击|ATK|攻击力)\s*[+\-]?\s*(\d+)", RegexOptions.IgnoreCase) },
            { "Defense", new Regex(@"(防御力|防御|DEF|防御力)\s*[+\-]?\s*(\d+)", RegexOptions.IgnoreCase) },
            { "CritRate", new Regex(@"(暴击率|暴击|暴击几率)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "CritDamage", new Regex(@"(暴击伤害|暴伤)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "HitRate", new Regex(@"(命中率|命中)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "DodgeRate", new Regex(@"(闪避率|闪避)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "HpRecovery", new Regex(@"(生命回复|回血|HP回复)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "MpRecovery", new Regex(@"(法力回复|回蓝|MP回复)\s*[+\-]?\s*(\d+)%?", RegexOptions.IgnoreCase) },
            { "RareDegree", new Regex(@"(稀有度|稀有)\s*[+\-]?\s*(\d+)", RegexOptions.IgnoreCase) },
            { "Slot", new Regex(@"(槽位|洞数|孔数)\s*[+\-]?\s*(\d+)", RegexOptions.IgnoreCase) }
        };

        /// <summary>
        /// 从装备格子区域读取属性
        /// </summary>
        public static async Task<EquipmentAttributes> ReadAttributesAsync(
            Bitmap screenshot,
            Rectangle equipmentRegion,
            CancellationToken cancellationToken = default)
        {
            if (screenshot == null || equipmentRegion.Width <= 0 || equipmentRegion.Height <= 0)
                return new EquipmentAttributes { LastReadTime = DateTime.UtcNow };

            return await Task.Run(() =>
            {
                var attrs = new EquipmentAttributes
                {
                    LastReadTime = DateTime.UtcNow
                };

                try
                {
                    // 截取装备区域（放大以便 OCR 识别）
                    using (var regionBitmap = screenshot.Clone(equipmentRegion, screenshot.PixelFormat))
                    {
                        // 预处理：增强对比度 + 二值化
                        var processedBitmap = PreprocessForOCR(regionBitmap);
                        
                        // 模拟 OCR 识别（实际接入需要 VisionTextRecognizer）
                        // 此处返回结构化的属性数据
                        attrs = ExtractFromOCRRegion(processedBitmap, equipmentRegion);
                        attrs.AttributeHash = attrs.CalculateHash();
                    }
                }
                catch (Exception ex)
                {
                    // OCR 失败，记录错误但不崩溃
                    attrs.AttributeHash = "read_failed";
                    attrs.LastReadTime = DateTime.UtcNow;
                }

                return attrs;
            }, cancellationToken);
        }

        /// <summary>
        /// OCR 预处理：提升识别准确率
        /// </summary>
        private static Bitmap PreprocessForOCR(Bitmap source)
        {
            // 创建灰度版本
            using (var grayBitmap = ConvertToGrayscale(source))
            {
                // 自适应阈值二值化
                return ApplyAdaptiveThreshold(grayBitmap);
            }
        }

        /// <summary>
        /// 转换为灰度图
        /// </summary>
        private static Bitmap ConvertToGrayscale(Bitmap source)
        {
            var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color color = source.GetPixel(x, y);
                    byte gray = (byte)(color.R * 0.299 + color.G * 0.587 + color.B * 0.114);
                    result.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
                }
            }

            return result;
        }

        /// <summary>
        /// 自适应阈值二值化
        /// </summary>
        private static Bitmap ApplyAdaptiveThreshold(Bitmap grayBitmap)
        {
            var result = new Bitmap(grayBitmap.Width, grayBitmap.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            int blockSize = 11;
            double c = 2.0;

            for (int y = 0; y < grayBitmap.Height; y++)
            {
                for (int x = 0; x < grayBitmap.Width; x++)
                {
                    // 计算局部均值
                    int sum = 0;
                    int count = 0;
                    for (int dy = -blockSize; dy <= blockSize; dy++)
                    {
                        for (int dx = -blockSize; dx <= blockSize; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx >= 0 && nx < grayBitmap.Width && ny >= 0 && ny < grayBitmap.Height)
                            {
                                sum += grayBitmap.GetPixel(nx, ny).R;
                                count++;
                            }
                        }
                    }

                    int mean = sum / count;
                    int value = grayBitmap.GetPixel(x, y).R;
                    int binary = value > (mean - c) ? 255 : 0;
                    result.SetPixel(x, y, Color.FromArgb(binary, binary, binary));
                }
            }

            return result;
        }

        /// <summary>
        /// 从 OCR 区域提取属性（模拟）
        /// 实际接入时需要调用 VisionTextRecognizer
        /// </summary>
        private static EquipmentAttributes ExtractFromOCRRegion(Bitmap processedBitmap, Rectangle originalRegion)
        {
            // TODO: 实际接入 VisionTextRecognizer
            // 此处返回带默认值的空属性对象，后续替换为真实 OCR 结果

            return new EquipmentAttributes
            {
                AttackPower = 0,
                DefensePower = 0,
                CritRate = 0,
                CritDamage = 0,
                HitRate = 0,
                DodgeRate = 0,
                HpRecovery = 0,
                MpRecovery = 0,
                RareDegree = 0,
                InstanceId = string.Empty,
                EquipmentName = string.Empty,
                EquipmentType = string.Empty,
                EquipmentQuality = string.Empty
            };
        }

        /// <summary>
        /// 解析 OCR 文本中的属性
        /// </summary>
        public static EquipmentAttributes ParseAttributeText(string ocrText, string instanceId = "")
        {
            var attrs = new EquipmentAttributes
            {
                InstanceId = instanceId,
                LastReadTime = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(ocrText))
                return attrs;

            // 遍历所有模式匹配
            foreach (var pair in AttributePatterns)
            {
                Match match = pair.Value.Match(ocrText);
                if (match.Success && match.Groups.Count >= 3)
                {
                    if (int.TryParse(match.Groups[2].Value, out int value))
                    {
                        switch (pair.Key)
                        {
                            case "Attack": attrs.AttackPower = value; break;
                            case "Defense": attrs.DefensePower = value; break;
                            case "CritRate": attrs.CritRate = value; break;
                            case "CritDamage": attrs.CritDamage = value; break;
                            case "HitRate": attrs.HitRate = value; break;
                            case "DodgeRate": attrs.DodgeRate = value; break;
                            case "HpRecovery": attrs.HpRecovery = value; break;
                            case "MpRecovery": attrs.MpRecovery = value; break;
                            case "RareDegree": attrs.RareDegree = value; break;
                            case "Slot": // Slot 暂未定义在 EquipmentAttributes 中，可忽略或扩展
                                break;
                        }
                    }
                }
            }

            attrs.AttributeHash = attrs.CalculateHash();
            return attrs;
        }

        /// <summary>
        /// 比较两组属性是否不同
        /// </summary>
        public static bool AttributesChanged(EquipmentAttributes before, EquipmentAttributes after)
        {
            if (before == null || after == null) return false;
            return before.AttributeHash != after.AttributeHash;
        }

        /// <summary>
        /// 打印属性摘要
        /// </summary>
        public static string FormatAttributes(EquipmentAttributes attrs)
        {
            if (attrs == null) return "null";

            return $"Attack:{attrs.AttackPower} Def:{attrs.DefensePower} " +
                   $"CritR:{attrs.CritRate}% CritD:{attrs.CritDamage}% " +
                   $"Hit:{attrs.HitRate}% Dodge:{attrs.DodgeRate}% " +
                   $"HP:{attrs.HpRecovery}% MP:{attrs.MpRecovery}% " +
                   $"Rare:{attrs.RareDegree}";
        }
    }
}