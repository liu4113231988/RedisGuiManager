using System.Drawing;
using System.Windows.Forms;

namespace RedisGuiManager
{
    /// <summary>Tab construction for <see cref="FormServerTools"/>.</summary>
    public partial class FormServerTools
    {
        private TabPage BuildConfigTab()
        {
            var page = new TabPage("Configuration");

            var patternLabel = MakeLabel("Pattern:", 70);
            patternLabel.Location = new Point(12, 16);
            configPattern = new TextBox { Location = new Point(84, 12), Width = 220, Text = "*" };
            var getButton = MakeButton("Get", async (s, e) => await LoadConfigAsync(), 80);
            getButton.Location = new Point(314, 11);
            page.Controls.Add(patternLabel);
            page.Controls.Add(configPattern);
            page.Controls.Add(getButton);

            configGrid = MakeGrid(("Setting", 320), ("Value", 600));
            configGrid.MultiSelect = true;
            configGrid.Dock = DockStyle.Fill;
            configGrid.CellDoubleClick += async (s, e) =>
            {
                if (e.RowIndex < 0) return;
                configPattern.Text = configGrid.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "*";
                await LoadConfigAsync();
            };
            page.Controls.Add(configGrid);

            var setGroup = new GroupBox
            {
                Text = "Change a setting",
                Dock = DockStyle.Bottom,
                Height = 78,
                Padding = new Padding(8)
            };

            var nameLabel = MakeLabel("Name:", 50);
            nameLabel.Location = new Point(12, 26);
            configSetName = new TextBox { Location = new Point(64, 22), Width = 220 };
            var valueLabel = MakeLabel("Value:", 50);
            valueLabel.Location = new Point(300, 26);
            configSetValue = new TextBox { Location = new Point(352, 22), Width = 350 };
            var applyButton = MakeButton("Apply (CONFIG SET)", async (s, e) =>
            {
                await ApplyConfigSetAsync(configSetName.Text, configSetValue.Text);
            }, 150);
            applyButton.Location = new Point(712, 20);
            applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            setGroup.Controls.Add(nameLabel);
            setGroup.Controls.Add(configSetName);
            setGroup.Controls.Add(valueLabel);
            setGroup.Controls.Add(configSetValue);
            setGroup.Controls.Add(applyButton);
            page.Controls.Add(setGroup);

            return page;
        }

        private TabPage BuildClientsTab()
        {
            var page = new TabPage("Clients");

            var refresh = MakeButton("Refresh", async (s, e) => await LoadClientsAsync(), 80);
            refresh.Location = new Point(12, 11);
            var kill = MakeButton("Kill selected", async (s, e) => await KillSelectedClientsAsync(), 110);
            kill.Location = new Point(100, 11);
            clientsCount = MakeLabel("0 connected clients", 320);
            clientsCount.Location = new Point(220, 16);

            page.Controls.Add(refresh);
            page.Controls.Add(kill);
            page.Controls.Add(clientsCount);

            clientsGrid = MakeGrid(
                ("Id", 70), ("Address", 150), ("Name", 110), ("DB", 45), ("Last command", 170),
                ("Idle", 65), ("Age", 65), ("Subs", 55), ("Flags", 110), ("Library", 130));
            clientsGrid.Dock = DockStyle.Fill;
            // "Kill selected" is meant to act on several clients at once.
            clientsGrid.MultiSelect = true;
            page.Controls.Add(clientsGrid);

            return page;
        }

        private TabPage BuildMemoryTab()
        {
            var page = new TabPage("Memory");

            var keyLabel = MakeLabel("Key:", 45);
            keyLabel.Location = new Point(12, 16);
            memoryKey = new TextBox { Location = new Point(58, 12), Width = 240 };
            var usage = MakeButton("MEMORY USAGE", async (s, e) => await LoadMemoryUsageAsync(), 130);
            usage.Location = new Point(306, 11);
            var stats = MakeButton("MEMORY STATS", async (s, e) => await LoadMemoryStatsAsync(), 130);
            stats.Location = new Point(444, 11);
            var doctor = MakeButton("MEMORY DOCTOR", async (s, e) => await LoadMemoryDoctorAsync(), 140);
            doctor.Location = new Point(582, 11);

            page.Controls.Add(keyLabel);
            page.Controls.Add(memoryKey);
            page.Controls.Add(usage);
            page.Controls.Add(stats);
            page.Controls.Add(doctor);

            memoryDoctorOutput = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 110
            };

