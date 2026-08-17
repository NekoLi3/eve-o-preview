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
		#region Client layout, window position, snap and zoom
		private void ThumbnailZoomIn(IThumbnailView view)
		{
			this.DisableViewEvents();

			view.ZoomIn(ViewZoomAnchorConverter.Convert(view.ClientZoomAnchor), this._configuration.ThumbnailZoomFactor);
			view.Refresh(false);

			this.EnableViewEvents();
		}


		private void ThumbnailZoomOut(IThumbnailView view)
		{
			this.DisableViewEvents();

			view.ZoomOut();
			view.Refresh(false);

			this.EnableViewEvents();
		}


		private void SnapThumbnailView(IThumbnailView view)
		{
			// Check if this feature is enabled
			if (!this._configuration.EnableThumbnailSnap)
			{
				return;
			}

			// Only borderless thumbnails can be docked
			if (this._configuration.ShowThumbnailFrames)
			{
				return;
			}

			int width = this._configuration.ThumbnailSize.Width;
			int height = this._configuration.ThumbnailSize.Height;

			// TODO Extract method
			int baseX = view.ThumbnailLocation.X;
			int baseY = view.ThumbnailLocation.Y;

			Point[] viewPoints = { new Point(baseX, baseY), new Point(baseX + width, baseY), new Point(baseX, baseY + height), new Point(baseX + width, baseY + height) };

			// TODO Extract constants
			int thresholdX = Math.Max(20, width / 10);
			int thresholdY = Math.Max(20, height / 10);

			foreach (var entry in this._thumbnailViews)
			{
				IThumbnailView testView = entry.Value;

				if (view.Id == testView.Id)
				{
					continue;
				}

				int testX = testView.ThumbnailLocation.X;
				int testY = testView.ThumbnailLocation.Y;

				Point[] testPoints = { new Point(testX, testY), new Point(testX + width, testY), new Point(testX, testY + height), new Point(testX + width, testY + height) };

				var delta = ThumbnailManager.TestViewPoints(viewPoints, testPoints, thresholdX, thresholdY);

				if ((delta.X == 0) && (delta.Y == 0))
				{
					continue;
				}

				view.ThumbnailLocation = new Point(view.ThumbnailLocation.X + delta.X, view.ThumbnailLocation.Y + delta.Y);
				this._configuration.SetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation);
				break;
			}
		}


		private static (int X, int Y) TestViewPoints(Point[] viewPoints, Point[] testPoints, int thresholdX, int thresholdY)
		{
			// Point combinations that we need to check
			// No need to check all 4x4 combinations
			(int ViewOffset, int TestOffset)[] testOffsets =
								{   ( 0, 3 ), ( 0, 2 ), ( 1, 2 ),
									( 0, 1 ), ( 0, 0 ), ( 1, 0 ),
									( 2, 1 ), ( 2, 0 ), ( 3, 0 )};

			foreach (var testOffset in testOffsets)
			{
				Point viewPoint = viewPoints[testOffset.ViewOffset];
				Point testPoint = testPoints[testOffset.TestOffset];

				int deltaX = testPoint.X - viewPoint.X;
				int deltaY = testPoint.Y - viewPoint.Y;

				if ((Math.Abs(deltaX) <= thresholdX) && (Math.Abs(deltaY) <= thresholdY))
				{
					return (deltaX, deltaY);
				}
			}

			return (0, 0);
		}

		private bool SetWindowStyle(IThumbnailView view, UInt32 styleToChange, bool remove)
		{
			IntPtr handle = view.Id;
			uint style = User32NativeMethods.GetWindowLong(handle, InteropConstants.GWL_STYLE);
			if (((style & styleToChange) == styleToChange) && remove == true)
			{
				style = style & ~styleToChange;
				User32NativeMethods.SetWindowLong(handle, InteropConstants.GWL_STYLE, style);
				return true;
			}
			if (((style & styleToChange) != styleToChange) && remove == false)
			{
				style = style | styleToChange;
				User32NativeMethods.SetWindowLong(handle, InteropConstants.GWL_STYLE, style);
				return true;
			}
			return false;
		}

		private void ApplyCaptionBar(IThumbnailView view)

		{
			if (view.Title == ThumbnailManager.DEFAULT_CLIENT_TITLE) return;
			IntPtr handle = view.Id;

			bool enable = this._configuration.HideCaptionOnClients;
			bool changed = false;
			changed = changed | SetWindowStyle(view, InteropConstants.WS_CAPTION, enable);
			changed = changed | SetWindowStyle(view, InteropConstants.WS_THICKFRAME, enable);
		}

		private void ApplyClientLayout(IThumbnailView view)
		{
			IntPtr clientHandle = view.Id;
			string clientTitle = view.Title;

			if (!this._configuration.EnableClientLayoutTracking)
			{
				return;
			}

			// No need to apply layout for not yet logged-in clients
			if (clientTitle == ThumbnailManager.DEFAULT_CLIENT_TITLE)
			{
				return;
			}

			ClientLayout clientLayout = this._configuration.GetClientLayout(clientTitle);

			if (clientLayout == null)
			{
				return;
			}

			if (clientLayout.IsMaximized)
			{
				this._windowManager.MaximizeWindow(clientHandle);
			}
			else
			{
				this._windowManager.MoveWindow(clientHandle, clientLayout.X, clientLayout.Y, clientLayout.Width, clientLayout.Height);
			}
		}


		private void UpdateClientLayouts()
		{
			if (!this._configuration.EnableClientLayoutTracking)
			{
				return;
			}

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				IThumbnailView view = entry.Value;

				// No need to save layout for not yet logged-in clients
				if (view.Title == ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					continue;
				}

				(int Left, int Top, int Right, int Bottom) position = this._windowManager.GetWindowPosition(view.Id);
				int width = Math.Abs(position.Right - position.Left);
				int height = Math.Abs(position.Bottom - position.Top);

				var isMaximized = this._windowManager.IsWindowMaximized(view.Id);

				if (!(isMaximized || this.IsValidWindowPosition(position.Left, position.Top, width, height)))
				{
					continue;
				}

				this._configuration.SetClientLayout(view.Title, new ClientLayout(position.Left, position.Top, width, height, isMaximized));
			}
		}


		private void EnqueueLocationChange(IThumbnailView view)
		{
			string activeClientTitle = this._activeClient.Title;
			// TODO ??
			this._configuration.SetThumbnailLocation(view.Title, activeClientTitle, view.ThumbnailLocation);

			lock (this._locationChangeNotificationSyncRoot)
			{
				if (this._enqueuedLocationChangeNotification.Handle == IntPtr.Zero)
				{
					this._enqueuedLocationChangeNotification = (view.Id, view.Title, activeClientTitle, view.ThumbnailLocation, ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY);
					return;
				}

				// Reset the delay and exit
				if ((this._enqueuedLocationChangeNotification.Handle == view.Id) &&
					(this._enqueuedLocationChangeNotification.ActiveClient == activeClientTitle))
				{
					this._enqueuedLocationChangeNotification.Delay = ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY;
					return;
				}

				this.RaiseThumbnailLocationUpdatedNotification(this._enqueuedLocationChangeNotification.Title);
				this._enqueuedLocationChangeNotification = (view.Id, view.Title, activeClientTitle, view.ThumbnailLocation, ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY);
			}
		}


		private bool TryDequeueLocationChange(out (IntPtr Handle, string Title, string ActiveClient, Point Location) change)
		{
			lock (this._locationChangeNotificationSyncRoot)
			{
				change = (IntPtr.Zero, null, null, Point.Empty);

				if (this._enqueuedLocationChangeNotification.Handle == IntPtr.Zero)
				{
					return false;
				}

				this._enqueuedLocationChangeNotification.Delay--;

				if (this._enqueuedLocationChangeNotification.Delay > 0)
				{
					return false;
				}

				change = (this._enqueuedLocationChangeNotification.Handle, this._enqueuedLocationChangeNotification.Title, this._enqueuedLocationChangeNotification.ActiveClient, this._enqueuedLocationChangeNotification.Location);
				this._enqueuedLocationChangeNotification = (IntPtr.Zero, null, null, Point.Empty, -1);

				return true;
			}
		}


		private async void RaiseThumbnailLocationUpdatedNotification(string title)
		{
			if (string.IsNullOrEmpty(title) || (title == ThumbnailManager.DEFAULT_CLIENT_TITLE))
			{
				return;
			}

			await this._mediator.Send(new SaveConfiguration());
		}

		// Quick sanity check that the window is not minimized
		private bool IsValidWindowPosition(int left, int top, int width, int height)
		{
			return (left > ThumbnailManager.WINDOW_POSITION_THRESHOLD_LOW) && (left < ThumbnailManager.WINDOW_POSITION_THRESHOLD_HIGH)
					&& (top > ThumbnailManager.WINDOW_POSITION_THRESHOLD_LOW) && (top < ThumbnailManager.WINDOW_POSITION_THRESHOLD_HIGH)
					&& (width > ThumbnailManager.WINDOW_SIZE_THRESHOLD) && (height > ThumbnailManager.WINDOW_SIZE_THRESHOLD);
		}
		#endregion
	}
}
