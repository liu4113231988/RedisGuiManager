using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using StackExchange.Redis;
using System.Text.RegularExpressions;

namespace RedisGuiManager
{
    public partial class ConsoleControl : UserControl
    {
        private RedisClient client;
        private IDatabase redis;
        private int db_num = 0;
        private List<string> cmd_history = new List<string>();
        private int cmd_history_cursor = 0;

        public ConsoleControl()
        {
            InitializeComponent();
        }

        private void textBox_input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
			{
                e.Handled = true;

                if (cmd_history.Count > 0 &&
                    cmd_history_cursor > 0)
				{
                    cmd_history_cursor--;
                    textBox_input.Text = cmd_history[cmd_history_cursor];
                    textBox_input.SelectionStart = textBox_input.Text.Length;
                }
			}
            else if (e.KeyCode == Keys.Down)
			{
                e.Handled = true;

                cmd_history_cursor++;
                if (cmd_history_cursor >= cmd_history.Count)
				{
                    cmd_history_cursor = cmd_history.Count;
                }

                if (cmd_history_cursor == cmd_history.Count)
				{
                    textBox_input.Text = "";
				}
                else
				{
                    textBox_input.Text = cmd_history[cmd_history_cursor];
                    textBox_input.SelectionStart = textBox_input.Text.Length;
                    e.Handled = true;
                }
			}
            else if (e.KeyCode == Keys.Enter)
            {
                if (string.IsNullOrEmpty(textBox_input.Text))
                {
                    return;
                }

                cmd_history.Add(textBox_input.Text);
                cmd_history_cursor = cmd_history.Count;

                if (this.client == null)
				{
					return;
				}

				if (client.Redis == null)
				{
					OperateResult connect = client.Connect();
					if (connect.IsSuccess == false)
					{
						AppendOutput("Connection failed" + "\r\n");
						return;
					}

                    redis = client.GetDB(db_num);
                }

				AppendOutput($"{client.Settings.name}:{db_num}> " + textBox_input.Text + Environment.NewLine);

				var args = Regex.Matches(textBox_input.Text, @"[\""](?<value>.+?)[\""]|(?<value>[^\s]+)")
                    .Cast<Match>()
                    .Select(m => m.Groups["value"].Value)
                    .ToArray();
				var cmd = args[0];
				args = args.Skip(1).ToArray();

				try
				{
                    // The gate needs the whole command, not just the verb: read-only families such as
                    // CONFIG GET, CLIENT LIST or XINFO STREAM are only recognised with their subcommand.
                    if (client.Settings.read_only && Utils.IsReadOnlyCommandAllowed(textBox_input.Text) == false)
                        throw new InvalidOperationException($"[{cmd}] is not allowed in read-only mode");
                    var result = redis.Execute(cmd, args);
                    if (result.IsNull)
					{
                        AppendOutput($"(nil)\r\n");
                    }

                    // Resp2Type (not the obsolete ResultType alias) so RESP3 replies are still classified.
                    switch (result.Resp2Type)
					{
                        case ResultType.None:
							{
                                AppendOutput($"(none)\r\n");
                            }
                            break;

                        case ResultType.SimpleString:
							{
                                AppendOutput($"{result}\r\n");
                            }
                            break;

                        case ResultType.Error:
							{
                                AppendOutput($"ERROR : {result}\r\n");
                            }
                            break;

                        case ResultType.Integer:
							{
                                AppendOutput($"(integer) {result}\r\n");
                            }
                            break;

                        case ResultType.BulkString:
							{
                                AppendOutput($"{result}\r\n");
                            }
                            break;

                        case ResultType.Array:
                        						{
                                                    int idx = 0;
                        							var results = (RedisResult[])result;

                                                    if (results.Count() > 0)
                                                    {
                                                        foreach (var item in results)
                                                        {
                                                            if (item.Resp2Type != ResultType.Array)
                        									{
                                            if (item.IsNull)
											{
                                                AppendOutput($" {++idx}) (nil)\r\n");
                                            }
                                            else
											{
                                                AppendOutput($" {++idx}) \"{item}\"\r\n");
                                            }
                                        }
                                        else
										{
                                            AppendOutput($" {++idx})");
                                            print_sub_multi_value(item);
                                        }
                                    }
                                }
                                else
								{
                                    AppendOutput("(empty list or set)\r\n");
								}
							}
                            break;
					}

                    AppendOutput($"\r\n");
                }
                catch (Exception ex)
				{
                    AppendOutput(ex.Message + "\r\n");
                }

                // Only a command that actually ran may move the local db pointer. In read-only mode
                // SELECT is rejected, so switching anyway would silently redirect every later
                // command to a database the user never picked.
                if (Utils.IsReadOnlyCommandAllowed(textBox_input.Text))
                {
                    check_change_db(textBox_input.Text);
                }

                textBox_input.Text = "";
            }
        }

        // A single command (e.g. LRANGE 0 -1 on a big list) can return megabytes, which would grow the
        // RichTextBox without bound and freeze the UI. Cap per-value and total output size.
        private const int MaxValueLength = 10_000;
        private const int MaxOutputLength = 2_000_000;

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (text.Length > MaxValueLength)
            {
                text = text.Substring(0, MaxValueLength) +
                       $"\r\n... [{text.Length - MaxValueLength:N0} more characters truncated]";
            }

            textBox_output.AppendText(text);

            if (textBox_output.TextLength > MaxOutputLength)
            {
                int drop = textBox_output.TextLength - MaxOutputLength;
                textBox_output.Select(0, drop);
                textBox_output.SelectedText = string.Empty;
                textBox_output.SelectionStart = 0;
                textBox_output.SelectionLength = 0;
                AppendOutputMarker();
            }
        }

        private void AppendOutputMarker()
        {
            textBox_output.AppendText("[earlier output trimmed]\r\n");
        }

        private void print_sub_multi_value(RedisResult val)
		{
            bool is_first = true;
			int idx = 0;
			var results = (RedisResult[])val;

			if (results.Count() > 0)
			{
				foreach (var item in results)
				{
                    if (is_first)
					{
                        is_first = false;
                        AppendOutput($" {++idx}) \"{item}\"\r\n");
                    }
                    else
					{
                        AppendOutput($"    {++idx}) \"{item}\"\r\n");
                    }
				}
			}
		}

        private void check_change_db(string text)
		{
            var m = Regex.Match(text, "^select (?<db_num>[\\d]+)");
            if (m.Success)
			{
                db_num = int.Parse(m.Groups["db_num"].Value);
                redis = client.GetDB(db_num);
                label_cur.Text = $"{client.Settings.name}:{db_num}>";
            }
		}

        public void SetRedis(RedisClient client)
        {
            this.client = client;
            db_num = 0;

			if (client.Redis == null)
			{
				OperateResult connect = client.Connect();
				if (connect.IsSuccess == false)
				{
					AppendOutput("Connection failed" + "\r\n");
					return;
				}
			}

            redis = client.GetDB(db_num);
            label_cur.Text = $"{client.Settings.name}:{db_num}>";
            textBox_input.Location = new Point(label_cur.Left + label_cur.Width + 2, textBox_input.Location.Y);
        }

        private void ConsoleControl_Load(object sender, EventArgs e)
        {
            textBox_input.Focus();
        }

		private void label_cur_TextChanged(object sender, EventArgs e)
		{
            label_cur.Width = TextRenderer.MeasureText(label_cur.Text, label_cur.Font).Width;
            textBox_input.Location = new Point(label_cur.Left + label_cur.Width + 2, textBox_input.Location.Y);
		}
	}
}
