using System;

namespace WPELibrary.Lib.Vision
{
    /// <summary>
    /// 持久化在机器人指令行中的藏宝图静态配置。
    /// 真实发送授权、Socket、C6 会话和运行状态都不属于指令内容。
    /// </summary>
    public sealed class TreasureMapInstructionDefinition
    {
        public TreasureMapInstructionDefinition(
            TreasureMapInstructionVersion version,
            TreasureMapExecutionMode mode,
            TreasureControlMode control = TreasureControlMode.Exclusive,
            TreasureEvidenceMode evidence = TreasureEvidenceMode.Shadow)
        {
            if (version != TreasureMapInstructionVersion.V1 &&
                version != TreasureMapInstructionVersion.V2 &&
                version != TreasureMapInstructionVersion.V3)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (mode != TreasureMapExecutionMode.CurrentSnapshot &&
                mode != TreasureMapExecutionMode.Continuous)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (control != TreasureControlMode.Exclusive &&
                control != TreasureControlMode.Cooperative)
            {
                throw new ArgumentOutOfRangeException(nameof(control));
            }

            if (evidence != TreasureEvidenceMode.Shadow &&
                evidence != TreasureEvidenceMode.Enforced)
            {
                throw new ArgumentOutOfRangeException(nameof(evidence));
            }

            this.Version = version;
            this.Mode = mode;
            this.Control = control;
            this.Evidence = evidence;
        }

        public TreasureMapInstructionVersion Version { get; private set; }

        public TreasureMapExecutionMode Mode { get; private set; }

        public TreasureControlMode Control { get; private set; }

        public TreasureEvidenceMode Evidence { get; private set; }
    }

    /// <summary>
    /// 藏宝图机器人指令内容的版本化编解码器。
    /// </summary>
    public static class TreasureMapInstructionCodec
    {
        public const string Prefix = "TREASURE_MAP_V2|";
        public const string V3Prefix = "TREASURE_MAP_V3|";
        public const string LegacyPresetId = "treasure-map-pure-packet";

        public static string Encode(TreasureMapExecutionMode mode)
        {
            if (mode != TreasureMapExecutionMode.CurrentSnapshot &&
                mode != TreasureMapExecutionMode.Continuous)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return Prefix + "mode=" + EncodeMode(mode);
        }

        public static string Encode(
            TreasureMapExecutionMode mode,
            TreasureControlMode control,
            TreasureEvidenceMode evidence)
        {
            ValidateMode(mode);
            ValidateControl(control);
            ValidateEvidence(evidence);
            return V3Prefix +
                "mode=" + EncodeMode(mode) +
                "|control=" + EncodeControl(control) +
                "|evidence=" + EncodeEvidence(evidence);
        }

