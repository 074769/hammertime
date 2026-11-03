using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using SharpDX.Direct3D;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Viewports;
using Veldrid;

using Sledge.Common.Logging;
namespace Sledge.Rendering.Engine
{
	public class Engine : IDisposable
	{
		internal static Engine Instance { get; } = new Engine();
		public static EngineInterface Interface { get; } = new EngineInterface();

		public GraphicsDevice Device { get; }
		public Thread RenderThread { get; private set; }
		public Scene Scene { get; }
		internal bool IsShadowsEnabled { get; set; } = false;
		internal Vector3 LightAngle { get; set; } = Vector3.Zero;
		internal RenderContext Context { get; }

		private CancellationTokenSource _token;

		private readonly GraphicsDeviceOptions _options;
		private readonly Stopwatch _timer;
		private readonly object _lock = new object();
		private readonly List<IViewport> _renderTargets;
		private readonly Dictionary<PipelineGroup, List<IPipeline>> _pipelines;
		private readonly CommandList _commandList;

		private RgbaFloat _clearColourPerspective;
		private RgbaFloat _clearColourOrthographic;
		internal int InactiveTargetFps { get; set; } = 10;
		/// <summary>Upper limit for the render loop, in frames per second. 0 means no limit.</summary>
		internal int MaxTargetFps { get; set; } = 144;
		// When each viewport was last rendered (Stopwatch ticks), so unfocused viewports can be throttled one by one
		private readonly Dictionary<IViewport, long> _lastRendered = new Dictionary<IViewport, long>();

