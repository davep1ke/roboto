using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;


namespace RobotoChatBot
{
    /// <summary>
    /// Interaction logic for LogWindow.xaml
    /// </summary>
    public partial class LogWindow : Window
    {
        /*
        //So that we can pause updating
        private const int WM_SETREDRAW = 0x000B;
        private const int WM_USER = 0x400;
        private const int EM_GETEVENTMASK = (WM_USER + 59);
        private const int EM_SETEVENTMASK = (WM_USER + 69);
        [DllImport("user32", CharSet = CharSet.Auto)]
        private extern static IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);
        IntPtr eventMask = IntPtr.Zero;
        */

        private bool backgroundCompleted = false;

        public LogWindow()
        {
            InitializeComponent();
            this.Title = Roboto.log.getWindowTitle();
        }

        public void appendText(string s)
        {
            if (!CheckAccess())
            {
                //call ourselves back via the GUI thread. 
                Dispatcher.Invoke(new Action(() => { appendText(s); }));
            }
            else
            {
                Paragraph p = (Paragraph)LogText.Document.Blocks.Last();
                p.Inlines.Add(s);

                /*/if called from the GUI thread.
                try
                {
                    BrushConverter bc = new BrushConverter();
                    TextRange tr = new TextRange(LogText.Document.ContentEnd, LogText.Document.ContentEnd);
                    tr.Text = s;
                    try
                    {
                        tr.ApplyPropertyValue(TextElement.ForegroundProperty, Colors.White);
                    }
                    catch (FormatException) { }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
                */
            }
        }

        /// <summary>
        /// Called whenever a longop is updated (either title or value)
        /// Each row (child grid) should represent a longop.
        /// very messy
        /// </summary>
        /// <param name="longOp"></param>
        public void addOrUpdateLongOp(logging.longOp longOp)
        {
            if (!CheckAccess())
            {
                //call ourselves back via the GUI thread. 
                Dispatcher.Invoke(new Action(() => { addOrUpdateLongOp(longOp); }));
            }
            else
            {
                //does it exist?
                bool found = false;
                
                foreach (Grid g in grpLongOps.Children)
                {
                    if (g.Tag == longOp)
                    {
                        updateBar(g);
                        found = true;
                    }
                }

                if (!found)
                {
                    Grid g = new Grid();
                    g.Width = grpLongOps.Width - 10;
                    g.Height = 25;
                    g.HorizontalAlignment = HorizontalAlignment.Left;
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.RowDefinitions.Add(new RowDefinition());
                    g.Tag = longOp;
                    updateBar(g);
                    if (longOp.Parent != null)
                    {
                        //get the rowid of the parent
                        int parentPos = grpLongOps.Children.Count - 1;
                        int currentPos = 0;
                        foreach (Grid gParent in grpLongOps.Children)
                        {
                            if (gParent.Tag == longOp.Parent) { parentPos = currentPos; }
                            currentPos++;
                        }
                        //insert after parent
                        grpLongOps.Children.Insert(parentPos + 1, g);
                    }
                    else
                    {
                        //no parent, add at end
                        grpLongOps.Children.Add(g);
                    }
                }


            }
        }

        private void updateBar(Grid g)
        {
            logging.longOp l = (logging.longOp)g.Tag;

            if (g.Children.Count == 0)
            {


                TextBlock lbl = new TextBlock();
                lbl.Text = l.name;
                if (l.Parent != null)
                { 
                    //indent if has a parent
                    Thickness margin = lbl.Margin;
                    margin.Left = 10;
                    lbl.Margin = margin;
                }

                ProgressBar p = new ProgressBar();
                p.Maximum = l.totalLength;
                if (l.CurrentPos > p.Maximum) { p.Maximum = l.CurrentPos; }
                p.Value = l.CurrentPos;
                if (l.Parent != null)
                {
                    //indent if has a parent
                    Thickness margin = p.Margin;
                    margin.Left = 10;
                    p.Margin = margin;
                }

                Grid.SetColumn(lbl, 0);
                g.Children.Add(lbl);
                Grid.SetColumn(p, 1);
                g.Children.Add(p);

            }
            else
            {
                TextBlock lbl = (TextBlock)g.Children[0];
                lbl.Text = l.name;

                ProgressBar p = (ProgressBar)g.Children[1];
                p.Maximum = l.totalLength;
                if (l.CurrentPos > p.Maximum) { p.Maximum = l.CurrentPos; }
                p.Value = l.CurrentPos;

            }


        }

