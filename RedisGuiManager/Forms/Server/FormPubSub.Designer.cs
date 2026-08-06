namespace RedisGuiManager
{
    partial class FormPubSub
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
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage_subscribe = new System.Windows.Forms.TabPage();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.listBox_channels = new System.Windows.Forms.ListBox();
            this.label_channels = new System.Windows.Forms.Label();
            this.button_unsubscribe = new System.Windows.Forms.Button();
            this.button_subscribe = new System.Windows.Forms.Button();
            this.textBox_channel = new System.Windows.Forms.TextBox();
            this.label_channel = new System.Windows.Forms.Label();
            this.label_sub_count = new System.Windows.Forms.Label();
            this.dataGridView_messages = new System.Windows.Forms.DataGridView();
            this.Col_time = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Col_channel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Col_message = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.button_clear = new System.Windows.Forms.Button();
            this.tabPage_publish = new System.Windows.Forms.TabPage();
            this.button_publish = new System.Windows.Forms.Button();
            this.textBox_pub_message = new System.Windows.Forms.TextBox();
            this.label_pub_message = new System.Windows.Forms.Label();
            this.textBox_pub_channel = new System.Windows.Forms.TextBox();
            this.label_pub_channel = new System.Windows.Forms.Label();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.tabControl1.SuspendLayout();
            this.tabPage_subscribe.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_messages)).BeginInit();
            this.tabPage_publish.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            //
            // tabControl1
            //
            this.tabControl1.Controls.Add(this.tabPage_subscribe);
            this.tabControl1.Controls.Add(this.tabPage_publish);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("Consolas", 11F);
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(784, 497);
            this.tabControl1.TabIndex = 0;
            //
            // tabPage_subscribe
            //
            this.tabPage_subscribe.Controls.Add(this.splitContainer1);
            this.tabPage_subscribe.Location = new System.Drawing.Point(4, 27);
            this.tabPage_subscribe.Name = "tabPage_subscribe";
            this.tabPage_subscribe.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_subscribe.Size = new System.Drawing.Size(776, 466);
            this.tabPage_subscribe.TabIndex = 0;
            this.tabPage_subscribe.Text = "Subscribe";
            this.tabPage_subscribe.UseVisualStyleBackColor = true;
            //
            // splitContainer1
            //
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(3, 3);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Panel1.Controls.Add(this.listBox_channels);
            this.splitContainer1.Panel1.Controls.Add(this.label_channels);
            this.splitContainer1.Panel1.Controls.Add(this.button_unsubscribe);
            this.splitContainer1.Panel1.Controls.Add(this.button_subscribe);
            this.splitContainer1.Panel1.Controls.Add(this.textBox_channel);
            this.splitContainer1.Panel1.Controls.Add(this.label_channel);
            this.splitContainer1.Panel1.Controls.Add(this.label_sub_count);
            this.splitContainer1.Panel2.Controls.Add(this.button_clear);
            this.splitContainer1.Panel2.Controls.Add(this.dataGridView_messages);
            this.splitContainer1.Size = new System.Drawing.Size(770, 460);
            this.splitContainer1.SplitterDistance = 250;
            this.splitContainer1.TabIndex = 0;
            //
            // label_channel
            //
            this.label_channel.AutoSize = true;
            this.label_channel.Location = new System.Drawing.Point(3, 5);
            this.label_channel.Name = "label_channel";
            this.label_channel.Size = new System.Drawing.Size(60, 18);
            this.label_channel.TabIndex = 0;
            this.label_channel.Text = "Channel";
            //
            // textBox_channel
            //
            this.textBox_channel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_channel.Location = new System.Drawing.Point(3, 26);
            this.textBox_channel.Name = "textBox_channel";
            this.textBox_channel.Size = new System.Drawing.Size(180, 25);
            this.textBox_channel.TabIndex = 1;
            //
            // button_subscribe
            //
            this.button_subscribe.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_subscribe.Location = new System.Drawing.Point(189, 24);
            this.button_subscribe.Name = "button_subscribe";
            this.button_subscribe.Size = new System.Drawing.Size(55, 28);
            this.button_subscribe.TabIndex = 2;
            this.button_subscribe.Text = "Sub";
            this.button_subscribe.UseVisualStyleBackColor = true;
            this.button_subscribe.Click += new System.EventHandler(this.button_subscribe_Click);
            //
            // button_unsubscribe
            //
            this.button_unsubscribe.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button_unsubscribe.Location = new System.Drawing.Point(3, 429);
            this.button_unsubscribe.Name = "button_unsubscribe";
            this.button_unsubscribe.Size = new System.Drawing.Size(100, 28);
            this.button_unsubscribe.TabIndex = 4;
            this.button_unsubscribe.Text = "Unsubscribe";
            this.button_unsubscribe.UseVisualStyleBackColor = true;
            this.button_unsubscribe.Click += new System.EventHandler(this.button_unsubscribe_Click);
            //
            // label_channels
            //
            this.label_channels.AutoSize = true;
            this.label_channels.Location = new System.Drawing.Point(3, 57);
            this.label_channels.Name = "label_channels";
            this.label_channels.Size = new System.Drawing.Size(75, 18);
            this.label_channels.TabIndex = 3;
            this.label_channels.Text = "Channels";
            //
            // listBox_channels
            //
            this.listBox_channels.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listBox_channels.Font = new System.Drawing.Font("Consolas", 11F);
            this.listBox_channels.IntegralHeight = false;
            this.listBox_channels.ItemHeight = 18;
            this.listBox_channels.Location = new System.Drawing.Point(3, 78);
            this.listBox_channels.Name = "listBox_channels";
            this.listBox_channels.Size = new System.Drawing.Size(241, 345);
            this.listBox_channels.TabIndex = 5;
            //
            // label_sub_count
            //
            this.label_sub_count.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.label_sub_count.AutoSize = true;
            this.label_sub_count.Location = new System.Drawing.Point(145, 434);
            this.label_sub_count.Name = "label_sub_count";
            this.label_sub_count.Size = new System.Drawing.Size(99, 18);
            this.label_sub_count.TabIndex = 6;
            this.label_sub_count.Text = "Subscriptions: 0";
            //
            // dataGridView_messages
            //
            this.dataGridView_messages.AllowUserToAddRows = false;
            this.dataGridView_messages.AllowUserToDeleteRows = false;
            this.dataGridView_messages.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridView_messages.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView_messages.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView_messages.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Col_time,
            this.Col_channel,
            this.Col_message});
            this.dataGridView_messages.Location = new System.Drawing.Point(3, 3);
            this.dataGridView_messages.Name = "dataGridView_messages";
            this.dataGridView_messages.ReadOnly = true;
            this.dataGridView_messages.RowHeadersVisible = false;
            this.dataGridView_messages.RowTemplate.Height = 23;
            this.dataGridView_messages.Size = new System.Drawing.Size(510, 428);
            this.dataGridView_messages.TabIndex = 0;
            //
            // Col_time
            //
            this.Col_time.HeaderText = "Time";
            this.Col_time.Name = "Col_time";
            this.Col_time.ReadOnly = true;
            this.Col_time.Width = 120;
            //
            // Col_channel
            //
            this.Col_channel.HeaderText = "Channel";
            this.Col_channel.Name = "Col_channel";
            this.Col_channel.ReadOnly = true;
            this.Col_channel.Width = 150;
            //
            // Col_message
            //
            this.Col_message.HeaderText = "Message";
            this.Col_message.Name = "Col_message";
            this.Col_message.ReadOnly = true;
            this.Col_message.Width = 400;
            //
            // button_clear
            //
            this.button_clear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button_clear.Location = new System.Drawing.Point(428, 437);
            this.button_clear.Name = "button_clear";
            this.button_clear.Size = new System.Drawing.Size(85, 28);
            this.button_clear.TabIndex = 1;
            this.button_clear.Text = "Clear";
            this.button_clear.UseVisualStyleBackColor = true;
            this.button_clear.Click += new System.EventHandler(this.button_clear_Click);
            //
            // tabPage_publish
            //
            this.tabPage_publish.Controls.Add(this.button_publish);
            this.tabPage_publish.Controls.Add(this.textBox_pub_message);
            this.tabPage_publish.Controls.Add(this.label_pub_message);
            this.tabPage_publish.Controls.Add(this.textBox_pub_channel);
            this.tabPage_publish.Controls.Add(this.label_pub_channel);
            this.tabPage_publish.Location = new System.Drawing.Point(4, 27);
            this.tabPage_publish.Name = "tabPage_publish";
            this.tabPage_publish.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_publish.Size = new System.Drawing.Size(776, 466);
            this.tabPage_publish.TabIndex = 1;
            this.tabPage_publish.Text = "Publish";
            this.tabPage_publish.UseVisualStyleBackColor = true;
            //
            // label_pub_channel
            //
            this.label_pub_channel.AutoSize = true;
            this.label_pub_channel.Location = new System.Drawing.Point(20, 30);
            this.label_pub_channel.Name = "label_pub_channel";
            this.label_pub_channel.Size = new System.Drawing.Size(60, 18);
            this.label_pub_channel.TabIndex = 0;
            this.label_pub_channel.Text = "Channel";
            //
            // textBox_pub_channel
            //
            this.textBox_pub_channel.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_pub_channel.Location = new System.Drawing.Point(100, 27);
            this.textBox_pub_channel.Name = "textBox_pub_channel";
            this.textBox_pub_channel.Size = new System.Drawing.Size(400, 26);
            this.textBox_pub_channel.TabIndex = 1;
            //
            // label_pub_message
            //
            this.label_pub_message.AutoSize = true;
            this.label_pub_message.Location = new System.Drawing.Point(20, 70);
            this.label_pub_message.Name = "label_pub_message";
            this.label_pub_message.Size = new System.Drawing.Size(60, 18);
            this.label_pub_message.TabIndex = 2;
            this.label_pub_message.Text = "Message";
            //
            // textBox_pub_message
            //
            this.textBox_pub_message.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_pub_message.Location = new System.Drawing.Point(100, 67);
            this.textBox_pub_message.Multiline = true;
            this.textBox_pub_message.Name = "textBox_pub_message";
            this.textBox_pub_message.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_pub_message.Size = new System.Drawing.Size(400, 100);
            this.textBox_pub_message.TabIndex = 3;
            //
            // button_publish
            //
            this.button_publish.Font = new System.Drawing.Font("Consolas", 12F);
            this.button_publish.Location = new System.Drawing.Point(100, 180);
            this.button_publish.Name = "button_publish";
            this.button_publish.Size = new System.Drawing.Size(140, 31);
            this.button_publish.TabIndex = 4;
            this.button_publish.Text = "Publish";
            this.button_publish.UseVisualStyleBackColor = true;
            this.button_publish.Click += new System.EventHandler(this.button_publish_Click);
            //
            // toolStripStatusLabel1
            //
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(50, 20);
            this.toolStripStatusLabel1.Text = "Ready";
            //
            // statusStrip1
            //
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1});
            this.statusStrip1.Location = new System.Drawing.Point(0, 497);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(784, 25);
            this.statusStrip1.TabIndex = 1;
            //
            // FormPubSub
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 522);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.statusStrip1);
            this.Font = new System.Drawing.Font("Consolas", 11F);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormPubSub";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Pub/Sub";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormPubSub_FormClosing);
            this.Load += new System.EventHandler(this.FormPubSub_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage_subscribe.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_messages)).EndInit();
            this.tabPage_publish.ResumeLayout(false);
            this.tabPage_publish.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage_subscribe;
        private System.Windows.Forms.TabPage tabPage_publish;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Label label_channel;
        private System.Windows.Forms.TextBox textBox_channel;
        private System.Windows.Forms.Button button_subscribe;
        private System.Windows.Forms.Button button_unsubscribe;
        private System.Windows.Forms.Label label_channels;
        private System.Windows.Forms.ListBox listBox_channels;
        private System.Windows.Forms.Label label_sub_count;
        private System.Windows.Forms.DataGridView dataGridView_messages;
        private System.Windows.Forms.DataGridViewTextBoxColumn Col_time;
        private System.Windows.Forms.DataGridViewTextBoxColumn Col_channel;
        private System.Windows.Forms.DataGridViewTextBoxColumn Col_message;
        private System.Windows.Forms.Button button_clear;
        private System.Windows.Forms.Label label_pub_channel;
        private System.Windows.Forms.TextBox textBox_pub_channel;
        private System.Windows.Forms.Label label_pub_message;
        private System.Windows.Forms.TextBox textBox_pub_message;
        private System.Windows.Forms.Button button_publish;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.StatusStrip statusStrip1;
    }
}
