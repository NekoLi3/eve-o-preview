using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages;
using EveOPreview.Services.Interop;
using EveOPreview.UI.Hotkeys;
using EveOPreview.View;
using MediatR;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;

namespace EveOPreview.Services
{
	public partial class ThumbnailManager : IThumbnailManager
	{
		#region Private constants
		private const int WINDOW_POSITION_THRESHOLD_LOW = -10_000;
		private const int WINDOW_POSITION_THRESHOLD_HIGH = 31_000;
		private const int WINDOW_SIZE_THRESHOLD = 10;
		private const int FORCED_REFRESH_CYCLE_THRESHOLD = 2;
		private const int DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY = 2;

		private const string DEFAULT_CLIENT_TITLE = "EVE";
		#endregion

		#region Private fields
		private readonly IMediator _mediator;
		private readonly IProcessMonitor _processMonitor;
		private readonly IWindowManager _windowManager;
		private readonly IThumbnailConfiguration _configuration;
		private readonly IGamelogMonitor _gamelogMonitor;
		private readonly DispatcherTimer _thumbnailUpdateTimer;
		private readonly IThumbnailViewFactory _thumbnailViewFactory;
		private readonly Dictionary<IntPtr, IThumbnailView> _thumbnailViews;

		private (IntPtr Handle, string Title) _activeClient;
		private IntPtr _externalApplication;

		private readonly object _locationChangeNotificationSyncRoot;
		private (IntPtr Handle, string Title, string ActiveClient, Point Location, int Delay) _enqueuedLocationChangeNotification;

		private bool _ignoreViewEvents;
		private bool _isHoverEffectActive;

		private int _refreshCycleCount;
		private int _hideThumbnailsDelay;

		private List<HotkeyHandler> _cycleClientHotkeyHandlers = new List<HotkeyHandler>();
		private List<HotkeyHandler> _minimizeAllClientsHotkeyHandlers = new List<HotkeyHandler>();
		#endregion

		public ThumbnailManager(IMediator mediator, IThumbnailConfiguration configuration, IProcessMonitor processMonitor, IWindowManager windowManager, IThumbnailViewFactory factory, IGamelogMonitor gamelogMonitor)
		{
			this._mediator = mediator;
			this._processMonitor = processMonitor;
			this._windowManager = windowManager;
			this._configuration = configuration;
			this._gamelogMonitor = gamelogMonitor;
			this._thumbnailViewFactory = factory;

			this._activeClient = (IntPtr.Zero, ThumbnailManager.DEFAULT_CLIENT_TITLE);

			this.EnableViewEvents();
			this._isHoverEffectActive = false;

			this._refreshCycleCount = 0;
			this._locationChangeNotificationSyncRoot = new object();
			this._enqueuedLocationChangeNotification = (IntPtr.Zero, null, null, Point.Empty, -1);

			this._thumbnailViews = new Dictionary<IntPtr, IThumbnailView>();

			//  DispatcherTimer setup
			this._thumbnailUpdateTimer = new DispatcherTimer();
			this._thumbnailUpdateTimer.Tick += ThumbnailUpdateTimerTick;
			this._thumbnailUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, configuration.ThumbnailRefreshPeriod);

			this._hideThumbnailsDelay = this._configuration.HideThumbnailsDelay;

			if (this._configuration.CycleHotkeysEnabled)
			{
				this.RegisterCycleHotkeys();
			}

			RegisterMinimizeAllClientsHotkey(this._configuration.MinimizeAllClientsHotkeys?.Select(x => this._configuration.StringToKey(x)));

			RegisterCycleHotkeysToggleHotkey(this._configuration.ToggleCycleHotkeysHotkey?.Select(x => this._configuration.StringToKey(x)));
		}

		public IThumbnailView GetClientByTitle(string title)
		{
			return _thumbnailViews.FirstOrDefault(x => x.Value.Title == title).Value;
		}


		public void MinimizeAllClients()
		{
			foreach (var x in _thumbnailViews.Reverse())
			{
				this._windowManager.MinimizeWindow(x.Value.Id, this._configuration.WindowsAnimationStyle, false);
			}
		}
		public void CycleNextClient(bool isForwards, Dictionary<string, int> cycleOrder)
		{
			IOrderedEnumerable<KeyValuePair<string, int>> clientOrder;
			Dictionary<string, int> _cycleOrder = new Dictionary<string, int>(cycleOrder);

			if ( _cycleOrder.Count == 0 ) 
			{
				int order = 0;
				foreach( var x in _thumbnailViews )
				{
					if (!_cycleOrder.ContainsKey(x.Value.Title)) {
						_cycleOrder.Add(x.Value.Title, order++);
					}
				}
			}

			if (isForwards)
			{
				clientOrder = _cycleOrder.OrderBy(x => x.Value);
			}
			else
			{
				clientOrder = _cycleOrder.OrderByDescending(x => x.Value);
			}

			bool setNextClient = false;
			IThumbnailView lastClient = null;

			foreach (var t in clientOrder)
			{
				if (t.Key == _activeClient.Title && t.Key != DEFAULT_CLIENT_TITLE)
				{
					setNextClient = true;
					lastClient = _thumbnailViews.FirstOrDefault(x => x.Value.Title == t.Key).Value;
					continue;
				}

				// cycle through login screens ?
				if (t.Key == _activeClient.Title && t.Key == DEFAULT_CLIENT_TITLE)
				{
					lastClient = _thumbnailViews.FirstOrDefault(x => x.Value.Title == t.Key && x.Value.Id == _activeClient.Handle).Value;
					if (lastClient == null)
					{
						setNextClient = true;
						continue;
					}
					var possibleClients = (isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).Where(x => x.Value.Title == t.Key && ! x.Value.IsExcludedFromCycleGroup);
					foreach (var pc in possibleClients)
					{
						if ( pc.Value.Id.Equals(lastClient.Id) )
						{
							setNextClient = true;
							continue;
						}

						if (!setNextClient)
						{
							continue;
						}

						// this is the next client (at login screen)
						SetActive(pc);
						return;
					}

					// rolled off top of list - back to first (if any there!)
					// set next client ?
					continue;
				}

				if (!setNextClient)
				{
					continue;
				}

				if (_thumbnailViews.Any(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup))
				{
					var ptr = t.Key.Equals(DEFAULT_CLIENT_TITLE) ? 
						(isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).FirstOrDefault(x => x.Value.Title == t.Key && ! x.Value.IsExcludedFromCycleGroup)
						: _thumbnailViews.First(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup);
					SetActive(ptr);
					return;
				}
			}

			// we didn't get a next one. just get the first one from the start.
			foreach (var t in clientOrder)
			{
				if (_thumbnailViews.Any(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup))
				{
					var ptr = t.Key.Equals(DEFAULT_CLIENT_TITLE) ?
						(isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).FirstOrDefault(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup)
						: _thumbnailViews.First(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup);
					SetActive(ptr);
					_activeClient = (ptr.Key, t.Key);
					return;
				}
			}

			// unable to select anything !
			return;
		}

