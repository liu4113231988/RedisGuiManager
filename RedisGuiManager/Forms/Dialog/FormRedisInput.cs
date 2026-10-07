using StackExchange.Redis;
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
    public partial class FormRedisInput : Form
    {
        private IDatabase redis;

        public FormRedisInput(IDatabase redis)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            this.redis = redis;
        }

		private void FormRedisInput_Load(object sender, EventArgs e)
		{
            tabControl_type.SelectedTab.SelectNextControl(tabControl_type.SelectedTab, true, true, true, true);
        }

		private void button_string_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_string_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
            }
            else
            {
                if (redis.StringSet(textBox_string_key.Text, textBox_string_value.Text, when: When.NotExists))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddStringFailed);
                }
            }
        }

        private void button_hash_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_hash_key.Text) || string.IsNullOrEmpty(textBox_hash_hash_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
            }
            else
            {
                if (redis.HashSet(textBox_hash_key.Text, textBox_hash_hash_key.Text, textBox_hash_value.Text))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddHashFailed);
                }
            }
        }

        private void button_list_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_list_value.Text) || string.IsNullOrEmpty(textBox_list_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
            }
            else
            {
                bool result = false;
                if (radioButton_list_right_push.Checked)
                {
                    result = redis.ListRightPush(textBox_list_key.Text, textBox_list_value.Text) > 0;
                }
                else
                {
                    result = redis.ListLeftPush(textBox_list_key.Text, textBox_list_value.Text) > 0;
                }

                if (result)
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddListFailed);
                }
            }
        }

        private void button_set_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_set_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
            }
            else
            {
                if (redis.SetAdd(textBox_set_key.Text, textBox_set_value.Text))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddSetFailed);
                }
            }
        }

        private void button_zset_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_zset_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
            }
            else
            {
                if (double.TryParse(textBox_zset_score.Text, out double score) == false)
                {
                    MessageBox.Show(UiText.ScoreMustBeNumber);
                    return;
                }

                if (redis.SortedSetAdd(textBox_zset_key.Text, textBox_zset_value.Text, score))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddZSetFailed);
                }
            }
        }

        private void button_stream_save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_stream_key.Text))
            {
                MessageBox.Show(UiText.KeyIsEmpty);
                return;
            }

            if (string.IsNullOrEmpty(textBox_stream_field.Text))
            {
                MessageBox.Show(UiText.FieldIsEmpty);
                return;
            }

            string id = string.IsNullOrEmpty(textBox_stream_id.Text) ? "*" : textBox_stream_id.Text;

            try
            {
                var nameValues = new NameValueEntry[]
                {
                    new NameValueEntry(textBox_stream_field.Text, textBox_stream_value.Text)
                };

                var result = redis.StreamAdd(textBox_stream_key.Text, nameValues, id);

                if (!result.IsNull)
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddStreamEntryFailed);
                }
            }
            catch (RedisServerException ex)
            {
                MessageBox.Show(ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

		private void tabControl_type_SelectedIndexChanged(object sender, EventArgs e)
		{
            tabControl_type.SelectedTab.SelectNextControl(tabControl_type.SelectedTab, true, true, true, true);
        }
	}
}
