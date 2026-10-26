using System.Drawing;

namespace EveOPreview.Services
{
	/// <summary>
	/// A temporary label parsed from an EVE Gamelog event.
	/// </summary>
	public sealed class EventLabelSnapshot
	{
		public EventLabelSnapshot(string text, Color color)
		{
			this.Text = text;
			this.Color = color;
		}

		public string Text { get; }
		public Color Color { get; }
	}

	public interface IEventLogMonitor
	{
		void Start();
		void Stop();

		/// <summary>
		/// Returns the latest still-visible event label for a character, or null.
		/// </summary>
		EventLabelSnapshot GetLatestEventForCharacter(string characterName);
	}
}
