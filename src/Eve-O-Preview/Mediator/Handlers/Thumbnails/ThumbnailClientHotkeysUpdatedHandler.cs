using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages;
using EveOPreview.Services;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailClientHotkeysUpdatedHandler : INotificationHandler<ThumbnailClientHotkeysUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailClientHotkeysUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailClientHotkeysUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateClientHotkeys();

			return Task.CompletedTask;
		}
	}
}