        public static bool TryDecode(
            string content,
            out TreasureMapInstructionDefinition definition)
        {
            definition = null;
            string normalized = (content ?? string.Empty).Trim();

            // 兼容旧版纯封包预设。旧版语义是持续监听，不能把它误判为新的一次模式。
            if (string.Equals(normalized, LegacyPresetId, StringComparison.Ordinal))
            {
                definition = new TreasureMapInstructionDefinition(
                    TreasureMapInstructionVersion.V1,
                    TreasureMapExecutionMode.Continuous);
                return true;
            }

            TreasureMapInstructionVersion version;
            string prefix;
            if (normalized.StartsWith(Prefix, StringComparison.Ordinal))
            {
                version = TreasureMapInstructionVersion.V2;
                prefix = Prefix;
            }
            else if (normalized.StartsWith(V3Prefix, StringComparison.Ordinal))
            {
                version = TreasureMapInstructionVersion.V3;
                prefix = V3Prefix;
            }
            else
            {
                return false;
            }

            string payload = normalized.Substring(prefix.Length);
            string[] fields = payload.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            TreasureMapExecutionMode mode = TreasureMapExecutionMode.Continuous;
            TreasureControlMode control = TreasureControlMode.Exclusive;
            TreasureEvidenceMode evidence = TreasureEvidenceMode.Shadow;
            bool hasMode = false;
            bool hasControl = false;
            bool hasEvidence = false;
            foreach (string field in fields)
            {
                int separator = field.IndexOf('=');
                if (separator <= 0 || separator == field.Length - 1)
                {
                    return false;
                }

                string key = field.Substring(0, separator).Trim();
                string value = field.Substring(separator + 1).Trim();
                if (string.Equals(key, "mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasMode || !TryDecodeMode(value, out mode))
                    {
                        return false;
                    }
                    hasMode = true;
                    continue;
                }

                if (version == TreasureMapInstructionVersion.V3 &&
                    string.Equals(key, "control", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasControl || !TryDecodeControl(value, out control))
                    {
                        return false;
                    }
                    hasControl = true;
                    continue;
                }

                if (version == TreasureMapInstructionVersion.V3 &&
                    string.Equals(key, "evidence", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasEvidence || !TryDecodeEvidence(value, out evidence))
                    {
                        return false;
                    }
                    hasEvidence = true;
                    continue;
                }

                return false;
            }

            if (!hasMode ||
                (version == TreasureMapInstructionVersion.V3 &&
                 (!hasControl || !hasEvidence)))
            {
                return false;
            }

            definition = new TreasureMapInstructionDefinition(
                version,
                mode,
                control,
                evidence);
            return true;
        }

        public static TreasureMapInstructionDefinition DecodeOrDefault(string content)
        {
            TreasureMapInstructionDefinition definition;
            if (TryDecode(content, out definition))
            {
                return definition;
            }

            return new TreasureMapInstructionDefinition(
                TreasureMapInstructionVersion.V2,
                TreasureMapExecutionMode.Continuous);
        }

        private static string EncodeMode(TreasureMapExecutionMode mode)
        {
            return mode == TreasureMapExecutionMode.Continuous
                ? "continuous"
                : "current";
        }

        private static string EncodeControl(TreasureControlMode control)
        {
            return control == TreasureControlMode.Cooperative
                ? "cooperative"
                : "exclusive";
        }

        private static string EncodeEvidence(TreasureEvidenceMode evidence)
        {
            return evidence == TreasureEvidenceMode.Enforced
                ? "enforced"
                : "shadow";
        }

        private static void ValidateMode(TreasureMapExecutionMode mode)
        {
            if (mode != TreasureMapExecutionMode.CurrentSnapshot &&
                mode != TreasureMapExecutionMode.Continuous)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static void ValidateControl(TreasureControlMode control)
        {
            if (control != TreasureControlMode.Exclusive &&
                control != TreasureControlMode.Cooperative)
            {
                throw new ArgumentOutOfRangeException(nameof(control));
            }
        }

        private static void ValidateEvidence(TreasureEvidenceMode evidence)
        {
            if (evidence != TreasureEvidenceMode.Shadow &&
                evidence != TreasureEvidenceMode.Enforced)
            {
                throw new ArgumentOutOfRangeException(nameof(evidence));
            }
        }

        private static bool TryDecodeMode(
            string value,
            out TreasureMapExecutionMode mode)
        {
            if (string.Equals(value, "continuous", StringComparison.OrdinalIgnoreCase))
            {
                mode = TreasureMapExecutionMode.Continuous;
                return true;
            }

            if (string.Equals(value, "current", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "snapshot", StringComparison.OrdinalIgnoreCase))
            {
                mode = TreasureMapExecutionMode.CurrentSnapshot;
                return true;
            }

            mode = TreasureMapExecutionMode.Continuous;
            return false;
        }

        private static bool TryDecodeControl(
            string value,
            out TreasureControlMode control)
        {
            if (string.Equals(value, "exclusive", StringComparison.OrdinalIgnoreCase))
            {
                control = TreasureControlMode.Exclusive;
                return true;
            }

            if (string.Equals(value, "cooperative", StringComparison.OrdinalIgnoreCase))
            {
                control = TreasureControlMode.Cooperative;
                return true;
            }

            control = TreasureControlMode.Exclusive;
            return false;
        }

        private static bool TryDecodeEvidence(
            string value,
            out TreasureEvidenceMode evidence)
        {
            if (string.Equals(value, "shadow", StringComparison.OrdinalIgnoreCase))
            {
                evidence = TreasureEvidenceMode.Shadow;
                return true;
            }

            if (string.Equals(value, "enforced", StringComparison.OrdinalIgnoreCase))
            {
                evidence = TreasureEvidenceMode.Enforced;
                return true;
            }

            evidence = TreasureEvidenceMode.Shadow;
            return false;
        }
    }
}
