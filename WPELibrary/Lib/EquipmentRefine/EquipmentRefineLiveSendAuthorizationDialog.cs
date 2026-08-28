using System;
using System.Drawing;
using System.Windows.Forms;

namespace WPELibrary.Lib.EquipmentRefine
{
    /// <summary>
    /// Per-run confirmation for the equipment-refine live sender. The token is
    /// intentionally not persisted in a preset or assistant record.
    /// </summary>
    public sealed class EquipmentRefineLiveSendAuthorizationDialog : Form
    {
        private readonly TextBox _confirmationBox = new TextBox();

        private EquipmentRefineLiveSendAuthorizationDialog()
        {
            this.Text = "装备炼化真实发送确认";
            this.Width = 560;
            this.Height = 230;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            Label warning = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(10),
                Text = "本次启动已具备已验收模板和当前 Socket 路由，继续后可能发送装备炼化封包。\r\n"
                    + "授权只对本次运行有效，不会保存到预设。确认发送前请先确认目标装备、结果读取器和协议模板均正确。"
            };
            Label prompt = new Label
            {
                AutoSize = true,
                Text = "请输入确认文本：" + EquipmentRefineLiveSendAuthorization.RequiredConfirmationText,
                Padding = new Padding(10, 4, 10, 4)
            };
            this._confirmationBox.Dock = DockStyle.Top;
            this._confirmationBox.Margin = new Padding(10);

            Button confirm = new Button { Text = "授权本次运行", DialogResult = DialogResult.None, AutoSize = true };
            confirm.Click += this.ConfirmClick;
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            actions.Controls.Add(cancel);
            actions.Controls.Add(confirm);

            this.Controls.Add(actions);
            this.Controls.Add(this._confirmationBox);
            this.Controls.Add(prompt);
            this.Controls.Add(warning);
            this.AcceptButton = confirm;
            this.CancelButton = cancel;
        }

        public static bool TryShow(
            IWin32Window owner,
            out EquipmentRefineLiveSendAuthorization authorization)
        {
            authorization = null;
            using (EquipmentRefineLiveSendAuthorizationDialog dialog =
                new EquipmentRefineLiveSendAuthorizationDialog())
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                try
                {
                    authorization = EquipmentRefineLiveSendAuthorization.Create(
                        dialog._confirmationBox.Text);
                    return true;
                }
                catch (ArgumentException)
                {
                    // ConfirmClick prevents this path for normal interaction;
                    // keep the public helper fail-closed if the dialog is
                    // invoked through an unusual UI automation path.
                    authorization = null;
                    return false;
                }
            }
        }

        private void ConfirmClick(object sender, EventArgs e)
        {
            if (!string.Equals(
                this._confirmationBox.Text,
                EquipmentRefineLiveSendAuthorization.RequiredConfirmationText,
                StringComparison.Ordinal))
            {
                MessageBox.Show(
                    this,
                    "确认文本不正确，未授予发送权限。",
                    "装备炼化发送未授权",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._confirmationBox.Focus();
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