            memoryGrid = MakeGrid(("Metric", 380), ("Value", 540));
            memoryGrid.Dock = DockStyle.Fill;
            page.Controls.Add(memoryGrid);
            page.Controls.Add(memoryDoctorOutput);

            return page;
        }

        private TabPage BuildPersistenceTab()
        {
            var page = new TabPage("Persistence");

            var refresh = MakeButton("Refresh", async (s, e) => await LoadPersistenceAsync(), 80);
            refresh.Location = new Point(12, 11);
            var save = MakeButton("SAVE (blocking)", async (s, e) => await RunSaveAsync(background: false), 140);
            save.Location = new Point(100, 11);
            var bgsave = MakeButton("BGSAVE", async (s, e) => await RunSaveAsync(background: true), 90);
            bgsave.Location = new Point(248, 11);
            var rewrite = MakeButton("BGREWRITEAOF", async (s, e) => await RunRewriteAofAsync(), 130);
            rewrite.Location = new Point(346, 11);

            page.Controls.Add(refresh);
            page.Controls.Add(save);
            page.Controls.Add(bgsave);
            page.Controls.Add(rewrite);

            persistenceGrid = MakeGrid(("Setting", 380), ("Value", 540));
            persistenceGrid.Dock = DockStyle.Fill;
            page.Controls.Add(persistenceGrid);

            return page;
        }

        private TabPage BuildClusterTab()
        {
            var page = new TabPage("Cluster");

            var refresh = MakeButton("Refresh", async (s, e) => await LoadClusterAsync(), 80);
            refresh.Location = new Point(12, 11);
            refresh.BringToFront();

            clusterInfo = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 110
            };

            clusterGrid = MakeGrid(
                ("Node", 100), ("Endpoint", 170), ("Role", 80), ("Link state", 100), ("Slots", 60), ("Flag", 70));
            clusterGrid.Dock = DockStyle.Fill;

            page.Controls.Add(clusterGrid);
            page.Controls.Add(clusterInfo);
            page.Controls.Add(refresh);

            return page;
        }

        private TabPage BuildDiagnosticsTab()
        {
            var page = new TabPage("Diagnostics");

            var keyLabel = MakeLabel("Key:", 45);
            keyLabel.Location = new Point(12, 16);
            diagKey = new TextBox { Location = new Point(58, 12), Width = 240 };
            var objectInfo = MakeButton("OBJECT info", async (s, e) => await LoadObjectInfoAsync(), 120);
            objectInfo.Location = new Point(306, 11);

            page.Controls.Add(keyLabel);
            page.Controls.Add(diagKey);
            page.Controls.Add(objectInfo);

            diagGrid = MakeGrid(("Property", 260), ("Value", 660));
            diagGrid.Dock = DockStyle.Fill;
            page.Controls.Add(diagGrid);

            var sentinelGroup = new GroupBox
            {
                Text = "Sentinel (only when this connection points at a Sentinel instance)",
                Dock = DockStyle.Bottom,
                Height = 200
            };

            sentinelCombo = new ComboBox
            {
                Location = new Point(12, 24),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            sentinelCombo.SelectedIndexChanged += async (s, e) => await LoadSentinelMasterAsync();
            var sentinelRefresh = MakeButton("Refresh masters", async (s, e) => await LoadSentinelMastersAsync(), 130);
            sentinelRefresh.Location = new Point(260, 22);

            sentinelGrid = MakeGrid(("Property", 260), ("Value", 660));
            sentinelGrid.Dock = DockStyle.Fill;

            sentinelGroup.Controls.Add(sentinelCombo);
            sentinelGroup.Controls.Add(sentinelRefresh);
            sentinelGroup.Controls.Add(sentinelGrid);
            page.Controls.Add(sentinelGroup);

            return page;
        }
    }
}