namespace RedisGuiManager
{
    partial class FormServerInfo
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
            this.tabPage_summary = new System.Windows.Forms.TabPage();
            this.groupBox_stats = new System.Windows.Forms.GroupBox();
            this.label_pubsub_val = new System.Windows.Forms.Label();
            this.label_pubsub = new System.Windows.Forms.Label();
            this.label_evicted_val = new System.Windows.Forms.Label();
            this.label_evicted = new System.Windows.Forms.Label();
            this.label_expired_val = new System.Windows.Forms.Label();
            this.label_expired = new System.Windows.Forms.Label();
            this.label_totalcmd_val = new System.Windows.Forms.Label();
            this.label_totalcmd = new System.Windows.Forms.Label();
            this.label_ops_val = new System.Windows.Forms.Label();
            this.label_ops = new System.Windows.Forms.Label();
            this.label_hitrate_val = new System.Windows.Forms.Label();
            this.label_hitrate = new System.Windows.Forms.Label();
            this.groupBox_memory = new System.Windows.Forms.GroupBox();
            this.label_maxmem_val = new System.Windows.Forms.Label();
            this.label_maxmem = new System.Windows.Forms.Label();
            this.label_peakmem_val = new System.Windows.Forms.Label();
            this.label_peakmem = new System.Windows.Forms.Label();
            this.label_mem_val = new System.Windows.Forms.Label();
            this.label_mem = new System.Windows.Forms.Label();
            this.groupBox_server = new System.Windows.Forms.GroupBox();
            this.label_uptime_val = new System.Windows.Forms.Label();
            this.label_uptime = new System.Windows.Forms.Label();
            this.label_clients_val = new System.Windows.Forms.Label();
            this.label_clients = new System.Windows.Forms.Label();
            this.label_os_val = new System.Windows.Forms.Label();
            this.label_os = new System.Windows.Forms.Label();
            this.label_role_val = new System.Windows.Forms.Label();
            this.label_role = new System.Windows.Forms.Label();
            this.label_mode_val = new System.Windows.Forms.Label();
            this.label_mode = new System.Windows.Forms.Label();
            this.label_version_val = new System.Windows.Forms.Label();
            this.label_version = new System.Windows.Forms.Label();
            this.tabPage_raw = new System.Windows.Forms.TabPage();
            this.dataGridView_info = new System.Windows.Forms.DataGridView();
            this.Column_key = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.button_refresh = new System.Windows.Forms.Button();
            this.button_close = new System.Windows.Forms.Button();
            this.tabControl1.SuspendLayout();
            this.tabPage_summary.SuspendLayout();
            this.groupBox_stats.SuspendLayout();
            this.groupBox_memory.SuspendLayout();
            this.groupBox_server.SuspendLayout();
            this.tabPage_raw.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_info)).BeginInit();
            this.SuspendLayout();
            //
            // tabControl1
            //
            this.tabControl1.Controls.Add(this.tabPage_summary);
            this.tabControl1.Controls.Add(this.tabPage_raw);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("Consolas", 11F);
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(784, 521);
            this.tabControl1.TabIndex = 0;
            //
            // tabPage_summary
            //
            this.tabPage_summary.Controls.Add(this.groupBox_stats);
            this.tabPage_summary.Controls.Add(this.groupBox_memory);
            this.tabPage_summary.Controls.Add(this.groupBox_server);
            this.tabPage_summary.Location = new System.Drawing.Point(4, 27);
            this.tabPage_summary.Name = "tabPage_summary";
            this.tabPage_summary.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_summary.Size = new System.Drawing.Size(776, 490);
            this.tabPage_summary.TabIndex = 0;
            this.tabPage_summary.Text = "Summary";
            this.tabPage_summary.UseVisualStyleBackColor = true;
            //
            // groupBox_server
            //
            this.groupBox_server.Controls.Add(this.label_version_val);
            this.groupBox_server.Controls.Add(this.label_version);
            this.groupBox_server.Controls.Add(this.label_mode_val);
            this.groupBox_server.Controls.Add(this.label_mode);
            this.groupBox_server.Controls.Add(this.label_role_val);
            this.groupBox_server.Controls.Add(this.label_role);
            this.groupBox_server.Controls.Add(this.label_os_val);
            this.groupBox_server.Controls.Add(this.label_os);
            this.groupBox_server.Controls.Add(this.label_clients_val);
            this.groupBox_server.Controls.Add(this.label_clients);
            this.groupBox_server.Controls.Add(this.label_uptime_val);
            this.groupBox_server.Controls.Add(this.label_uptime);
            this.groupBox_server.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.groupBox_server.Location = new System.Drawing.Point(6, 6);
            this.groupBox_server.Name = "groupBox_server";
            this.groupBox_server.Size = new System.Drawing.Size(370, 180);
            this.groupBox_server.TabIndex = 0;
            this.groupBox_server.TabStop = false;
            this.groupBox_server.Text = "Server";
            //
            // label_version
            //
            this.label_version.AutoSize = true;
            this.label_version.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_version.Location = new System.Drawing.Point(10, 25);
            this.label_version.Name = "label_version";
            this.label_version.Size = new System.Drawing.Size(64, 18);
            this.label_version.TabIndex = 0;
            this.label_version.Text = "Version";
            //
            // label_version_val
            //
            this.label_version_val.AutoSize = true;
            this.label_version_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_version_val.ForeColor = System.Drawing.Color.Blue;
            this.label_version_val.Location = new System.Drawing.Point(120, 25);
            this.label_version_val.Name = "label_version_val";
            this.label_version_val.Size = new System.Drawing.Size(18, 18);
            this.label_version_val.TabIndex = 1;
            this.label_version_val.Text = "-";
            //
            // label_mode
            //
            this.label_mode.AutoSize = true;
            this.label_mode.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_mode.Location = new System.Drawing.Point(10, 50);
            this.label_mode.Name = "label_mode";
            this.label_mode.Size = new System.Drawing.Size(48, 18);
            this.label_mode.TabIndex = 2;
            this.label_mode.Text = "Mode";
            //
            // label_mode_val
            //
            this.label_mode_val.AutoSize = true;
            this.label_mode_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_mode_val.ForeColor = System.Drawing.Color.Blue;
            this.label_mode_val.Location = new System.Drawing.Point(120, 50);
            this.label_mode_val.Name = "label_mode_val";
            this.label_mode_val.Size = new System.Drawing.Size(18, 18);
            this.label_mode_val.TabIndex = 3;
            this.label_mode_val.Text = "-";
            //
            // label_role
            //
            this.label_role.AutoSize = true;
            this.label_role.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_role.Location = new System.Drawing.Point(10, 75);
            this.label_role.Name = "label_role";
            this.label_role.Size = new System.Drawing.Size(39, 18);
            this.label_role.TabIndex = 4;
            this.label_role.Text = "Role";
            //
            // label_role_val
            //
            this.label_role_val.AutoSize = true;
            this.label_role_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_role_val.ForeColor = System.Drawing.Color.Blue;
            this.label_role_val.Location = new System.Drawing.Point(120, 75);
            this.label_role_val.Name = "label_role_val";
            this.label_role_val.Size = new System.Drawing.Size(18, 18);
            this.label_role_val.TabIndex = 5;
            this.label_role_val.Text = "-";
            //
            // label_os
            //
            this.label_os.AutoSize = true;
            this.label_os.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_os.Location = new System.Drawing.Point(10, 100);
            this.label_os.Name = "label_os";
            this.label_os.Size = new System.Drawing.Size(27, 18);
            this.label_os.TabIndex = 6;
            this.label_os.Text = "OS";
            //
            // label_os_val
            //
            this.label_os_val.AutoSize = true;
            this.label_os_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_os_val.ForeColor = System.Drawing.Color.Blue;
            this.label_os_val.Location = new System.Drawing.Point(120, 100);
            this.label_os_val.Name = "label_os_val";
            this.label_os_val.Size = new System.Drawing.Size(18, 18);
            this.label_os_val.TabIndex = 7;
            this.label_os_val.Text = "-";
            //
            // label_clients
            //
            this.label_clients.AutoSize = true;
            this.label_clients.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_clients.Location = new System.Drawing.Point(10, 125);
            this.label_clients.Name = "label_clients";
            this.label_clients.Size = new System.Drawing.Size(57, 18);
            this.label_clients.TabIndex = 8;
            this.label_clients.Text = "Clients";
            //
            // label_clients_val
            //
            this.label_clients_val.AutoSize = true;
            this.label_clients_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_clients_val.ForeColor = System.Drawing.Color.Blue;
            this.label_clients_val.Location = new System.Drawing.Point(120, 125);
            this.label_clients_val.Name = "label_clients_val";
            this.label_clients_val.Size = new System.Drawing.Size(18, 18);
            this.label_clients_val.TabIndex = 9;
            this.label_clients_val.Text = "-";
            //
            // label_uptime
            //
            this.label_uptime.AutoSize = true;
            this.label_uptime.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_uptime.Location = new System.Drawing.Point(10, 150);
            this.label_uptime.Name = "label_uptime";
            this.label_uptime.Size = new System.Drawing.Size(57, 18);
            this.label_uptime.TabIndex = 10;
            this.label_uptime.Text = "Uptime";
            //
            // label_uptime_val
            //
            this.label_uptime_val.AutoSize = true;
            this.label_uptime_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_uptime_val.ForeColor = System.Drawing.Color.Blue;
            this.label_uptime_val.Location = new System.Drawing.Point(120, 150);
            this.label_uptime_val.Name = "label_uptime_val";
            this.label_uptime_val.Size = new System.Drawing.Size(18, 18);
            this.label_uptime_val.TabIndex = 11;
            this.label_uptime_val.Text = "-";
            //
            // groupBox_memory
            //
            this.groupBox_memory.Controls.Add(this.label_maxmem_val);
            this.groupBox_memory.Controls.Add(this.label_maxmem);
            this.groupBox_memory.Controls.Add(this.label_peakmem_val);
            this.groupBox_memory.Controls.Add(this.label_peakmem);
            this.groupBox_memory.Controls.Add(this.label_mem_val);
            this.groupBox_memory.Controls.Add(this.label_mem);
            this.groupBox_memory.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.groupBox_memory.Location = new System.Drawing.Point(386, 6);
            this.groupBox_memory.Name = "groupBox_memory";
            this.groupBox_memory.Size = new System.Drawing.Size(370, 130);
            this.groupBox_memory.TabIndex = 1;
            this.groupBox_memory.TabStop = false;
            this.groupBox_memory.Text = "Memory";
            //
            // label_mem
            //
            this.label_mem.AutoSize = true;
            this.label_mem.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_mem.Location = new System.Drawing.Point(10, 25);
            this.label_mem.Name = "label_mem";
            this.label_mem.Size = new System.Drawing.Size(78, 18);
            this.label_mem.TabIndex = 0;
            this.label_mem.Text = "Used";
            //
            // label_mem_val
            //
            this.label_mem_val.AutoSize = true;
            this.label_mem_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_mem_val.ForeColor = System.Drawing.Color.Blue;
            this.label_mem_val.Location = new System.Drawing.Point(120, 25);
            this.label_mem_val.Name = "label_mem_val";
            this.label_mem_val.Size = new System.Drawing.Size(18, 18);
            this.label_mem_val.TabIndex = 1;
            this.label_mem_val.Text = "-";
            //
            // label_peakmem
            //
            this.label_peakmem.AutoSize = true;
            this.label_peakmem.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_peakmem.Location = new System.Drawing.Point(10, 50);
            this.label_peakmem.Name = "label_peakmem";
            this.label_peakmem.Size = new System.Drawing.Size(78, 18);
            this.label_peakmem.TabIndex = 2;
            this.label_peakmem.Text = "Peak";
            //
            // label_peakmem_val
            //
            this.label_peakmem_val.AutoSize = true;
            this.label_peakmem_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_peakmem_val.ForeColor = System.Drawing.Color.Blue;
            this.label_peakmem_val.Location = new System.Drawing.Point(120, 50);
            this.label_peakmem_val.Name = "label_peakmem_val";
            this.label_peakmem_val.Size = new System.Drawing.Size(18, 18);
            this.label_peakmem_val.TabIndex = 3;
            this.label_peakmem_val.Text = "-";
            //
            // label_maxmem
            //
            this.label_maxmem.AutoSize = true;
            this.label_maxmem.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_maxmem.Location = new System.Drawing.Point(10, 75);
            this.label_maxmem.Name = "label_maxmem";
            this.label_maxmem.Size = new System.Drawing.Size(78, 18);
            this.label_maxmem.TabIndex = 4;
            this.label_maxmem.Text = "Max";
            //
            // label_maxmem_val
            //
            this.label_maxmem_val.AutoSize = true;
            this.label_maxmem_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_maxmem_val.ForeColor = System.Drawing.Color.Blue;
            this.label_maxmem_val.Location = new System.Drawing.Point(120, 75);
            this.label_maxmem_val.Name = "label_maxmem_val";
            this.label_maxmem_val.Size = new System.Drawing.Size(18, 18);
            this.label_maxmem_val.TabIndex = 5;
            this.label_maxmem_val.Text = "-";
            //
            // groupBox_stats
            //
            this.groupBox_stats.Controls.Add(this.label_pubsub_val);
            this.groupBox_stats.Controls.Add(this.label_pubsub);
            this.groupBox_stats.Controls.Add(this.label_evicted_val);
            this.groupBox_stats.Controls.Add(this.label_evicted);
            this.groupBox_stats.Controls.Add(this.label_expired_val);
            this.groupBox_stats.Controls.Add(this.label_expired);
            this.groupBox_stats.Controls.Add(this.label_totalcmd_val);
            this.groupBox_stats.Controls.Add(this.label_totalcmd);
            this.groupBox_stats.Controls.Add(this.label_ops_val);
            this.groupBox_stats.Controls.Add(this.label_ops);
            this.groupBox_stats.Controls.Add(this.label_hitrate_val);
            this.groupBox_stats.Controls.Add(this.label_hitrate);
            this.groupBox_stats.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.groupBox_stats.Location = new System.Drawing.Point(386, 142);
            this.groupBox_stats.Name = "groupBox_stats";
            this.groupBox_stats.Size = new System.Drawing.Size(370, 180);
            this.groupBox_stats.TabIndex = 2;
            this.groupBox_stats.TabStop = false;
            this.groupBox_stats.Text = "Stats";
            //
            // label_hitrate
            //
            this.label_hitrate.AutoSize = true;
            this.label_hitrate.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_hitrate.Location = new System.Drawing.Point(10, 25);
            this.label_hitrate.Name = "label_hitrate";
            this.label_hitrate.Size = new System.Drawing.Size(75, 18);
            this.label_hitrate.TabIndex = 0;
            this.label_hitrate.Text = "Hit Rate";
            //
            // label_hitrate_val
            //
            this.label_hitrate_val.AutoSize = true;
            this.label_hitrate_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_hitrate_val.ForeColor = System.Drawing.Color.Green;
            this.label_hitrate_val.Location = new System.Drawing.Point(120, 25);
            this.label_hitrate_val.Name = "label_hitrate_val";
            this.label_hitrate_val.Size = new System.Drawing.Size(18, 18);
            this.label_hitrate_val.TabIndex = 1;
            this.label_hitrate_val.Text = "-";
            //
            // label_ops
            //
            this.label_ops.AutoSize = true;
            this.label_ops.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_ops.Location = new System.Drawing.Point(10, 50);
            this.label_ops.Name = "label_ops";
            this.label_ops.Size = new System.Drawing.Size(90, 18);
            this.label_ops.TabIndex = 2;
            this.label_ops.Text = "Ops/sec";
            //
            // label_ops_val
            //
            this.label_ops_val.AutoSize = true;
            this.label_ops_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_ops_val.ForeColor = System.Drawing.Color.Blue;
            this.label_ops_val.Location = new System.Drawing.Point(120, 50);
            this.label_ops_val.Name = "label_ops_val";
            this.label_ops_val.Size = new System.Drawing.Size(18, 18);
            this.label_ops_val.TabIndex = 3;
            this.label_ops_val.Text = "-";
            //
            // label_totalcmd
            //
            this.label_totalcmd.AutoSize = true;
            this.label_totalcmd.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_totalcmd.Location = new System.Drawing.Point(10, 75);
            this.label_totalcmd.Name = "label_totalcmd";
            this.label_totalcmd.Size = new System.Drawing.Size(90, 18);
            this.label_totalcmd.TabIndex = 4;
            this.label_totalcmd.Text = "Total Cmds";
            //
            // label_totalcmd_val
            //
            this.label_totalcmd_val.AutoSize = true;
            this.label_totalcmd_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_totalcmd_val.ForeColor = System.Drawing.Color.Blue;
            this.label_totalcmd_val.Location = new System.Drawing.Point(120, 75);
            this.label_totalcmd_val.Name = "label_totalcmd_val";
            this.label_totalcmd_val.Size = new System.Drawing.Size(18, 18);
            this.label_totalcmd_val.TabIndex = 5;
            this.label_totalcmd_val.Text = "-";
            //
            // label_expired
            //
            this.label_expired.AutoSize = true;
            this.label_expired.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_expired.Location = new System.Drawing.Point(10, 100);
            this.label_expired.Name = "label_expired";
            this.label_expired.Size = new System.Drawing.Size(66, 18);
            this.label_expired.TabIndex = 6;
            this.label_expired.Text = "Expired";
            //
            // label_expired_val
            //
            this.label_expired_val.AutoSize = true;
            this.label_expired_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_expired_val.ForeColor = System.Drawing.Color.Blue;
            this.label_expired_val.Location = new System.Drawing.Point(120, 100);
            this.label_expired_val.Name = "label_expired_val";
            this.label_expired_val.Size = new System.Drawing.Size(18, 18);
            this.label_expired_val.TabIndex = 7;
            this.label_expired_val.Text = "-";
            //
            // label_evicted
            //
            this.label_evicted.AutoSize = true;
            this.label_evicted.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_evicted.Location = new System.Drawing.Point(10, 125);
            this.label_evicted.Name = "label_evicted";
            this.label_evicted.Size = new System.Drawing.Size(66, 18);
            this.label_evicted.TabIndex = 8;
            this.label_evicted.Text = "Evicted";
            //
            // label_evicted_val
            //
            this.label_evicted_val.AutoSize = true;
            this.label_evicted_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_evicted_val.ForeColor = System.Drawing.Color.Blue;
            this.label_evicted_val.Location = new System.Drawing.Point(120, 125);
            this.label_evicted_val.Name = "label_evicted_val";
            this.label_evicted_val.Size = new System.Drawing.Size(18, 18);
            this.label_evicted_val.TabIndex = 9;
            this.label_evicted_val.Text = "-";
            //
            // label_pubsub
            //
            this.label_pubsub.AutoSize = true;
            this.label_pubsub.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_pubsub.Location = new System.Drawing.Point(10, 150);
            this.label_pubsub.Name = "label_pubsub";
            this.label_pubsub.Size = new System.Drawing.Size(90, 18);
            this.label_pubsub.TabIndex = 10;
            this.label_pubsub.Text = "PubSub Ch";
            //
            // label_pubsub_val
            //
            this.label_pubsub_val.AutoSize = true;
            this.label_pubsub_val.Font = new System.Drawing.Font("Consolas", 11F);
            this.label_pubsub_val.ForeColor = System.Drawing.Color.Blue;
            this.label_pubsub_val.Location = new System.Drawing.Point(120, 150);
            this.label_pubsub_val.Name = "label_pubsub_val";
            this.label_pubsub_val.Size = new System.Drawing.Size(18, 18);
            this.label_pubsub_val.TabIndex = 11;
            this.label_pubsub_val.Text = "-";
            //
            // tabPage_raw
            //
            this.tabPage_raw.Controls.Add(this.dataGridView_info);
            this.tabPage_raw.Location = new System.Drawing.Point(4, 27);
            this.tabPage_raw.Name = "tabPage_raw";
            this.tabPage_raw.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_raw.Size = new System.Drawing.Size(776, 490);
            this.tabPage_raw.TabIndex = 1;
            this.tabPage_raw.Text = "All Info";
            this.tabPage_raw.UseVisualStyleBackColor = true;
            //
            // dataGridView_info
            //
            this.dataGridView_info.AllowUserToAddRows = false;
            this.dataGridView_info.AllowUserToDeleteRows = false;
            this.dataGridView_info.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridView_info.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView_info.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView_info.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Column_key,
            this.Column_value});
            this.dataGridView_info.Location = new System.Drawing.Point(3, 3);
            this.dataGridView_info.Name = "dataGridView_info";
            this.dataGridView_info.ReadOnly = true;
            this.dataGridView_info.RowHeadersVisible = false;
            this.dataGridView_info.RowTemplate.Height = 23;
            this.dataGridView_info.Size = new System.Drawing.Size(770, 484);
            this.dataGridView_info.TabIndex = 0;
            //
            // Column_key
            //
            this.Column_key.HeaderText = "Key";
            this.Column_key.Name = "Column_key";
            this.Column_key.ReadOnly = true;
            this.Column_key.Width = 250;
            //
            // Column_value
            //
            this.Column_value.HeaderText = "Value";
            this.Column_value.Name = "Column_value";
            this.Column_value.ReadOnly = true;
            this.Column_value.Width = 500;
            //
            // button_refresh
            //
            this.button_refresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button_refresh.Font = new System.Drawing.Font("Consolas", 11F);
            this.button_refresh.Location = new System.Drawing.Point(600, 527);
            this.button_refresh.Name = "button_refresh";
            this.button_refresh.Size = new System.Drawing.Size(85, 28);
            this.button_refresh.TabIndex = 1;
            this.button_refresh.Text = "Refresh";
            this.button_refresh.UseVisualStyleBackColor = true;
            this.button_refresh.Click += new System.EventHandler(this.button_refresh_Click);
            //
            // button_close
            //
            this.button_close.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button_close.Font = new System.Drawing.Font("Consolas", 11F);
            this.button_close.Location = new System.Drawing.Point(691, 527);
            this.button_close.Name = "button_close";
            this.button_close.Size = new System.Drawing.Size(85, 28);
            this.button_close.TabIndex = 2;
            this.button_close.Text = "Close";
            this.button_close.UseVisualStyleBackColor = true;
            this.button_close.Click += new System.EventHandler(this.button_close_Click);
            //
            // FormServerInfo
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 562);
            this.Controls.Add(this.button_close);
            this.Controls.Add(this.button_refresh);
            this.Controls.Add(this.tabControl1);
            this.Font = new System.Drawing.Font("Consolas", 11F);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormServerInfo";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Server Info";
            this.Load += new System.EventHandler(this.FormServerInfo_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage_summary.ResumeLayout(false);
            this.groupBox_stats.ResumeLayout(false);
            this.groupBox_stats.PerformLayout();
            this.groupBox_memory.ResumeLayout(false);
            this.groupBox_memory.PerformLayout();
            this.groupBox_server.ResumeLayout(false);
            this.groupBox_server.PerformLayout();
            this.tabPage_raw.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_info)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage_summary;
        private System.Windows.Forms.TabPage tabPage_raw;
        private System.Windows.Forms.DataGridView dataGridView_info;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_key;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_value;
        private System.Windows.Forms.Button button_refresh;
        private System.Windows.Forms.Button button_close;
        private System.Windows.Forms.GroupBox groupBox_server;
        private System.Windows.Forms.GroupBox groupBox_memory;
        private System.Windows.Forms.GroupBox groupBox_stats;
        private System.Windows.Forms.Label label_version;
        private System.Windows.Forms.Label label_version_val;
        private System.Windows.Forms.Label label_mode;
        private System.Windows.Forms.Label label_mode_val;
        private System.Windows.Forms.Label label_role;
        private System.Windows.Forms.Label label_role_val;
        private System.Windows.Forms.Label label_os;
        private System.Windows.Forms.Label label_os_val;
        private System.Windows.Forms.Label label_clients;
        private System.Windows.Forms.Label label_clients_val;
        private System.Windows.Forms.Label label_uptime;
        private System.Windows.Forms.Label label_uptime_val;
        private System.Windows.Forms.Label label_mem;
        private System.Windows.Forms.Label label_mem_val;
        private System.Windows.Forms.Label label_peakmem;
        private System.Windows.Forms.Label label_peakmem_val;
        private System.Windows.Forms.Label label_maxmem;
        private System.Windows.Forms.Label label_maxmem_val;
        private System.Windows.Forms.Label label_hitrate;
        private System.Windows.Forms.Label label_hitrate_val;
        private System.Windows.Forms.Label label_ops;
        private System.Windows.Forms.Label label_ops_val;
        private System.Windows.Forms.Label label_totalcmd;
        private System.Windows.Forms.Label label_totalcmd_val;
        private System.Windows.Forms.Label label_expired;
        private System.Windows.Forms.Label label_expired_val;
        private System.Windows.Forms.Label label_evicted;
        private System.Windows.Forms.Label label_evicted_val;
        private System.Windows.Forms.Label label_pubsub;
        private System.Windows.Forms.Label label_pubsub_val;
    }
}
