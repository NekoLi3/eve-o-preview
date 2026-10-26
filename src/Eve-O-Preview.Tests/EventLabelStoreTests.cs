using System;
using System.Drawing;
using EveOPreview.Services;
using EveOPreview.Services.Implementation;
using Xunit;

namespace EveOPreview.Tests
{
    public class EventLabelStoreTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Returns_Recorded_Label()
        {
            var store = new EventLabelStore();
            store.Record("Pilot", "DECLOAK", Color.Orange, Now, 1500, Now);

            EventLabelSnapshot snapshot = store.GetLatest("Pilot", Now.AddMilliseconds(500));

            Assert.NotNull(snapshot);
            Assert.Equal("DECLOAK", snapshot.Text);
            Assert.Equal(Color.Orange, snapshot.Color);
        }

        [Fact]
        public void Latest_Event_Wins()
        {
            var store = new EventLabelStore();
            store.Record("Pilot", "OLD", Color.Red, Now, 1500, Now);
            store.Record("Pilot", "NEW", Color.Blue, Now.AddSeconds(1), 1500, Now.AddSeconds(1));

            EventLabelSnapshot snapshot = store.GetLatest("Pilot", Now.AddSeconds(1));

            Assert.Equal("NEW", snapshot.Text);
            Assert.Equal(Color.Blue, snapshot.Color);
        }

        [Fact]
        public void Older_Event_Cannot_Overwrite_Newer_One()
        {
            var store = new EventLabelStore();
            store.Record("Pilot", "NEW", Color.Blue, Now.AddSeconds(2), 1500, Now.AddSeconds(2));
            store.Record("Pilot", "OLD", Color.Red, Now, 1500, Now.AddSeconds(2));

            EventLabelSnapshot snapshot = store.GetLatest("Pilot", Now.AddSeconds(2));

            Assert.Equal("NEW", snapshot.Text);
        }

        [Fact]
        public void Label_Expires_After_Duration()
        {
            var store = new EventLabelStore();
            store.Record("Pilot", "DECLOAK", Color.Orange, Now, 1500, Now);

            Assert.NotNull(store.GetLatest("Pilot", Now.AddMilliseconds(1499)));
            Assert.Null(store.GetLatest("Pilot", Now.AddMilliseconds(1500)));
        }

        [Fact]
        public void Character_Lookup_Is_Case_Insensitive()
        {
            var store = new EventLabelStore();
            store.Record("Pilot One", "WARP NekoLi", Color.DeepSkyBlue, Now, 1500, Now);

            Assert.NotNull(store.GetLatest("pilot one", Now));
        }

        [Fact]
        public void Empty_Names_And_Text_Are_Ignored()
        {
            var store = new EventLabelStore();
            store.Record("", "DECLOAK", Color.Orange, Now, 1500, Now);
            store.Record("Pilot", "", Color.Orange, Now, 1500, Now);

            Assert.Null(store.GetLatest("", Now));
            Assert.Null(store.GetLatest("Pilot", Now));
        }

        [Fact]
        public void RemoveExpired_Clears_Old_Entries()
        {
            var store = new EventLabelStore();
            store.Record("Pilot", "DECLOAK", Color.Orange, Now, 1000, Now);

            store.RemoveExpired(Now.AddMilliseconds(999));
            Assert.NotNull(store.GetLatest("Pilot", Now.AddMilliseconds(999)));

            store.RemoveExpired(Now.AddMilliseconds(1000));
            Assert.Null(store.GetLatest("Pilot", Now.AddMilliseconds(1000)));
        }
    }
}
