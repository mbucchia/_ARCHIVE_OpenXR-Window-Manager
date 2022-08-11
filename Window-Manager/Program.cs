using StereoKit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Window_Manager
{
    class Program
    {
        class BrowserInstance
        {
            public string url;
            public Browser browser;
            public Pose windowPose;
        }

        class DesktopWindowInstance
        {
            public IntPtr handle;
            public Pose windowPose;
            public Tex surface;
            public Sprite sprite;
        }

        private static StreamWriter debugLog = null;

        static void Main(string[] args)
        {
            // Initialize StereoKit.
            SKSettings settings = new SKSettings
            {
                appName = "Window_Manager",
                assetsFolder = "Assets",
                displayPreference = DisplayMode.MixedReality,
                overlayApp = true,
                overlayPriority = 100,
                disableUnfocusedSleep = true,
                disableFlatscreenMRSim = true,
                noFlatscreenFallback = true,
            };
            Backend.OpenXR.UseMinimumExts = true;
            Backend.OpenXR.RequestExt("XR_EXTX_overlay");

            {
                var parentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\OpenXR-Window-Manager";
                Directory.CreateDirectory(parentDirectory);
                debugLog = new StreamWriter(parentDirectory + "\\debug.log");
            }
            Log.Filter = LogLevel.Info;
            Log.Subscribe(OnLog);
            Log.Write(LogLevel.Info, "Startup");
            if (!SK.Initialize(settings))
            {
                Environment.Exit(1);
            }

            if (!SK.System.overlayApp)
            {
                if (!Backend.OpenXR.ExtEnabled("XR_EXTX_overlay"))
                {
                    MessageBox.Show("Fail to start as an overlay application. Please check that EXTX_overlay is available on the system.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("Fail to start as an overlay application. Please make sure that the main application is started.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
#if !DEBUG
                Environment.Exit(1);
#endif
            }

            // Create the desktop form for controlling the overlay.
            DesktopMain desktopMain = new();
            bool running = true;
            Thread thread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(desktopMain);
                running = false;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            List<BrowserInstance> browserWindows = new();
            List<DesktopWindowInstance> desktopWindows = new();

            // Core application loop.
            while (running && SK.Step(() =>
            {
                Application.DoEvents();

                if (desktopMain.isHelperTabEnabled())
                {
                    Pose hintPose = new Pose(0, 0, -0.3f, Quat.LookDir(0, 0, 1));
                    UI.WindowBegin("OpenXR Window Manager", ref hintPose, V.XY(0.3f, 0), UIWin.Head, UIMove.None);
                    UI.WindowEnd();
                }

                DrawBrowserOverlays(desktopMain, ref browserWindows);
                DrawDesktopWindowOverlays(desktopMain, ref desktopWindows);
            })) ;
            SK.Shutdown();
            Application.Exit();
        }

        private static void DrawBrowserOverlays(DesktopMain desktopMain, ref List<BrowserInstance> browserWindows)
        {
            // Update the list of browsers to show.
            var browsersToDisplay = desktopMain.getBrowserWindowsList();
            List<BrowserInstance> newBrowserWindows = new();
            foreach (var browserUrl in browsersToDisplay)
            {
                var existingBrowserInstance = browserWindows.Find(x => x.url == browserUrl);
                if (existingBrowserInstance == null)
                {
                    BrowserInstance newBrowserInstance = new()
                    {
                        url = browserUrl,
                        browser = new Browser(browserUrl),
                        windowPose = new Pose(0, 0, -0.5f, Quat.LookDir(0, 0, 1)),
                    };
                    newBrowserWindows.Add(newBrowserInstance);
                }
                else
                {
                    newBrowserWindows.Add(existingBrowserInstance);
                }
            }
            browserWindows = newBrowserWindows;

            // Show the browser windows.
            foreach (var browserWindow in browserWindows)
            {
                var browser = browserWindow.browser;

                UI.WindowBegin(browserWindow.url, ref browserWindow.windowPose, V.XY(0.6f, 0));
                UI.PushEnabled(browser.HasBack);
                if (UI.Button("Back")) browser.Back();
                UI.PopEnabled();

                UI.SameLine();
                UI.PushEnabled(browser.HasForward);
                if (UI.Button("Forward")) browser.Forward();
                UI.PopEnabled();

                UI.SameLine();
                UI.Label(browser.Url, V.XY(UI.LayoutRemaining.x, 0));

                UI.HSeparator();

                browser.StepAsUI();
                UI.WindowEnd();
            }
        }
        private static void DrawDesktopWindowOverlays(DesktopMain desktopMain, ref List<DesktopWindowInstance> desktopWindows)
        {
            // Update the list of browsers to show.
            var desktopWindowsToDisplay = desktopMain.getDesktopWindowsList();
            List<DesktopWindowInstance> newDesktopWindows = new();
            foreach (var windowHandle in desktopWindowsToDisplay)
            {
                var existingDesktopWindowInstance = desktopWindows.Find(x => x.handle == windowHandle);
                if (existingDesktopWindowInstance == null)
                {
                    DesktopWindowInstance newDesktopWindowInstance = new()
                    {
                        handle = windowHandle,
                        windowPose = new Pose(0, 0, -0.5f, Quat.LookDir(0, 0, 1)),
                    };
                    newDesktopWindows.Add(newDesktopWindowInstance);
                }
                else
                {
                    newDesktopWindows.Add(existingDesktopWindowInstance);
                }
            }
            desktopWindows = newDesktopWindows;

            // Show the desktop windows.
            foreach (var desktopWindow in desktopWindows)
            {
                var title = DesktopMain.getWindowText(desktopWindow.handle);

                // Copy the window content into the StereoKit surface.
                var capture = CaptureWindow(desktopWindow.handle);
                if (capture == null)
                {
                    continue;
                }
                var image = new Bitmap(capture);
                if (desktopWindow.surface == null || desktopWindow.surface.Width != image.Width || desktopWindow.surface.Height != image.Height)
                {
                    desktopWindow.surface = Tex.GenColor(StereoKit.Color.BlackTransparent, image.Width, image.Height);
                    desktopWindow.sprite = Sprite.FromTex(desktopWindow.surface);
                }
                var bitmapData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                desktopWindow.surface.SetColors(image.Width, image.Height, bitmapData.Scan0);
                image.UnlockBits(bitmapData);

                UI.WindowBegin(title, ref desktopWindow.windowPose, V.XY(0.6f, 0));
                UI.Image(desktopWindow.sprite, V.XY(1.0f, 0));
                UI.WindowEnd();
            }
        }

        private static void OnLog(LogLevel level, string text)
        {
            var line = "[" + level.ToString() + "] " + text;
            Debug.Write(line);
            debugLog.Write(line);
            debugLog.Flush();
        }

        // https://ourcodeworld.com/articles/read/195/capturing-screenshots-of-different-ways-with-c-and-winforms
        public static Image CaptureWindow(IntPtr handle)
        {
            IntPtr hdcSrc = User32.GetWindowDC(handle);
            if (hdcSrc == IntPtr.Zero)
            {
                return null;
            }

            User32.RECT windowRect = new User32.RECT();
            User32.GetWindowRect(handle, ref windowRect);
            int width = windowRect.right - windowRect.left;
            int height = windowRect.bottom - windowRect.top;

            IntPtr hdcDest = GDI32.CreateCompatibleDC(hdcSrc);
            if (hdcDest == IntPtr.Zero)
            {
                User32.ReleaseDC(handle, hdcSrc);
                return null;
            }

            IntPtr hBitmap = GDI32.CreateCompatibleBitmap(hdcSrc, width, height);
            if (hBitmap == IntPtr.Zero)
            {
                GDI32.DeleteDC(hdcDest);
                User32.ReleaseDC(handle, hdcSrc);
                return null;
            }

            IntPtr hOld = GDI32.SelectObject(hdcDest, hBitmap);

            GDI32.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, GDI32.SRCCOPY);

            GDI32.SelectObject(hdcDest, hOld);

            GDI32.DeleteDC(hdcDest);
            User32.ReleaseDC(handle, hdcSrc);

            Image img = Image.FromHbitmap(hBitmap);

            GDI32.DeleteObject(hBitmap);

            return img;
        }

        private class GDI32
        {
            public const int SRCCOPY = 0x00CC0020;
            [DllImport("gdi32.dll")]
            public static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest,
                int nWidth, int nHeight, IntPtr hObjectSource,
                int nXSrc, int nYSrc, int dwRop);
            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth,
                int nHeight);
            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
            [DllImport("gdi32.dll")]
            public static extern bool DeleteDC(IntPtr hDC);
            [DllImport("gdi32.dll")]
            public static extern bool DeleteObject(IntPtr hObject);
            [DllImport("gdi32.dll")]
            public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        }

        private class User32
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int left;
                public int top;
                public int right;
                public int bottom;
            }
            [DllImport("user32.dll")]
            public static extern IntPtr GetDesktopWindow();
            [DllImport("user32.dll")]
            public static extern IntPtr GetWindowDC(IntPtr hWnd);
            [DllImport("user32.dll")]
            public static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);
            [DllImport("user32.dll")]
            public static extern IntPtr GetWindowRect(IntPtr hWnd, ref RECT rect);
            [DllImport("user32.dll")]
            public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
            public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        }
    }
}
