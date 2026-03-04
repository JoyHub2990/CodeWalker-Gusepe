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
            // ── Tab control ───────────────────────────────────────────────────
            this.MainTabControl         = new System.Windows.Forms.TabControl();

            // ── Entities tab ──────────────────────────────────────────────────
            this.EntitiesTabPage        = new System.Windows.Forms.TabPage();
            this.EntityListView         = new System.Windows.Forms.ListView();
            this.ColName                = new System.Windows.Forms.ColumnHeader();
            this.ColPosition            = new System.Windows.Forms.ColumnHeader();
            this.ColRotation            = new System.Windows.Forms.ColumnHeader();
            this.EntityButtonsPanel     = new System.Windows.Forms.Panel();
            this.EntityCountLabel       = new System.Windows.Forms.Label();
            this.AddToListButton        = new System.Windows.Forms.Button();
            this.RemoveButton           = new System.Windows.Forms.Button();
            this.ClearListButton        = new System.Windows.Forms.Button();
            this.HideButton             = new System.Windows.Forms.Button();
            this.SendListButton         = new System.Windows.Forms.Button();
            this.SendSelectionButton    = new System.Windows.Forms.Button();
            this.ImportAsAssetCheckBox  = new System.Windows.Forms.CheckBox();
            this.WithPositionsCheckBox  = new System.Windows.Forms.CheckBox();

            // ── YMAPs tab ─────────────────────────────────────────────────────
            this.YmapsTabPage           = new System.Windows.Forms.TabPage();
            this.YmapListView           = new System.Windows.Forms.ListView();
            this.ColYmapFilename        = new System.Windows.Forms.ColumnHeader();
            this.ColYmapPath            = new System.Windows.Forms.ColumnHeader();
            this.YmapButtonsPanel       = new System.Windows.Forms.Panel();
            this.YmapCountLabel         = new System.Windows.Forms.Label();
            this.YmapAddButton          = new System.Windows.Forms.Button();
            this.YmapRemoveButton       = new System.Windows.Forms.Button();
            this.YmapSendButton         = new System.Windows.Forms.Button();
            this.YmapClearButton        = new System.Windows.Forms.Button();

            // ── YBN tab ───────────────────────────────────────────────────────
            this.YbnTabPage             = new System.Windows.Forms.TabPage();
            this.YbnListView            = new System.Windows.Forms.ListView();
            this.ColYbnName             = new System.Windows.Forms.ColumnHeader();
            this.YbnButtonsPanel        = new System.Windows.Forms.Panel();
            this.YbnCountLabel          = new System.Windows.Forms.Label();
            this.YbnAddToListButton     = new System.Windows.Forms.Button();
            this.YbnRemoveButton        = new System.Windows.Forms.Button();
            this.YbnClearListButton     = new System.Windows.Forms.Button();
            this.YbnSendListButton      = new System.Windows.Forms.Button();
            this.YbnSendSelectionButton = new System.Windows.Forms.Button();

            // ── Bottom strip ──────────────────────────────────────────────────
            this.BottomStrip            = new System.Windows.Forms.Panel();
            this.PortLabel              = new System.Windows.Forms.Label();
            this.PortNumericUpDown      = new System.Windows.Forms.NumericUpDown();
            this.StatusLabel            = new System.Windows.Forms.Label();

            this.MainTabControl.SuspendLayout();
            this.EntitiesTabPage.SuspendLayout();
            this.EntityButtonsPanel.SuspendLayout();
            this.YmapsTabPage.SuspendLayout();
            this.YmapButtonsPanel.SuspendLayout();
            this.YbnTabPage.SuspendLayout();
            this.YbnButtonsPanel.SuspendLayout();
            this.BottomStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PortNumericUpDown)).BeginInit();
            this.SuspendLayout();

            // ── BottomStrip ──────────────────────────────────────────────────
            this.BottomStrip.Controls.Add(this.PortLabel);
            this.BottomStrip.Controls.Add(this.PortNumericUpDown);
            this.BottomStrip.Controls.Add(this.StatusLabel);
            this.BottomStrip.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomStrip.Height = 28;
            this.BottomStrip.Name = "BottomStrip";

            this.PortLabel.AutoSize = true;
            this.PortLabel.Location = new System.Drawing.Point(4, 7);
            this.PortLabel.Name = "PortLabel";
            this.PortLabel.Text = "Port:";

            this.PortNumericUpDown.Location = new System.Drawing.Point(36, 4);
            this.PortNumericUpDown.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
            this.PortNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.PortNumericUpDown.Value   = new decimal(new int[] { 8765, 0, 0, 0 });
            this.PortNumericUpDown.Size = new System.Drawing.Size(68, 20);
            this.PortNumericUpDown.Name = "PortNumericUpDown";

            this.StatusLabel.AutoSize = false;
            this.StatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right;
            this.StatusLabel.Location = new System.Drawing.Point(112, 7);
            this.StatusLabel.Size = new System.Drawing.Size(380, 15);
            this.StatusLabel.Name = "StatusLabel";
            this.StatusLabel.Text = "Ready.";

            // ── MainTabControl ───────────────────────────────────────────────
            this.MainTabControl.Controls.Add(this.EntitiesTabPage);
            this.MainTabControl.Controls.Add(this.YmapsTabPage);
            this.MainTabControl.Controls.Add(this.YbnTabPage);
            this.MainTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainTabControl.Name = "MainTabControl";
            this.MainTabControl.TabIndex = 0;

            // ─────────────────────────────────────────────────────────────────
            // ENTITIES TAB
            // ─────────────────────────────────────────────────────────────────
            this.EntitiesTabPage.Controls.Add(this.EntityListView);
            this.EntitiesTabPage.Controls.Add(this.EntityButtonsPanel);
            this.EntitiesTabPage.Text = "Entities";
            this.EntitiesTabPage.Name = "EntitiesTabPage";
            this.EntitiesTabPage.UseVisualStyleBackColor = true;

            // EntityButtonsPanel
            // Row 0 (y=2):   count label
            // Row 1 (y=18):  [Add to List] [Remove] [Clear List]
            // Row 2 (y=46):  [Send List  ── full width ──]
            // Row 3 (y=74):  [Send Selection ── full width ──]
            // Row 4 (y=104): ☑ Import as asset   ☑ With positions
            this.EntityButtonsPanel.Controls.Add(this.EntityCountLabel);
            this.EntityButtonsPanel.Controls.Add(this.AddToListButton);
            this.EntityButtonsPanel.Controls.Add(this.RemoveButton);
            this.EntityButtonsPanel.Controls.Add(this.ClearListButton);
            this.EntityButtonsPanel.Controls.Add(this.HideButton);
            this.EntityButtonsPanel.Controls.Add(this.SendListButton);
            this.EntityButtonsPanel.Controls.Add(this.SendSelectionButton);
            this.EntityButtonsPanel.Controls.Add(this.ImportAsAssetCheckBox);
            this.EntityButtonsPanel.Controls.Add(this.WithPositionsCheckBox);
            this.EntityButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.EntityButtonsPanel.Height = 128;
            this.EntityButtonsPanel.Name = "EntityButtonsPanel";

            this.EntityCountLabel.AutoSize = true;
            this.EntityCountLabel.Location = new System.Drawing.Point(4, 2);
            this.EntityCountLabel.Name = "EntityCountLabel";
            this.EntityCountLabel.Text = "0 entity/ies";

            this.AddToListButton.Location = new System.Drawing.Point(4, 18);
            this.AddToListButton.Size = new System.Drawing.Size(82, 24);
            this.AddToListButton.Name = "AddToListButton";
            this.AddToListButton.Text = "Add to List";
            this.AddToListButton.UseVisualStyleBackColor = true;
            this.AddToListButton.Click += new System.EventHandler(this.AddToListButton_Click);

            this.RemoveButton.Location = new System.Drawing.Point(90, 18);
            this.RemoveButton.Size = new System.Drawing.Size(66, 24);
            this.RemoveButton.Name = "RemoveButton";
            this.RemoveButton.Text = "Remove";
            this.RemoveButton.UseVisualStyleBackColor = true;
            this.RemoveButton.Click += new System.EventHandler(this.RemoveButton_Click);

            this.ClearListButton.Location = new System.Drawing.Point(160, 18);
            this.ClearListButton.Size = new System.Drawing.Size(70, 24);
            this.ClearListButton.Name = "ClearListButton";
            this.ClearListButton.Text = "Clear List";
            this.ClearListButton.UseVisualStyleBackColor = true;
            this.ClearListButton.Click += new System.EventHandler(this.ClearListButton_Click);

            this.HideButton.Location = new System.Drawing.Point(234, 18);
            this.HideButton.Size = new System.Drawing.Size(62, 24);
            this.HideButton.Name = "HideButton";
            this.HideButton.Text = "o Hide";
            this.HideButton.UseVisualStyleBackColor = true;
            this.HideButton.Click += new System.EventHandler(this.HideButton_Click);

            this.SendListButton.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.SendListButton.Location = new System.Drawing.Point(4, 46);
            this.SendListButton.Size = new System.Drawing.Size(490, 24);
            this.SendListButton.Name = "SendListButton";
            this.SendListButton.Text = "Send List";
            this.SendListButton.UseVisualStyleBackColor = true;
            this.SendListButton.Click += new System.EventHandler(this.SendListButton_Click);

            this.SendSelectionButton.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.SendSelectionButton.Location = new System.Drawing.Point(4, 74);
            this.SendSelectionButton.Size = new System.Drawing.Size(490, 24);
            this.SendSelectionButton.Name = "SendSelectionButton";
            this.SendSelectionButton.Text = "Send Selection";
            this.SendSelectionButton.UseVisualStyleBackColor = true;
            this.SendSelectionButton.Click += new System.EventHandler(this.SendSelectionButton_Click);

            this.ImportAsAssetCheckBox.AutoSize = true;
            this.ImportAsAssetCheckBox.Checked = true;
            this.ImportAsAssetCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ImportAsAssetCheckBox.Location = new System.Drawing.Point(4, 104);
            this.ImportAsAssetCheckBox.Name = "ImportAsAssetCheckBox";
            this.ImportAsAssetCheckBox.Text = "Import as asset";
            this.ImportAsAssetCheckBox.UseVisualStyleBackColor = true;

            this.WithPositionsCheckBox.AutoSize = true;
            this.WithPositionsCheckBox.Location = new System.Drawing.Point(136, 104);
            this.WithPositionsCheckBox.Name = "WithPositionsCheckBox";
            // EntityListView
            this.WithPositionsCheckBox.Text = "With positions";
            this.WithPositionsCheckBox.UseVisualStyleBackColor = true;

            this.EntityListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.ColName, this.ColPosition, this.ColRotation });
            this.EntityListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EntityListView.FullRowSelect = true;
            this.EntityListView.GridLines = true;
            this.EntityListView.View = System.Windows.Forms.View.Details;
            this.EntityListView.Name = "EntityListView";
            this.EntityListView.UseCompatibleStateImageBehavior = false;

            this.ColName.Text = "Asset Name";
            this.ColName.Width = 160;
            this.ColPosition.Text = "Position";
            this.ColPosition.Width = 120;
            this.ColRotation.Text = "Rotation (X,Y,Z,W)";
            this.ColRotation.Width = 140;

            // ─────────────────────────────────────────────────────────────────
            // YMAPs TAB
            // ─────────────────────────────────────────────────────────────────
            this.YmapsTabPage.Controls.Add(this.YmapListView);
            this.YmapsTabPage.Controls.Add(this.YmapButtonsPanel);
            this.YmapsTabPage.Text = "YMAPs";
            this.YmapsTabPage.Name = "YmapsTabPage";
            this.YmapsTabPage.UseVisualStyleBackColor = true;

            this.YmapButtonsPanel.Controls.Add(this.YmapCountLabel);
            this.YmapButtonsPanel.Controls.Add(this.YmapAddButton);
            this.YmapButtonsPanel.Controls.Add(this.YmapRemoveButton);
            this.YmapButtonsPanel.Controls.Add(this.YmapSendButton);
            this.YmapButtonsPanel.Controls.Add(this.YmapClearButton);
            this.YmapButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.YmapButtonsPanel.Height = 52;
            this.YmapButtonsPanel.Name = "YmapButtonsPanel";

            this.YmapCountLabel.AutoSize = true;
            this.YmapCountLabel.Location = new System.Drawing.Point(4, 2);
            this.YmapCountLabel.Name = "YmapCountLabel";
            this.YmapCountLabel.Text = "0 YMAP(s)";

            this.YmapAddButton.Location = new System.Drawing.Point(4, 18);
            this.YmapAddButton.Size = new System.Drawing.Size(118, 24);
            this.YmapAddButton.Name = "YmapAddButton";
            this.YmapAddButton.Text = "Add from Explorer";
            this.YmapAddButton.UseVisualStyleBackColor = true;
            this.YmapAddButton.Click += new System.EventHandler(this.YmapAddButton_Click);

            this.YmapRemoveButton.Location = new System.Drawing.Point(126, 18);
            this.YmapRemoveButton.Size = new System.Drawing.Size(66, 24);
            this.YmapRemoveButton.Name = "YmapRemoveButton";
            this.YmapRemoveButton.Text = "Remove";
            this.YmapRemoveButton.UseVisualStyleBackColor = true;
            this.YmapRemoveButton.Click += new System.EventHandler(this.YmapRemoveButton_Click);

            this.YmapSendButton.Location = new System.Drawing.Point(196, 18);
            this.YmapSendButton.Size = new System.Drawing.Size(100, 24);
            this.YmapSendButton.Name = "YmapSendButton";
            this.YmapSendButton.Text = "Send to Blender";
            this.YmapSendButton.UseVisualStyleBackColor = true;
            this.YmapSendButton.Click += new System.EventHandler(this.YmapSendButton_Click);

            this.YmapClearButton.Location = new System.Drawing.Point(300, 18);
            this.YmapClearButton.Size = new System.Drawing.Size(56, 24);
            this.YmapClearButton.Name = "YmapClearButton";
            this.YmapClearButton.Text = "Clear";
            this.YmapClearButton.UseVisualStyleBackColor = true;
            this.YmapClearButton.Click += new System.EventHandler(this.YmapClearButton_Click);

            this.YmapListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.ColYmapFilename, this.ColYmapPath });
            this.YmapListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.YmapListView.FullRowSelect = true;
            this.YmapListView.GridLines = true;
            this.YmapListView.View = System.Windows.Forms.View.Details;
            this.YmapListView.Name = "YmapListView";
            this.YmapListView.UseCompatibleStateImageBehavior = false;

            this.ColYmapFilename.Text = "YMAP Filename";
            this.ColYmapFilename.Width = 150;
            this.ColYmapPath.Text = "Full Path";
            this.ColYmapPath.Width = 300;

            // ─────────────────────────────────────────────────────────────────
            // YBN TAB
            // Same layout as Entities but simpler (no position/asset options —
            // collisions are imported as-is, positions come from the .ybn data).
            // ─────────────────────────────────────────────────────────────────
            this.YbnTabPage.Controls.Add(this.YbnListView);
            this.YbnTabPage.Controls.Add(this.YbnButtonsPanel);
            this.YbnTabPage.Text = "YBN";
            this.YbnTabPage.Name = "YbnTabPage";
            this.YbnTabPage.UseVisualStyleBackColor = true;

            // YbnButtonsPanel
            // Row 0 (y=2):  count label
            // Row 1 (y=18): [Add to List] [Remove] [Clear List]
            // Row 2 (y=46): [Send List  ── full width ──]
            // Row 3 (y=74): [Send Selection ── full width ──]
            this.YbnButtonsPanel.Controls.Add(this.YbnCountLabel);
            this.YbnButtonsPanel.Controls.Add(this.YbnAddToListButton);
            this.YbnButtonsPanel.Controls.Add(this.YbnRemoveButton);
            this.YbnButtonsPanel.Controls.Add(this.YbnClearListButton);
            this.YbnButtonsPanel.Controls.Add(this.YbnSendListButton);
            this.YbnButtonsPanel.Controls.Add(this.YbnSendSelectionButton);
            this.YbnButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.YbnButtonsPanel.Height = 104;
            this.YbnButtonsPanel.Name = "YbnButtonsPanel";

            this.YbnCountLabel.AutoSize = true;
            this.YbnCountLabel.Location = new System.Drawing.Point(4, 2);
            this.YbnCountLabel.Name = "YbnCountLabel";
            this.YbnCountLabel.Text = "0 YBN(s)";

            this.YbnAddToListButton.Location = new System.Drawing.Point(4, 18);
            this.YbnAddToListButton.Size = new System.Drawing.Size(82, 24);
            this.YbnAddToListButton.Name = "YbnAddToListButton";
            this.YbnAddToListButton.Text = "Add to List";
            this.YbnAddToListButton.UseVisualStyleBackColor = true;
            this.YbnAddToListButton.Click += new System.EventHandler(this.YbnAddToListButton_Click);

            this.YbnRemoveButton.Location = new System.Drawing.Point(90, 18);
            this.YbnRemoveButton.Size = new System.Drawing.Size(66, 24);
            this.YbnRemoveButton.Name = "YbnRemoveButton";
            this.YbnRemoveButton.Text = "Remove";
            this.YbnRemoveButton.UseVisualStyleBackColor = true;
            this.YbnRemoveButton.Click += new System.EventHandler(this.YbnRemoveButton_Click);

            this.YbnClearListButton.Location = new System.Drawing.Point(160, 18);
            this.YbnClearListButton.Size = new System.Drawing.Size(70, 24);
            this.YbnClearListButton.Name = "YbnClearListButton";
            this.YbnClearListButton.Text = "Clear List";
            this.YbnClearListButton.UseVisualStyleBackColor = true;
            this.YbnClearListButton.Click += new System.EventHandler(this.YbnClearListButton_Click);

            this.YbnSendListButton.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.YbnSendListButton.Location = new System.Drawing.Point(4, 46);
            this.YbnSendListButton.Size = new System.Drawing.Size(490, 24);
            this.YbnSendListButton.Name = "YbnSendListButton";
            this.YbnSendListButton.Text = "Send List";
            this.YbnSendListButton.UseVisualStyleBackColor = true;
            this.YbnSendListButton.Click += new System.EventHandler(this.YbnSendListButton_Click);

            this.YbnSendSelectionButton.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.YbnSendSelectionButton.Location = new System.Drawing.Point(4, 74);
            this.YbnSendSelectionButton.Size = new System.Drawing.Size(490, 24);
            this.YbnSendSelectionButton.Name = "YbnSendSelectionButton";
            this.YbnSendSelectionButton.Text = "Send Selection";
            this.YbnSendSelectionButton.UseVisualStyleBackColor = true;
            this.YbnSendSelectionButton.Click += new System.EventHandler(this.YbnSendSelectionButton_Click);

            this.YbnListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.ColYbnName });
            this.YbnListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.YbnListView.FullRowSelect = true;
            this.YbnListView.GridLines = true;
            this.YbnListView.View = System.Windows.Forms.View.Details;
            this.YbnListView.Name = "YbnListView";
            this.YbnListView.UseCompatibleStateImageBehavior = false;

            this.ColYbnName.Text = "YBN Name";
            this.ColYbnName.Width = 260;

            // ── RageQuitBridgePanel root ──────────────────────────────────────
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(500, 400);
            this.Controls.Add(this.MainTabControl);
            this.Controls.Add(this.BottomStrip);
            this.HideOnClose = true;
            this.Name = "RageQuitBridgePanel";
            this.Text = "RageQuit Bridge";

            this.MainTabControl.ResumeLayout(false);
            this.EntitiesTabPage.ResumeLayout(false);
            this.EntityButtonsPanel.ResumeLayout(false);
            this.EntityButtonsPanel.PerformLayout();
            this.YmapsTabPage.ResumeLayout(false);
            this.YmapButtonsPanel.ResumeLayout(false);
            this.YmapButtonsPanel.PerformLayout();
            this.YbnTabPage.ResumeLayout(false);
            this.YbnButtonsPanel.ResumeLayout(false);
            this.YbnButtonsPanel.PerformLayout();
            this.BottomStrip.ResumeLayout(false);
            this.BottomStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PortNumericUpDown)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        // Entities
        private System.Windows.Forms.TabControl  MainTabControl;
        private System.Windows.Forms.TabPage     EntitiesTabPage;
        private System.Windows.Forms.ListView    EntityListView;
        private System.Windows.Forms.ColumnHeader ColName;
        private System.Windows.Forms.ColumnHeader ColPosition;
        private System.Windows.Forms.ColumnHeader ColRotation;
        private System.Windows.Forms.Panel       EntityButtonsPanel;
        private System.Windows.Forms.Label       EntityCountLabel;
        private System.Windows.Forms.Button      AddToListButton;
        private System.Windows.Forms.Button      RemoveButton;
        private System.Windows.Forms.Button      ClearListButton;
        private System.Windows.Forms.Button      HideButton;
        private System.Windows.Forms.Button      SendListButton;
        private System.Windows.Forms.Button      SendSelectionButton;
        private System.Windows.Forms.CheckBox    ImportAsAssetCheckBox;
        private System.Windows.Forms.CheckBox    WithPositionsCheckBox;

        // YMAPs
        private System.Windows.Forms.TabPage     YmapsTabPage;
        private System.Windows.Forms.ListView    YmapListView;
        private System.Windows.Forms.ColumnHeader ColYmapFilename;
        private System.Windows.Forms.ColumnHeader ColYmapPath;
        private System.Windows.Forms.Panel       YmapButtonsPanel;
        private System.Windows.Forms.Label       YmapCountLabel;
        private System.Windows.Forms.Button      YmapAddButton;
        private System.Windows.Forms.Button      YmapRemoveButton;
        private System.Windows.Forms.Button      YmapSendButton;
        private System.Windows.Forms.Button      YmapClearButton;

        // YBN
        private System.Windows.Forms.TabPage     YbnTabPage;
        private System.Windows.Forms.ListView    YbnListView;
        private System.Windows.Forms.ColumnHeader ColYbnName;
        private System.Windows.Forms.Panel       YbnButtonsPanel;
        private System.Windows.Forms.Label       YbnCountLabel;
        private System.Windows.Forms.Button      YbnAddToListButton;
        private System.Windows.Forms.Button      YbnRemoveButton;
        private System.Windows.Forms.Button      YbnClearListButton;
        private System.Windows.Forms.Button      YbnSendListButton;
        private System.Windows.Forms.Button      YbnSendSelectionButton;

        // Shared bottom strip
        private System.Windows.Forms.Panel       BottomStrip;
        private System.Windows.Forms.Label       PortLabel;
        private System.Windows.Forms.NumericUpDown PortNumericUpDown;
        private System.Windows.Forms.Label       StatusLabel;
    }
}
