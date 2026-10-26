namespace EveOPreview.Configuration
{
	/// <summary>
	/// A single rule that turns a Gamelog message into a temporary thumbnail label.
	/// The label supports a {name} placeholder that is replaced with the value of
	/// the optional named "name" regex group.
	/// </summary>
	public sealed class EventLabelRule
	{
		public string Pattern { get; set; } = "";
		public string Label { get; set; } = "";
		public string Color { get; set; } = "White";
		public bool SelfOnly { get; set; }
		public bool Enabled { get; set; } = true;
	}
}
