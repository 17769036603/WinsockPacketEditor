using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using WPELibrary.Lib.MountSpeed;

namespace WPELibrary.Lib.MountSpeed
{
    /// <summary>
    /// 坐骑速度预设 JSON 序列化器。
    /// 使用版本化 schema，未知字段忽略，缺失字段使用默认值。
    /// 保存/加载均为纯本地文本操作，不涉及游戏进程或网络。
    /// </summary>
    public static class MountSpeedPresetSerializer
    {
        private const string JsonDateFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 序列化为 JSON 字符串（缩进格式，UTF-8 语义）。
        /// </summary>
        public static string Serialize(MountSpeedPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            var settings = CreateSettings();
            return JsonConvert.SerializeObject(preset, Formatting.Indented, settings);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化。失败时返回 false，不抛出异常。
        /// </summary>
        public static bool TryDeserialize(string json, out MountSpeedPreset preset, out string error)
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
                var settings = CreateSettings();
                var loaded = JsonConvert.DeserializeObject<MountSpeedPreset>(json, settings);
                if (loaded == null)
                {
                    error = "预设内容无法解析为有效对象。";
                    return false;
                }

                if (loaded.SchemaVersionProperty > MountSpeedPreset.SchemaVersion)
                {
                    error = $"预设 schema 版本过高：{loaded.SchemaVersionProperty} > {MountSpeedPreset.SchemaVersion}。";
                    return false;
                }

                preset = loaded;
                return true;
            }
            catch (JsonException ex)
            {
                error = $"预设 JSON 解析失败: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"预设加载失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 通过序列化往返创建深拷贝。源为 null 时返回 null。
        /// </summary>
        public static MountSpeedPreset DeserializeClone(MountSpeedPreset source)
        {
            if (source == null)
            {
                return null;
            }

            string json = Serialize(source);
            MountSpeedPreset clone;
            string error;
            if (!TryDeserialize(json, out clone, out error))
            {
                throw new InvalidOperationException(error);
            }

            return clone;
        }

        /// <summary>
        /// 保存预设到文件（UTF-8 with BOM）。
        /// </summary>
        public static void SaveToFile(MountSpeedPreset preset, string filePath)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("文件路径为空。", nameof(filePath));

            var json = Serialize(preset);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, json, new UTF8Encoding(true));
        }

        /// <summary>
        /// 从文件加载预设。失败时返回 false，不抛出异常。
        /// </summary>
        public static bool TryLoadFromFile(string filePath, out MountSpeedPreset preset, out string error)
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
                error = $"预设文件不存在: {filePath}";
                return false;
            }

            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                return TryDeserialize(json, out preset, out error);
            }
            catch (IOException ex)
            {
                error = $"读取预设文件失败: {ex.Message}";
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