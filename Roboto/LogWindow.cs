﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roboto
{
    public partial class LogWindow : Form
    {
        //private List<logging.logItem> logitems = new List<logging.logItem>();
        private bool backgroundCompleted = false;
        
        public LogWindow()
        {
            this.Text = Roboto.log.windowTitle;

            InitializeComponent();
            

        }


        public void appendText(string s)
        {
            if (InvokeRequired)
            {
                //call ourselves back via the GUI thread. 
                this.Invoke(new Action(() => { appendText(s); }));
            }
            else
            {
                //if called from the GUI thread.
                try
                {
                    LogText.SelectionStart = LogText.TextLength;
                    LogText.SelectionLength = 0;
                    LogText.AppendText(s);
                    LogText.SelectionColor = Color.White;

                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
            } 


        }



        public void addLogItem(logging.logItem li)
        {
            if (InvokeRequired)
            {
                //call ourselves back via the GUI thread. 
                this.Invoke(new Action(() => { addLogItem(li); }));
            }
            else
            {
                //if called from the GUI thread.
                try
                {
                    if (Roboto.Settings != null)
                    {
                        //LogText.SuspendLayout();
                        LogText.SuspendLayout();
                        Roboto.Settings.maxLogItems = 44;
                        int linesToRemove = LogText.Lines.Count() - Roboto.Settings.maxLogItems;
                        if (linesToRemove > 0)
                        {
                            int start_index = LogText.GetFirstCharIndexFromLine(linesToRemove);
                            LogText.Select(0, start_index);
                            LogText.SelectedText = "";
                        }
                        appendLog(li);
                        LogText.ResumeLayout();
                        //while ( && LogText.Lines.Count() > Roboto.Settings.maxLogItems)
                        //    {
                        //        LogText.Text = LogText. .Lines[0].Remove();
                        //    }

                    }
                    
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
            }
        }

        private void appendLog(logging.logItem li)
        {
            //build the string
            //string s = li.level.ToString() + "\t" + li.methodName + "\t" + li.logText;
            Color c = li.getColor();
            string text = li.ToString() + Environment.NewLine;


            int startpos = LogText.TextLength;
            LogText.AppendText(text);
            LogText.Select(startpos, text.Length);
            LogText.SelectionColor = c;
            LogText.Select(LogText.TextLength,0);


        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (backgroundCompleted)
            {
                return;
            }

            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                //Immediate save. This will then close the form automatically.
                Roboto.Settings.save();
            }

            // UI button - override close with a clean shutdown
            e.Cancel = true;
            switch (MessageBox.Show(this, "Are you sure you want to close?", "Closing", MessageBoxButtons.YesNo))
            {
                case DialogResult.No:
                    break;
                default:
                    //Stop the background thread cleanly. Will signal back when OK to exit properly.  
                    Roboto.shudownMainThread();
                    break;
            }
        }

        public void unlockExit()
        {
            backgroundCompleted = true;
        }
    }
}
