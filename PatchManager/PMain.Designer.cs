namespace PatchManager
{
    partial class PMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.labelControl1 = new DevExpress.XtraEditors.LabelControl();
            this.CleanClientButtonEdit = new DevExpress.XtraEditors.ButtonEdit();
            this.DLookAndFeel = new DevExpress.LookAndFeel.DefaultLookAndFeel();
            this.labelControl2 = new DevExpress.XtraEditors.LabelControl();
            this.UsernameTextEdit = new DevExpress.XtraEditors.TextEdit();
            this.UseLoginCheckEdit = new DevExpress.XtraEditors.CheckEdit();
            this.labelControl3 = new DevExpress.XtraEditors.LabelControl();
            this.labelControl4 = new DevExpress.XtraEditors.LabelControl();
            this.labelControl5 = new DevExpress.XtraEditors.LabelControl();
            this.HostTextEdit = new DevExpress.XtraEditors.TextEdit();
            this.PasswordTextEdit = new DevExpress.XtraEditors.TextEdit();
            this.UploadPatchButton = new DevExpress.XtraEditors.SimpleButton();
            this.labelControl6 = new DevExpress.XtraEditors.LabelControl();
            this.StatusLabel = new DevExpress.XtraEditors.LabelControl();
            this.labelControl8 = new DevExpress.XtraEditors.LabelControl();
            this.UploadSizeLabel = new DevExpress.XtraEditors.LabelControl();
            this.TotalProgressBar = new DevExpress.XtraEditors.ProgressBarControl();
            this.UploadSpeedLabel = new DevExpress.XtraEditors.LabelControl();
            this.labelControl10 = new DevExpress.XtraEditors.LabelControl();
            this.FolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            ((System.ComponentModel.ISupportInitialize)(this.CleanClientButtonEdit.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.UsernameTextEdit.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.UseLoginCheckEdit.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.HostTextEdit.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PasswordTextEdit.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.TotalProgressBar.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // labelControl1
            // 
            this.labelControl1.Location = new System.Drawing.Point(14, 16);
            this.labelControl1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl1.Name = "labelControl1";
            this.labelControl1.Size = new System.Drawing.Size(67, 14);
            this.labelControl1.TabIndex = 0;
            this.labelControl1.Text = "Clean Client:";
            // 
            // CleanClientButtonEdit
            // 
            this.CleanClientButtonEdit.Location = new System.Drawing.Point(92, 13);
            this.CleanClientButtonEdit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.CleanClientButtonEdit.Name = "CleanClientButtonEdit";
            this.CleanClientButtonEdit.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton()});
            this.CleanClientButtonEdit.Size = new System.Drawing.Size(272, 20);
            this.CleanClientButtonEdit.TabIndex = 1;
            this.CleanClientButtonEdit.EditValueChanged += new System.EventHandler(this.CleanClientButtonEdit_EditValueChanged);
            // 
            // DLookAndFeel
            // 
            this.DLookAndFeel.LookAndFeel.SkinName = "Blue";
            // 
            // labelControl2
            // 
            this.labelControl2.Location = new System.Drawing.Point(55, 44);
            this.labelControl2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl2.Name = "labelControl2";
            this.labelControl2.Size = new System.Drawing.Size(29, 14);
            this.labelControl2.TabIndex = 2;
            this.labelControl2.Text = "Host:";
            // 
            // UsernameTextEdit
            // 
            this.UsernameTextEdit.Location = new System.Drawing.Point(92, 96);
            this.UsernameTextEdit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.UsernameTextEdit.Name = "UsernameTextEdit";
            this.UsernameTextEdit.Size = new System.Drawing.Size(272, 20);
            this.UsernameTextEdit.TabIndex = 3;
            this.UsernameTextEdit.EditValueChanged += new System.EventHandler(this.UsernameTextEdit_EditValueChanged);
            // 
            // UseLoginCheckEdit
            // 
            this.UseLoginCheckEdit.Location = new System.Drawing.Point(92, 69);
            this.UseLoginCheckEdit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.UseLoginCheckEdit.Name = "UseLoginCheckEdit";
            this.UseLoginCheckEdit.Properties.Caption = "";
            this.UseLoginCheckEdit.Size = new System.Drawing.Size(88, 19);
            this.UseLoginCheckEdit.TabIndex = 4;
            this.UseLoginCheckEdit.CheckedChanged += new System.EventHandler(this.UseLoginCheckEdit_CheckedChanged);
            // 
            // labelControl3
            // 
            this.labelControl3.Location = new System.Drawing.Point(27, 72);
            this.labelControl3.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl3.Name = "labelControl3";
            this.labelControl3.Size = new System.Drawing.Size(57, 14);
            this.labelControl3.TabIndex = 5;
            this.labelControl3.Text = "Use Login:";
            // 
            // labelControl4
            // 
            this.labelControl4.Location = new System.Drawing.Point(24, 99);
            this.labelControl4.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl4.Name = "labelControl4";
            this.labelControl4.Size = new System.Drawing.Size(58, 14);
            this.labelControl4.TabIndex = 6;
            this.labelControl4.Text = "Username:";
            // 
            // labelControl5
            // 
            this.labelControl5.Location = new System.Drawing.Point(27, 127);
            this.labelControl5.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl5.Name = "labelControl5";
            this.labelControl5.Size = new System.Drawing.Size(55, 14);
            this.labelControl5.TabIndex = 7;
            this.labelControl5.Text = "Password:";
            // 
            // HostTextEdit
            // 
            this.HostTextEdit.Location = new System.Drawing.Point(92, 41);
            this.HostTextEdit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.HostTextEdit.Name = "HostTextEdit";
            this.HostTextEdit.Size = new System.Drawing.Size(272, 20);
            this.HostTextEdit.TabIndex = 8;
            this.HostTextEdit.EditValueChanged += new System.EventHandler(this.HostTextEdit_EditValueChanged);
            // 
            // PasswordTextEdit
            // 
            this.PasswordTextEdit.Location = new System.Drawing.Point(92, 124);
            this.PasswordTextEdit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.PasswordTextEdit.Name = "PasswordTextEdit";
            this.PasswordTextEdit.Properties.PasswordChar = '*';
            this.PasswordTextEdit.Size = new System.Drawing.Size(272, 20);
            this.PasswordTextEdit.TabIndex = 9;
            this.PasswordTextEdit.EditValueChanged += new System.EventHandler(this.PasswordTextEdit_EditValueChanged);
            // 
            // UploadPatchButton
            // 
            this.UploadPatchButton.Location = new System.Drawing.Point(92, 152);
            this.UploadPatchButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.UploadPatchButton.Name = "UploadPatchButton";
            this.UploadPatchButton.Size = new System.Drawing.Size(272, 25);
            this.UploadPatchButton.TabIndex = 10;
            this.UploadPatchButton.Text = "Upload Patch";
            this.UploadPatchButton.Click += new System.EventHandler(this.UploadPatchButton_Click);
            // 
            // labelControl6
            // 
            this.labelControl6.Location = new System.Drawing.Point(16, 196);
            this.labelControl6.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl6.Name = "labelControl6";
            this.labelControl6.Size = new System.Drawing.Size(39, 14);
            this.labelControl6.TabIndex = 11;
            this.labelControl6.Text = "Status:";
            // 
            // StatusLabel
            // 
            this.StatusLabel.Location = new System.Drawing.Point(64, 196);
            this.StatusLabel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(47, 14);
            this.StatusLabel.TabIndex = 12;
            this.StatusLabel.Text = "<None>";
            // 
            // labelControl8
            // 
            this.labelControl8.Location = new System.Drawing.Point(14, 216);
            this.labelControl8.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl8.Name = "labelControl8";
            this.labelControl8.Size = new System.Drawing.Size(41, 14);
            this.labelControl8.TabIndex = 13;
            this.labelControl8.Text = "Upload:";
            // 
            // UploadSizeLabel
            // 
            this.UploadSizeLabel.Location = new System.Drawing.Point(64, 216);
            this.UploadSizeLabel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.UploadSizeLabel.Name = "UploadSizeLabel";
            this.UploadSizeLabel.Size = new System.Drawing.Size(47, 14);
            this.UploadSizeLabel.TabIndex = 14;
            this.UploadSizeLabel.Text = "<None>";
            // 
            // TotalProgressBar
            // 
            this.TotalProgressBar.Location = new System.Drawing.Point(14, 237);
            this.TotalProgressBar.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.TotalProgressBar.Name = "TotalProgressBar";
            this.TotalProgressBar.Size = new System.Drawing.Size(350, 19);
            this.TotalProgressBar.TabIndex = 15;
            // 
            // UploadSpeedLabel
            // 
            this.UploadSpeedLabel.Location = new System.Drawing.Point(306, 216);
            this.UploadSpeedLabel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.UploadSpeedLabel.Name = "UploadSpeedLabel";
            this.UploadSpeedLabel.Size = new System.Drawing.Size(47, 14);
            this.UploadSpeedLabel.TabIndex = 17;
            this.UploadSpeedLabel.Text = "<None>";
            // 
            // labelControl10
            // 
            this.labelControl10.Location = new System.Drawing.Point(259, 216);
            this.labelControl10.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.labelControl10.Name = "labelControl10";
            this.labelControl10.Size = new System.Drawing.Size(39, 14);
            this.labelControl10.TabIndex = 16;
            this.labelControl10.Text = "Speed:";
            // 
            // PMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(379, 269);
            this.Controls.Add(this.UploadSpeedLabel);
            this.Controls.Add(this.labelControl10);
            this.Controls.Add(this.TotalProgressBar);
            this.Controls.Add(this.UploadSizeLabel);
            this.Controls.Add(this.labelControl8);
            this.Controls.Add(this.StatusLabel);
            this.Controls.Add(this.labelControl6);
            this.Controls.Add(this.UploadPatchButton);
            this.Controls.Add(this.PasswordTextEdit);
            this.Controls.Add(this.HostTextEdit);
            this.Controls.Add(this.labelControl5);
            this.Controls.Add(this.labelControl4);
            this.Controls.Add(this.labelControl3);
            this.Controls.Add(this.UseLoginCheckEdit);
            this.Controls.Add(this.UsernameTextEdit);
            this.Controls.Add(this.labelControl2);
            this.Controls.Add(this.CleanClientButtonEdit);
            this.Controls.Add(this.labelControl1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Name = "PMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Patch Manager";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.PMain_Load);
            ((System.ComponentModel.ISupportInitialize)(this.CleanClientButtonEdit.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.UsernameTextEdit.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.UseLoginCheckEdit.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.HostTextEdit.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PasswordTextEdit.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.TotalProgressBar.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelControl1;
        private DevExpress.XtraEditors.ButtonEdit CleanClientButtonEdit;
        private DevExpress.LookAndFeel.DefaultLookAndFeel DLookAndFeel;
        private DevExpress.XtraEditors.LabelControl labelControl2;
        private DevExpress.XtraEditors.TextEdit UsernameTextEdit;
        private DevExpress.XtraEditors.CheckEdit UseLoginCheckEdit;
        private DevExpress.XtraEditors.LabelControl labelControl3;
        private DevExpress.XtraEditors.LabelControl labelControl4;
        private DevExpress.XtraEditors.LabelControl labelControl5;
        private DevExpress.XtraEditors.TextEdit HostTextEdit;
        private DevExpress.XtraEditors.TextEdit PasswordTextEdit;
        private DevExpress.XtraEditors.SimpleButton UploadPatchButton;
        private DevExpress.XtraEditors.LabelControl labelControl6;
        private DevExpress.XtraEditors.LabelControl StatusLabel;
        private DevExpress.XtraEditors.LabelControl labelControl8;
        private DevExpress.XtraEditors.LabelControl UploadSizeLabel;
        private DevExpress.XtraEditors.ProgressBarControl TotalProgressBar;
        private DevExpress.XtraEditors.LabelControl UploadSpeedLabel;
        private DevExpress.XtraEditors.LabelControl labelControl10;
        private System.Windows.Forms.FolderBrowserDialog FolderDialog;
    }
}

