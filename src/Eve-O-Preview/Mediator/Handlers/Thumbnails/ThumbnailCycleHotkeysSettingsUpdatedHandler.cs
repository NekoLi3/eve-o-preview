using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages;
using EveOPreview.Services;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailCycleHotkeysSettingsUpdatedHandler : INotificationHandler<ThumbnailCycleHotkeysSettingsUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailCycleHotkeysSettingsUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailCycleHotkeysSettingsUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateCycleHotkeys();

			return Task.CompletedTask;
		}
	}
}