        public void removeProgressBar(logging.longOp l)
        {
            if (!CheckAccess())
            {
                //call ourselves back via the GUI thread. 
                Dispatcher.Invoke(new Action(() => { removeProgressBar(l); }));
            }
            else
            {
                List<Grid> targets = new List<Grid>();
                foreach (Grid g in grpLongOps.Children)
                {
                    if (g.Tag == l) { targets.Add(g); }
                }
                foreach (Grid g in targets)
                {
                    grpLongOps.Children.Remove(g);
                }
            }
        }

        public void addLogItem(logging.logItem li)
        {
            if (!CheckAccess())
            {
                //call ourselves back via the GUI thread. 
                Dispatcher.Invoke(new Action(() => { addLogItem(li); }));
            }
            else
            {
                //if called from the GUI thread.
                try
                {
                    if (Roboto.Settings != null)
                    {
                        /* TODO - not WPFable. 
                        // Stop redrawing:
                        SendMessage(LogText.Handle, WM_SETREDRAW, 0, IntPtr.Zero);
                        // Stop sending of events:
                        eventMask = SendMessage(LogText.Handle, EM_GETEVENTMASK, 0, IntPtr.Zero);


                        Roboto.Settings.maxLogItems = 50;
                        int linesToRemove = LogText.Lines.Count() - Roboto.Settings.maxLogItems;
                        if (linesToRemove > 0)
                        {
                            int start_index = LogText.GetFirstCharIndexFromLine(linesToRemove);
                            LogText.Select(0, start_index);
                            LogText.SelectedText = "";
                        }
                        */
                        if (LogText.Document.Blocks.Count() > 50)
                        {
                            LogText.Document.Blocks.Remove(LogText.Document.Blocks.FirstBlock);
                        }
                        appendLog(li);
                        /*
                        // turn on events
                        SendMessage(LogText.Handle, EM_SETEVENTMASK, 0, eventMask);
                        // turn on redrawing
                        SendMessage(LogText.Handle, WM_SETREDRAW, 1, IntPtr.Zero);
                        LogText.Invalidate();

                        //while ( && LogText.Lines.Count() > Roboto.Settings.maxLogItems)
                        //    {
                        //        LogText.Text = LogText. .Lines[0].Remove();
                        //    }
                        */
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
            Paragraph p = new Paragraph();
            p.Inlines.Add ( li.ToString() );//+ Environment.NewLine
            
            /*int startpos = LogText.TextLength;
            LogText.AppendText(text);
            LogText.Select(startpos, text.Length);
            LogText.SelectionColor = c;
            LogText.Select(LogText.TextLength, 0);
            */


            //BrushConverter bc = new BrushConverter();
            //TextRange tr = new TextRange(LogText.Document.ContentEnd, LogText.Document.ContentEnd);


            try
            {
                //TODO - new brush each time is probably shitty for perf.
                p.Foreground = new SolidColorBrush(c);
                LogText.Document.Blocks.Add(p);
            }
            catch (FormatException) { }




        }



        public void unlockExit()
        {
            backgroundCompleted = true;
        }

        public void setTitle(string title)
        {
            if (!CheckAccess())
            {
                //call ourselves back via the GUI thread. 
                Dispatcher.Invoke(new Action(() => { setTitle(title); }));
            }
            else
            {
                //if called from the GUI thread.
                try
                {
                    this.Title = title;

                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
            }

        }

        private void Window_Closing_1(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (backgroundCompleted)
            {
                return;
            }

            /* TODO: Is this still valid in WPF?
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                //Immediate save. This will then close the form automatically.
                Roboto.Settings.save();
            }
            */

            // UI button - override close with a clean shutdown
            e.Cancel = true;
            switch (MessageBox.Show(this, "Are you sure you want to close?", "Closing", MessageBoxButton.YesNo))
            {
                case MessageBoxResult.No:
                    break;
                default:
                    //Stop the background thread cleanly. Will signal back when OK to exit properly.  
                    Roboto.shudownMainThread();
                    break;
            }

        }
    }
}
