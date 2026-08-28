using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 装备炼化预设的本地 JSON 序列化器。
    /// 只保存预设配置，不保存游戏快照，也不包含真实发送授权。
    /// </summary>
    public static class EquipmentRefinePresetSerializer
    {
        private const string JsonDateFormat = "yyyy-MM-dd HH:mm:ss";

        public static string Serialize(EquipmentRefinePreset preset)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            return JsonConvert.SerializeObject(
                preset,
                Formatting.Indented,
                CreateSettings());
        }

        public static bool TryDeserialize(
            string json,
            out EquipmentRefinePreset preset,
            out string error)
        {
            preset = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "预设内容为空。";
                return false;
            }

            try
            {
                EquipmentRefinePreset loaded = JsonConvert.DeserializeObject<EquipmentRefinePreset>(
                    json,
                    CreateSettings());
                if (loaded == null)
                {
                    error = "预设内容无法解析为有效对象。";
                    return false;
                }

                if (loaded.SchemaVersionProperty > EquipmentRefinePreset.SchemaVersion)
                {
                    error = string.Format(
                        "预设 schema 版本过高：{0} > {1}。",
                        loaded.SchemaVersionProperty,
                        EquipmentRefinePreset.SchemaVersion);
                    return false;
                }

                if (loaded.Rules == null)
                {
                    loaded.Rules = new System.Collections.Generic.List<RefineRule>();
                }
                foreach (RefineRule rule in loaded.Rules)
                {
                    if (rule == null)
                    {
                        error = "炼化规则不能包含空项。";
                        return false;
                    }
                    string ruleError;
                    if (!rule.IsValid(out ruleError))
                    {
                        error = ruleError;
                        return false;
                    }
                }
                if (loaded.Target == null)
                {
                    loaded.Target = new EquipmentRefineDetector.EquipmentTargetSelector();
                }
                if (loaded.BagTarget == null)
                {
                    loaded.BagTarget = new EquipmentRefineDetector.BagTargetSelector();
                }
                if (loaded.VerifiedAttributeFields == null)
                {
                    loaded.VerifiedAttributeFields = new System.Collections.Generic.Dictionary<string, TargetAttribute>(
                        StringComparer.Ordinal);
                }
                if (!Enum.IsDefined(typeof(RuleLogic), loaded.RuleLogic))
                {
                    error = "预设规则组合逻辑无效。";
                    return false;
                }

                preset = loaded;
                return true;
            }
            catch (JsonException ex)
            {
                error = "预设 JSON 解析失败: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "预设加载失败: " + ex.Message;
                return false;
            }
        }

        public static EquipmentRefinePreset DeserializeClone(EquipmentRefinePreset source)
        {
            if (source == null) return null;

            EquipmentRefinePreset clone;
            string error;
            if (!TryDeserialize(Serialize(source), out clone, out error))
            {
                throw new InvalidOperationException(error);
            }

            return clone;
        }

        public static void SaveToFile(EquipmentRefinePreset preset, string filePath)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("文件路径为空。", "filePath");
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, Serialize(preset), new UTF8Encoding(true));
        }

        public static bool TryLoadFromFile(
            string filePath,
            out EquipmentRefinePreset preset,
            out string error)
        {
            preset = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "文件路径为空。";
                return false;
            }
            if (!File.Exists(filePath))
            {
                error = "预设文件不存在: " + filePath;
                return false;
            }

            try
            {
                return TryDeserialize(
                    File.ReadAllText(filePath, Encoding.UTF8),
                    out preset,
                    out error);
            }
            catch (IOException ex)
            {
                error = "读取预设文件失败: " + ex.Message;
                return false;
            }
        }

        private static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                DateFormatString = JsonDateFormat,
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            };
        }
    }
}
