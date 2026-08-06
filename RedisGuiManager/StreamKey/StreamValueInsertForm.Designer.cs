namespace RedisGuiManager
{
    partial class StreamValueInsertForm
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
            this.label_key = new System.Windows.Forms.Label();
            this.textBox_key = new System.Windows.Forms.TextBox();
            this.label_id = new System.Windows.Forms.Label();
            this.textBox_id = new System.Windows.Forms.TextBox();
            this.label_field = new System.Windows.Forms.Label();
            this.textBox_field = new System.Windows.Forms.TextBox();
            this.label_value = new System.Windows.Forms.Label();
            this.textBox_value = new System.Windows.Forms.TextBox();
            this.button_save = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label_key
            // 
            this.label_key.AutoSize = true;
            this.label_key.Font = new System.Drawing.Font("Consolas", 12F);
            this.label_key.Location = new System.Drawing.Point(20, 20);
            this.label_key.Name = "label_key";
            this.label_key.Size = new System.Drawing.Size(36, 19);
            this.label_key.TabIndex = 0;
            this.label_key.Text = "Key";
            // 
            // textBox_key
            // 
            this.textBox_key.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_key.Location = new System.Drawing.Point(100, 17);
            this.textBox_key.Name = "textBox_key";
            this.textBox_key.ReadOnly = true;
            this.textBox_key.Size = new System.Drawing.Size(380, 26);
            this.textBox_key.TabIndex = 1;
            // 
            // label_id
            // 
            this.label_id.AutoSize = true;
            this.label_id.Font = new System.Drawing.Font("Consolas", 12F);
            this.label_id.Location = new System.Drawing.Point(20, 55);
            this.label_id.Name = "label_id";
            this.label_id.Size = new System.Drawing.Size(30, 19);
            this.label_id.TabIndex = 2;
            this.label_id.Text = "ID";
            // 
            // textBox_id
            // 
            this.textBox_id.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_id.Location = new System.Drawing.Point(100, 52);
            this.textBox_id.Name = "textBox_id";
            this.textBox_id.Size = new System.Drawing.Size(380, 26);
            this.textBox_id.TabIndex = 3;
            // 
            // label_field
            // 
            this.label_field.AutoSize = true;
            this.label_field.Font = new System.Drawing.Font("Consolas", 12F);
            this.label_field.Location = new System.Drawing.Point(20, 90);
            this.label_field.Name = "label_field";
            this.label_field.Size = new System.Drawing.Size(48, 19);
            this.label_field.TabIndex = 4;
            this.label_field.Text = "Field";
            // 
            // textBox_field
            // 
            this.textBox_field.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_field.Location = new System.Drawing.Point(100, 87);
            this.textBox_field.Name = "textBox_field";
            this.textBox_field.Size = new System.Drawing.Size(380, 26);
            this.textBox_field.TabIndex = 5;
            // 
            // label_value
            // 
            this.label_value.AutoSize = true;
            this.label_value.Font = new System.Drawing.Font("Consolas", 12F);
            this.label_value.Location = new System.Drawing.Point(20, 125);
            this.label_value.Name = "label_value";
            this.label_value.Size = new System.Drawing.Size(54, 19);
            this.label_value.TabIndex = 6;
            this.label_value.Text = "Value";
            // 
            // textBox_value
            // 
            this.textBox_value.Font = new System.Drawing.Font("Consolas", 12F);
            this.textBox_value.Location = new System.Drawing.Point(100, 122);
            this.textBox_value.Multiline = true;
            this.textBox_value.Name = "textBox_value";
            this.textBox_value.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_value.Size = new System.Drawing.Size(380, 80);
            this.textBox_value.TabIndex = 7;
            // 
            // button_save
            // 
            this.button_save.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.button_save.Font = new System.Drawing.Font("Consolas", 12F);
            this.button_save.Location = new System.Drawing.Point(170, 215);
            this.button_save.Name = "button_save";
            this.button_save.Size = new System.Drawing.Size(140, 31);
            this.button_save.TabIndex = 8;
            this.button_save.Text = "Save";
            this.button_save.UseVisualStyleBackColor = true;
            this.button_save.Click += new System.EventHandler(this.button_save_Click);
            // 
            // StreamValueInsertForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(500, 260);
            this.Controls.Add(this.button_save);
            this.Controls.Add(this.textBox_value);
            this.Controls.Add(this.label_value);
            this.Controls.Add(this.textBox_field);
            this.Controls.Add(this.label_field);
            this.Controls.Add(this.textBox_id);
            this.Controls.Add(this.label_id);
            this.Controls.Add(this.textBox_key);
            this.Controls.Add(this.label_key);
            this.Font = new System.Drawing.Font("Consolas", 12F);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "StreamValueInsertForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Add Stream Entry";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label label_key;
        private System.Windows.Forms.TextBox textBox_key;
        private System.Windows.Forms.Label label_id;
        private System.Windows.Forms.TextBox textBox_id;
        private System.Windows.Forms.Label label_field;
        private System.Windows.Forms.TextBox textBox_field;
        private System.Windows.Forms.Label label_value;
        private System.Windows.Forms.TextBox textBox_value;
        private System.Windows.Forms.Button button_save;
    }
}
