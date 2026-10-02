using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.Xml;
using System.Xml.Linq;
using StackExchange.Redis;
using System.IO;

namespace RedisGuiManager
{
    public partial class ValueControl : UserControl
    {
        public enum DisplayType
        {
            Text,
            Json,
            Xml,
            Hex,
        }

        private RedisValue value;
        private string hexTempFile;
        private string baseline = "";
        private bool changingDisplay;
        private DisplayType shownType;
        public Func<bool> HasOtherChanges { get; set; }
        public Action DiscardOtherChanges { get; set; }
        private bool connectionReadOnly;
        public bool ConnectionReadOnly
        {
            get => connectionReadOnly;
            set { connectionReadOnly = value; textBox_value.ReadOnly = value || !CanEditText; }
        }
        public bool IsDirty => textBox_value.Text != baseline || (HasOtherChanges?.Invoke() ?? false);
        public RedisValue OriginalValue => value;
        public void AcceptChanges() { baseline = textBox_value.Text; }
        public bool ConfirmDiscard()
        {
            if (!IsDirty) return true;
            if (MessageBox.Show(this, "Discard unsaved edits?", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            DiscardOtherChanges?.Invoke();
            AcceptChanges();
            return true;
        }
        public static bool ConfirmAll(Control root)
        {
            if (root is ValueControl value && !value.ConfirmDiscard()) return false;
            foreach (Control child in root.Controls) if (!ConfirmAll(child)) return false;
            return true;
        }
        public void ProtectSelection(DataGridView grid)
        {
            DataGridViewRow previous = null;
            bool restoring = false;
            grid.SelectionChanged += (s, e) =>
            {
                if (restoring) return;
                var selected = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0] : null;
                if (selected == previous) return;
                if (!ConfirmDiscard() && previous?.DataGridView == grid)
                {
                    restoring = true;
                    grid.ClearSelection();
                    previous.Selected = true;
                    grid.CurrentCell = previous.Cells[0];
                    restoring = false;
                    return;
                }
                previous = selected;
            };
        }
        public bool CanEditText { get; private set; } = true;

        private int lastSeachIndex = -1;
        private string searchCondition = string.Empty;

        public static DisplayType dpType = DisplayType.Text;

        public ValueControl()
        {
            InitializeComponent();
            hexEditor_value.ReadOnlyMode = true;
            hexEditor_value.ZoomScale = 1.5;

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }

            dpType = (DisplayType)Config.dp_type;

            switch (dpType)
            {
                case DisplayType.Text:
                {
                    radioButton_display_type_text.Checked = true;
                }
                break;
                case DisplayType.Json:
                {
                    radioButton_display_type_json.Checked = true;
                }
                break;
                case DisplayType.Xml:
                {
                    radioButton_display_type_xml.Checked = true;
                }
                break;
                case DisplayType.Hex:
				{
                    radioButton_display_type_hex.Checked = true;
				}
                break;
                default:
                {
                    radioButton_display_type_text.Checked = true;
                }
                break;
            }
        }

        public static DisplayType GetDisplayType()
        {
            return dpType;
        }

        public string KeySourceValue()
        {
            return value.ToString();
        }

        public string EditedValue()
        {
            return textBox_value.Text;
        }

        public void SetValue(object value)
        {
            if (!ConfirmDiscard()) return;
            if (value == null)
			{
                this.value = new RedisValue("");
			}
            else if (value is string)
			{
                this.value = new RedisValue((string)value);
			}
            else
			{
                this.value = (RedisValue)value;
            }

            byte[] bytes = (byte[])this.value;
            CanEditText = bytes == null || Encoding.UTF8.GetBytes(this.value.ToString()).SequenceEqual(bytes);
            textBox_value.ReadOnly = connectionReadOnly || !CanEditText;
            ShowValue();
            AcceptChanges();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            hexEditor_value.CloseProvider();
            if (hexTempFile != null) File.Delete(hexTempFile);
            base.OnHandleDestroyed(e);
        }

        private void ShowValue()
        {
            try
            {
                shownType = dpType;
                int size = (int)value.Length();
                label_size.Text = "Size : " + Utils.GetSizeDescription(size);

                if (radioButton_display_type_text.Checked)
                {
                    textBox_value.Text = value.ToString();
                }
                else if (radioButton_display_type_json.Checked)
                {
                    if (value != string.Empty)
                    {
                        try
                        {
                            textBox_value.Text = JsonConvert.DeserializeObject(value.ToString()).ToString();
                        }
                        catch (Exception)
                        {
                            textBox_value.Text = value.ToString();
                        }
                    }
                    else
                    {
                        textBox_value.Text = value.ToString();
                    }
                }
                else if (radioButton_display_type_xml.Checked)
                {
                    if (value != string.Empty)
                    {
                        try
                        {
                            textBox_value.Text = XElement.Parse(value.ToString()).ToString();
                        }
                        catch (Exception)
                        {
                            textBox_value.Text = value.ToString();
                        }
                    }
                    else
                    {
                        textBox_value.Text = value.ToString();
                    }
                }
                else if (radioButton_display_type_hex.Checked)
				{
                    hexEditor_value.CloseProvider();
                    if (hexTempFile != null) File.Delete(hexTempFile);
                    hexTempFile = Path.GetTempFileName();
                    File.WriteAllBytes(hexTempFile, (byte[])value ?? Array.Empty<byte>());
                    hexEditor_value.FileName = hexTempFile;
                }
            }
            catch
            {
                textBox_value.Text = value;
            }
        }

