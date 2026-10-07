using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    public partial class FormMigrateKey : Form
    {
        public FormMigrateKey()
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }
        }

        public string Host
        {
            get { return textBox_host.Text; }
        }

		public int Port
		{
			get { return int.Parse(textBox_port.Text); }
		}

        public int DBNumber
		{
            get { return int.Parse(textBox_db_num.Text); }
		}

        public string KeyPattern
		{
            get { return textBox_key_pattern.Text; }
		}

        private bool usePattern = false;
        public bool UsePattern
        {
            get
			{
                return usePattern;
			}
            set
			{
                label_key_pattern.Visible = value;
                textBox_key_pattern.Visible = value;
			}
        }

		private void button_ok_Click(object sender, EventArgs e)
        {
            if (textBox_host.Text == "")
			{
                MessageBox.Show(UiText.HostIsEmpty);
                return;
			}

            if (int.TryParse(textBox_port.Text, out int port) == false)
			{
                MessageBox.Show(UiText.PortMustBeNumber);
                return;
			}

            if (int.TryParse(textBox_db_num.Text, out int db_num) == false)
			{
                MessageBox.Show(UiText.DbMustBeNumber);
                return;
			}

            if (usePattern == true &&
                textBox_key_pattern.Text == "")
			{
                MessageBox.Show(UiText.KeyPatternRequired);
                return;
			}

            DialogResult = DialogResult.OK;
        }

        private void textBox_input_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                button_ok_Click(null, null);
                return;
            }
        }
    }
}
