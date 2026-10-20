using System;
using System.Collections.Generic;
using EveOPreview.Configuration.Implementation;
using EveOPreview.Services;
using Newtonsoft.Json;
using Xunit;

namespace EveOPreview.Tests
{
    public class ExcludedCycleTests
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        [Fact]
        public void Configuration_Initializes_Excluded_Hotkey_Lists_Empty()
        {
            var configuration = new ThumbnailConfiguration();

            Assert.NotNull(configuration.CycleExcludedForwardHotkeys);
            Assert.NotNull(configuration.CycleExcludedBackwardHotkeys);
            Assert.Empty(configuration.CycleExcludedForwardHotkeys);
            Assert.Empty(configuration.CycleExcludedBackwardHotkeys);
        }

        [Fact]
        public void Configuration_Serializes_Excluded_Hotkeys_With_Expected_Names()
        {
            var configuration = new ThumbnailConfiguration
            {
                CycleExcludedForwardHotkeys = new List<string> { "Control+F17" },
                CycleExcludedBackwardHotkeys = new List<string> { "Control+F18" }
            };

            string json = JsonConvert.SerializeObject(configuration, SerializerSettings);

            Assert.Contains("\"CycleExcludedForwardHotkeys\"", json);
            Assert.Contains("\"CycleExcludedBackwardHotkeys\"", json);
            Assert.Contains("Control+F17", json);
            Assert.Contains("Control+F18", json);
        }

        [Fact]
        public void Configuration_Loads_Excluded_Hotkeys_From_Json()
        {
            const string json = @"{
                ""CycleExcludedForwardHotkeys"": [""Control+F17""],
                ""CycleExcludedBackwardHotkeys"": [""Control+F18""]
            }";

            var configuration = new ThumbnailConfiguration();
            JsonConvert.PopulateObject(json, configuration, SerializerSettings);

            Assert.Equal(new[] { "Control+F17" }, configuration.CycleExcludedForwardHotkeys);
            Assert.Equal(new[] { "Control+F18" }, configuration.CycleExcludedBackwardHotkeys);
        }

        [Fact]
        public void NextIndex_Starts_At_First_Excluded_Client_When_Active_Is_Not_Excluded()
        {
            var handles = Handles(10, 20, 30);

            int index = ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(999));

            Assert.Equal(0, index);
        }

        [Fact]
        public void NextIndex_Wraps_Forward_Through_Ordered_Clients()
        {
            var handles = Handles(10, 20, 30);

            Assert.Equal(1, ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(10)));
            Assert.Equal(2, ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(20)));
            Assert.Equal(0, ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(30)));
        }

        [Fact]
        public void NextIndex_With_Single_Client_Returns_That_Client()
        {
            var handles = Handles(10);

            Assert.Equal(0, ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(10)));
            Assert.Equal(0, ThumbnailManager.GetNextExcludedClientIndex(handles, new IntPtr(20)));
        }

        [Fact]
        public void NextIndex_Returns_Minus_One_For_Empty_List()
        {
            Assert.Equal(-1, ThumbnailManager.GetNextExcludedClientIndex(new List<IntPtr>(), IntPtr.Zero));
            Assert.Equal(-1, ThumbnailManager.GetNextExcludedClientIndex(null, IntPtr.Zero));
        }

        [Fact]
        public void ExcludedOrder_Follows_Addition_Order_When_Cycling_Forward()
        {
            var additionOrder = Handles(30, 10, 20);

            var result = ThumbnailManager.GetExcludedClientOrder(additionOrder, _ => true, true);

            Assert.Equal(additionOrder, result);
        }

        [Fact]
        public void ExcludedOrder_Is_Reversed_When_Cycling_Backward()
        {
            var additionOrder = Handles(30, 10, 20);

            var result = ThumbnailManager.GetExcludedClientOrder(additionOrder, _ => true, false);

            Assert.Equal(Handles(20, 10, 30), result);
        }

        [Fact]
        public void ExcludedOrder_Skips_Clients_No_Longer_Excluded()
        {
            var additionOrder = Handles(30, 10, 20);

            var result = ThumbnailManager.GetExcludedClientOrder(additionOrder, handle => handle != new IntPtr(10), true);

            Assert.Equal(Handles(30, 20), result);
        }

        [Fact]
        public void ExcludedOrder_Handles_Empty_And_Null_Orders()
        {
            Assert.Empty(ThumbnailManager.GetExcludedClientOrder(null, _ => true, true));
            Assert.Empty(ThumbnailManager.GetExcludedClientOrder(new List<IntPtr>(), _ => true, true));
        }

        private static List<IntPtr> Handles(params long[] values)
        {
            var handles = new List<IntPtr>();
            foreach (long value in values)
            {
                handles.Add(new IntPtr(value));
            }

            return handles;
        }
    }
}