        private void ValueControl_Load(object sender, EventArgs e)
        {
            radioButton_display_type_text.CheckedChanged += radioButton_display_type_CheckedChanged;
            radioButton_display_type_json.CheckedChanged += radioButton_display_type_CheckedChanged;
            radioButton_display_type_xml.CheckedChanged += radioButton_display_type_CheckedChanged;
            radioButton_display_type_hex.CheckedChanged += radioButton_display_type_CheckedChanged;
        }

        private void radioButton_display_type_CheckedChanged(object sender, EventArgs e)
        {
            if (changingDisplay || sender is RadioButton radio && !radio.Checked) return;
            if (!ConfirmDiscard())
            {
                changingDisplay = true;
                new[] { radioButton_display_type_text, radioButton_display_type_json, radioButton_display_type_xml, radioButton_display_type_hex }[(int)shownType].Checked = true;
                changingDisplay = false;
                return;
            }
            ShowValue();
            AcceptChanges();
        }

        private void linkLabel_search_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (string.IsNullOrEmpty(textBox_value.Text) || string.IsNullOrEmpty(textBox_search.Text))
            {
                return;
            };

            if (string.IsNullOrEmpty(searchCondition))
            {
                searchCondition = textBox_search.Text;
            }
            else
            {
                if (searchCondition != textBox_search.Text)
                {
                    lastSeachIndex = -1;
                    searchCondition = textBox_search.Text;
                }
            }

            StringComparison case_sensitive = StringComparison.CurrentCultureIgnoreCase;
            if (checkBox_case_sensitive.Checked)
            {
                case_sensitive = StringComparison.CurrentCulture;
            }

            int index = textBox_value.Text.IndexOf(textBox_search.Text, lastSeachIndex == -1 ? 0 : lastSeachIndex, case_sensitive);
            if (index == -1)
            {
                lastSeachIndex = -1;

                index = textBox_value.Text.IndexOf(textBox_search.Text, lastSeachIndex == -1 ? 0 : lastSeachIndex, case_sensitive);
                if (index == -1)
                {
                    textBox_value.SelectionLength = 0;
                    return;
                }
            }

            lastSeachIndex = index + 1;

            textBox_value.SelectionStart = index;
            textBox_value.SelectionLength = textBox_search.Text.Length;
            textBox_value.ScrollToCaret();
            textBox_value.Focus();

            textBox_search.Focus();
        }

        private void textBox_search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                linkLabel_search_LinkClicked(null, null);
            }
        }

        private void radioButton_display_type_text_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_display_type_text.Checked)
            {
				elementHost_hex.Visible = false;
                label_byte_per_line.Visible = false;
                numericUpDown_byte_per_line.Visible = false;

                textBox_value.Visible = true;

				dpType = DisplayType.Text;
                Config.dp_type = (int)dpType;
            }
        }

        private void radioButton_display_type_json_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_display_type_json.Checked)
            {
                elementHost_hex.Visible = false;
                label_byte_per_line.Visible = false;
                numericUpDown_byte_per_line.Visible = false;

                textBox_value.Visible = true;

				dpType = DisplayType.Json;
                Config.dp_type = (int)dpType;
            }
        }

        private void radioButton_display_type_xml_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_display_type_xml.Checked)
            {
                elementHost_hex.Visible = false;
                label_byte_per_line.Visible = false;
                numericUpDown_byte_per_line.Visible = false;

                textBox_value.Visible = true;

				dpType = DisplayType.Xml;
                Config.dp_type = (int)dpType;
            }
        }

		private void radioButton_display_type_hex_CheckedChanged(object sender, EventArgs e)
		{
            if (radioButton_display_type_hex.Checked)
			{
                elementHost_hex.Visible = true;
                label_byte_per_line.Visible = true;
                label_byte_per_line.BringToFront();
                numericUpDown_byte_per_line.Visible = true;
                numericUpDown_byte_per_line.BringToFront();

                textBox_value.Visible = false;

				dpType = DisplayType.Hex;
				Config.dp_type = (int)dpType;
			}
		}

		private void numericUpDown_byte_per_line_ValueChanged(object sender, EventArgs e)
		{
            hexEditor_value.BytePerLine = (int)numericUpDown_byte_per_line.Value;
		}
	}
}
