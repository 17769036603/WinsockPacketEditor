using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// 仅离线 fixture/占位格式，不是游戏真实协议。未来真实接入只替换此 adapter，
    /// 不得把真实二进制头、旧快照或猜测字段映射到这里。
    /// 格式：{format:"equipment-refine-fixture-v1",sequence,identity,stable,errorCode,cards[]}
    /// </summary>
    public sealed class JsonFixtureRefineResponseAdapter : IRefineResponseAdapter
    {
        private const string Format = "equipment-refine-fixture-v1";

        private sealed class WireEnvelope
        {
            public string format { get; set; }
            public long? sequence { get; set; }
            public string identity { get; set; }
            public bool? stable { get; set; }
            public string errorCode { get; set; }
            public List<WireCard> cards { get; set; }
        }

        private sealed class WireCard
        {
            public int? index { get; set; }
            public bool? complete { get; set; }
            public List<WireAttribute> attributes { get; set; }
        }

        private sealed class WireAttribute
        {
            public int? type { get; set; }
            public string name { get; set; }
            public int? value { get; set; }
        }

        public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null || frame.Length == 0)
            {
                error = "fixture_frame_empty";
                return false;
            }

            try
            {
                WireEnvelope wire = JsonConvert.DeserializeObject<WireEnvelope>(Encoding.UTF8.GetString(frame),
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (wire == null || wire.format != Format || !wire.sequence.HasValue || wire.sequence.Value <= 0 ||
                    string.IsNullOrWhiteSpace(wire.identity) || !wire.stable.HasValue || wire.cards == null)
                {
                    error = "fixture_schema_invalid";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(wire.errorCode) && wire.cards.Count != 20)
                {
                    error = "fixture_cards_incomplete";
                    return false;
                }

                RefineResponseEnvelope decoded = new RefineResponseEnvelope
                {
                    Sequence = wire.sequence.Value,
                    EquipmentIdentity = wire.identity,
                    ErrorCode = wire.errorCode ?? string.Empty,
                    IsStable = wire.stable.Value
                };
                foreach (WireCard card in wire.cards)
                {
                    if (card == null || !card.index.HasValue || !card.complete.HasValue || card.attributes == null)
                    {
                        error = "fixture_card_invalid";
                        return false;
                    }
                    RefineResponseCard decodedCard = new RefineResponseCard
                    {
                        CardIndex = card.index.Value,
                        IsComplete = card.complete.Value
                    };
                    foreach (WireAttribute attribute in card.attributes)
                    {
                        if (attribute == null || !attribute.type.HasValue || !Enum.IsDefined(typeof(TargetAttribute), attribute.type.Value) ||
                            attribute.type.Value == (int)TargetAttribute.Unknown ||
                            !attribute.value.HasValue)
                        {
                            error = "fixture_attribute_invalid";
                            return false;
                        }
                        decodedCard.Attributes.Add(new AttributeValue
                        {
                            Type = (TargetAttribute)attribute.type.Value,
                            Name = attribute.name ?? string.Empty,
                            CurrentValue = attribute.value.Value,
                            RawValue = attribute.value.Value
                        });
                    }
                    decoded.Cards.Add(decodedCard);
                }
                if (string.IsNullOrWhiteSpace(wire.errorCode) &&
                    (decoded.Cards.Select(card => card.CardIndex).Distinct().Count() != 20 ||
                     !decoded.Cards.Select(card => card.CardIndex).OrderBy(index => index)
                         .SequenceEqual(Enumerable.Range(1, 20))))
                {
                    error = "fixture_card_indexes_invalid";
                    return false;
                }
                response = decoded;
                return true;
            }
            catch (Exception)
            {
                response = null;
                error = "fixture_json_invalid";
                return false;
            }
        }
    }
}
