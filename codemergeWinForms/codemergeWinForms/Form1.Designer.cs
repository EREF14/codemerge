namespace codemergeWinForms
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnTree = new Button();
            tbxToken = new TextBox();
            tbxId = new TextBox();
            lblToken = new Label();
            lblId = new Label();
            lblBranch = new Label();
            trvTree = new TreeView();
            btnMarkdown = new Button();
            listBox1 = new ListBox();
            btnBranch = new Button();
            cbxBranch = new ComboBox();
            button1 = new Button();
            cbxTypeGit = new ComboBox();
            lblTypeGit = new Label();
            SuspendLayout();
            // 
            // btnTree
            // 
            btnTree.Location = new Point(12, 229);
            btnTree.Name = "btnTree";
            btnTree.Size = new Size(193, 23);
            btnTree.TabIndex = 0;
            btnTree.Text = "Générer l'arborescence";
            btnTree.UseVisualStyleBackColor = true;
            btnTree.Click += btnTree_Click;
            // 
            // tbxToken
            // 
            tbxToken.Location = new Point(12, 73);
            tbxToken.Name = "tbxToken";
            tbxToken.Size = new Size(121, 23);
            tbxToken.TabIndex = 1;
            tbxToken.Text = "glpat-EXEMPLE-TOKEN";
            tbxToken.UseSystemPasswordChar = true;
            // 
            // tbxId
            // 
            tbxId.Location = new Point(12, 117);
            tbxId.Name = "tbxId";
            tbxId.Size = new Size(121, 23);
            tbxId.TabIndex = 2;
            tbxId.Text = "6988";
            // 
            // lblToken
            // 
            lblToken.AutoSize = true;
            lblToken.Location = new Point(12, 55);
            lblToken.Name = "lblToken";
            lblToken.Size = new Size(45, 15);
            lblToken.TabIndex = 4;
            lblToken.Text = "Token :";
            // 
            // lblId
            // 
            lblId.AutoSize = true;
            lblId.Location = new Point(12, 99);
            lblId.Name = "lblId";
            lblId.Size = new Size(74, 15);
            lblId.TabIndex = 5;
            lblId.Text = "Id du projet :";
            // 
            // lblBranch
            // 
            lblBranch.AutoSize = true;
            lblBranch.Location = new Point(12, 143);
            lblBranch.Name = "lblBranch";
            lblBranch.Size = new Size(107, 15);
            lblBranch.TabIndex = 6;
            lblBranch.Text = "Branche du projet :";
            // 
            // trvTree
            // 
            trvTree.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            trvTree.CheckBoxes = true;
            trvTree.Location = new Point(380, 12);
            trvTree.Name = "trvTree";
            trvTree.Size = new Size(408, 432);
            trvTree.TabIndex = 10;
            // 
            // btnMarkdown
            // 
            btnMarkdown.Enabled = false;
            btnMarkdown.Location = new Point(12, 258);
            btnMarkdown.Name = "btnMarkdown";
            btnMarkdown.Size = new Size(193, 23);
            btnMarkdown.TabIndex = 11;
            btnMarkdown.Text = "Générer le MarkDown";
            btnMarkdown.UseVisualStyleBackColor = true;
            btnMarkdown.Click += btnMarkdown_Click;
            // 
            // listBox1
            // 
            listBox1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            listBox1.FormattingEnabled = true;
            listBox1.ItemHeight = 15;
            listBox1.Location = new Point(12, 290);
            listBox1.Name = "listBox1";
            listBox1.Size = new Size(362, 154);
            listBox1.TabIndex = 12;
            // 
            // btnBranch
            // 
            btnBranch.Location = new Point(12, 200);
            btnBranch.Name = "btnBranch";
            btnBranch.Size = new Size(193, 23);
            btnBranch.TabIndex = 13;
            btnBranch.Text = "Chercher les branches";
            btnBranch.UseVisualStyleBackColor = true;
            btnBranch.Click += btnBranch_Click;
            // 
            // cbxBranch
            // 
            cbxBranch.FormattingEnabled = true;
            cbxBranch.Location = new Point(12, 161);
            cbxBranch.Name = "cbxBranch";
            cbxBranch.Size = new Size(121, 23);
            cbxBranch.TabIndex = 14;
            cbxBranch.Text = "Regis";
            // 
            // button1
            // 
            button1.Location = new Point(139, 73);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 15;
            button1.Text = "Révéler";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // cbxTypeGit
            // 
            cbxTypeGit.FormattingEnabled = true;
            cbxTypeGit.Location = new Point(12, 27);
            cbxTypeGit.Name = "cbxTypeGit";
            cbxTypeGit.Size = new Size(121, 23);
            cbxTypeGit.TabIndex = 16;
            // 
            // lblTypeGit
            // 
            lblTypeGit.AutoSize = true;
            lblTypeGit.Location = new Point(12, 9);
            lblTypeGit.Name = "lblTypeGit";
            lblTypeGit.Size = new Size(65, 15);
            lblTypeGit.TabIndex = 17;
            lblTypeGit.Text = "Type de git";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(lblTypeGit);
            Controls.Add(cbxTypeGit);
            Controls.Add(button1);
            Controls.Add(cbxBranch);
            Controls.Add(btnBranch);
            Controls.Add(listBox1);
            Controls.Add(btnMarkdown);
            Controls.Add(trvTree);
            Controls.Add(lblBranch);
            Controls.Add(lblId);
            Controls.Add(lblToken);
            Controls.Add(tbxId);
            Controls.Add(tbxToken);
            Controls.Add(btnTree);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnTree;
        private TextBox tbxToken;
        private TextBox tbxId;
        private Label lblToken;
        private Label lblId;
        private Label lblBranch;
        private TreeView trvTree;
        private Button btnMarkdown;
        private ListBox listBox1;
        private Button btnBranch;
        private ComboBox cbxBranch;
        private Button button1;
        private ComboBox cbxTypeGit;
        private Label lblTypeGit;
    }
}
