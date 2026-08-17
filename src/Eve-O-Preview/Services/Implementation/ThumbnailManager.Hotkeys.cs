using EveOPreview.Mediator.Messages;
using EveOPreview.UI.Hotkeys;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace EveOPreview.Services
{
	public partial class ThumbnailManager : IThumbnailManager
	{
		#region Global hotkeys (toggle for cycle hotkeys)
		private readonly List<HotkeyHandler> _cycleHotkeysToggleHotkeyHandlers = new List<HotkeyHandler>();

		/// <summary>
		/// Registers the hotkey that toggles CycleHotkeysEnabled on the fly:
		/// when the cycle hotkeys are active it unregisters them, and vice versa.
		/// This hotkey stays registered regardless of the flag so the user can
		/// always re-enable the cycle hotkeys without opening the UI.
		/// Follows the same pattern as RegisterMinimizeAllClientsHotkey.
		/// </summary>
		public void RegisterCycleHotkeysToggleHotkey(IEnumerable<Keys> keys)
		{
			foreach (var hotkey in keys)
			{
				if (hotkey == Keys.None)
				{
					return;
				}

				var newHandler = new HotkeyHandler(default(IntPtr), hotkey);
				newHandler.Pressed += async (object s, HandledEventArgs e) =>
				{
					await this._mediator.Publish(new CycleHotkeysToggleRequested());
					e.Handled = true;
				};

				newHandler.Register();
				this._cycleHotkeysToggleHotkeyHandlers.Add(newHandler);
			}
		}
		#endregion
	}
}