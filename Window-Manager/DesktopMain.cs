using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Window_Manager
{
    public partial class DesktopMain : Form
    {
        public DesktopMain()
        {
            InitializeComponent();
            refreshDesktopWindowsList();
        }

        public bool isHelperTabEnabled()
        {
            return showHelperTab.Checked;
        }

        public List<string> getBrowserWindowsList()
        {
            return browserWindows.CheckedItems.Cast<string>().ToList();
        }

        private List<IntPtr> windowHandle = new();

        public List<IntPtr> getDesktopWindowsList()
        {
            List<IntPtr> result = new();

            for (var i = 0; i < desktopWindows.Items.Count; i++)
            {
                if (desktopWindows.GetItemChecked(i))
                {
                    result.Add(windowHandle[i]);
                }
            }

            return result;
        }

        public static string getWindowText(IntPtr hWnd)
        {
            int size = User32.GetWindowTextLength(hWnd);
            if (size > 0)
            {
                var builder = new StringBuilder(size + 1);
                User32.GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString();
            }

            return String.Empty;
        }

        public void refreshDesktopWindowsList()
        {
            desktopWindows.Items.Clear();
            windowHandle.Clear();

            User32.EnumWindows(delegate (IntPtr hwnd, IntPtr param)
            {
                // Use the filters from the StereoKit sample: https://github.com/StereoKit/StereoKit/blob/74e7c745e05d41edd1b78498855a22def0cbcb0f/Examples/StereoKitCTest/demo_windows.cpp#L47
                bool isVisible = User32.IsWindowVisible(hwnd);
                bool isEnabled = (User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE) & User32.WS_DISABLED) == 0;
                bool isRoot = User32.GetAncestor(hwnd, User32.GA_ROOT) == hwnd;
                if (hwnd == IntPtr.Zero || hwnd == User32.GetShellWindow() || !isVisible || !isRoot || !isEnabled)
                {
                    return true;
                }

                string title = getWindowText(hwnd);
                if (title == "")
                {
                    return true;
                }

                desktopWindows.Items.Add(title);
                windowHandle.Add(hwnd);

                return true;
            }, IntPtr.Zero);
        }

        private void newBrowser_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                var index = browserWindows.Items.Add(newBrowser.Text);
                browserWindows.SetItemChecked(index, true);
                newBrowser.Text = "";
            }
        }

        private class User32
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetShellWindow();

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetWindowTextLength(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll", ExactSpelling = true)]
            public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
            public const uint GA_ROOT = 2;

            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
            public static extern uint GetWindowLongPtr(IntPtr hWnd, int nIndex);
            public const int GWL_STYLE = -16;
            public const uint WS_DISABLED = 0x8000000;

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            refreshDesktopWindowsList();
        }
    }
}
