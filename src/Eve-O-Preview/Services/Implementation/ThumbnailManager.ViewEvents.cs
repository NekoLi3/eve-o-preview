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
		#region Thumbnail view event handlers (focus, activation, resize, move, cycle group)
		private void ThumbnailViewFocused(IntPtr id)
		{
			if (this._isHoverEffectActive)
			{
				return;
			}

			this._isHoverEffectActive = true;

			IThumbnailView view = this._thumbnailViews[id];

			view.SetTopMost(true);
			view.SetOpacity(1.0);

			if (this._configuration.ThumbnailZoomEnabled && ! view.IsPreventPreviews() )
			{
				this.ThumbnailZoomIn(view);
			}
		}


		private void ThumbnailViewLostFocus(IntPtr id)
		{
			if (!this._isHoverEffectActive)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];

			if (this._configuration.ThumbnailZoomEnabled)
			{
				this.ThumbnailZoomOut(view);
			}

			view.SetOpacity(this._configuration.ThumbnailOpacity);

			this._isHoverEffectActive = false;
		}


		private void ThumbnailActivated(IntPtr id)
		{
			IThumbnailView view = this._thumbnailViews[id];

			System.Diagnostics.Debug.WriteLine($"ThumbnailActivated {view.Title}");

			Task.Run(() =>
				{
#if LINUX
					this._windowManager.ActivateWindow(view.Id, view.Title);
#else
					this._windowManager.ActivateWindow(view.Id, this._configuration.WindowsAnimationStyle);
#endif
				})
				.ContinueWith((task) =>
				{
					// This code should be executed on UI thread
					this.SwitchActiveClient(view.Id, view.Title);
					this.UpdateClientLayouts();
					this.RefreshThumbnails();
				}, TaskScheduler.FromCurrentSynchronizationContext());
		}


		private void ThumbnailDeactivated(IntPtr id, bool switchOut)
		{
			System.Diagnostics.Debug.WriteLine($"ThumbnailDeactivated");

			if (switchOut)
			{
#if LINUX
				this._windowManager.ActivateWindow(this._externalApplication, null);
#else
				this._windowManager.ActivateWindow(this._externalApplication, this._configuration.WindowsAnimationStyle);
#endif
			}
			else
			{
				if (!this._thumbnailViews.TryGetValue(id, out IThumbnailView view))
				{
					return;
				}

				this._windowManager.MinimizeWindow(view.Id, this._configuration.WindowsAnimationStyle, true);
				this.RefreshThumbnails();
			}
		}


		private void ThumbnailToggleCycleGroup(IntPtr id)
		{
			var view = GetClientByPointer(id);
			if ( view != null )
			{
				view.IsExcludedFromCycleGroup = !view.IsExcludedFromCycleGroup;
				view.SetCycleGroupIndicator(view.IsExcludedFromCycleGroup, _configuration.CycleGroupIndicatorAnchor);

			}
			this.RefreshThumbnails();
		}



		private async void ThumbnailViewResized(IntPtr id)
		{
			if (this._ignoreViewEvents)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];

			this.SetThumbnailsSize(view.ThumbnailSize);

			view.Refresh(false);

			await this._mediator.Publish(new ThumbnailActiveSizeUpdated(view.ThumbnailSize));
		}


		private void ThumbnailViewMoved(IntPtr id)
		{
			if (this._ignoreViewEvents)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];
			view.Refresh(false);
			this.EnqueueLocationChange(view);
		}
		#endregion
	}
}
