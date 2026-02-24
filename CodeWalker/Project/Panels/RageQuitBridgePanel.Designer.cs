namespace CodeWalker.Project.Panels
{
    partial class RageQuitBridgePanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.MainPanel = new System.Windows.Forms.Panel();
            this.EntitiesListView = new System.Windows.Forms.ListView();
            this.NameColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.PositionColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.ControlsPanel = new System.Windows.Forms.Panel();
            this.StatusLabel = new System.Windows.Forms.Label();
            this.BottomPanel = new System.Windows.Forms.Panel();
            this.WithPositionsCheckBox = new System.Windows.Forms.CheckBox();
            this.ImportAsMeshCheckBox = new System.Windows.Forms.CheckBox();
            this.SendButton = new System.Windows.Forms.Button();
            this.RefreshButton = new System.Windows.Forms.Button();
            this.TopPanel = new System.Windows.Forms.Panel();
            this.PortLabel = new System.Windows.Forms.Label();
            this.PortNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.TitleLabel = new System.Windows.Forms.Label();
            this.MainPanel.SuspendLayout();
            this.ControlsPanel.SuspendLayout();
            this.BottomPanel.SuspendLayout();
            this.TopPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PortNumericUpDown)).BeginInit();
            this.SuspendLayout();
            // 
            // TitleLabel
            // 
            this.TitleLabel = new System.Windows.Forms.Label();
            this.TitleLabel.AutoSize = true;
            this.TitleLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.TitleLabel.Location = new System.Drawing.Point(4, 6);
            this.TitleLabel.Name = "TitleLabel";
            this.TitleLabel.Size = new System.Drawing.Size(120, 15);
            this.TitleLabel.TabIndex = 0;
            this.TitleLabel.Text = "RageQuit Bridge";
            // 
            // TopPanel
            // 
            this.TopPanel.Controls.Add(this.TitleLabel);
            this.TopPanel.Controls.Add(this.PortLabel);
            this.TopPanel.Controls.Add(this.PortNumericUpDown);
            this.TopPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPanel.Location = new System.Drawing.Point(0, 0);
            this.TopPanel.Name = "TopPanel";
            this.TopPanel.Size = new System.Drawing.Size(300, 56);
            this.TopPanel.TabIndex = 0;
            // 
            // PortLabel
            // 
            this.PortLabel.AutoSize = true;
            this.PortLabel.Location = new System.Drawing.Point(4, 32);
            this.PortLabel.Name = "PortLabel";
            this.PortLabel.Size = new System.Drawing.Size(26, 13);
            this.PortLabel.TabIndex = 1;
            this.PortLabel.Text = "Port";
            // 
            // PortNumericUpDown
            // 
            this.PortNumericUpDown.Location = new System.Drawing.Point(50, 29);
            this.PortNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.PortNumericUpDown.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
            this.PortNumericUpDown.Name = "PortNumericUpDown";
            this.PortNumericUpDown.Size = new System.Drawing.Size(75, 20);
            this.PortNumericUpDown.TabIndex = 2;
            this.PortNumericUpDown.Value = new decimal(new int[] { 8765, 0, 0, 0 });
            // 
            // ControlsPanel (between top and list)
            // 
            this.ControlsPanel.Controls.Add(this.RefreshButton);
            this.ControlsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ControlsPanel.Location = new System.Drawing.Point(0, 56);
            this.ControlsPanel.Name = "ControlsPanel";
            this.ControlsPanel.Size = new System.Drawing.Size(300, 34);
            this.ControlsPanel.TabIndex = 1;
            // 
            // RefreshButton
            // 
            this.RefreshButton.Location = new System.Drawing.Point(4, 4);
            this.RefreshButton.Name = "RefreshButton";
            this.RefreshButton.Size = new System.Drawing.Size(140, 26);
            this.RefreshButton.TabIndex = 0;
            this.RefreshButton.Text = "Refresh from Selection";
            this.RefreshButton.UseVisualStyleBackColor = true;
            this.RefreshButton.Click += new System.EventHandler(this.RefreshButton_Click);
            // 
            // EntitiesListView
            // 
            this.EntitiesListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NameColumn,
            this.PositionColumn});
            this.EntitiesListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EntitiesListView.FullRowSelect = true;
            this.EntitiesListView.GridLines = true;
            this.EntitiesListView.Location = new System.Drawing.Point(0, 90);
            this.EntitiesListView.Name = "EntitiesListView";
            this.EntitiesListView.Size = new System.Drawing.Size(300, 260);
            this.EntitiesListView.TabIndex = 2;
            this.EntitiesListView.UseCompatibleStateImageBehavior = false;
            this.EntitiesListView.View = System.Windows.Forms.View.Details;
            // 
            // NameColumn
            // 
            this.NameColumn.Text = "Asset Name";
            this.NameColumn.Width = 160;
            // 
            // PositionColumn
            // 
            this.PositionColumn.Text = "Position";
            this.PositionColumn.Width = 130;
            // 
            // BottomPanel
            // 
            this.BottomPanel.Controls.Add(this.WithPositionsCheckBox);
            this.BottomPanel.Controls.Add(this.ImportAsMeshCheckBox);
            this.BottomPanel.Controls.Add(this.SendButton);
            this.BottomPanel.Controls.Add(this.StatusLabel);
            this.BottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPanel.Location = new System.Drawing.Point(0, 350);
            this.BottomPanel.Name = "BottomPanel";
            this.BottomPanel.Size = new System.Drawing.Size(300, 90);
            this.BottomPanel.TabIndex = 3;
            // 
            // ImportAsMeshCheckBox
            // 
            this.ImportAsMeshCheckBox.AutoSize = true;
            this.ImportAsMeshCheckBox.Checked = true;
            this.ImportAsMeshCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ImportAsMeshCheckBox.Location = new System.Drawing.Point(7, 6);
            this.ImportAsMeshCheckBox.Name = "ImportAsMeshCheckBox";
            this.ImportAsMeshCheckBox.Size = new System.Drawing.Size(104, 17);
            this.ImportAsMeshCheckBox.TabIndex = 0;
            this.ImportAsMeshCheckBox.Text = "Import as Mesh";
            this.ImportAsMeshCheckBox.UseVisualStyleBackColor = true;
            this.ImportAsMeshCheckBox.CheckedChanged += new System.EventHandler(this.ImportAsMeshCheckBox_CheckedChanged);
            // 
            // WithPositionsCheckBox
            // 
            this.WithPositionsCheckBox.AutoSize = true;
            this.WithPositionsCheckBox.Location = new System.Drawing.Point(7, 28);
            this.WithPositionsCheckBox.Name = "WithPositionsCheckBox";
            this.WithPositionsCheckBox.Size = new System.Drawing.Size(133, 17);
            this.WithPositionsCheckBox.TabIndex = 1;
            this.WithPositionsCheckBox.Text = "Send world positions";
            this.WithPositionsCheckBox.UseVisualStyleBackColor = true;
            // 
            // SendButton
            // 
            this.SendButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SendButton.Location = new System.Drawing.Point(4, 50);
            this.SendButton.Name = "SendButton";
            this.SendButton.Size = new System.Drawing.Size(292, 30);
            this.SendButton.TabIndex = 2;
            this.SendButton.Text = "Send to Blender";
            this.SendButton.UseVisualStyleBackColor = true;
            this.SendButton.Click += new System.EventHandler(this.SendButton_Click);
            // 
            // StatusLabel
            // 
            this.StatusLabel.AutoSize = false;
            this.StatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusLabel.Location = new System.Drawing.Point(4, 84);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Size = new System.Drawing.Size(292, 13);
            this.StatusLabel.TabIndex = 3;
            this.StatusLabel.Text = "Select entities in the viewport, then click Refresh.";
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.EntitiesListView);
            this.MainPanel.Controls.Add(this.ControlsPanel);
            this.MainPanel.Controls.Add(this.TopPanel);
            this.MainPanel.Controls.Add(this.BottomPanel);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Size = new System.Drawing.Size(300, 440);
            this.MainPanel.TabIndex = 0;
            // 
            // RageQuitBridgePanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(300, 440);
            this.Controls.Add(this.MainPanel);
            this.HideOnClose = true;
            this.Name = "RageQuitBridgePanel";
            this.Text = "RageQuit Bridge";
            this.MainPanel.ResumeLayout(false);
            this.ControlsPanel.ResumeLayout(false);
            this.TopPanel.ResumeLayout(false);
            this.TopPanel.PerformLayout();
            this.BottomPanel.ResumeLayout(false);
            this.BottomPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PortNumericUpDown)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel MainPanel;
        private System.Windows.Forms.Panel TopPanel;
        private System.Windows.Forms.Label TitleLabel;
        private System.Windows.Forms.Label PortLabel;
        private System.Windows.Forms.NumericUpDown PortNumericUpDown;
        private System.Windows.Forms.Panel ControlsPanel;
        private System.Windows.Forms.Button RefreshButton;
        private System.Windows.Forms.ListView EntitiesListView;
        private System.Windows.Forms.ColumnHeader NameColumn;
        private System.Windows.Forms.ColumnHeader PositionColumn;
        private System.Windows.Forms.Panel BottomPanel;
        private System.Windows.Forms.CheckBox ImportAsMeshCheckBox;
        private System.Windows.Forms.CheckBox WithPositionsCheckBox;
        private System.Windows.Forms.Button SendButton;
        private System.Windows.Forms.Label StatusLabel;
    }
}
