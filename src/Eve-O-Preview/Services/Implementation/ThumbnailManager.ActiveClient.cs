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
		#region Active client management (switch, highlight, border colour)
		public IThumbnailView GetClientByPointer(IntPtr ptr)
		{
			return _thumbnailViews.FirstOrDefault(x => x.Key == ptr).Value;
		}

		public IThumbnailView GetActiveClient()
		{
			return GetClientByPointer(this._activeClient.Handle);
		}

		public void SetActive(KeyValuePair<IntPtr, IThumbnailView> newClient)
		{
			System.Diagnostics.Debug.WriteLine($"SetActive {newClient.Value.Title}");

			this.GetActiveClient()?.ClearBorder();

/*
#if LINUX
			this._windowManager.ActivateWindow(newClient.Key, newClient.Value.Title);
#else
			this._windowManager.ActivateWindow(newClient.Key, this._configuration.WindowsAnimationStyle);
#endif
*/
			this.SwitchActiveClient(newClient.Key, newClient.Value.Title);

			newClient.Value.SetHighlight();
			newClient.Value.Refresh(true);
		}

		public void UpdateActiveColour()
		{
			this.DisableViewEvents();

			var view = GetActiveClient();
			if (view != null)
			{
				view.SetDefaultBorderColor();
				view.SetHighlight(false, 0);
				view.SetHighlight();
			}

			this.EnableViewEvents();
		}

		private void SwitchActiveClient(IntPtr foregroundClientHandle, string foregroundClientTitle)
		{
			// Check if any actions are needed
			if (this._activeClient.Handle == foregroundClientHandle)
			{
				return;
			}
			System.Diagnostics.Debug.WriteLine($"SwitchActiveClient {foregroundClientTitle}");

#if LINUX
   			    this._windowManager.ActivateWindow(foregroundClientHandle, foregroundClientTitle);
#else
			this._windowManager.ActivateWindow(foregroundClientHandle, this._configuration.WindowsAnimationStyle);
#endif

			// Minimize the currently active client if needed
			if (this._configuration.MinimizeInactiveClients && !this._configuration.IsPriorityClient(this._activeClient.Title))
			{
				System.Diagnostics.Debug.WriteLine($"Calling MinimizeWindow {this._activeClient.Title}");

				System.Threading.Thread.Sleep(20);
				this._windowManager.MinimizeWindow(this._activeClient.Handle, this._configuration.WindowsAnimationStyle, false);
			}

			this._activeClient = (foregroundClientHandle, foregroundClientTitle);
		}
		#endregion
	}
}
