using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 添加股票代码输入窗（始终置顶）
    /// </summary>
    public class AddStockForm : Form
    {
        private TextBox txtCode;
        private Button btnOK;
        private Button btnCancel;

        public string StockCode { get; private set; }

        public AddStockForm()
        {
            SetupForm();
        }

        private void SetupForm()
        {
            this.Text = "添加股票";
            this.Size = new Size(250, 140);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 8);

            // 标签：与输入框同行
            var lbl = new Label
            {
                Text = "股票代码：",
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(16, 24),
                AutoSize = true
            };
            this.Controls.Add(lbl);

            // 输入框：跟在标签后面
            txtCode = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(lbl.Right + 4, 20),
                Size = new Size(120, 26),
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 6
            };
            txtCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Confirm();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };
            this.Controls.Add(txtCode);

            // 取消按钮（居中：两按钮总宽75+15+75=165，窗体宽250，(250-165)/2=42）
            btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(75, 28),
                Location = new Point(42, 62),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(235, 235, 235),
                ForeColor = Color.FromArgb(80, 80, 80),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            // 确定按钮
            btnOK = new Button
            {
                Text = "确定",
                Size = new Size(75, 28),
                Location = new Point(132, 62),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnOK.FlatAppearance.BorderSize = 0;
            btnOK.Click += (s, e) => Confirm();
            this.Controls.Add(btnOK);

            this.Load += (s, e) =>
            {
                txtCode.Focus();
                this.Activate();
                this.BringToFront();
            };
        }

        private void Confirm()
        {
            StockCode = (txtCode.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(StockCode))
            {
                MessageBox.Show("请输入股票代码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCode.Focus();
                return;
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
