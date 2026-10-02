using System.Collections.Generic;
using System.Linq;

namespace WPELibrary
{
    /// <summary>Describes the explicitly compatible assistant run slots.</summary>
    public enum AssistantRunKind
    {
        Other = 0,
        TreasureMap = 1,
        MountRefine = 2,
        DelayOnly = 3,
        MountSpeedHold = 4
    }

    public static class AssistantRunConcurrencyPolicy
    {
        public static AssistantRunKind Classify(bool hasTreasureMapInstruction, bool isMountRefinePreset)
        {
            return Classify(
                hasTreasureMapInstruction,
                isMountRefinePreset,
                false,
                false);
        }

        public static AssistantRunKind Classify(
            bool hasTreasureMapInstruction,
            bool isMountRefinePreset,
            bool isMountSpeedHoldOnly,
            bool isDelayOnly)
        {
            if (hasTreasureMapInstruction == isMountRefinePreset)
            {
                if (hasTreasureMapInstruction || isMountSpeedHoldOnly == isDelayOnly)
                {
                    return AssistantRunKind.Other;
                }

                return isMountSpeedHoldOnly
                    ? AssistantRunKind.MountSpeedHold
                    : AssistantRunKind.DelayOnly;
            }

            if (isMountSpeedHoldOnly || isDelayOnly)
            {
                return AssistantRunKind.Other;
            }

            if (hasTreasureMapInstruction)
            {
                return AssistantRunKind.TreasureMap;
            }

            return AssistantRunKind.MountRefine;
        }

        public static bool CanRunTogether(AssistantRunKind left, AssistantRunKind right)
        {
            // ponytail: explicit pair allowlist; add pairs only after verifying their shared runtime resources.
            return (left == AssistantRunKind.TreasureMap && right == AssistantRunKind.MountRefine) ||
                (left == AssistantRunKind.MountRefine && right == AssistantRunKind.TreasureMap) ||
                (left == AssistantRunKind.MountSpeedHold && right == AssistantRunKind.DelayOnly) ||
                (left == AssistantRunKind.DelayOnly && right == AssistantRunKind.MountSpeedHold);
        }

        public static bool CanStart(
            IEnumerable<AssistantRunKind> activeKinds,
            AssistantRunKind candidateKind)
        {
            if (activeKinds == null)
            {
                return true;
            }

            return activeKinds.All(activeKind => CanRunTogether(activeKind, candidateKind));
        }
    }
}
