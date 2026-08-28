using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.PetSkillBook
{
    /// <summary>
    /// 五行升级可配置的测试适配器
    /// 提供确定性、可脚本化的测试场景
    /// </summary>
    public class WuxingUpgradeTestActionAdapter : IWuxingUpgradeActionAdapter
    {
        private readonly List<WuxingUpgradeOperation> _operationHistory = new List<WuxingUpgradeOperation>();
        private WuxingUpgradeResult _nextResult = WuxingUpgradeResult.Accepted;
        private bool _simulateDelay = true;
        private int _delayMs = 100;

        public void SetNextResult(WuxingUpgradeResult result)
        {
            _nextResult = result;
        }

        public void SetSimulateDelay(bool simulate)
        {
            _simulateDelay = simulate;
        }

        public void SetDelayMs(int delay)
        {
            _delayMs = delay;
        }

        public Task<WuxingUpgradeResult> SubmitElementUpgradeAsync(
            WuxingUpgradeOperation operation,
            CancellationToken cancellationToken)
        {
            _operationHistory.Add(operation);

            if (_simulateDelay)
            {
                Thread.Sleep(_delayMs);
            }

            return Task.FromResult(_nextResult);
        }

        public IReadOnlyList<WuxingUpgradeOperation> OperationHistory => _operationHistory;
        public int OperationCount => _operationHistory.Count;
        
        public void Reset()
        {
            _operationHistory.Clear();
            _nextResult = WuxingUpgradeResult.Accepted;
        }
    }

    /// <summary>
    /// 五行升级简化版测试适配器（仅用于测试状态机逻辑）
    /// </summary>
    public class WuxingUpgradeSimpleTestAdapter : IWuxingUpgradeActionAdapter
    {
        public int WoodSent { get; private set; }
        public int WaterSent { get; private set; }
        public int SoilSent { get; private set; }
        public int GoldSent { get; private set; }
        public int FireSent { get; private set; }
        public int OverallSent { get; private set; }
        public int TotalSent => WoodSent + WaterSent + SoilSent + GoldSent + FireSent + OverallSent;

        public Task<WuxingUpgradeResult> SubmitElementUpgradeAsync(
            WuxingUpgradeOperation operation,
            CancellationToken cancellationToken)
        {
            if (operation.IsOverallUpgrade)
            {
                OverallSent++;
            }
            else
            {
                switch (operation.ElementType)
                {
                    case ElementalType.Wood: WoodSent++; break;
                    case ElementalType.Water: WaterSent++; break;
                    case ElementalType.Soil: SoilSent++; break;
                    case ElementalType.Gold: GoldSent++; break;
                    case ElementalType.Fire: FireSent++; break;
                }
            }

            return Task.FromResult(WuxingUpgradeResult.Accepted);
        }

        public void Reset()
        {
            WoodSent = 0;
            WaterSent = 0;
            SoilSent = 0;
            GoldSent = 0;
            FireSent = 0;
            OverallSent = 0;
        }

        public int GetCountForElement(ElementalType type)
        {
            switch (type)
            {
                case ElementalType.Wood: return WoodSent;
                case ElementalType.Water: return WaterSent;
                case ElementalType.Soil: return SoilSent;
                case ElementalType.Gold: return GoldSent;
                case ElementalType.Fire: return FireSent;
                case ElementalType.Overall: return OverallSent;
                default: return 0;
            }
        }
    }
}