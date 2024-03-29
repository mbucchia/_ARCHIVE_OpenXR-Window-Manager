﻿// This file is taken from the StereoKit sample: https://github.com/StereoKit/StereoKit-Browser/blob/main/Browser.cs

using CefSharp;
using CefSharp.DevTools.Input;
using CefSharp.OffScreen;
using StereoKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Window_Manager
{

	public class Browser
	{
		class NoNewTabLifeSpanHandler : ILifeSpanHandler
		{
			public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => true;
			public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
			public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
			public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
			{
				browser.MainFrame.LoadUrl(targetUrl);
				newBrowser = null;
				return true;
			}
		}

		public Tex Texture { get; internal set; }
		public string Url { get => browser == null ? url : browser.Address; set { url = value; if (browser != null) browser.LoadUrl(url); } }

		ChromiumWebBrowser browser;
		Tex[] tex;
		int texCurr = 0;
		string url;

		float browserAspect = 9.0f / 16.0f;
		Material material;

		public Browser(string url)
		{
			Texture = Tex.White;
			Url = url;
			tex = new Tex[] {
			new Tex(TexType.ImageNomips, TexFormat.Bgra32),
			new Tex(TexType.ImageNomips, TexFormat.Bgra32) };
			tex[0].AddressMode = TexAddress.Clamp;
			tex[1].AddressMode = TexAddress.Clamp;
			material = Material.Unlit.Copy();

			//Only required for PlatformTarget of AnyCPU
			//CefRuntime.SubscribeAnyCpuAssemblyResolver();

			Init();
		}
		~Browser() => Cef.Shutdown();

		async Task Init()
		{
			CefSettings settings = new CefSettings
			{
				UserAgent = "Mozilla/5.0 (Linux; Android 10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.79 Mobile Safari/537.36",
				CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CefSharp\\Cache")
			};
			await Cef.InitializeAsync(settings, performDependencyCheck: true, browserProcessHandler: null);

			browser = new ChromiumWebBrowser(Url);
			await browser.WaitForInitialLoadAsync();
			browser.Paint += Browser_Paint;
			browserAspect = browser.Size.Height / (float)browser.Size.Width;
			browser.LifeSpanHandler = new NoNewTabLifeSpanHandler();
		}

		private void Browser_Paint(object sender, OnPaintEventArgs e)
		{
			tex[texCurr].SetColors(e.Width, e.Height, e.BufferHandle);
			Texture = tex[texCurr];
			texCurr = (texCurr + 1) % 2;
		}

		TouchPoint TouchPoint(Bounds bounds, Handed hand)
		{
			Hand h = Input.Hand(hand);
			HandJoint j = h[FingerId.Index, JointId.Tip];
			Plane p = new Plane(V.XYZ(bounds.center.x, bounds.center.y, bounds.center.z - bounds.dimensions.z / 2), Vec3.Forward);
			Vec3 at = p.Closest(Hierarchy.ToLocal(j.position));

			Vec3 pt = (at - (bounds.center + (bounds.dimensions * 0.5f)));
			pt = new Vec3(-pt.x / bounds.dimensions.x, -pt.y / bounds.dimensions.y, 0);

			return new TouchPoint
			{
				X = pt.x * browser.Size.Width,
				Y = pt.y * browser.Size.Height,
				RadiusX = j.radius,
				RadiusY = j.radius,
			};
		}

		Vec2 startAt;
		Vec2 prevAt;
		public void StepAsUI()
		{
			float width = UI.LayoutRemaining.x;
			Bounds bounds = UI.LayoutReserve(new Vec2(width, browserAspect * width));
			bounds.center.z += 0.01f;
			bounds.dimensions.z += 0.03f;
			BtnState state = UI.VolumeAt("browser", bounds, UIConfirm.Push, out Handed hand);

			material[MatParamName.DiffuseTex] = Texture;
			Mesh.Quad.Draw(material, Matrix.TS(bounds.center + V.XYZ(0, 0, -0.015f), bounds.dimensions));

			if (browser == null || !browser.IsBrowserInitialized) return;

			if (state.IsJustActive())
			{
				TouchPoint pt = TouchPoint(bounds, hand);
				startAt = prevAt = new Vec2((float)pt.X, (float)pt.Y);
				browser
					.GetDevToolsClient()
					.Input
					.DispatchTouchEventAsync(DispatchTouchEventType.TouchStart, new TouchPoint[] { pt });
			}
			if (state.IsActive())
			{
				TouchPoint pt = TouchPoint(bounds, hand);
				Vec2 currAt = new Vec2((float)pt.X, (float)pt.Y);
				if (!Vec2.InRadius(currAt, startAt, 6) && !Vec2.InRadius(currAt, prevAt, 1))
				{
					browser
						.GetDevToolsClient()
						.Input
						.DispatchTouchEventAsync(DispatchTouchEventType.TouchMove, new TouchPoint[] { pt });
					prevAt = currAt;
				}
			}
			if (state.IsJustInactive())
			{
				TouchPoint pt = new TouchPoint
				{
					X = prevAt.x,
					Y = prevAt.y,
				};
				browser
					.GetDevToolsClient()
					.Input
					.DispatchTouchEventAsync(DispatchTouchEventType.TouchEnd, new TouchPoint[] { pt });
			}
		}

		public bool HasForward => browser == null ? false : browser.CanGoForward;
		public bool HasBack => browser == null ? false : browser.CanGoBack;
		public void Back() => browser?.Back();
		public void Forward() => browser?.Forward();
	}
}