		public void RegisterCycleClientHotkey(IEnumerable<Keys> keys, bool isForwards, Dictionary<string, int> cycleOrder)
		{
			foreach (var hotkey in keys)
			{
				if (hotkey == Keys.None)
				{
					return;
				}

				var newHandler = new HotkeyHandler(default(IntPtr), hotkey);
				newHandler.Pressed += (object s, HandledEventArgs e) =>
				{
					this.CycleNextClient(isForwards, cycleOrder);
					e.Handled = true;
				};

				newHandler.Register();
				this._cycleClientHotkeyHandlers.Add(newHandler);
			}
		}
		public void RegisterMinimizeAllClientsHotkey(IEnumerable<Keys> keys)
		{
			foreach (var hotkey in keys)
			{
				if (hotkey == Keys.None)
				{
					return;
				}

				var newHandler = new HotkeyHandler(default(IntPtr), hotkey);
				newHandler.Pressed += (object s, HandledEventArgs e) =>
				{
					this.MinimizeAllClients();
					e.Handled = true;
				};

				newHandler.Register();
				this._minimizeAllClientsHotkeyHandlers.Add(newHandler);
			}
		}

		public void UpdateCycleHotkeys()
		{
			if (this._configuration.CycleHotkeysEnabled)
			{
				this.RegisterCycleHotkeys();
			}
			else
			{
				this.UnregisterCycleHotkeys();
			}
		}

		public void UpdateClientHotkeys()
		{
			// Re-register the per-client hotkeys (e.g. after a layout profile was applied)
			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				entry.Value.RegisterHotkey(this._configuration.GetClientHotkey(entry.Value.Title));
			}
		}

		private void RegisterCycleHotkeys()
		{
			// The hotkeys are already active, nothing to do
			if (this._cycleClientHotkeyHandlers.Count > 0)
			{
				return;
			}

			RegisterCycleClientHotkey(this._configuration.CycleGroup1ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup1ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup1BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup1ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup2ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup2ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup2BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup2ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup3ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup3ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup3BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup3ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup4ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup4ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup4BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup4ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup5ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup5ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup5BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup5ClientsOrder);
		}

		private void UnregisterCycleHotkeys()
		{
			foreach (HotkeyHandler handler in this._cycleClientHotkeyHandlers)
			{
				handler.Unregister();
			}

			this._cycleClientHotkeyHandlers.Clear();
		}

		public void Start()
		{
			this._thumbnailUpdateTimer.Start();
			this._gamelogMonitor.Start();

			this.RefreshThumbnails();
		}

		public void Stop()
		{
			this._thumbnailUpdateTimer.Stop();
			this._gamelogMonitor.Stop();
		}

		private void ThumbnailUpdateTimerTick(object sender, EventArgs e)
		{
			this.UpdateThumbnailsList();
			this.RefreshThumbnails();
		}



		public void UpdateThumbnailsSize()
		{
			this.SetThumbnailsSize(this._configuration.ThumbnailSize);
		}
		public void UpdateCycleGroupIndicator()
		{
			this.SetCycleGroupIndicator(this._configuration.CycleGroupIndicatorAnchor);
		}

		private void SetCycleGroupIndicator(ZoomAnchor anchor)
		{
			this.DisableViewEvents();

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				entry.Value.SetCycleGroupIndicator(entry.Value.IsExcludedFromCycleGroup, anchor);
				entry.Value.Refresh(false);
			}

			this.EnableViewEvents();
		}

		private void SetThumbnailsSize(Size size)
		{
			this.DisableViewEvents();

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				entry.Value.ThumbnailSize = size;
				entry.Value.Refresh(false);
			}

			this.EnableViewEvents();
		}

		public void UpdateThumbnailFrames()
		{
			this.DisableViewEvents();

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				entry.Value.SetFrames(this._configuration.ShowThumbnailFrames);
				ApplyCaptionBar(entry.Value);
				entry.Value.SetPreventPreviews();
			}

			this.EnableViewEvents();
		}

		private void EnableViewEvents()
		{
			this._ignoreViewEvents = false;
		}

		private void DisableViewEvents()
		{
			this._ignoreViewEvents = true;
		}





















	}
}