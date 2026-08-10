using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Xml.Linq;

namespace WPELibrary.Lib
{
    public static partial class Socket_Cache
    {
        public static partial class DataBase
        {
            public static bool CreateTable_DynamicVariables()
            {
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(conStr))
                    using (SQLiteCommand cmd = new SQLiteCommand(
                        "CREATE TABLE IF NOT EXISTS DynamicVariableDefinition (" +
                        "VariableId TEXT NOT NULL PRIMARY KEY,Symbol TEXT NOT NULL UNIQUE," +
                        "DisplayName TEXT,Description TEXT,Length INTEGER NOT NULL,IsAutoUpdate BOOLEAN NOT NULL DEFAULT 1);" +
                        "CREATE TABLE IF NOT EXISTS ExtractionRule (" +
                        "RuleId TEXT NOT NULL PRIMARY KEY,Name TEXT NOT NULL,IsEnabled BOOLEAN NOT NULL DEFAULT 1," +
                        "PatternBytes BLOB NOT NULL,WildcardMask BLOB NOT NULL,PacketType INTEGER NOT NULL);" +
                        "CREATE TABLE IF NOT EXISTS DynamicField (" +
                        "FieldId TEXT NOT NULL PRIMARY KEY,RuleId TEXT NOT NULL,VariableId TEXT NOT NULL," +
                        "Offset INTEGER NOT NULL,Length INTEGER NOT NULL,Description TEXT," +
                        "FOREIGN KEY (RuleId) REFERENCES ExtractionRule(RuleId)," +
                        "FOREIGN KEY (VariableId) REFERENCES DynamicVariableDefinition(VariableId));" +
                        "CREATE TABLE IF NOT EXISTS KnownVariableValue (" +
                        "VariableId TEXT NOT NULL,Value BLOB NOT NULL,Label TEXT," +
                        "FirstSeenUtc TEXT NOT NULL,LastSeenUtc TEXT NOT NULL,SeenCount INTEGER NOT NULL DEFAULT 0," +
                        "SourceRuleId TEXT,PRIMARY KEY (VariableId, Value));", conn))
                    {
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(CreateTable_DynamicVariables), ex.Message);
                    if (atomicSaveInProgress)
                    {
                        throw;
                    }
                    return false;
                }
            }

