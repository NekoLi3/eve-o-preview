using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages;
using EveOPreview.Presenters;
using EveOPreview.Services;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Thumbnails
{
	sealed class CycleHotkeysToggleRequestedHandler : INotificationHandler<CycleHotkeysToggleRequested>
	{
		#region Private fields
		private readonly IMainFormPresenter _presenter;
		private readonly IThumbnailManager _manager;
		private readonly IThumbnailConfiguration _configuration;
		private readonly IConfigurationStorage _configurationStorage;
		#endregion

		public CycleHotkeysToggleRequestedHandler(MainFormPresenter presenter, IThumbnailManager manager, IThumbnailConfiguration configuration, IConfigurationStorage configurationStorage)
		{
			this._presenter = presenter;
			this._manager = manager;
			this._configuration = configuration;
			this._configurationStorage = configurationStorage;
		}

		public Task Handle(CycleHotkeysToggleRequested notification, CancellationToken cancellationToken)
		{
			// 1. Flip the configuration flag (source of truth)
			this._configuration.CycleHotkeysEnabled = !this._configuration.CycleHotkeysEnabled;

			// 2. Re-apply the cycle hotkeys according to the new state
			// (RegisterCycleHotkeys has a guard against duplicate registration)
			this._manager.UpdateCycleHotkeys();

			// 3. Synchronize the "Enable cycle hotkeys" checkbox, exactly like a manual toggle
			this._presenter.ApplyCycleHotkeysState();

			// 4. Persist the new state
			this._configurationStorage.Save();

			return Task.CompletedTask;
		}
	}
}