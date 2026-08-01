using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using WPELibrary.Lib;

namespace WPELibrary
{
    public partial class Socket_Form
    {
        private TabPage tpByteSweepLog;
        private DataGridView dgvByteSweepLog;
        private BindingList<Socket_ByteSweepLogEntry> byteSweepLogEntries;
        private Socket_ByteSweepLogEntry byteSweepLiveLog;

        private void InitByteSweepLogUI()
        {
            this.byteSweepLogEntries = new BindingList<Socket_ByteSweepLogEntry>();
            this.tpByteSweepLog = new TabPage
            {
                Name = "tpByteSweepLog",
                Text = UiText("ByteSweep_LogTitle"),
                Padding = new Padding(3),
                BackColor = SystemColors.Control
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(3, 3, 3, 0)
            };
            Button clear = new Button
            {
                Name = "bClearByteSweepLog",
                Text = UiText("ByteSweep_LogClear"),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            clear.Click += this.ClearByteSweepLog_Click;
            Button export = new Button
            {
                Name = "bExportByteSweepLog",
                Text = UiText("ByteSweep_LogExport"),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            export.Click += this.ExportByteSweepLog_Click;
            actions.Controls.Add(clear);
            actions.Controls.Add(export);

            this.dgvByteSweepLog = new DataGridView
            {
                Name = "dgvByteSweepLog",
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 28,
                AccessibleName = UiText("ByteSweep_LogTitle")
            };
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogTime", UiText("ByteSweep_LogTime"), "Time", 88));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogState", UiText("ByteSweep_LogState"), "State", 78));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogPreset", UiText("ByteSweep_LogPreset"), "PresetName", 130));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogMode", UiText("ByteSweep_LogMode"), "Mode", 100));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogProgress", UiText("ByteSweep_LogProgress"), "Progress", 390));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogTotal", UiText("ByteSweep_LogTotal"), "TotalSend", 75));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogSuccess", UiText("ByteSweep_LogSuccess"), "Success", 75));
            this.dgvByteSweepLog.Columns.Add(CreateLogColumn("cByteSweepLogFailure", UiText("ByteSweep_LogFailure"), "Failure", 75));
            DataGridViewTextBoxColumn detail = CreateLogColumn("cByteSweepLogDetail", UiText("ByteSweep_LogDetail"), "Detail", 180);
            detail.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            this.dgvByteSweepLog.Columns.Add(detail);
            this.dgvByteSweepLog.DataSource = this.byteSweepLogEntries;
            this.dgvByteSweepLog.CellFormatting += this.dgvByteSweepLog_CellFormatting;

            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(this.dgvByteSweepLog, 0, 1);
            this.tpByteSweepLog.Controls.Add(layout);
            this.tcAdvancedTools.Controls.Add(this.tpByteSweepLog);

            Socket_ByteSweepRuntime.Current.StateChanged += this.ByteSweepRuntime_StateChanged;
            Socket_ByteSweepRuntime.Current.ProgressChanged += this.ByteSweepRuntime_ProgressChanged;
            Socket_ByteSweepRuntime.Current.LogAdded += this.ByteSweepRuntime_LogAdded;
        }

        private static DataGridViewTextBoxColumn CreateLogColumn(string name, string header, string property, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = property,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    Padding = new Padding(3, 1, 3, 1)
                }
            };
        }

        private void ByteSweepRuntime_StateChanged(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            this.InvokeByteSweepLogAction(() =>
            {
                bool busy = snapshot.State == Socket_ByteSweepRuntimeState.Starting ||
                    snapshot.State == Socket_ByteSweepRuntimeState.Running ||
                    snapshot.State == Socket_ByteSweepRuntimeState.Stopping;
                this.byteSweepRunning = busy;
                if (busy && snapshot.PresetId != Guid.Empty)
                {
                    this.activeByteSweepPresetId = snapshot.PresetId;
                }
                else if (!busy)
                {
                    this.activeByteSweepPresetId = Guid.Empty;
                    this.tsByteSweepContext.Text = snapshot.Detail ?? string.Empty;
                }
                this.RefreshByteSweepView();
                if (snapshot.State != Socket_ByteSweepRuntimeState.Running &&
                    snapshot.State != Socket_ByteSweepRuntimeState.Starting &&
                    snapshot.State != Socket_ByteSweepRuntimeState.Stopping)
                {
                    this.byteSweepLiveLog = null;
                }
                this.dgvByteSweepLog.Invalidate();
            });
        }

        private void ByteSweepRuntime_ProgressChanged(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            this.InvokeByteSweepLogAction(() =>
            {
                this.UpdateByteSweepLiveLog(snapshot);
                if (snapshot.JobId != this.byteSweepJobId)
                {
                    this.tsByteSweepContext.Text = FormatRuntimeProgress(snapshot);
                }
            });
        }

        private void ByteSweepRuntime_LogAdded(Socket_ByteSweepLogEntry entry)
        {
            this.InvokeByteSweepLogAction(() =>
            {
                entry.State = FormatByteSweepState(entry.State);
                if (entry.Detail == "PresetSwitched")
                {
                    entry.Detail = UiText("ByteSweep_LogPresetChanged");
                }
                this.byteSweepLogEntries.Add(entry);
                this.TrimByteSweepLogEntries();
                this.AutoScrollDataGridView(this.dgvByteSweepLog, true);
            });
        }

        private void UpdateByteSweepLiveLog(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (this.byteSweepLiveLog == null ||
                !string.Equals(this.byteSweepLiveLog.PresetName, snapshot.PresetName, StringComparison.Ordinal) ||
                this.byteSweepLiveLog.Mode != snapshot.Mode)
            {
                this.byteSweepLiveLog = Socket_ByteSweepLogEntry.Create(
                    snapshot,
                    FormatByteSweepState(snapshot.State.ToString()),
                    FormatByteSweepProgress(snapshot),
                    UiText("ByteSweep_LogLive"));
                this.byteSweepLogEntries.Add(this.byteSweepLiveLog);
            }
            else
            {
                this.byteSweepLiveLog.State = FormatByteSweepState(snapshot.State.ToString());
                this.byteSweepLiveLog.Progress = FormatByteSweepProgress(snapshot);
                this.byteSweepLiveLog.TotalSend = snapshot.TotalSend;
                this.byteSweepLiveLog.Success = snapshot.Success;
                this.byteSweepLiveLog.Failure = snapshot.Failure;
                this.byteSweepLiveLog.Detail = UiText("ByteSweep_LogLive");
            }

            this.TrimByteSweepLogEntries();
            this.AutoScrollDataGridView(this.dgvByteSweepLog, true);
        }

        private static string FormatByteSweepProgress(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (snapshot.IsPairCombination)
            {
                return string.Format(
                    "A={0:X2} ({1}/{2}) · B={3:X2} ({4}/{5}) · {6}/{7}",
                    snapshot.PairFirstValue,
                    snapshot.PairFirstValueNumber,
                    snapshot.PairFirstValueCount,
                    snapshot.PairSecondValue,
                    snapshot.PairSecondValueNumber,
                    snapshot.PairSecondValueCount,
                    snapshot.TotalSend,
                    snapshot.PlannedTotal);
            }

            return string.Format(
                "字节 {0}/{1} · {2}/255 · {3}/{4}",
                snapshot.ByteNumber,
                snapshot.ByteCount,
                snapshot.CurrentValue.ToString("X2"),
                snapshot.TotalSend,
                snapshot.PlannedTotal);
        }

        private static string FormatRuntimeProgress(Socket_ByteSweepRuntimeSnapshot snapshot)
        {
            if (snapshot.IsPairCombination)
            {
                return string.Format(
                    UiText("ByteSweep_BatchPairProgress"),
                    ShortText(snapshot.PresetName, 10),
                    snapshot.CurrentLoop,
                    snapshot.LoopCount,
                    snapshot.PairFirstValue,
                    snapshot.PairFirstValueNumber,
                    snapshot.PairFirstValueCount,
                    snapshot.PairSecondValue,
                    snapshot.PairSecondValueNumber,
                    snapshot.PairSecondValueCount,
                    snapshot.TotalSend,
                    snapshot.PlannedTotal);
            }

            return string.Format(
                UiText("UI_SweepProgress"),
                ShortText(snapshot.PresetName, 10),
                snapshot.CurrentLoop,
                snapshot.LoopCount,
                snapshot.ByteNumber,
                snapshot.ByteCount,
                snapshot.ValueNumber);
        }

        private static string FormatByteSweepState(string state)
        {
            switch (state)
            {
                case "Starting": return UiText("ByteSweep_LogStarting");
                case "Running": return UiText("ByteSweep_LogRunning");
                case "Stopping": return UiText("ByteSweep_LogStopping");
                case "Completed": return UiText("ByteSweep_LogCompleted");
                case "Cancelled": return UiText("ByteSweep_LogCancelled");
                case "Faulted": return UiText("ByteSweep_LogFaulted");
                default: return UiText("ByteSweep_LogIdle");
            }
        }

        private void TrimByteSweepLogEntries()
        {
            while (this.byteSweepLogEntries.Count > 500)
            {
                this.byteSweepLogEntries.RemoveAt(0);
            }
        }

        private void ClearByteSweepLog_Click(object sender, EventArgs e)
        {
            this.byteSweepLogEntries.Clear();
            this.byteSweepLiveLog = null;
        }

        private void ExportByteSweepLog_Click(object sender, EventArgs e)
        {
            if (this.byteSweepLogEntries.Count == 0)
            {
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "递进日志.csv",
                AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("时间,状态,预设,模式,进度,总发送,成功,失败,详情");
                foreach (Socket_ByteSweepLogEntry item in this.byteSweepLogEntries)
                {
                    csv.AppendLine(string.Join(",", new[]
                    {
                        EscapeCsv(item.Time), EscapeCsv(item.State), EscapeCsv(item.PresetName),
                        EscapeCsv(item.Mode), EscapeCsv(item.Progress), item.TotalSend.ToString(),
                        item.Success.ToString(), item.Failure.ToString(), EscapeCsv(item.Detail)
                    }));
                }
                File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8);
            }
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private void dgvByteSweepLog_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= this.dgvByteSweepLog.Rows.Count)
            {
                return;
            }

            Socket_ByteSweepLogEntry entry =
                this.dgvByteSweepLog.Rows[e.RowIndex].DataBoundItem as Socket_ByteSweepLogEntry;
            if (entry == this.byteSweepLiveLog)
            {
                e.CellStyle.BackColor = Color.LightGoldenrodYellow;
                e.CellStyle.SelectionBackColor = Color.DarkOrange;
                e.CellStyle.SelectionForeColor = Color.Black;
            }
        }

        private void InvokeByteSweepLogAction(Action action)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
                // 窗口关闭过程中不再更新日志界面。
            }
        }
    }
}