            public static bool LoadDynamicVariableData(
                out List<DynamicVariableDefinition> definitions,
                out List<ExtractionRule> rules,
                out List<KnownVariableValue> knownValues)
            {
                definitions = new List<DynamicVariableDefinition>();
                rules = new List<ExtractionRule>();
                knownValues = new List<KnownVariableValue>();
                try
                {
                    CreateTable_DynamicVariables();
                    using (SQLiteConnection conn = new SQLiteConnection(conStr))
                    {
                        conn.Open();
                        Dictionary<Guid, ExtractionRule> ruleMap = new Dictionary<Guid, ExtractionRule>();
                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "SELECT VariableId,Symbol,DisplayName,Description,Length,IsAutoUpdate FROM DynamicVariableDefinition ORDER BY Symbol;", conn))
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Guid id;
                                if (!Guid.TryParse(reader["VariableId"].ToString(), out id) || id == Guid.Empty)
                                {
                                    continue;
                                }
                                definitions.Add(new DynamicVariableDefinition
                                {
                                    VariableId = id,
                                    Symbol = reader["Symbol"].ToString(),
                                    DisplayName = ReadString(reader["DisplayName"]),
                                    Description = ReadString(reader["Description"]),
                                    Length = Convert.ToInt32(reader["Length"]),
                                    IsAutoUpdateEnabled = Convert.ToBoolean(reader["IsAutoUpdate"])
                                });
                            }
                        }

                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "SELECT RuleId,Name,IsEnabled,PatternBytes,WildcardMask,PacketType FROM ExtractionRule ORDER BY Name,RuleId;", conn))
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Guid id;
                                if (!Guid.TryParse(reader["RuleId"].ToString(), out id) || id == Guid.Empty)
                                {
                                    continue;
                                }
                                ExtractionRule rule = new ExtractionRule
                                {
                                    RuleId = id,
                                    Name = ReadString(reader["Name"]),
                                    IsEnabled = Convert.ToBoolean(reader["IsEnabled"]),
                                    PatternBytes = reader["PatternBytes"] as byte[] ?? new byte[0],
                                    WildcardMask = reader["WildcardMask"] as byte[] ?? new byte[0],
                                    PacketType = (Socket_Cache.SocketPacket.PacketType)Convert.ToInt32(reader["PacketType"])
                                };
                                rules.Add(rule);
                                ruleMap[id] = rule;
                            }
                        }

                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "SELECT FieldId,RuleId,VariableId,Offset,Length,Description FROM DynamicField ORDER BY RuleId,Offset;", conn))
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Guid fieldId;
                                Guid ruleId;
                                Guid variableId;
                                if (!Guid.TryParse(reader["FieldId"].ToString(), out fieldId) ||
                                    !Guid.TryParse(reader["RuleId"].ToString(), out ruleId) ||
                                    !Guid.TryParse(reader["VariableId"].ToString(), out variableId))
                                {
                                    continue;
                                }
                                ExtractionRule rule;
                                if (!ruleMap.TryGetValue(ruleId, out rule))
                                {
                                    continue;
                                }
                                rule.Fields.Add(new DynamicField
                                {
                                    FieldId = fieldId,
                                    VariableId = variableId,
                                    Offset = Convert.ToInt32(reader["Offset"]),
                                    Length = Convert.ToInt32(reader["Length"]),
                                    Description = ReadString(reader["Description"])
                                });
                            }
                        }

                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "SELECT VariableId,Value,Label,FirstSeenUtc,LastSeenUtc,SeenCount,SourceRuleId FROM KnownVariableValue ORDER BY LastSeenUtc DESC;", conn))
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Guid variableId;
                                Guid sourceRuleId;
                                if (!Guid.TryParse(reader["VariableId"].ToString(), out variableId))
                                {
                                    continue;
                                }
                                Guid.TryParse(ReadString(reader["SourceRuleId"]), out sourceRuleId);
                                knownValues.Add(new KnownVariableValue
                                {
                                    VariableId = variableId,
                                    Value = reader["Value"] as byte[] ?? new byte[0],
                                    Label = ReadString(reader["Label"]),
                                    FirstSeenUtc = DynamicVariableFormatting.ParseDate(reader["FirstSeenUtc"], DateTime.UtcNow),
                                    LastSeenUtc = DynamicVariableFormatting.ParseDate(reader["LastSeenUtc"], DateTime.UtcNow),
                                    SeenCount = Convert.ToInt64(reader["SeenCount"]),
                                    SourceRuleId = sourceRuleId
                                });
                            }
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(LoadDynamicVariableData), ex.Message);
                    return false;
                }
            }

            public static bool SaveDynamicVariableData(
                IEnumerable<DynamicVariableDefinition> definitions,
                IEnumerable<ExtractionRule> rules,
                IEnumerable<KnownVariableValue> knownValues)
            {
                try
                {
                    CreateTable_DynamicVariables();
                    using (SQLiteConnection conn = new SQLiteConnection(conStr))
                    {
                        conn.Open();
                        using (SQLiteTransaction transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                ExecuteWithTransaction(conn, transaction,
                                    "DELETE FROM DynamicField;DELETE FROM ExtractionRule;DELETE FROM DynamicVariableDefinition;DELETE FROM KnownVariableValue;");

                                foreach (DynamicVariableDefinition definition in definitions ?? Enumerable.Empty<DynamicVariableDefinition>())
                                {
                                    using (SQLiteCommand cmd = new SQLiteCommand(
                                        "INSERT INTO DynamicVariableDefinition (VariableId,Symbol,DisplayName,Description,Length,IsAutoUpdate) VALUES (@VariableId,@Symbol,@DisplayName,@Description,@Length,@IsAutoUpdate);", conn, transaction))
                                    {
                                        AddDefinitionParameters(cmd, definition);
                                        cmd.ExecuteNonQuery();
                                    }
                                }

                                foreach (ExtractionRule rule in rules ?? Enumerable.Empty<ExtractionRule>())
                                {
                                    using (SQLiteCommand cmd = new SQLiteCommand(
                                        "INSERT INTO ExtractionRule (RuleId,Name,IsEnabled,PatternBytes,WildcardMask,PacketType) VALUES (@RuleId,@Name,@IsEnabled,@PatternBytes,@WildcardMask,@PacketType);", conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@RuleId", rule.RuleId.ToString("N"));
                                        cmd.Parameters.AddWithValue("@Name", rule.Name ?? string.Empty);
                                        cmd.Parameters.AddWithValue("@IsEnabled", rule.IsEnabled);
                                        cmd.Parameters.AddWithValue("@PatternBytes", rule.PatternBytes ?? new byte[0]);
                                        cmd.Parameters.AddWithValue("@WildcardMask", rule.WildcardMask ?? new byte[0]);
                                        cmd.Parameters.AddWithValue("@PacketType", (int)rule.PacketType);
                                        cmd.ExecuteNonQuery();
                                    }
                                    foreach (DynamicField field in rule.Fields ?? new List<DynamicField>())
                                    {
                                        using (SQLiteCommand cmd = new SQLiteCommand(
                                            "INSERT INTO DynamicField (FieldId,RuleId,VariableId,Offset,Length,Description) VALUES (@FieldId,@RuleId,@VariableId,@Offset,@Length,@Description);", conn, transaction))
                                        {
                                            cmd.Parameters.AddWithValue("@FieldId", field.FieldId.ToString("N"));
                                            cmd.Parameters.AddWithValue("@RuleId", rule.RuleId.ToString("N"));
                                            cmd.Parameters.AddWithValue("@VariableId", field.VariableId.ToString("N"));
                                            cmd.Parameters.AddWithValue("@Offset", field.Offset);
                                            cmd.Parameters.AddWithValue("@Length", field.Length);
                                            cmd.Parameters.AddWithValue("@Description", field.Description ?? string.Empty);
                                            cmd.ExecuteNonQuery();
                                        }
                                    }
                                }

                                foreach (KnownVariableValue value in knownValues ?? Enumerable.Empty<KnownVariableValue>())
                                {
                                    InsertKnownValue(conn, transaction, value);
                                }

                                transaction.Commit();
                                return true;
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(SaveDynamicVariableData), ex.Message);
                    if (atomicSaveInProgress)
                    {
                        throw;
                    }
                    return false;
                }
            }

            public static bool UpsertDynamicKnownValues(IEnumerable<KnownVariableValue> values)
            {
                List<KnownVariableValue> list = (values ?? Enumerable.Empty<KnownVariableValue>()).ToList();
                if (list.Count == 0)
                {
                    return true;
                }
                try
                {
                    CreateTable_DynamicVariables();
                    using (SQLiteConnection conn = new SQLiteConnection(conStr))
                    {
                        conn.Open();
                        using (SQLiteTransaction transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                foreach (KnownVariableValue value in list)
                                {
                                    using (SQLiteCommand update = new SQLiteCommand(
                                        "UPDATE KnownVariableValue SET " +
                                        "FirstSeenUtc = CASE WHEN FirstSeenUtc > @FirstSeenUtc THEN @FirstSeenUtc ELSE FirstSeenUtc END," +
                                        "LastSeenUtc = CASE WHEN LastSeenUtc < @LastSeenUtc THEN @LastSeenUtc ELSE LastSeenUtc END," +
                                        "SeenCount = SeenCount + @SeenCount," +
                                        "SourceRuleId = @SourceRuleId," +
                                        "Label = CASE WHEN @Label = '' THEN Label ELSE @Label END " +
                                        "WHERE VariableId = @VariableId AND Value = @Value;", conn, transaction))
                                    {
                                        update.Parameters.AddWithValue("@VariableId", value.VariableId.ToString("N"));
                                        update.Parameters.AddWithValue("@Value", value.Value ?? new byte[0]);
                                        update.Parameters.AddWithValue("@FirstSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.FirstSeenUtc));
                                        update.Parameters.AddWithValue("@LastSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.LastSeenUtc));
                                        update.Parameters.AddWithValue("@SeenCount", Math.Max(1, value.SeenCount));
                                        update.Parameters.AddWithValue("@SourceRuleId", value.SourceRuleId == Guid.Empty ? string.Empty : value.SourceRuleId.ToString("N"));
                                        update.Parameters.AddWithValue("@Label", value.Label ?? string.Empty);
                                        int updated = update.ExecuteNonQuery();
                                        if (updated == 0)
                                        {
                                            using (SQLiteCommand insert = new SQLiteCommand(
                                                "INSERT OR IGNORE INTO KnownVariableValue (VariableId,Value,Label,FirstSeenUtc,LastSeenUtc,SeenCount,SourceRuleId) VALUES (@VariableId,@Value,@Label,@FirstSeenUtc,@LastSeenUtc,@SeenCount,@SourceRuleId);", conn, transaction))
                                            {
                                                AddKnownValueParameters(insert, value);
                                                insert.ExecuteNonQuery();
                                            }
                                        }
                                    }
                                }
                                transaction.Commit();
                                return true;
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(UpsertDynamicKnownValues), ex.Message);
                    return false;
                }
            }

            private static void AddDefinitionParameters(SQLiteCommand cmd, DynamicVariableDefinition definition)
            {
                cmd.Parameters.AddWithValue("@VariableId", definition.VariableId.ToString("N"));
                cmd.Parameters.AddWithValue("@Symbol", definition.Symbol ?? string.Empty);
                cmd.Parameters.AddWithValue("@DisplayName", definition.DisplayName ?? string.Empty);
                cmd.Parameters.AddWithValue("@Description", definition.Description ?? string.Empty);
                cmd.Parameters.AddWithValue("@Length", definition.Length);
                cmd.Parameters.AddWithValue("@IsAutoUpdate", definition.IsAutoUpdateEnabled);
            }

            private static void InsertKnownValue(SQLiteConnection conn, SQLiteTransaction transaction, KnownVariableValue value)
            {
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "INSERT INTO KnownVariableValue (VariableId,Value,Label,FirstSeenUtc,LastSeenUtc,SeenCount,SourceRuleId) VALUES (@VariableId,@Value,@Label,@FirstSeenUtc,@LastSeenUtc,@SeenCount,@SourceRuleId);", conn, transaction))
                {
                    AddKnownValueParameters(cmd, value);
                    cmd.ExecuteNonQuery();
                }
            }

            private static void AddKnownValueParameters(SQLiteCommand cmd, KnownVariableValue value)
            {
                cmd.Parameters.AddWithValue("@VariableId", value.VariableId.ToString("N"));
                cmd.Parameters.AddWithValue("@Value", value.Value ?? new byte[0]);
                cmd.Parameters.AddWithValue("@Label", value.Label ?? string.Empty);
                cmd.Parameters.AddWithValue("@FirstSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.FirstSeenUtc));
                cmd.Parameters.AddWithValue("@LastSeenUtc", DynamicVariableFormatting.ToInvariantDate(value.LastSeenUtc));
                cmd.Parameters.AddWithValue("@SeenCount", Math.Max(0, value.SeenCount));
                cmd.Parameters.AddWithValue("@SourceRuleId", value.SourceRuleId == Guid.Empty ? string.Empty : value.SourceRuleId.ToString("N"));
            }

            private static void ExecuteWithTransaction(SQLiteConnection conn, SQLiteTransaction transaction, string sql)
            {
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            private static string ReadString(object value)
            {
                return value == null || value == DBNull.Value ? string.Empty : value.ToString();
            }

            private static string SerializeVariableBindings(IEnumerable<PresetVariableBinding> bindings)
            {
                XElement element = DynamicVariableSerialization.ToBindingsElement(bindings);
                return element == null ? string.Empty : element.ToString(SaveOptions.DisableFormatting);
            }

            internal static List<PresetVariableBinding> DeserializeVariableBindings(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return new List<PresetVariableBinding>();
                }
                try
                {
                    return DynamicVariableSerialization.FromBindingsElement(XElement.Parse(value));
                }
                catch
                {
                    return new List<PresetVariableBinding>();
                }
            }
        }
    }
}
