using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages;
using EveOPreview.Services;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Thumbnails
{
	sealed class ThumbnailActiveColourUpdatedHandler : INotificationHandler<ThumbnailActiveColourUpdated>
	{
		private readonly IThumbnailManager _manager;

		public ThumbnailActiveColourUpdatedHandler(IThumbnailManager manager)
		{
			this._manager = manager;
		}

		public Task Handle(ThumbnailActiveColourUpdated notification, CancellationToken cancellationToken)
		{
			this._manager.UpdateActiveColour();
			return Task.CompletedTask;
		}
	}
}
