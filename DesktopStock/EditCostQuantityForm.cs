using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 修改成本价和数量的对话框
    /// </summary>
    public class EditCostQuantityForm : Form
    {
        private TextBox txtCostPrice;
        private TextBox txtQuantity;
        private Button btnOK;
        private Button btnCancel;
        private Label lblStockCode;

        public decimal CostPrice { get; private set; }
        public int Quantity { get; private set; }

        public EditCostQuantityForm(string stockCode, string stockName, decimal costPrice, int quantity)
        {
            CostPrice = costPrice;
            Quantity = quantity;
            SetupForm(stockCode, stockName);
        }

        private void SetupForm(string stockCode, string stockName)
        {
            this.Text = "修改成本与数量";
            this.Size = new Size(280, 200);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 8);

            // 股票名称+代码标签
            string displayName = string.IsNullOrEmpty(stockName) ? stockCode : (stockName + "（" + stockCode + "）");
            lblStockCode = new Label
            {
                Text = "股票：" + displayName,
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(16, 16),
                AutoSize = true
            };
            this.Controls.Add(lblStockCode);

            // 成本价标签
            var lblCost = new Label
            {
                Text = "成本价：",
                Font = new Font("Microsoft YaHei", 8),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(16, 50),
                AutoSize = true
            };
            this.Controls.Add(lblCost);

            // 成本价输入框
            txtCostPrice = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(80, 46),
                Size = new Size(170, 26),
                BorderStyle = BorderStyle.FixedSingle,
                Text = CostPrice > 0 ? CostPrice.ToString("F2") : ""
            };
            txtCostPrice.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Confirm();
                else if (e.KeyCode == Keys.Escape)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };
            this.Controls.Add(txtCostPrice);

            // 数量标签
            var lblQty = new Label
            {
                Text = "数量：",
                Font = new Font("Microsoft YaHei", 8),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(16, 84),
                AutoSize = true
            };
            this.Controls.Add(lblQty);

            // 数量输入框
            txtQuantity = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(80, 80),
                Size = new Size(170, 26),
                BorderStyle = BorderStyle.FixedSingle,
                Text = Quantity > 0 ? Quantity.ToString() : ""
            };
            txtQuantity.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Confirm();
                else if (e.KeyCode == Keys.Escape)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };
            this.Controls.Add(txtQuantity);

            // 取消按钮
            btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(90, 28),
                Location = new Point(50, 120),
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
                Size = new Size(90, 28),
                Location = new Point(148, 120),
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
                txtCostPrice.Focus();
                this.Activate();
                this.BringToFront();
            };
        }

        private void Confirm()
        {
            // 解析成本价
            decimal costPrice;
            if (!decimal.TryParse(txtCostPrice.Text, out costPrice))
            {
                MessageBox.Show("请输入正确的成本价", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCostPrice.Focus();
                return;
            }
            CostPrice = costPrice;

            // 解析数量
            int quantity;
            if (!int.TryParse(txtQuantity.Text, out quantity))
            {
                MessageBox.Show("请输入正确的数量", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtQuantity.Focus();
                return;
            }
            Quantity = quantity;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
