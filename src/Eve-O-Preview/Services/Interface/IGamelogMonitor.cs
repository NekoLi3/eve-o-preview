namespace EveOPreview.Services
{
	public interface IGamelogMonitor
	{
		void Start();
		void Stop();

		/// <summary>
		/// Returns the solar system (or station name) where the given character
		/// currently is, according to the EVE Gamelog. Returns null when the
		/// character has no detected session (or is unknown).
		/// </summary>
		string GetSystemForCharacter(string characterName);
	}
}
