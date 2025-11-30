namespace Blue_Screen_Repair_Detector
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            labelTime1 = new AntdUI.LabelTime();
            进度条且拥有6个格子 = new AntdUI.Progress();
            开始检测的按钮 = new AntdUI.Button();
            label1 = new Label();
            table1 = new AntdUI.Table();
            SuspendLayout();
            // 
            // labelTime1
            // 
            labelTime1.Location = new Point(704, 12);
            labelTime1.Name = "labelTime1";
            labelTime1.Size = new Size(75, 23);
            labelTime1.TabIndex = 0;
            labelTime1.Text = "labelTime1";
            // 
            // 进度条且拥有6个格子
            // 
            进度条且拥有6个格子.Location = new Point(618, 327);
            进度条且拥有6个格子.Name = "进度条且拥有6个格子";
            进度条且拥有6个格子.Shape = AntdUI.TShapeProgress.Steps;
            进度条且拥有6个格子.Size = new Size(141, 32);
            进度条且拥有6个格子.State = AntdUI.TType.Info;
            进度条且拥有6个格子.StepGap = 5;
            进度条且拥有6个格子.Steps = 6;
            进度条且拥有6个格子.TabIndex = 1;
            进度条且拥有6个格子.Text = "";
            进度条且拥有6个格子.Click += progress1_Click;
            // 
            // 开始检测的按钮
            // 
            开始检测的按钮.Location = new Point(565, 365);
            开始检测的按钮.Name = "开始检测的按钮";
            开始检测的按钮.Size = new Size(223, 73);
            开始检测的按钮.TabIndex = 2;
            开始检测的按钮.Text = "开始检测";
            开始检测的按钮.Click += 开始检测的按钮_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(618, 307);
            label1.Name = "label1";
            label1.Size = new Size(32, 17);
            label1.TabIndex = 3;
            label1.Text = "状态";
            label1.Click += label1_Click;
            // 
            // table1
            // 
            table1.Gap = 12;
            table1.Location = new Point(12, 12);
            table1.Name = "table1";
            table1.Size = new Size(547, 426);
            table1.TabIndex = 4;
            table1.Text = "table1";
            table1.CellClick += table1_CellClick;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(table1);
            Controls.Add(label1);
            Controls.Add(开始检测的按钮);
            Controls.Add(进度条且拥有6个格子);
            Controls.Add(labelTime1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "异常关机检测器";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private AntdUI.LabelTime labelTime1;
        private AntdUI.Progress 进度条且拥有6个格子;
        private AntdUI.Button 开始检测的按钮;
        private Label label1;
        private AntdUI.Table table1;
    }
}
