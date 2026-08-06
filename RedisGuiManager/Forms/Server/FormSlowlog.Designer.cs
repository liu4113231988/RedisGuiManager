namespace RedisGuiManager
{
    partial class FormSlowlog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.label_count = new System.Windows.Forms.Label();
            this.label_count_val = new System.Windows.Forms.Label();
            this.numericUpDown_count = new System.Windows.Forms.NumericUpDown();
            this.label_count_label = new System.Windows.Forms.Label();
            this.button_refresh = new System.Windows.Forms.Button();
            this.button_clear = new System.Windows.Forms.Button();
            this.button_close = new System.Windows.Forms.Button();
            this.dataGridView_slowlog = new System.Windows.Forms.DataGridView();
            this.Column_id = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_time = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_duration = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_command = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_client = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_address = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_count)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_slowlog)).BeginInit();
            this.SuspendLayout();
            //
            // label_count
            //
            this.label_count.AutoSize = true;
            this.label_count.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_count.Location = new System.Drawing.Point(12, 12);
            this.label_count.Name = "label_count";
            this.label_count.Size = new System.Drawing.Size(51, 18);
            this.label_count.TabIndex = 0;
            this.label_count.Text = "Total:";
            //
            // label_count_val
            //
            this.label_count_val.AutoSize = true;
            this.label_count_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_count_val.ForeColor = System.Drawing.Color.Blue;
            this.label_count_val.Location = new System.Drawing.Point(69, 12);
            this.label_count_val.Name = "label_count_val";
            this.label_count_val.Size = new System.Drawing.Size(18, 18);
            this.label_count_val.TabIndex = 1;
            this.label_count_val.Text = "0";
            //
            // label_count_label
            //
            this.label_count_label.AutoSize = true;
            this.label_count_label.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_count_label.Location = new System.Drawing.Point(160, 12);
            this.label_count_label.Name = "label_count_label";
            this.label_count_label.Size = new System.Drawing.Size(57, 18);
            this.label_count_label.TabIndex = 2;
            this.label_count_label.Text = "Count:";
            //
            // numericUpDown_count
            //
            this.numericUpDown_count.Font = new System.Drawing.Font("Consolas", 11F);
            this.numericUpDown_count.Location = new System.Drawing.Point(223, 9);
            this.numericUpDown_count.Maximum = new decimal(new int[] {1000, 0, 0, 0});
            this.numericUpDown_count.Minimum = new decimal(new int[] {1, 0, 0, 0});
            this.numericUpDown_count.Name = "numericUpDown_count";
            this.numericUpDown_count.Size = new System.Drawing.Size(80, 25);
            this.numericUpDown_count.TabIndex = 3;
            this.numericUpDown_count.Value = new decimal(new int[] {128, 0, 0, 0});
            //
            // button_refresh
            //
            this.button_refresh.Font = new System.Drawing.Font("Consolas", 11F);
            this.button_refresh.Location = new System.Drawing.Point(320, 7);
            this.button_refresh.Name = "button_refresh";
            this.button_refresh.Size = new System.Drawing.Size(85, 28);
            this.button_refresh.TabIndex = 4;
            this.button_refresh.Text = "Refresh";
            this.button_refresh.UseVisualStyleBackColor = true;
            this.button_refresh.Click += new System.EventHandler(this.button_refresh_Click);
            //
            // button_clear
            //
            this.button_clear.Font = new System.Drawing.Font("Consolas", 11F);
            this.button_clear.Location = new System.Drawing.Point(411, 7);
            this.button_clear.Name = "button_clear";
            this.button_clear.Size = new System.Drawing.Size(85, 28);
            this.button_clear.TabIndex = 5;
            this.button_clear.Text = "Clear";
            this.button_clear.UseVisualStyleBackColor = true;
            this.button_clear.Click += new System.EventHandler(this.button_clear_Click);
            //
            // button_close
            //
            this.button_close.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_close.Font = new System.Drawing.Font("Consolas", 11F);
            this.button_close.Location = new System.Drawing.Point(690, 7);
            this.button_close.Name = "button_close";
            this.button_close.Size = new System.Drawing.Size(85, 28);
            this.button_close.TabIndex = 6;
            this.button_close.Text = "Close";
            this.button_close.UseVisualStyleBackColor = true;
            this.button_close.Click += new System.EventHandler(this.button_close_Click);
            //
            // dataGridView_slowlog
            //
            this.dataGridView_slowlog.AllowUserToAddRows = false;
            this.dataGridView_slowlog.AllowUserToDeleteRows = false;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(235)))), ((int)(((byte)(255)))));
            this.dataGridView_slowlog.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView_slowlog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridView_slowlog.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView_slowlog.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView_slowlog.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Column_id,
            this.Column_time,
            this.Column_duration,
            this.Column_command,
            this.Column_client,
            this.Column_address});
            this.dataGridView_slowlog.Location = new System.Drawing.Point(12, 42);
            this.dataGridView_slowlog.MultiSelect = false;
            this.dataGridView_slowlog.Name = "dataGridView_slowlog";
            this.dataGridView_slowlog.ReadOnly = true;
            this.dataGridView_slowlog.RowHeadersVisible = false;
            this.dataGridView_slowlog.RowTemplate.Height = 23;
            this.dataGridView_slowlog.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridView_slowlog.Size = new System.Drawing.Size(763, 518);
            this.dataGridView_slowlog.TabIndex = 7;
            this.dataGridView_slowlog.CellMouseUp += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dataGridView_slowlog_CellMouseUp);
            //
            // Column_id
            //
            this.Column_id.HeaderText = "ID";
            this.Column_id.Name = "Column_id";
            this.Column_id.ReadOnly = true;
            this.Column_id.Width = 80;
            //
            // Column_time
            //
            this.Column_time.HeaderText = "Time";
            this.Column_time.Name = "Column_time";
            this.Column_time.ReadOnly = true;
            this.Column_time.Width = 150;
            //
            // Column_duration
            //
            this.Column_duration.HeaderText = "Duration";
            this.Column_duration.Name = "Column_duration";
            this.Column_duration.ReadOnly = true;
            this.Column_duration.Width = 100;
            //
            // Column_command
            //
            this.Column_command.HeaderText = "Command";
            this.Column_command.Name = "Column_command";
            this.Column_command.ReadOnly = true;
            this.Column_command.Width = 350;
            //
            // Column_client
            //
            this.Column_client.HeaderText = "Client";
            this.Column_client.Name = "Column_client";
            this.Column_client.ReadOnly = true;
            this.Column_client.Width = 120;
            //
            // Column_address
            //
            this.Column_address.HeaderText = "Address";
            this.Column_address.Name = "Column_address";
            this.Column_address.ReadOnly = true;
            this.Column_address.Width = 150;
            //
            // FormSlowlog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(787, 572);
            this.Controls.Add(this.dataGridView_slowlog);
            this.Controls.Add(this.button_close);
            this.Controls.Add(this.button_clear);
            this.Controls.Add(this.button_refresh);
            this.Controls.Add(this.numericUpDown_count);
            this.Controls.Add(this.label_count_label);
            this.Controls.Add(this.label_count_val);
            this.Controls.Add(this.label_count);
            this.Font = new System.Drawing.Font("Consolas", 11F);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormSlowlog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Slowlog";
            this.Load += new System.EventHandler(this.FormSlowlog_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_count)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_slowlog)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label label_count;
        private System.Windows.Forms.Label label_count_val;
        private System.Windows.Forms.Label label_count_label;
        private System.Windows.Forms.NumericUpDown numericUpDown_count;
        private System.Windows.Forms.Button button_refresh;
        private System.Windows.Forms.Button button_clear;
        private System.Windows.Forms.Button button_close;
        private System.Windows.Forms.DataGridView dataGridView_slowlog;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_id;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_time;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_duration;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_command;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_client;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_address;
    }
}
