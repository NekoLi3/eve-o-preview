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
		#region Client process tracking (add / update / remove / refresh loop)
		private async void UpdateThumbnailsList()
		{
			this._processMonitor.GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses);

			List<string> viewsAdded = new List<string>();
			List<string> viewsRemoved = new List<string>();

			foreach (IProcessInfo process in addedProcesses)
			{
				Size initialSize = this._configuration.ThumbnailSize;
				if (this._configuration.PerClientThumbnailSize.Any(x => x.Key == process.Title))
				{
					initialSize = this._configuration.PerClientThumbnailSize[process.Title];
				}

				IThumbnailView view = this._thumbnailViewFactory.Create(process.Handle, process.Title, this._configuration.ThumbnailSize);
				view.IsOverlayEnabled = this._configuration.ShowThumbnailOverlays;
				view.IsExcludedFromCycleGroup = false;
				view.SetFrames(this._configuration.ShowThumbnailFrames);
				// Max/Min size limitations should be set AFTER the frames are disabled
				// Otherwise thumbnail window will be unnecessary resized
				view.SetSizeLimitations(this._configuration.ThumbnailMinimumSize, this._configuration.ThumbnailMaximumSize);
				view.SetTopMost(this._configuration.ShowThumbnailsAlwaysOnTop);

				view.ThumbnailLocation = this.IsManageableThumbnail(view)
											? this._configuration.GetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation)
											: this._configuration.LoginThumbnailLocation;

				this._thumbnailViews.Add(view.Id, view);

				view.ThumbnailResized = this.ThumbnailViewResized;
				view.ThumbnailMoved = this.ThumbnailViewMoved;
				view.ThumbnailFocused = this.ThumbnailViewFocused;
				view.ThumbnailLostFocus = this.ThumbnailViewLostFocus;
				view.ThumbnailActivated = this.ThumbnailActivated;
				view.ThumbnailDeactivated = this.ThumbnailDeactivated;

				view.ThumbnailToggleCycleGroup = this.ThumbnailToggleCycleGroup;

				view.RegisterHotkey(this._configuration.GetClientHotkey(view.Title));

				this.ApplyClientLayout(view);
				this.ApplyCaptionBar(view);

				// TODO Add extension filter here later
				if (view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					viewsAdded.Add(view.Title);
				}
			}

			foreach (IProcessInfo process in updatedProcesses)
			{
				this._thumbnailViews.TryGetValue(process.Handle, out IThumbnailView view);

				if (view == null)
				{
					// Something went terribly wrong
					continue;
				}

				if (process.Title != view.Title) // update thumbnail title
				{
					viewsRemoved.Add(view.Title);
					view.Title = process.Title;
					viewsAdded.Add(view.Title);

					view.RegisterHotkey(this._configuration.GetClientHotkey(process.Title));

					this.ApplyClientLayout(view);
					this.ApplyCaptionBar(view);
				}
			}

			foreach (IProcessInfo process in removedProcesses)
			{
				IThumbnailView view = this._thumbnailViews[process.Handle];

				this._thumbnailViews.Remove(view.Id);
				if (view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					viewsRemoved.Add(view.Title);
				}

				view.UnregisterHotkey();

				view.ThumbnailResized = null;
				view.ThumbnailMoved = null;
				view.ThumbnailFocused = null;
				view.ThumbnailLostFocus = null;
				view.ThumbnailActivated = null;
				view.ThumbnailToggleCycleGroup = null;

				view.Close();
			}

			if ((viewsAdded.Count > 0) || (viewsRemoved.Count > 0))
			{
				await this._mediator.Publish(new ThumbnailListUpdated(viewsAdded, viewsRemoved));
			}
		}

		private void RefreshThumbnails()
		{
			// TODO Split this method
			IntPtr foregroundWindowHandle = this._windowManager.GetForegroundWindowHandle();

			// The foreground window can be NULL in certain circumstances, such as when a window is losing activation.
			// It is safer to just skip this refresh round than to do something while the system state is undefined
			if (foregroundWindowHandle == IntPtr.Zero)
			{
				return;
			}

			string foregroundWindowTitle = null;

			// Check if the foreground window handle is one of the known handles for client windows or their thumbnails
			bool isClientWindow = this.IsClientWindowActive(foregroundWindowHandle);
			bool isMainWindowActive = this.IsMainWindowActive(foregroundWindowHandle);

			if (foregroundWindowHandle == this._activeClient.Handle)
			{
				foregroundWindowTitle = this._activeClient.Title;
			}
			else if (this._thumbnailViews.TryGetValue(foregroundWindowHandle, out IThumbnailView foregroundView))
			{
				// This code will work only on Alt+Tab switch between clients
				foregroundWindowTitle = foregroundView.Title;
			}
			else if (!isClientWindow)
			{
				this._externalApplication = foregroundWindowHandle;
			}

			// No need to minimize EVE clients when switching out to non-EVE window (like thumbnail)
			if (!string.IsNullOrEmpty(foregroundWindowTitle))
			{
				this.SwitchActiveClient(foregroundWindowHandle, foregroundWindowTitle);
			}

			bool hideAllThumbnails = this._configuration.HideThumbnailsOnLostFocus && !(isClientWindow || isMainWindowActive);

			// Wait for some time before hiding all previews
			if (hideAllThumbnails)
			{
				this._hideThumbnailsDelay--;
				if (this._hideThumbnailsDelay > 0)
				{
					hideAllThumbnails = false; // Postpone the 'hide all' operation
				}
				else
				{
					this._hideThumbnailsDelay = 0; // Stop the counter
				}
			}
			else
			{
				this._hideThumbnailsDelay = this._configuration.HideThumbnailsDelay; // Reset the counter
			}

			this._refreshCycleCount++;

			bool forceRefresh;
			if (this._refreshCycleCount >= ThumbnailManager.FORCED_REFRESH_CYCLE_THRESHOLD)
			{
				this._refreshCycleCount = 0;
				forceRefresh = true;
			}
			else
			{
				forceRefresh = false;
			}

			this.DisableViewEvents();

			// Snap thumbnail
			// No need to update Thumbnails while one of them is highlighted
			if ((!this._isHoverEffectActive) && this.TryDequeueLocationChange(out var locationChange))
			{
				if ((locationChange.ActiveClient == this._activeClient.Title) && this._thumbnailViews.TryGetValue(locationChange.Handle, out var view))
				{
					this.SnapThumbnailView(view);

					this.RaiseThumbnailLocationUpdatedNotification(view.Title);
				}
				else
				{
					this.RaiseThumbnailLocationUpdatedNotification(locationChange.Title);
				}
			}

			// Hide, show, resize and move - update ZoomAnchor setting
			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				IThumbnailView view = entry.Value;
			// update ZoomAnchor regardless
				view.ClientZoomAnchor = this._configuration.GetZoomAnchor(view.Title, this._configuration.ThumbnailZoomAnchor);

				// update the solar system location label from the EVE Gamelog (no-op when disabled)
				view.SetSystemLocation(this._gamelogMonitor.GetSystemForCharacter(this.GetCharacterName(view)));


				if (hideAllThumbnails || this._configuration.IsThumbnailDisabled(view.Title))
				{
					if (view.IsActive)
					{
						view.Hide();
					}
					continue;
				}

				if (this._configuration.HideActiveClientThumbnail && (view.Id == this._activeClient.Handle))
				{
					if (view.IsActive)
					{
						view.Hide();
					}
					continue;
				}

				if (this._configuration.HideLoginClientThumbnail && (view.Title == DEFAULT_CLIENT_TITLE ))
				{
					if (view.IsActive)
					{
						view.Hide();
					}
					continue;
				}

				// No need to update Thumbnails while one of them is highlighted
				if (!this._isHoverEffectActive)
				{
					// Do not even move thumbnails with default caption
					if (this.IsManageableThumbnail(view))
					{
						view.ThumbnailLocation = this._configuration.GetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation);
						view.ThumbnailSize = this._configuration.GetThumbnailSize(view.Title, this._activeClient.Title, view.ThumbnailSize);
					}

					view.SetOpacity(this._configuration.ThumbnailOpacity);
					view.SetTopMost(this._configuration.ShowThumbnailsAlwaysOnTop);
				}

				view.IsOverlayEnabled = this._configuration.ShowThumbnailOverlays;

				view.SetHighlight(
					this._configuration.EnableActiveClientHighlight && (view.Id == this._activeClient.Handle), 
					this._configuration.ActiveClientHighlightThickness);

				if (!view.IsActive)
				{
					view.Show();
				}
				else
				{
					view.Refresh(forceRefresh);
				}
			}

			this.EnableViewEvents();
		}

		// Checks whether currently active window belongs to an EVE client or its thumbnail
		private bool IsClientWindowActive(IntPtr windowHandle)
		{
			if (windowHandle == IntPtr.Zero)
			{
				return false;
			}

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				IThumbnailView view = entry.Value;

				if (view.IsKnownHandle(windowHandle))
				{
					return true;
				}
			}

			return false;
		}

		// Check whether the currently active window belongs to EVE-O-Preview itself
		private bool IsMainWindowActive(IntPtr windowHandle)
		{
			return (this._processMonitor.GetMainProcess().Handle == windowHandle);
		}

		// We shouldn't manage some thumbnails (like thumbnail of the EVE client sitting on the login screen)
		// TODO Move to a service (?)
		private bool IsManageableThumbnail(IThumbnailView view)
		{
			return view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE;
		}

		// EVE client titles are prefixed with the client type: "EVE - <character>" / "EVE Frontier - <character>"
		private string GetCharacterName(IThumbnailView view)
		{
			string title = view.Title;
			if (title.StartsWith("EVE - ", StringComparison.Ordinal))
			{
				return title.Substring("EVE - ".Length);
			}
			if (title.StartsWith("EVE Frontier - ", StringComparison.Ordinal))
			{
				return title.Substring("EVE Frontier - ".Length);
			}
			return title;
		}
		#endregion
	}
}
