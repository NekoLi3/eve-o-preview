using MediatR;

namespace EveOPreview.Mediator.Messages
{
	/// <summary>
	/// Fired by the ThumbnailManager when the toggle hotkey
	/// (ToggleCycleHotkeysHotkey) is pressed. The handler flips
	/// CycleHotkeysEnabled, re-applies the cycle hotkeys and
	/// synchronizes the UI checkbox + persisted configuration.
	/// </summary>
	sealed class CycleHotkeysToggleRequested : INotification
	{
	}
}