		[System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
		[System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
		private ViewProjectionBuffer _lightData;
		private ViewProjectionBuffer _cameraBuffer;

		private Engine()
		{
			_options = new GraphicsDeviceOptions
			{
				HasMainSwapchain = false,
				ResourceBindingModel = ResourceBindingModel.Improved,
				SwapchainDepthFormat = PixelFormat.R32_Float,
			};

			Device = GraphicsDevice.CreateD3D11(_options);
			DetectFeatures(Device);
			Scene = new Scene();

			_commandList = Device.ResourceFactory.CreateCommandList();

			SetClearColour(CameraType.Both, RgbaFloat.Black);

			float orthoSize = 5000f; // Adjust based on scene size
			float nearPlane = 0.01f;
			float farPlane = 10000f;

			var _lightProjection = Matrix4x4.CreateOrthographic(
				orthoSize, // Width
				orthoSize, // Height
				nearPlane,
				farPlane
			);

			Vector3 lightPosition = new Vector3(0, 0, 1000); // Light high above the scene
			Vector3 lightTarget = new Vector3(0, 0, 0); // Looking at the scene center
			Vector3 upVector = Vector3.UnitZ; // Adjust if needed


			var _lightView = Matrix4x4.CreateLookAt(
				lightPosition,
				lightTarget,
				upVector
			);
			_lightData = new ViewProjectionBuffer { Projection = _lightProjection, View = _lightView };

			_timer = new Stopwatch();
			_token = new CancellationTokenSource();

			_renderTargets = new List<IViewport>();
			_pipelines = new Dictionary<PipelineGroup, List<IPipeline>>();
			Context = new RenderContext(Device);
			Scene.Add(Context);
#if DEBUG
			Scene.Add(new FpsMonitor());
#endif

			RenderThread = new Thread(Loop);

			_pipelines.Add(PipelineGroup.Opaque, new List<IPipeline>());
			_pipelines.Add(PipelineGroup.Transparent, new List<IPipeline>());
			_pipelines.Add(PipelineGroup.Overlay, new List<IPipeline>());

			AddPipeline(new SkyboxPipeline());
			AddPipeline(new WireframePipeline());
			// Selected objects in the 2D views: drawn after everything else, ignoring depth, so the selection is never hidden
			AddPipeline(new WireframePipeline(PipelineType.WireframeSelected, 11, DepthStencilStateDescription.Disabled));
			AddPipeline(new TexturedOpaquePipeline());
			AddPipeline(new BillboardOpaquePipeline());
			AddPipeline(new WireframeModelPipeline());
			AddPipeline(new TexturedModelPipeline());
			AddPipeline(new TexturedModelBlendPipeline(PipelineType.TexturedModelAdditive, 6));
			AddPipeline(new TexturedModelBlendPipeline(PipelineType.TexturedModelAlpha, 6.5f));

			AddPipeline(new TexturedAlphaPipeline());
			AddPipeline(new TexturedAdditivePipeline());
			AddPipeline(new BeamAdditivePipeline());
			AddPipeline(new BillboardAlphaPipeline());
//#if DEBUG
//			AddPipeline(new SwapchainShadowOverlay(shadowdepth.NearShadowResourceTexture));
//#endif
			AddPipeline(new SwapchainOverlayPipeline());
			AddPipeline(new OverlayPipeline());
			AddPipeline(new ShadowOverlayPipeline(_lightData));

			Application.ApplicationExit += Shutdown;
		}

		private void DetectFeatures(GraphicsDevice device)
		{
			var dev = device.GetType().GetProperty("Device");
			var dxd = dev?.GetValue(device) as SharpDX.Direct3D11.Device;
			var fl = dxd?.FeatureLevel ?? FeatureLevel.Level_10_0; // Just assume it's DX10, whatever
			if (fl < FeatureLevel.Level_10_0)
			{
				MessageBox.Show($"Sledge requires DirectX 10, but your computer only has version {fl}.", "Unsupported graphics card!");
				Environment.Exit(1);
			}
			Features.FeatureLevel = fl;
		}

		internal void SetClearColour(CameraType type, RgbaFloat colour)
		{
			lock (_lock)
			{
				if (type == CameraType.Both) _clearColourOrthographic = _clearColourPerspective = colour;
				else if (type == CameraType.Orthographic) _clearColourOrthographic = colour;
				else _clearColourPerspective = colour;
			}
		}

		public void AddPipeline(IPipeline pipeline)
		{
			pipeline.Create(Context, TextureSampleCount.Count1);
			lock (_lock)
			{
				_pipelines[pipeline.Group].Add(pipeline);
				_pipelines[pipeline.Group].Sort((a, b) => a.Order.CompareTo(b.Order));
			}
		}

		public void Dispose()
		{
			_pipelines.SelectMany(x => x.Value).ToList().ForEach(x => x.Dispose());
			_pipelines.Clear();

			_renderTargets.ForEach(x => x.Dispose());
			_renderTargets.Clear();

			Device.Dispose();
			_token.Dispose();
		}

		private void Shutdown(object sender, EventArgs e)
		{
			Dispose();
			Application.ApplicationExit -= Shutdown;
		}

		// Render loop

		private void Start()
		{
			_timer.Start();
			RenderThread.Start(_token.Token);
			RenderThread.Name = "Render Thread";
		}

		private void Stop()
		{
			_token.Cancel();
			_timer.Stop();

			RenderThread = new Thread(Loop);
			_token = new CancellationTokenSource();
		}

		private int _paused = 0;
		private TextureSampleCount _sampleCount = TextureSampleCount.Count1;
		private readonly ManualResetEvent _pauseThreadEvent = new ManualResetEvent(false);

		public IDisposable Pause()
		{
			_paused++;
			if (_timer.IsRunning) _pauseThreadEvent.WaitOne();
			return new PauseImpl(() =>
			{
				_paused--;
				_pauseThreadEvent.Reset();
			});
		}
		private class PauseImpl : IDisposable
		{
			private readonly Action _disposeAction;
			public PauseImpl(Action disposeFunc) => _disposeAction = disposeFunc;
			public void Dispose() => _disposeAction();
		}

		private void Loop(object o)
		{
			var token = (CancellationToken)o;
			var timerRaised = false;
			try
			{
				// Without this Thread.Sleep(1) sleeps for up to ~15 ms, which makes the frame times uneven
				try { timerRaised = timeBeginPeriod(1) == 0; } catch { }

				var next = Stopwatch.GetTimestamp();
				while (!token.IsCancellationRequested)
				{
					if (_paused > 0)
					{
						_pauseThreadEvent.Set();
						Thread.Sleep(1);
						next = Stopwatch.GetTimestamp();
						continue;
					}

					// Wait for the next frame slot. Sleep while there is time to spare, then spin the last bit.
					var max = MaxTargetFps;
					var interval = max > 0 ? Stopwatch.Frequency / max : 0;
					var now = Stopwatch.GetTimestamp();
					if (interval > 0 && now < next)
					{
						var spare = (next - now) * 1000 / Stopwatch.Frequency;
						if (spare > 2) Thread.Sleep((int) (spare - 1));
						else Thread.SpinWait(50);
						continue;
					}

					// Keep a fixed cadence instead of "now + interval", but don't try to catch up after a slow frame
					next = (interval > 0 && now - next < interval) ? next + interval : now + interval;

					var frame = _timer.ElapsedMilliseconds;
					using (PerfLog.Measure("Engine: frame")) Render(frame);
					using (PerfLog.Measure("Engine: GPU wait")) Device.WaitForIdle();
					PerfLog.FlushIfDue("Engine and views (last 2 s)", 2000, "Engine:", "View update", "Overlay build", "Overlay renderable", "Render ", "Pipeline", "Transparent", "Submit", "ImGui", "Update:", "Listener:", "Scene:");
				}
			}
			catch (ThreadInterruptedException)
			{
				// exit
			}
			catch (ThreadAbortException)
			{
				// exit
			}
			finally
			{
				if (timerRaised) { try { timeEndPeriod(1); } catch { } }
			}
		}

		private void Render(long frame)
		{
			lock (_lock)
			{
				using (PerfLog.Measure("Engine: Scene.Update")) Scene.Update(frame);
				var overlays = Scene.GetOverlayRenderables().ToList();

				var nowTicks = Stopwatch.GetTimestamp();
				var inactiveInterval = Stopwatch.Frequency / Math.Max(InactiveTargetFps, 1);

				// Focused viewports render every frame. Unfocused ones render when their turn is due, but at most one
				// per frame (the most overdue), so the work per frame stays even instead of every unfocused view
				// landing on the same frame and causing a long frame every so often.
				var toRender = new List<IViewport>();
				var focusedViews = new HashSet<IViewport>();
				IViewport overdue = null;
				var overdueBy = -1L;
				foreach (var rt in _renderTargets)
				{
					using (PerfLog.MeasureForView("View update", rt.ID)) rt.Update(frame);

					if (rt.IsFocused)
					{
						focusedViews.Add(rt);
						toRender.Add(rt);
						continue;
					}

					var late = _lastRendered.TryGetValue(rt, out var last) ? nowTicks - last : long.MaxValue;
					if (late < inactiveInterval)
					{
						PerfLog.CountForView("Render skipped", rt.ID);
						continue;
					}

					if (late > overdueBy)
					{
						if (overdue != null) PerfLog.CountForView("Render deferred", overdue.ID);
						overdue = rt;
						overdueBy = late;
					}
					else
					{
						PerfLog.CountForView("Render deferred", rt.ID);
					}
				}
				if (overdue != null) toRender.Add(overdue);

				foreach (var rt in toRender)
				{
					PerfLog.CountForView(focusedViews.Contains(rt) ? "Render focused" : "Render unfocused", rt.ID);
					_lastRendered[rt] = nowTicks;
					_cameraBuffer = new ViewProjectionBuffer { Projection = rt.Camera.Projection, View = rt.Camera.View };
					if (PerfLog.Enabled) PerfLog.SetViewLabel(rt.ID, rt.Camera.Type.ToString());
					using (PerfLog.MeasureForView("Overlay build", rt.ID)) rt.Overlay.Build(overlays);
					using (PerfLog.MeasureForView("Render view", rt.ID)) Render(rt);
				}
			}
		}

		private void Render(IViewport renderTarget)
		{
			_commandList.Begin();
			_commandList.SetFramebuffer(renderTarget.ViewportFramebuffer);
			_commandList.ClearDepthStencil(1);

			var cc = renderTarget.Camera.Type == CameraType.Perspective
				? _clearColourPerspective
				: _clearColourOrthographic;
			_commandList.ClearColorTarget(0, cc);

			//foreach (var group in _pipelines.OrderBy(x => (int) x.Key))
			//{
			//    foreach (var pipeline in group.Value.OrderBy(x => x.Order))
			//    {
			//        pipeline.SetupFrame(Context, renderTarget);
			//    }
			//}

			//var renderables = _pipelines.ToDictionary(x => x, x => Scene.GetRenderables(x, renderTarget).OrderBy(r => r.Order).ToList());

			// foreach (var pg in _pipelines.GroupBy(x => x.Group).OrderBy(x => x.Key))
			// {
			//     foreach (var pipeline in pg.OrderBy(x => x.Order))
			//     {
			//         if (!renderables.ContainsKey(pipeline)) continue;
			//         pipeline.Render(Context, renderTarget, _commandList, renderables[pipeline]);
			//     }
			// 
			//     foreach (var pipeline in pg.OrderBy(x => x.Order))
			//     {
			//         if (!renderables.ContainsKey(pipeline)) continue;
			//         pipeline.RenderTransparent(Context, renderTarget, _commandList, renderables[pipeline]);
			//     }
			// }

			var cameraLocation = renderTarget.Camera.Location;
			var transparentPipelines = _pipelines[PipelineGroup.Transparent];

			// Every transparent object the pipelines report this frame, before culling and sorting
			var candidateLocations =
				from t in transparentPipelines
				from renderable in Scene.GetRenderables(t, renderTarget)
				from location in renderable.GetLocationObjects(t, renderTarget)
				select new { Pipeline = t, Renderable = renderable, Location = location };


			foreach (var opaque in _pipelines[PipelineGroup.Opaque])
			{
				opaque.SetupFrame(Context, _cameraBuffer);
				using (PerfLog.MeasureForView("Pipeline opaque", opaque, renderTarget.ID)) opaque.Render(Context, renderTarget, _commandList, Scene.GetRenderables(opaque, renderTarget));
			}


			foreach (var transparent in transparentPipelines)
			{
				transparent.SetupFrame(Context, _cameraBuffer);
			}
			var sortTimer = PerfLog.MeasureForView("Transparent sort", renderTarget.ID);
			var frustum = new ViewCulling.Frustum(renderTarget.Camera.View * renderTarget.Camera.Projection);
			var candidates = candidateLocations.ToList();
			var visible = ViewCulling.Enabled ? candidates.Where(x => !frustum.IsOutside(x.Location)).ToList() : candidates;
			var sortedLocations = visible.OrderByDescending(x => (cameraLocation - x.Location.Location).LengthSquared()).ToList();
			sortTimer.Dispose();
			if (PerfLog.Enabled)
			{
				PerfLog.AddCountForView("Transparent candidates", renderTarget.ID, candidates.Count);
				PerfLog.AddCountForView("Transparent culled", renderTarget.ID, candidates.Count - sortedLocations.Count);
				PerfLog.AddCountForView("Transparent objects", renderTarget.ID, sortedLocations.Count);
				foreach (var g in sortedLocations.GroupBy(x => x.Renderable.GetType().Name))
					PerfLog.AddCountForView("Transparent type:" + g.Key, renderTarget.ID, g.Count());
			}
			var drawTimer = PerfLog.MeasureForView("Transparent draw", renderTarget.ID);
			foreach (var lo in sortedLocations)
			{
				lo.Pipeline.Render(Context, renderTarget, _commandList, lo.Renderable, lo.Location);
			}

			drawTimer.Dispose();
			foreach (var pipeline in transparentPipelines)
			{
				if (pipeline.Type == PipelineType.BillboardAlpha || (IsShadowsEnabled && pipeline.Type == PipelineType.ShadowOverlay))
					using (PerfLog.MeasureForView("Pipeline transparent", pipeline, renderTarget.ID)) pipeline.Render(Context, renderTarget, _commandList, Scene.GetRenderables(pipeline, renderTarget));
			}

			_commandList.End();
			using (PerfLog.MeasureForView("Submit", renderTarget.ID)) Device.SubmitCommands(_commandList);

			_commandList.Begin();
			renderTarget.ResolveRenderTexture(_commandList);
			_commandList.End();
			using (PerfLog.MeasureForView("Submit", renderTarget.ID)) Device.SubmitCommands(_commandList);

			_commandList.Begin();
			_commandList.SetFramebuffer(renderTarget.Swapchain.Framebuffer);
			_commandList.ClearDepthStencil(1);
			_commandList.ClearColorTarget(0, cc);

			foreach (var overlay in _pipelines[PipelineGroup.Overlay])
			{
				overlay.SetupFrame(Context, _cameraBuffer);
					using (PerfLog.MeasureForView("Pipeline overlay", overlay, renderTarget.ID)) overlay.Render(Context, renderTarget, _commandList, Scene.GetRenderables(overlay, renderTarget));
			}
			_commandList.End();
			using (PerfLog.MeasureForView("Submit", renderTarget.ID)) Device.SubmitCommands(_commandList);
			Device.SwapBuffers(renderTarget.Swapchain);
		}

		// Viewports

		internal event EventHandler<IViewport> ViewportCreated;
		internal event EventHandler<IViewport> ViewportDestroyed;

		internal IViewport CreateViewport()
		{
			lock (_lock)
			{
				var control = new Viewports.Viewport(Device, _options, _sampleCount);
				control.Disposed += DestroyViewport;

				if (!_renderTargets.Any()) Start();
				_renderTargets.Add(control);

				Scene.Add((IRenderable)control.Overlay);
				Scene.Add((IUpdateable)control.Overlay);
				ViewportCreated?.Invoke(this, control);

				return control;
			}
		}

		private void DestroyViewport(object viewport, EventArgs e)
		{
			if (!(viewport is IViewport t)) return;

			lock (_lock)
			{
				_renderTargets.Remove(t);
				_lastRendered.Remove(t);
				Device.WaitForIdle();

				if (!_renderTargets.Any()) Stop();

				ViewportDestroyed?.Invoke(this, t);
				Scene.Remove((IRenderable)t.Overlay);
				Scene.Remove((IUpdateable)t.Overlay);

				t.Control.Disposed -= DestroyViewport;
				t.Dispose();
			}
		}

		internal void SetMSAA(int mSAAoption)
		{
			lock (_lock)
			{
				_sampleCount = (TextureSampleCount)mSAAoption;
				foreach (var pl in _pipelines.SelectMany(x => x.Value))
				{
					if (pl.Group == PipelineGroup.Overlay) continue;
					pl.Create(Context, _sampleCount);
				}
				foreach (var rt in _renderTargets)
				{
					rt.InitFramebuffer(_sampleCount);
				}
			}
		}
		public class ViewProjectionBuffer
		{
			public Matrix4x4 Projection;
			public Matrix4x4 View;
		}
	}
}
